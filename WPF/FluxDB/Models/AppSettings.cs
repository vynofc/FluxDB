using System;
using System.Collections.Generic;

namespace FluxDB.Models
{
    /// <summary>
    /// Application settings stored locally
    /// </summary>
    public class AppSettings
    {
        public string LastRootFolder { get; set; }
        public bool AutoUpdateCheck { get; set; } = false;
        public List<string> RecentFolders { get; set; } = new List<string>();

        public Dictionary<string, string> FolderFilters { get; set; } = new Dictionary<string, string>();
    }
}
