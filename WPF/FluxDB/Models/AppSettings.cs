using System;
using System.Collections.Generic;

namespace FluxDB.Models
{
    /// <summary>
    /// Controls which UI state gets persisted to settings.json
    /// </summary>
    public class PersistenceOptions
    {
        public bool LastRootFolder { get; set; } = true;
        public bool LastViewFolder { get; set; } = true;
        public bool Filter { get; set; } = true;
        public bool Sort { get; set; } = true;
        public bool ColumnVisibility { get; set; } = true;
        public bool RecentFolders { get; set; } = true;
    }

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

        // Per-root-folder last viewed subfolder
        public Dictionary<string, string> FolderLastView { get; set; } = new Dictionary<string, string>();

        // Per-root-folder sort state
        public Dictionary<string, string> FolderSortColumn { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> FolderSortDirection { get; set; } = new Dictionary<string, string>();

        public PersistenceOptions Persistence { get; set; } = new PersistenceOptions();
    }
}
