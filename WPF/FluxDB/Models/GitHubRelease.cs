using Newtonsoft.Json;

namespace FluxDB.Models
{
    public class GitHubRelease
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }
    }
}