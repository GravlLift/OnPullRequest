using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.TeamFoundation.Build.WebApi;
using System.Net.Http;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts;
using Microsoft.AspNetCore.WebHooks;
using Microsoft.Extensions.Configuration;

namespace OnPullRequest
{
    public class PullRequestUpdated
    {
        private readonly IConfiguration configuration;
        private readonly ILogger<PullRequestUpdated> logger;

        public PullRequestUpdated(IConfiguration configuration, ILogger<PullRequestUpdated> logger)
        {
            this.configuration = configuration;
            this.logger = logger;
        }

        [FunctionName("pull-request-updated")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequestMessage req)
        {
            var ev = await req.Content.ReadAsAsync<GitPullRequestUpdatedPayload>();

            if (ev.EventType != "git.pullrequest.updated")
            {
                return new BadRequestObjectResult($"Event {ev.EventType} is unsupported.");
            }

            // Only act on PR completion
            var pullRequest = ev.Resource;
            if (pullRequest.Status == "completed")
            {
                var projectConfiguration = new ProjectConfiguration
                {
                    DevopsBaseUrl = configuration
                        .GetValue<Uri>($"{ev.Resource.Repository.Project.Name}:{nameof(ProjectConfiguration.DevopsBaseUrl)}")
                        ?? throw new InvalidOperationException(
                        $"{ev.Resource.Repository.Project.Name}:{nameof(ProjectConfiguration.DevopsBaseUrl)} has not been configured."),
                    PersonalAccessToken = configuration
                        .GetValue<string>($"{ev.Resource.Repository.Project.Name}:{nameof(ProjectConfiguration.PersonalAccessToken)}")
                        ?? throw new InvalidOperationException(
                        $"{ev.Resource.Repository.Project.Name}:{nameof(ProjectConfiguration.PersonalAccessToken)} has not been configured."),
                    ReleaseDefinitionId = configuration
                        .GetValue<int?>($"{ev.Resource.Repository.Project.Name}:{nameof(ProjectConfiguration.ReleaseDefinitionId)}")
                        ?? throw new InvalidOperationException(
                        $"{ev.Resource.Repository.Project.Name}:{nameof(ProjectConfiguration.ReleaseDefinitionId)} has not been configured."),
                };

                var connection = new VssConnection(projectConfiguration.DevopsBaseUrl,
                    new VssBasicCredential(string.Empty, projectConfiguration.PersonalAccessToken));

                // Look for the most recent successful build associated with this PR
                using var buildClient = connection.GetClient<BuildHttpClient>();
                var builds = await buildClient.GetBuildsAsync(pullRequest.Repository.Project.Name, statusFilter: BuildStatus.Completed,
                    resultFilter: BuildResult.Succeeded);
                var mostRecentPrBuild = builds
                    .Where(b => b.TriggerInfo.ContainsKey("pr.number") &&
                        b.TriggerInfo["pr.number"] == pullRequest.PullRequestId.ToString())
                    .OrderByDescending(b => b.FinishTime)
                    .FirstOrDefault();

                if (mostRecentPrBuild != null)
                {
                    // If there's been a successful build for this PR, release the latest build using
                    // the configured release definition
                    using var releaseClient = connection.GetClient<ReleaseHttpClient>();
                    var releaseDefinition = await releaseClient.GetReleaseDefinitionAsync(
                        ev.Resource.Repository.Project.Name, projectConfiguration.ReleaseDefinitionId.Value);
                    var release = await releaseClient.CreateReleaseAsync(new ReleaseStartMetadata
                    {
                        DefinitionId = 3,
                        Artifacts = releaseDefinition.Artifacts.Select(a => new ArtifactMetadata
                        {
                            Alias = a.Alias,
                            InstanceReference = new BuildVersion
                            {
                                Id = mostRecentPrBuild.Id.ToString(),
                                DefinitionId = a.DefinitionReference["definition"].Id,
                                IsMultiDefinitionType = a.DefinitionReference["IsMultiDefinitionType"].Id == "True",
                            }
                        }).ToList()
                    }, pullRequest.Repository.Project.Name);

                    return new CreatedResult(((ReferenceLink)release.Links.Links["self"]).Href, null);
                }
                else
                {
                    logger.LogInformation($"Received pull request update for completed PR {pullRequest.PullRequestId} of project {pullRequest.Repository.Project.Name}, but no builds exist for it.");
                }
            }

            return new NoContentResult();
        }
    }
}
