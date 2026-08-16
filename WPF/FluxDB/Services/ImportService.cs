using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluxDB.Models;
using Newtonsoft.Json;

namespace FluxDB.Services
{
    public class ImportService
    {
        private readonly DatabaseService _database;

        public ImportService(DatabaseService database)
        {
            _database = database;
        }

        public void ImportFromFile(string filePath, string rootFolder)
        {
            using (var stream = OpenFileStream(filePath))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            using (var jsonReader = new JsonTextReader(reader))
            {
                var serializer = new JsonSerializer();
                var export = serializer.Deserialize<ExportData>(jsonReader);

                if (export == null || export.Files == null)
                    throw new InvalidDataException("Invalid import file: no file entries found.");

                using (var transaction = _database.BeginTransaction())
                {
                    try
                    {
                        foreach (var file in export.Files)
                        {
                            if (string.IsNullOrEmpty(file.Path))
                                continue;

                            var entry = new FileEntry
                            {
                                Path = file.Path,
                                Name = file.Name ?? Path.GetFileName(file.Path),
                                Extension = file.Extension ?? Path.GetExtension(file.Path),
                                Size = file.Size,
                                CreatedAt = DateTime.TryParse(file.CreatedAt, out var ca) ? ca : DateTime.MinValue,
                                ModifiedAt = DateTime.TryParse(file.ModifiedAt, out var ma) ? ma : DateTime.MinValue,
                                Deleted = false,
                                LastIndexedAt = DateTime.Now,
                                Tags = file.Tags,
                                Note = file.Note ?? ""
                            };

                            var fileId = _database.UpsertFile(entry, transaction);

                            if (file.Tags != null && file.Tags.Count > 0)
                                _database.SetTagsForFile(fileId, file.Tags, transaction);

                            if (!string.IsNullOrEmpty(file.Note))
                                _database.SetNoteForFile(fileId, file.Note, transaction);
                        }

                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private static Stream OpenFileStream(string filePath)
        {
            var fs = File.OpenRead(filePath);
            if (filePath.EndsWith(".gz"))
                return new GZipStream(fs, CompressionMode.Decompress);
            return fs;
        }

        private class ExportData
        {
            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("exportedAt")]
            public string ExportedAt { get; set; }

            [JsonProperty("rootFolder")]
            public string RootFolder { get; set; }

            [JsonProperty("totalFiles")]
            public int TotalFiles { get; set; }

            [JsonProperty("files")]
            public List<ImportedFile> Files { get; set; }
        }

        private class ImportedFile
        {
            [JsonProperty("path")]
            public string Path { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("extension")]
            public string Extension { get; set; }

            [JsonProperty("createdAt")]
            public string CreatedAt { get; set; }

            [JsonProperty("modifiedAt")]
            public string ModifiedAt { get; set; }

            [JsonProperty("size")]
            public long Size { get; set; }

            [JsonProperty("tags")]
            public List<string> Tags { get; set; }

            [JsonProperty("note")]
            public string Note { get; set; }
        }
    }
}