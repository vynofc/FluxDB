using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluxDB.Models;
using Newtonsoft.Json;

namespace FluxDB.Services
{
    /// <summary>
    /// Service for exporting index to JSON
    /// </summary>
    public class ExportService
    {
        private readonly DatabaseService _database;
        private readonly SettingsService _settings;

        public ExportService(DatabaseService database, SettingsService settings)
        {
            _database = database;
            _settings = settings;
        }

        /// <summary>
        /// Export all files to an IndexExport object
        /// </summary>
        public IndexExport CreateExport(string rootFolder)
        {
            var files = _database.GetAllFiles();
            var export = new IndexExport
            {
                Version = "1.0",
                ExportedAt = DateTime.Now,
                RootFolder = rootFolder,
                TotalFiles = files.Count
            };

            foreach (var file in files)
            {
                export.Files.Add(new IndexExportItem
                {
                    Path = file.Path,
                    Name = file.Name,
                    Extension = file.Extension,
                    CreatedAt = file.CreatedAt,
                    ModifiedAt = file.ModifiedAt,
                    Size = file.Size,
                    Tags = file.Tags ?? new List<string>(),
                    Note = file.Note
                });
            }

            return export;
        }

        /// <summary>
        /// Export to JSON file
        /// </summary>
        public void ExportToJson(string filePath, string rootFolder)
        {
            var export = CreateExport(rootFolder);
            var json = JsonConvert.SerializeObject(export, Formatting.Indented);
            File.WriteAllText(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Export to compressed GZIP file
        /// </summary>
        public void ExportToGzip(string filePath, string rootFolder)
        {
            var export = CreateExport(rootFolder);
            var json = JsonConvert.SerializeObject(export);
            var bytes = Encoding.UTF8.GetBytes(json);

            using (var fileStream = File.Create(filePath))
            using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
            {
                gzipStream.Write(bytes, 0, bytes.Length);
            }
        }

        /// <summary>
        /// Get default export path
        /// </summary>
        public string GetDefaultExportPath()
        {
            return Path.Combine(_settings.GetAppDataDirectory(), "index.json");
        }
    }
}
