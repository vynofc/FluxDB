using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
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

            LoggingService.LogDebug($"ScanFolderAsync started: root={rootPath}");

            // Phase 1: Count files (iterative, no recursion)
            StatusChanged?.Invoke(this, "Counting files...");
            await Task.Run(() =>
            {
                try
                {
                    result.FilesIndexed = EnumerateAllFiles(rootPath, cancellationToken).Count();
                }
                catch (UnauthorizedAccessException) { }
                catch (DirectoryNotFoundException) { }
            }, cancellationToken);
            result.TotalFiles = result.FilesIndexed;
            result.FilesIndexed = 0;

            LoggingService.LogDebug($"File count complete: {result.TotalFiles} files found under {rootPath}");
            StatusChanged?.Invoke(this, $"Found {result.TotalFiles} files. Indexing...");

            if (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                result.EndTime = DateTime.Now;
                return result;
            }

            // Phase 2: Index files (streaming enumeration, no intermediate list)
            var existingPaths = new HashSet<string>();
            int processed = 0;
            var BatchSize = new SettingsService().GetDevSettingInt(DevSettingsRegistry.IndexerBatchSizeKey);
            if (BatchSize <= 0) BatchSize = 1000;
            const int MaxPathLength = 260;
            var currentTransaction = _database.BeginTransaction();

            try
            {
                foreach (var filePath in EnumerateAllFiles(rootPath, cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        break;
                    }

                    try
                    {
                        // Windows MAX_PATH limit — skip paths that would fail in the OS/DB layer
                        if (filePath.Length >= MaxPathLength)
                        {
                            result.Errors.Add($"Path too long (skipped): {filePath}");
                            processed++;
                            continue;
                        }

                        var fi = new FileInfo(filePath);
                        var fileEntry = new FileEntry
                        {
                            Path = filePath,
                            Name = fi.Name,
                            Extension = fi.Extension,
                            Size = fi.Length,
                            CreatedAt = fi.CreationTime,
                            ModifiedAt = fi.LastWriteTime,
                            Deleted = false,
                            LastIndexedAt = DateTime.Now
                        };

                        _database.UpsertFile(fileEntry, currentTransaction);
                        existingPaths.Add(filePath);
                        result.FilesIndexed++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{filePath}: {ex.Message}");
                    }

                    processed++;

                    if (processed % BatchSize == 0)
                    {
                        await CommitBatchWithRetry(currentTransaction);
                        currentTransaction.Dispose();
                        currentTransaction = _database.BeginTransaction();
                    }

                    if (processed % 100 == 0 || processed == result.TotalFiles)
                    {
                        var progress = (double)processed / result.TotalFiles * 100;
                        ProgressChanged?.Invoke(this, new IndexProgressEventArgs
                        {
                            Current = processed,
                            Total = result.TotalFiles,
                            Percentage = progress,
                            CurrentFile = filePath
                        });
                    }
                }

                if (!result.Cancelled)
                {
                    await CommitBatchWithRetry(currentTransaction);
                }
                else
                {
                    try { currentTransaction.Rollback(); } catch { }
                }
            }
            catch (OperationCanceledException)
            {
                result.Cancelled = true;
                try { currentTransaction.Rollback(); } catch { }
            }
            finally
            {
                try { currentTransaction.Dispose(); } catch { }
            }

            // Mark deleted files
            if (!cancellationToken.IsCancellationRequested && !result.Cancelled)
            {
                StatusChanged?.Invoke(this, "Checking for deleted files...");
                _database.MarkDeletedFiles(existingPaths, rootPath);
            }

            result.EndTime = DateTime.Now;
            result.Success = !result.Cancelled;
            LoggingService.LogDebug($"ScanFolderAsync done: success={result.Success} cancelled={result.Cancelled} indexed={result.FilesIndexed}/{result.TotalFiles} errors={result.Errors.Count} duration={result.Duration.TotalSeconds:F2}s");
            StatusChanged?.Invoke(this, result.Cancelled ? "Cancelled" : "Indexing complete!");

            return result;
        }

        private async Task CommitBatchWithRetry(SQLiteTransaction transaction)
        {
            const int maxRetries = 3;
            int delayMs = 100;
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    transaction.Commit();
                    return;
                }
                catch (Exception)
                {
                    if (attempt < maxRetries - 1)
                    {
                        await Task.Delay(delayMs);
                        delayMs *= 2;
                    }
                    else
                    {
                        try { transaction.Rollback(); } catch { }
                        throw;
                    }
                }
            }
        }

        private static bool IsHiddenOrSystem(string path)
        {
            try
            {
                var attr = File.GetAttributes(path);
                return (attr & (FileAttributes.Hidden | FileAttributes.System)) != 0;
            }
            catch { return true; }
        }

        private IEnumerable<string> EnumerateAllFiles(string path, CancellationToken cancellationToken)
        {
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                var current = stack.Pop();
                string[] files;
                string[] dirs;

                try
                {
                    files = Directory.GetFiles(current);
                    dirs = Directory.GetDirectories(current);
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (DirectoryNotFoundException) { continue; }

                foreach (var file in files)
                {
                    if (cancellationToken.IsCancellationRequested) yield break;
                    if (IsHiddenOrSystem(file)) continue;
                    yield return file;
                }

                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    if (IsHiddenOrSystem(dirs[i])) continue;
                    stack.Push(dirs[i]);
                }
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
