using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Microsoft.AspNetCore.WebHooks;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.Common;
using System.Linq;
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi;

namespace OnPullRequest
{
    internal class PullRequestUpdated
    {
        private readonly OnPullRequestConfiguration configuration;
        private readonly ILogger<PullRequestUpdated> logger;

        public PullRequestUpdated(IOptions<OnPullRequestConfiguration> configuration,
            ILogger<PullRequestUpdated> logger)
        {
            this.configuration = configuration.Value;
            this.logger = logger;
        }


        [FunctionName("pull-request-updated")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "POST", Route = null)] HttpRequestMessage req)
        {
            var ev = await req.Content.ReadAsAsync<GitPullRequestUpdatedPayload>();

            if (ev.EventType != "git.pullrequest.updated")
            {
                return new BadRequestObjectResult($"Event {ev.EventType} is unsupported.");
            }

            // Only act on PR completion or abandonment
            var pullRequest = ev.Resource;
            if (pullRequest.Status == "completed" || pullRequest.Status == "abandoned")
            {
                var connection = new VssConnection(configuration.DevopsBaseUrl,
                    new VssBasicCredential(string.Empty, configuration.PersonalAccessToken));
                using (var releaseClient = connection.GetClient<ReleaseHttpClient>())
                {
                    // Only get active releases created after the PR was created
                    var releases = await releaseClient.GetReleasesAsync(configuration.ReleaseDefinitionId,
                        statusFilter: ReleaseStatus.Active, minCreatedTime: pullRequest.CreationDate);

                    // Releases don't contain environment information, so we need to check each of them individually
                    foreach (var releaseId in releases.Select(r => r.Id))
                    {
                        var release = await releaseClient.GetReleaseAsync(configuration.ProjectName, releaseId);
                        logger.LogInformation($"Cancelling all environments for release {release.Id}...");
                        foreach (var env in release.Environments
                            .Where(e => (EnvironmentStatus.NotStarted | EnvironmentStatus.InProgress).HasFlag(e.Status)))
                        {
                            logger.LogInformation($"Cancelling environment {env.Id}...");
                            await releaseClient.UpdateReleaseEnvironmentAsync(
                                new ReleaseEnvironmentUpdateMetadata
                                {
                                    Status = EnvironmentStatus.Canceled
                                },
                                configuration.ProjectName,
                                release.Id,
                                env.Id);
                        }
                        logger.LogInformation($"All environments cancelled for release {release.Id}.");
                    }
                }

                AzureCredentials creds;
                if (configuration.UseAzureCLICredentials == true)
                {
                    creds = AzureCliCredentials.Create();
                }
                else
                {
                    creds = new AzureCredentialsFactory()
                        .FromSystemAssignedManagedServiceIdentity(MSIResourceType.AppService, AzureEnvironment.AzureGlobalCloud);
                }

                IAzure azure = Azure.Authenticate(creds).WithSubscription(configuration.SubscriptionId);

                var webApp = await azure.WebApps.GetByIdAsync(
                    $"/subscriptions/{configuration.SubscriptionId}/resourceGroups/{configuration.ResourceGroup}/providers/Microsoft.Web/sites/{configuration.AppServiceName}");
                var slot = await webApp.DeploymentSlots.GetByNameAsync(pullRequest.PullRequestId.ToString());
                if (slot != null)
                {
                    await webApp.DeploymentSlots.DeleteByNameAsync(pullRequest.PullRequestId.ToString());
                }
                return new OkResult();
            }
            else
            {
                logger.LogInformation($"Unhandled status {pullRequest.Status}");
            }
            return new NoContentResult();
        }
    }
}
