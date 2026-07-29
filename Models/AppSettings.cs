using System;
using System.Collections.Generic;

namespace FluxDB.Models
{
    /// <summary>
    /// Application settings stored locally
    /// </summary>
    public class AppSettings
    {
        public string DeviceId { get; set; }
        public string LastRootFolder { get; set; }
        public string Theme { get; set; } = "Dark";
        public double PreviewScale { get; set; } = 1.0;
        public bool AutoUpdateCheck { get; set; } = false;
        public List<string> RecentFolders { get; set; } = new List<string>();

        public Dictionary<string, string> FolderFilters { get; set; } = new Dictionary<string, string>();

        public List<string> DisabledPlugins { get; set; } = new List<string>();
    }
}
