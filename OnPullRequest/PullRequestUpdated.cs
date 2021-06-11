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

namespace OnPullRequest
{
    public class PullRequestUpdated
    {
        private readonly OnPullRequestConfiguration configuration;
        private readonly ILogger<PullRequestUpdated> logger;

        public PullRequestUpdated(IOptions<OnPullRequestConfiguration> configuration, ILogger<PullRequestUpdated> logger)
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
