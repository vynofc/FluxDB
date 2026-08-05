using System;
using System.Collections.Generic;
using System.Data.SQLite;
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

            LoggingService.LogDebug($"ScanFolderAsync started: root={rootPath}");
            StatusChanged?.Invoke(this, "Scanning folders...");
            await Task.Run(() => CollectFiles(rootPath, files, cancellationToken), cancellationToken);

            result.TotalFiles = files.Count;
            LoggingService.LogDebug($"File collection complete: {files.Count} files found under {rootPath}");
            StatusChanged?.Invoke(this, $"Found {files.Count} files. Indexing...");

            // Index files
            int processed = 0;
            const int BatchSize = 1000;
            var currentTransaction = _database.BeginTransaction();
            
            try
            {
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

                        LoggingService.LogDebug($"UpsertFile: {filePath} | size={fileInfo.Length} | ext={fileInfo.Extension} | modified={fileInfo.LastWriteTime:o}");
                        _database.UpsertFile(fileEntry, currentTransaction);
                        existingPaths.Add(filePath);
                        result.FilesIndexed++;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogDebug($"UpsertFile FAILED: {filePath} — {ex.GetType().Name}: {ex.Message}");
                        result.Errors.Add($"{filePath}: {ex.Message}");
                    }

                    processed++;
                    
                    if (processed % BatchSize == 0)
                    {
                        LoggingService.LogDebug($"Committing batch at {processed}/{files.Count} files");
                        CommitBatchWithRetry(currentTransaction);
                        currentTransaction.Dispose();
                        currentTransaction = _database.BeginTransaction();
                    }

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

                if (!result.Cancelled)
                {
                    LoggingService.LogDebug($"Final batch commit: {result.FilesIndexed} files indexed");
                    CommitBatchWithRetry(currentTransaction);
                }
                else
                {
                    LoggingService.LogDebug("Scan cancelled — rolling back current transaction");
                    try { currentTransaction.Rollback(); } catch (Exception ex) { LoggingService.LogDebug($"Rollback failed: {ex.Message}"); }
                }
            }
            finally
            {
                try { currentTransaction.Dispose(); } catch (Exception ex) { LoggingService.LogDebug($"Transaction dispose failed: {ex.Message}"); }
            }

            // Mark deleted files
            if (!cancellationToken.IsCancellationRequested)
            {
                StatusChanged?.Invoke(this, "Checking for deleted files...");
                _database.MarkDeletedFiles(existingPaths, rootPath);
            }

            result.EndTime = DateTime.Now;
            result.Success = !result.Cancelled;
            LoggingService.LogDebug($"ScanFolderAsync done: success={result.Success} cancelled={result.Cancelled} indexed={result.FilesIndexed}/{result.TotalFiles} errors={result.Errors.Count} duration={result.Duration.TotalSeconds:F2}s");
            if (result.Errors.Count > 0)
                foreach (var err in result.Errors)
                    LoggingService.LogDebug($"  IndexError: {err}");
            StatusChanged?.Invoke(this, result.Cancelled ? "Cancelled" : "Indexing complete!");

            return result;
        }

        private void CommitBatchWithRetry(SQLiteTransaction transaction)
        {
            const int maxRetries = 3;
            int delayMs = 100;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    transaction.Commit();
                    LoggingService.LogDebug($"CommitBatchWithRetry: success on attempt {attempt + 1}");
                    return;
                }
                catch (Exception ex)
                {
                    LoggingService.LogDebug($"CommitBatchWithRetry: attempt {attempt + 1} failed — {ex.Message}, retryDelayMs={delayMs}");
                    if (attempt < maxRetries - 1)
                    {
                        Thread.Sleep(delayMs);
                        delayMs *= 2;
                    }
                    else
                    {
                        LoggingService.LogDebug("CommitBatchWithRetry: all retries exhausted, rolling back");
                        try { transaction.Rollback(); } catch (Exception ex) { LoggingService.LogDebug($"Retry rollback failed: {ex.Message}"); }
                        throw;
                    }
                }
            }
        }

        private void CollectFiles(string path, List<string> files, CancellationToken cancellationToken)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    files.Add(file);
                }

                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    
                    try
                    {
                        CollectFiles(dir, files, cancellationToken);
                    }
                    catch (UnauthorizedAccessException) { LoggingService.LogDebug($"Skipping inaccessible directory: {dir}"); }
                    catch (DirectoryNotFoundException) { LoggingService.LogDebug($"Skipping missing directory: {dir}"); }
                }
            }
            catch (UnauthorizedAccessException) { LoggingService.LogDebug($"Skipping inaccessible root: {path}"); }
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
