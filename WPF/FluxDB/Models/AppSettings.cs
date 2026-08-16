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
    /// Definition of a single developer setting (dotted key, description, default).
    /// </summary>
    public class DevSettingDefinition
    {
        public string Key { get; set; }
        public string Description { get; set; }
        public string DefaultValue { get; set; }
    }

    /// <summary>
    /// Application settings stored locally
    /// </summary>
    public class AppSettings
    {
        public string LastRootFolder { get; set; }
        public bool AutoUpdateCheck { get; set; } = false;
        public bool SearchInPathEnabled { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public List<string> RecentFolders { get; set; } = new List<string>();
        public Dictionary<string, bool> ColumnVisibility { get; set; } = new Dictionary<string, bool>();

        public Dictionary<string, string> FolderFilters { get; set; } = new Dictionary<string, string>();

        // Per-root-folder last viewed subfolder
        public Dictionary<string, string> FolderLastView { get; set; } = new Dictionary<string, string>();

        // Per-root-folder sort state
        public Dictionary<string, string> FolderSortColumn { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> FolderSortDirection { get; set; } = new Dictionary<string, string>();

        public PersistenceOptions Persistence { get; set; } = new PersistenceOptions();

        // Developer settings (dotted key -> value), editable via F9 dev settings window
        public Dictionary<string, string> DevSettings { get; set; } = new Dictionary<string, string>();
    }
}
