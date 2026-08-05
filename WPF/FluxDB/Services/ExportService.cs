using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FluxDB.Models;
using Newtonsoft.Json;

namespace FluxDB.Services
{
    public class ExportService
    {
        private readonly DatabaseService _database;
        private readonly SettingsService _settings;

        public ExportService(DatabaseService database, SettingsService settings)
        {
            _database = database;
            _settings = settings;
        }

        public void ExportToJson(string filePath, string rootFolder)
        {
            var files = _database.GetAllFiles();
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            using (var jsonWriter = new JsonTextWriter(writer) { Formatting = Formatting.Indented })
            {
                WriteExport(jsonWriter, files, rootFolder);
            }
        }

        public void ExportToGzip(string filePath, string rootFolder)
        {
            var files = _database.GetAllFiles();
            using (var fileStream = File.Create(filePath))
            using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
            using (var writer = new StreamWriter(gzipStream, Encoding.UTF8))
            using (var jsonWriter = new JsonTextWriter(writer))
            {
                WriteExport(jsonWriter, files, rootFolder);
            }
        }

        private void WriteExport(JsonTextWriter jsonWriter, List<FileEntry> files, string rootFolder)
        {
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("version");
            jsonWriter.WriteValue("1.0");
            jsonWriter.WritePropertyName("exportedAt");
            jsonWriter.WriteValue(DateTime.Now.ToString("o"));
            jsonWriter.WritePropertyName("rootFolder");
            jsonWriter.WriteValue(rootFolder);
            jsonWriter.WritePropertyName("totalFiles");
            jsonWriter.WriteValue(files.Count);
            jsonWriter.WritePropertyName("files");
            jsonWriter.WriteStartArray();

            foreach (var file in files)
            {
                jsonWriter.WriteStartObject();
                jsonWriter.WritePropertyName("path"); jsonWriter.WriteValue(file.Path);
                jsonWriter.WritePropertyName("name"); jsonWriter.WriteValue(file.Name);
                jsonWriter.WritePropertyName("extension"); jsonWriter.WriteValue(file.Extension);
                jsonWriter.WritePropertyName("createdAt"); jsonWriter.WriteValue(file.CreatedAt);
                jsonWriter.WritePropertyName("modifiedAt"); jsonWriter.WriteValue(file.ModifiedAt);
                jsonWriter.WritePropertyName("size"); jsonWriter.WriteValue(file.Size);

                jsonWriter.WritePropertyName("tags");
                jsonWriter.WriteStartArray();
                if (file.Tags != null)
                    foreach (var tag in file.Tags)
                        jsonWriter.WriteValue(tag);
                jsonWriter.WriteEndArray();

                jsonWriter.WritePropertyName("note"); jsonWriter.WriteValue(file.Note ?? "");
                jsonWriter.WriteEndObject();
            }

            jsonWriter.WriteEndArray();
            jsonWriter.WriteEndObject();
        }
    }
}