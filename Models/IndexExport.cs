using System;
using System.Collections.Generic;

namespace FluxDB.Models
{
    /// <summary>
    /// Model for index export
    /// </summary>
    public class IndexExportItem
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public long Size { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public string Note { get; set; }
    }

    /// <summary>
    /// Complete index export
    /// </summary>
    public class IndexExport
    {
        public string Version { get; set; } = "1.0";
        public DateTime ExportedAt { get; set; }
        public string RootFolder { get; set; }
        public int TotalFiles { get; set; }
        public List<IndexExportItem> Files { get; set; } = new List<IndexExportItem>();
    }
}
