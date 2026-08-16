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
        public Task<IndexResult> ScanFolderAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => ScanFolderCore(rootPath, cancellationToken), cancellationToken);
        }

        private IndexResult ScanFolderCore(string rootPath, CancellationToken cancellationToken)
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

            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"ScanFolderAsync started: root={rootPath}");

            StatusChanged?.Invoke(this, "Scanning files...");
            var skippedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (cancellationToken.IsCancellationRequested)
            {
                result.Cancelled = true;
                result.EndTime = DateTime.Now;
                return result;
            }

            // Phase 2: Index files (streaming enumeration, no intermediate list, no pre-count)
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownFiles = _database.GetAllFileMetadata();
            result.TotalFiles = 0; // Will be updated during scan
            int processed = 0;
            var BatchSize = new SettingsService().GetDevSettingInt(DevSettingsRegistry.IndexerBatchSizeKey);
            if (BatchSize <= 0) BatchSize = 1000;
            const int MaxPathLength = 260;
            var currentTransaction = _database.BeginTransaction();
            var lastProgressReport = DateTime.MinValue;

            try
            {
                foreach (var filePath in EnumerateAllFiles(rootPath, cancellationToken, skippedDirs))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        break;
                    }

                    result.TotalFiles++;

                    try
                    {
                        // Windows MAX_PATH limit
                        if (filePath.Length >= MaxPathLength)
                        {
                            result.AddError($"Path too long (skipped): {filePath}");
                            processed++;
                            continue;
                        }

                        var fi = new FileInfo(filePath);

                        // Incremental: only upsert if new or changed
                        bool isKnown = knownFiles.TryGetValue(filePath, out var known);
                        if (isKnown && known.Size == fi.Length && known.ModifiedAt == fi.LastWriteTime)
                        {
                            existingPaths.Add(filePath);
                            result.FilesIndexed++;
                        }
                        else
                        {
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
                    }
                    catch (Exception ex)
                    {
                        result.AddError($"{filePath}: {ex.Message}");
                    }

                    processed++;

                    if (processed % BatchSize == 0 && !result.Cancelled)
                    {
                        CommitBatchWithRetryAsync(currentTransaction).GetAwaiter().GetResult();
                        currentTransaction.Dispose();
                        _database.InvalidateUpsertCommand();
                        currentTransaction = _database.BeginTransaction();
                    }

                    var now = DateTime.UtcNow;
                    bool isLast = processed == result.TotalFiles;
                    if (processed % 500 == 0 || isLast || (now - lastProgressReport).TotalMilliseconds >= 100)
                    {
                        lastProgressReport = now;
                        var progress = result.TotalFiles > 0
                            ? Math.Min(100.0, (double)processed / result.TotalFiles * 100)
                            : 0.0;
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
                    CommitBatchWithRetryAsync(currentTransaction).GetAwaiter().GetResult();
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
                _database.InvalidateUpsertCommand();
                try { currentTransaction.Dispose(); } catch { }
            }

            // Mark deleted files
            if (!cancellationToken.IsCancellationRequested && !result.Cancelled)
            {
                StatusChanged?.Invoke(this, "Checking for deleted files...");
                _database.MarkDeletedFiles(existingPaths, rootPath, skippedDirs.Count > 0 ? skippedDirs : null);
            }

            result.EndTime = DateTime.Now;
            result.Success = !result.Cancelled;
            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"ScanFolderAsync done: success={result.Success} cancelled={result.Cancelled} indexed={result.FilesIndexed}/{result.TotalFiles} errors={result.Errors.Count} duration={result.Duration.TotalSeconds:F2}s");
            StatusChanged?.Invoke(this, result.Cancelled ? "Cancelled" : "Indexing complete!");

            return result;
        }

        private async Task CommitBatchWithRetryAsync(SQLiteTransaction transaction)
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

        private IEnumerable<string> EnumerateAllFiles(string path, CancellationToken cancellationToken, HashSet<string> skippedDirs = null)
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
                catch (UnauthorizedAccessException)
                {
                    skippedDirs?.Add(current);
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    skippedDirs?.Add(current);
                    continue;
                }
                catch
                {
                    skippedDirs?.Add(current);
                    continue;
                }

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
        private const int MaxErrors = 1000;

        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string ErrorMessage { get; set; }
        public string RootPath { get; set; }
        public int TotalFiles { get; set; }
        public int FilesIndexed { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public List<string> Errors { get; } = new List<string>();
        public int TotalErrors { get; private set; }

        public void AddError(string error)
        {
            TotalErrors++;
            if (Errors.Count < MaxErrors)
                Errors.Add(error);
        }
    }
}
