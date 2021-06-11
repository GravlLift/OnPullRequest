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
using Microsoft.Azure.Management.ResourceManager.Fluent.Core;
using Microsoft.Azure.Management.AppService.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;

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

            // Only act on PR completion or abandonment
            var pullRequest = ev.Resource;
            if (pullRequest.Status == "completed" || pullRequest.Status == "abandoned")
            {
                var creds = new AzureCredentialsFactory().
                IAzure azure = Azure.Authenticate(creds).WithDefaultSubscription();

                var webApp = await azure.WebApps.GetByIdAsync("");
                var slot = await webApp.DeploymentSlots.GetByNameAsync(pullRequest.PullRequestId.ToString());
                if (slot != null)
                {
                    await webApp.DeploymentSlots.DeleteByNameAsync(pullRequest.PullRequestId.ToString());
                }
            }
            else
            {
                logger.LogInformation($"Unhandled status {pullRequest.Status}");
            }
            return new NoContentResult();
        }
    }
}
