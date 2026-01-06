using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluxDB.Models;

namespace FluxDB.Services
{
    /// <summary>
    /// Service for scanning and indexing folders
    /// </summary>
    public class IndexerService
    {
        private readonly DatabaseService _database;

        public event EventHandler<IndexProgressEventArgs> ProgressChanged;
        public event EventHandler<string> StatusChanged;

        public IndexerService(DatabaseService database)
        {
            _database = database;
        }

        /// <summary>
        /// Scan a folder and index all files
        /// </summary>
        public async Task<IndexResult> ScanFolderAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            var result = new IndexResult
            {
                StartTime = DateTime.Now,
                RootPath = rootPath
            };

            if (!Directory.Exists(rootPath))
            {
                result.Success = false;
                result.ErrorMessage = "Folder does not exist";
                return result;
            }

            var existingPaths = new HashSet<string>();
            var files = new List<string>();

            // Collect all files first
            StatusChanged?.Invoke(this, "Scanning folders...");
            await Task.Run(() => CollectFiles(rootPath, files, cancellationToken), cancellationToken);

            result.TotalFiles = files.Count;
            StatusChanged?.Invoke(this, $"Found {files.Count} files. Indexing...");

            // Index files
            int processed = 0;
            foreach (var filePath in files)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    break;
                }

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    var fileEntry = new FileEntry
                    {
                        Path = filePath,
                        Name = fileInfo.Name,
                        Extension = fileInfo.Extension,
                        Size = fileInfo.Length,
                        CreatedAt = fileInfo.CreationTime,
                        ModifiedAt = fileInfo.LastWriteTime,
                        Deleted = false,
                        LastIndexedAt = DateTime.Now
                    };

                    _database.UpsertFile(fileEntry);
                    existingPaths.Add(filePath);
                    result.FilesIndexed++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{filePath}: {ex.Message}");
                }

                processed++;
                if (processed % 100 == 0 || processed == files.Count)
                {
                    var progress = (double)processed / files.Count * 100;
                    ProgressChanged?.Invoke(this, new IndexProgressEventArgs
                    {
                        Current = processed,
                        Total = files.Count,
                        Percentage = progress,
                        CurrentFile = filePath
                    });
                }
            }

            // Mark deleted files
            if (!cancellationToken.IsCancellationRequested)
            {
                StatusChanged?.Invoke(this, "Checking for deleted files...");
                _database.MarkDeletedFiles(existingPaths);
            }

            result.EndTime = DateTime.Now;
            result.Success = !result.Cancelled;
            StatusChanged?.Invoke(this, result.Cancelled ? "Cancelled" : "Indexing complete!");

            return result;
        }

        private void CollectFiles(string path, List<string> files, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    files.Add(file);
                }

                foreach (var dir in Directory.GetDirectories(path))
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    
                    try
                    {
                        CollectFiles(dir, files, cancellationToken);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip folders we can't access
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Skip folders that don't exist anymore
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders we can't access
            }
        }
    }

    public class IndexProgressEventArgs : EventArgs
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public double Percentage { get; set; }
        public string CurrentFile { get; set; }
    }

    public class IndexResult
    {
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string ErrorMessage { get; set; }
        public string RootPath { get; set; }
        public int TotalFiles { get; set; }
        public int FilesIndexed { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public List<string> Errors { get; set; } = new List<string>();
    }
}
