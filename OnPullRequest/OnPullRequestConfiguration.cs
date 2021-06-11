namespace OnPullRequest
{
    public class OnPullRequestConfiguration
    {
        public string SubscriptionId { get; set; }
        public string ResourceGroup { get; set; }
        public string AppServiceName { get; set; }
        public bool? UseAzureCLICredentials { get; set; }
    }
}
