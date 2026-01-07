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
        public string LicenseKey { get; set; }
        public string LastRootFolder { get; set; }
        public DateTime? LastLicenseCheck { get; set; }
        public bool LicenseValid { get; set; }
        public DateTime? LicenseExpiresAt { get; set; }
        public List<string> RecentFolders { get; set; } = new List<string>();

        /// <summary>
        /// Stores filter settings per folder (folder path -> filter name)
        /// </summary>
        public Dictionary<string, string> FolderFilters { get; set; } = new Dictionary<string, string>();
    }
}
