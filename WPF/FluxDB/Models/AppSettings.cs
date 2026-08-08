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
        public string Theme { get; set; } = "Dark";
        public string AccentColor { get; set; } = "#0078D4";
        public List<string> RecentFolders { get; set; } = new List<string>();
        public Dictionary<string, bool> ColumnVisibility { get; set; } = new Dictionary<string, bool>();

        public Dictionary<string, string> FolderFilters { get; set; } = new Dictionary<string, string>();
    }
}
