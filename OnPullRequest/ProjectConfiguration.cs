using System;
using System.Collections.Generic;
using System.Text;

namespace OnPullRequest
{
    internal class ProjectConfiguration
    {
        public Uri DevopsBaseUrl { get; set; }
        public string PersonalAccessToken { get; set; }
        public int? ReleaseDefinitionId { get; set; }
    }
}
