using System;

namespace OnPullRequest
{
    public class OnPullRequestConfiguration
    {
        public string SubscriptionId { get; set; }
        public string ResourceGroup { get; set; }
        public string AppServiceName { get; set; }
        public bool? UseAzureCLICredentials { get; set; }
        public Uri DevopsBaseUrl { get; set; }
        public string PersonalAccessToken { get; set; }
        public int? ReleaseDefinitionId { get; set; }
        public string ProjectName { get; set; }
    }
}
