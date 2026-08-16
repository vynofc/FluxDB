using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SQLite;
using System.IO;
using System.Threading;
using FluxDB.Models;

namespace FluxDB.Services
{
    public class DatabaseService : IDisposable
    {
        private const string WalFileSuffix = "-wal";
        private const string ShmFileSuffix = "-shm";

        private SQLiteConnection _connection;

        private void ThrowIfDisposed()
        {
            if (_connection == null)
                throw new ObjectDisposedException(nameof(DatabaseService));
        }

        public DatabaseService(string databasePath)
        {
            var dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) 
                Directory.CreateDirectory(dir);
            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"DatabaseService: opening DB at {databasePath}");
            _connection = new SQLiteConnection(string.Format("Data Source={0};Version=3;", databasePath));
            _connection.Open();
            using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-8000;", _connection))
            {
                pragmaCmd.ExecuteNonQuery();
            }

            // Clean up orphaned WAL/SHM files from previous WAL mode
            try
            {
                var walPath = databasePath + WalFileSuffix;
                var shmPath = databasePath + ShmFileSuffix;
                if (File.Exists(walPath)) File.Delete(walPath);
                if (File.Exists(shmPath)) File.Delete(shmPath);
            }
            catch { /* best effort */ }

            InitDb();

            try
            {
                if (File.Exists(databasePath))
                {
                    var attr = File.GetAttributes(databasePath);
                    if ((attr & FileAttributes.Hidden) == 0)
                        File.SetAttributes(databasePath, attr | FileAttributes.Hidden);
                }
            }
            catch { /* best effort */ }

            LoggingService.LogDebug("DatabaseService: DB opened and schema initialized");
        }

        private void InitDb()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS files (id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT UNIQUE NOT NULL, name TEXT NOT NULL, extension TEXT, size INTEGER, created_at TEXT, modified_at TEXT, deleted INTEGER DEFAULT 0, last_indexed_at TEXT);
                CREATE TABLE IF NOT EXISTS tags (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE NOT NULL);
                CREATE TABLE IF NOT EXISTS file_tags (file_id INTEGER, tag_id INTEGER, PRIMARY KEY (file_id, tag_id));
                CREATE TABLE IF NOT EXISTS notes (file_id INTEGER PRIMARY KEY, note TEXT);
                CREATE INDEX IF NOT EXISTS idx_files_deleted ON files(deleted);
                CREATE INDEX IF NOT EXISTS idx_files_name ON files(name);
                CREATE INDEX IF NOT EXISTS idx_files_extension ON files(extension);
                CREATE INDEX IF NOT EXISTS idx_file_tags_tag_id ON file_tags(tag_id);
            ";
            using (var transaction = _connection.BeginTransaction())
            {
                using (var cmd = new SQLiteCommand(sql, _connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }

            ApplyMigrations();

            if (GetUserVersion() >= 2 && !_ftsAvailable)
            {
                try
                {
                    using (var cmd = new SQLiteCommand("SELECT rowid FROM files_fts LIMIT 1", _connection))
                        cmd.ExecuteScalar();
                    _ftsAvailable = true;
                }
                catch { _ftsAvailable = false; }
            }
        }

        private int GetUserVersion()
        {
            using (var cmd = new SQLiteCommand("PRAGMA user_version", _connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private void SetUserVersion(int version)
        {
            using (var cmd = new SQLiteCommand($"PRAGMA user_version = {version}", _connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private void ApplyMigrations()
        {
            var version = GetUserVersion();
            if (version < 1)
            {
                MigrateToV1();
                SetUserVersion(1);
            }
            if (version < 2)
            {
                MigrateToV2();
                SetUserVersion(2);
            }
        }

        private bool _ftsAvailable;

        private void MigrateToV2()
        {
            try
            {
                using (var cmd = new SQLiteCommand("CREATE VIRTUAL TABLE IF NOT EXISTS files_fts USING fts5(name, note, content='files', content_rowid='id')", _connection))
                    cmd.ExecuteNonQuery();

                // Backfill FTS index from existing rows
                using (var cmd = new SQLiteCommand(
                    @"INSERT INTO files_fts(rowid, name, note)
                      SELECT f.id, f.name, IFNULL(n.note, '') FROM files f
                      LEFT JOIN notes n ON n.file_id = f.id
                      WHERE f.deleted = 0 AND f.id NOT IN (SELECT rowid FROM files_fts)", _connection))
                    cmd.ExecuteNonQuery();

                _ftsAvailable = true;
            }
            catch { _ftsAvailable = false; }
        }

        private void MigrateToV1()
        {
            // Add parent_path column and index
            try
            {
                using (var cmd = new SQLiteCommand("ALTER TABLE files ADD COLUMN parent_path TEXT", _connection))
                    cmd.ExecuteNonQuery();
            }
            catch { /* column may already exist */ }

            using (var cmd = new SQLiteCommand("CREATE INDEX IF NOT EXISTS idx_files_parent ON files(parent_path)", _connection))
                cmd.ExecuteNonQuery();

            // Backfill parent_path in batches
            var allPaths = new List<(long Id, string Path)>();
            using (var cmd = new SQLiteCommand("SELECT id, path FROM files", _connection))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    allPaths.Add((r.GetInt64(0), r.GetString(1)));
            }

            using (var transaction = _connection.BeginTransaction())
            {
                using (var cmd = new SQLiteCommand("UPDATE files SET parent_path = @pp WHERE id = @id", _connection, transaction))
                {
                    cmd.Parameters.Add("@pp", System.Data.DbType.String);
                    cmd.Parameters.Add("@id", System.Data.DbType.Int64);
                    foreach (var (id, path) in allPaths)
                    {
                        cmd.Parameters["@pp"].Value = Path.GetDirectoryName(path) ?? "";
                        cmd.Parameters["@id"].Value = id;
                        cmd.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }

            // Drop old single-column index, create composite
            using (var cmd = new SQLiteCommand("DROP INDEX IF EXISTS idx_files_deleted", _connection))
                cmd.ExecuteNonQuery();
            using (var cmd = new SQLiteCommand("CREATE INDEX IF NOT EXISTS idx_files_deleted_parent ON files(deleted, parent_path)", _connection))
                cmd.ExecuteNonQuery();
        }

        public SQLiteTransaction BeginTransaction()
        {
            ThrowIfDisposed();
            return _connection.BeginTransaction();
        }

        private SQLiteCommand _upsertCommand;
        private SQLiteTransaction _upsertTransaction;

        public void InvalidateUpsertCommand()
        {
            _upsertCommand?.Dispose();
            _upsertCommand = null;
            _upsertTransaction = null;
        }

        public int UpsertFile(FileEntry f, SQLiteTransaction transaction = null)
        {
            ThrowIfDisposed();
            if (transaction != null && transaction != _upsertTransaction)
            {
                _upsertCommand?.Dispose();
                _upsertCommand = null;
                _upsertTransaction = transaction;
            }

            if (_upsertCommand == null)
            {
                var sql = "INSERT INTO files (path,name,extension,size,created_at,modified_at,deleted,last_indexed_at,parent_path) VALUES (@p,@n,@e,@s,@c,@m,@d,@l,@pp) ON CONFLICT(path) DO UPDATE SET name=@n,extension=@e,size=@s,modified_at=@m,deleted=@d,last_indexed_at=@l,parent_path=@pp RETURNING id";
                _upsertCommand = new SQLiteCommand(sql, _connection, transaction);
                _upsertCommand.Parameters.Add("@p", System.Data.DbType.String);
                _upsertCommand.Parameters.Add("@n", System.Data.DbType.String);
                _upsertCommand.Parameters.Add("@e", System.Data.DbType.String);
                _upsertCommand.Parameters.Add("@s", System.Data.DbType.Int64);
                _upsertCommand.Parameters.Add("@c", System.Data.DbType.String);
                _upsertCommand.Parameters.Add("@m", System.Data.DbType.String);
                _upsertCommand.Parameters.Add("@d", System.Data.DbType.Int32);
                _upsertCommand.Parameters.Add("@l", System.Data.DbType.String);
                _upsertCommand.Parameters.Add("@pp", System.Data.DbType.String);
            }

            _upsertCommand.Parameters["@p"].Value = f.Path;
            _upsertCommand.Parameters["@n"].Value = f.Name;
            _upsertCommand.Parameters["@e"].Value = f.Extension ?? "";
            _upsertCommand.Parameters["@s"].Value = f.Size;
            _upsertCommand.Parameters["@c"].Value = f.CreatedAt.ToString("o");
            _upsertCommand.Parameters["@m"].Value = f.ModifiedAt.ToString("o");
            _upsertCommand.Parameters["@d"].Value = f.Deleted ? 1 : 0;
            _upsertCommand.Parameters["@l"].Value = f.LastIndexedAt.ToString("o");
            _upsertCommand.Parameters["@pp"].Value = Path.GetDirectoryName(f.Path) ?? "";
            var r = _upsertCommand.ExecuteScalar();
            var id = r != null ? Convert.ToInt32(r) : 0;
            if (id > 0 && _ftsAvailable)
                FtsUpsert(id, f.Name, "", transaction);
            return id;
        }

        private void FtsUpsert(int fileId, string name, string note, SQLiteTransaction transaction)
        {
            try
            {
                using (var cmd = new SQLiteCommand("INSERT OR REPLACE INTO files_fts(rowid, name, note) VALUES (@id, @n, @no)", _connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@id", fileId);
                    cmd.Parameters.AddWithValue("@n", name ?? "");
                    cmd.Parameters.AddWithValue("@no", note ?? "");
                    cmd.ExecuteNonQuery();
                }
            }
            catch { _ftsAvailable = false; }
        }

        private void FtsDelete(int fileId, SQLiteTransaction transaction)
        {
            if (!_ftsAvailable) return;
            try
            {
                using (var cmd = new SQLiteCommand("DELETE FROM files_fts WHERE rowid = @id", _connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@id", fileId);
                    cmd.ExecuteNonQuery();
                }
            }
            catch { _ftsAvailable = false; }
        }

        public List<FileEntry> GetAllFiles(bool includeDeleted = false)
        {
            return EnumerateAllFiles(includeDeleted).ToList();
        }

        public IEnumerable<FileEntry> EnumerateAllFiles(bool includeDeleted = false)
        {
            ThrowIfDisposed();
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, char(0)) as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE (f.deleted = 0 OR @includeDeleted = 1)
                GROUP BY f.id";

            var cmd = new SQLiteCommand(sql, _connection);
            cmd.Parameters.AddWithValue("@includeDeleted", includeDeleted ? 1 : 0);
            var r = cmd.ExecuteReader();
            try
            {
                var ordinals = GetOrdinals(r);
                while (r.Read())
                {
                    yield return MapFileEntry(r, ordinals);
                }
            }
            finally
            {
                r.Dispose();
                cmd.Dispose();
            }
        }

        public int GetFileCount()
        {
            ThrowIfDisposed();
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM files WHERE deleted=0", _connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public Dictionary<string, (long Size, DateTime ModifiedAt)> GetAllFileMetadata()
        {
            ThrowIfDisposed();
            var dict = new Dictionary<string, (long, DateTime)>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = new SQLiteCommand("SELECT path, size, modified_at FROM files WHERE deleted=0", _connection))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var path = r.GetString(0);
                    var size = r.IsDBNull(1) ? 0 : r.GetInt64(1);
                    var modStr = r.IsDBNull(2) ? null : r.GetString(2);
                    var modified = DateTime.TryParse(modStr, out var m) ? m : DateTime.MinValue;
                    dict[path] = (size, modified);
                }
            }
            return dict;
        }

        public List<FileEntry> GetFilesInFolder(string folderPath)
        {
            ThrowIfDisposed();
            var files = new List<FileEntry>();
            var prefix = folderPath.EndsWith("\\") ? folderPath : folderPath + "\\";
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, char(0)) as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted = 0 AND f.path LIKE @folderPrefix
                GROUP BY f.id";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@folderPrefix", prefix + "%");
                using (var r = cmd.ExecuteReader())
                {
                    var ordinals = GetOrdinals(r);
                    while (r.Read())
                    {
                        files.Add(MapFileEntry(r, ordinals));
                    }
                }
            }
            return files;
        }

        public List<FileEntry> GetFilesDirectlyInFolder(string folderPath)
        {
            ThrowIfDisposed();
            var files = new List<FileEntry>();
            var normalizedFolder = folderPath.TrimEnd('\\');
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, char(0)) as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted = 0 AND f.parent_path = @folder
                GROUP BY f.id";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@folder", normalizedFolder);
                using (var r = cmd.ExecuteReader())
                {
                    var ordinals = GetOrdinals(r);
                    while (r.Read())
                    {
                        files.Add(MapFileEntry(r, ordinals));
                    }
                }
            }
            return files;
        }

        private readonly Dictionary<string, string> _extensionCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string InternExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return ext;
            if (_extensionCache.TryGetValue(ext, out var cached)) return cached;
            var lower = ext.ToLower();
            _extensionCache[lower] = lower;
            return lower;
        }

        private struct ColumnOrdinals
        {
            public int Id, Path, Name, Extension, Size, CreatedAt, ModifiedAt, Deleted, LastIndexedAt, Note, TagsText;
        }

        private ColumnOrdinals GetOrdinals(SQLiteDataReader r)
        {
            return new ColumnOrdinals
            {
                Id = r.GetOrdinal("id"),
                Path = r.GetOrdinal("path"),
                Name = r.GetOrdinal("name"),
                Extension = r.GetOrdinal("extension"),
                Size = r.GetOrdinal("size"),
                CreatedAt = r.GetOrdinal("created_at"),
                ModifiedAt = r.GetOrdinal("modified_at"),
                Deleted = r.GetOrdinal("deleted"),
                LastIndexedAt = r.GetOrdinal("last_indexed_at"),
                Note = r.GetOrdinal("note"),
                TagsText = r.GetOrdinal("tags_text")
            };
        }

        private FileEntry MapFileEntry(SQLiteDataReader r, ColumnOrdinals o)
        {
            var f = new FileEntry
            {
                Id = r.GetInt32(o.Id),
                Path = r.GetString(o.Path),
                Name = r.GetString(o.Name),
                Extension = r.IsDBNull(o.Extension) ? "" : InternExtension(r.GetString(o.Extension)),
                Size = r.IsDBNull(o.Size) ? 0 : r.GetInt64(o.Size),
                CreatedAt = r.IsDBNull(o.CreatedAt) ? DateTime.MinValue : (DateTime.TryParse(r.GetString(o.CreatedAt), out var c) ? c : DateTime.MinValue),
                ModifiedAt = r.IsDBNull(o.ModifiedAt) ? DateTime.MinValue : (DateTime.TryParse(r.GetString(o.ModifiedAt), out var m) ? m : DateTime.MinValue),
                Deleted = r.GetInt32(o.Deleted) == 1,
                LastIndexedAt = r.IsDBNull(o.LastIndexedAt) ? DateTime.MinValue : (DateTime.TryParse(r.GetString(o.LastIndexedAt), out var l) ? l : DateTime.MinValue),
                Note = r.IsDBNull(o.Note) ? "" : r.GetString(o.Note)
            };
            var rawTags = r.IsDBNull(o.TagsText) ? "" : r.GetString(o.TagsText);
            if (!string.IsNullOrEmpty(rawTags))
            {
                f.Tags = new List<string>(rawTags.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)));
            }
            f.TagsText = f.Tags != null ? string.Join(", ", f.Tags) : "";
            return f;
        }

        private FileEntry MapFileEntry(SQLiteDataReader r)
        {
            return MapFileEntry(r, GetOrdinals(r));
        }

        public void SetTagsForFile(int fileId, List<string> tags, SQLiteTransaction transaction = null)
        {
            ThrowIfDisposed();
            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"SetTagsForFile: fileId={fileId} tags=[{string.Join(", ", tags)}]");
            var ownTransaction = transaction == null;
            var tx = transaction ?? _connection.BeginTransaction();
            try
            {
                using (var cmd = new SQLiteCommand("DELETE FROM file_tags WHERE file_id=@id", _connection, tx))
                {
                    cmd.Parameters.AddWithValue("@id", fileId);
                    cmd.ExecuteNonQuery();
                }
                foreach (var tag in tags)
                {
                    if (string.IsNullOrWhiteSpace(tag)) continue;
                    var tagId = GetOrCreateTagInTransaction(tag, tx);
                    using (var cmd = new SQLiteCommand("INSERT OR IGNORE INTO file_tags (file_id,tag_id) VALUES (@f,@t)", _connection, tx))
                    {
                        cmd.Parameters.AddWithValue("@f", fileId);
                        cmd.Parameters.AddWithValue("@t", tagId);
                        cmd.ExecuteNonQuery();
                    }
                }
                if (ownTransaction)
                    tx.Commit();
            }
            catch
            {
                if (ownTransaction)
                    tx.Dispose();
                throw;
            }
        }

        private int GetOrCreateTagInTransaction(string name, SQLiteTransaction transaction)
        {
            name = name.Trim().ToLower();
            using (var cmd = new SQLiteCommand("INSERT OR IGNORE INTO tags (name) VALUES (@n); SELECT id FROM tags WHERE name=@n;", _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@n", name);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public void SetTagsForFiles(IReadOnlyCollection<long> fileIds, List<string> tags)
        {
            ThrowIfDisposed();
            if (fileIds == null || fileIds.Count == 0) return;

            using (var transaction = _connection.BeginTransaction())
            {
                try
                {
                    // Resolve tag IDs once
                    var tagIds = new List<int>();
                    foreach (var tag in tags)
                    {
                        if (string.IsNullOrWhiteSpace(tag)) continue;
                        tagIds.Add(GetOrCreateTagInTransaction(tag, transaction));
                    }

                    // Delete existing tags in chunks (SQLite variable limit)
                    const int chunkSize = 900;
                    var idList = fileIds.ToList();
                    for (int i = 0; i < idList.Count; i += chunkSize)
                    {
                        var chunk = idList.Skip(i).Take(chunkSize).ToList();
                        var paramNames = string.Join(",", chunk.Select((_, j) => $"@id{j}"));
                        using (var cmd = new SQLiteCommand($"DELETE FROM file_tags WHERE file_id IN ({paramNames})", _connection, transaction))
                        {
                            for (int j = 0; j < chunk.Count; j++)
                                cmd.Parameters.AddWithValue($"@id{j}", chunk[j]);
                            cmd.ExecuteNonQuery();
                        }
                    }

                    // Bulk insert new tags
                    using (var insertCmd = new SQLiteCommand("INSERT OR IGNORE INTO file_tags (file_id,tag_id) VALUES (@f,@t)", _connection, transaction))
                    {
                        insertCmd.Parameters.Add("@f", System.Data.DbType.Int64);
                        insertCmd.Parameters.Add("@t", System.Data.DbType.Int32);
                        foreach (var fileId in fileIds)
                        {
                            foreach (var tagId in tagIds)
                            {
                                insertCmd.Parameters["@f"].Value = fileId;
                                insertCmd.Parameters["@t"].Value = tagId;
                                insertCmd.ExecuteNonQuery();
                            }
                        }
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

        public void SetNoteForFile(int fileId, string note, SQLiteTransaction transaction = null)
        {
            ThrowIfDisposed();
            using (var cmd = new SQLiteCommand("INSERT INTO notes (file_id,note) VALUES (@id,@n) ON CONFLICT(file_id) DO UPDATE SET note=@n", _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.Parameters.AddWithValue("@n", note ?? "");
                cmd.ExecuteNonQuery();
            }
            if (_ftsAvailable)
            {
                string name = null;
                using (var nameCmd = new SQLiteCommand("SELECT name FROM files WHERE id=@id", _connection, transaction))
                {
                    nameCmd.Parameters.AddWithValue("@id", fileId);
                    name = nameCmd.ExecuteScalar() as string;
                }
                FtsUpsert(fileId, name ?? "", note, transaction);
            }
        }

        public void MarkDeletedFiles(HashSet<string> existingPaths, string scopePath = null, HashSet<string> preservedPaths = null)
        {
            ThrowIfDisposed();
            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"MarkDeletedFiles: scope={scopePath ?? "(all)"}, existingPaths.Count={existingPaths.Count}");

            using (var transaction = _connection.BeginTransaction())
            {
                // Create temp table with scanned paths
                using (var cmd = new SQLiteCommand("CREATE TEMP TABLE IF NOT EXISTS scanned_paths (path TEXT PRIMARY KEY)", _connection, transaction))
                    cmd.ExecuteNonQuery();
                using (var cmd = new SQLiteCommand("DELETE FROM scanned_paths", _connection, transaction))
                    cmd.ExecuteNonQuery();

                // Batch insert existing paths (SQLite variable limit: 999)
                const int chunkSize = 900;
                var pathList = existingPaths.ToList();
                for (int i = 0; i < pathList.Count; i += chunkSize)
                {
                    var chunk = pathList.Skip(i).Take(chunkSize).ToList();
                    var paramNames = string.Join(",", chunk.Select((_, j) => $"(@p{j})"));
                    using (var cmd = new SQLiteCommand($"INSERT OR IGNORE INTO scanned_paths (path) VALUES {paramNames}", _connection, transaction))
                    {
                        for (int j = 0; j < chunk.Count; j++)
                            cmd.Parameters.AddWithValue($"@p{j}", chunk[j]);
                        cmd.ExecuteNonQuery();
                    }
                }

                // Build WHERE clause for scope and preserved paths
                var whereClauses = new List<string> { "f.deleted=0" };
                var cmdParams = new List<SQLiteParameter>();

                if (scopePath != null)
                {
                    var scopePrefix = scopePath.EndsWith("\\") ? scopePath : scopePath + "\\";
                    whereClauses.Add("f.path LIKE @scopePrefix");
                    cmdParams.Add(new SQLiteParameter("@scopePrefix", scopePrefix + "%"));
                }

                if (preservedPaths != null && preservedPaths.Count > 0)
                {
                    var preservedClauses = new List<string>();
                    int idx = 0;
                    foreach (var pp in preservedPaths)
                    {
                        var ppPrefix = pp.EndsWith("\\") ? pp : pp + "\\";
                        preservedClauses.Add($"f.path NOT LIKE @preserved{idx}");
                        cmdParams.Add(new SQLiteParameter($"@preserved{idx}", ppPrefix + "%"));
                        idx++;
                    }
                    if (preservedClauses.Count > 0)
                        whereClauses.Add($"({string.Join(" AND ", preservedClauses)})");
                }

                var whereSql = string.Join(" AND ", whereClauses);
                var updateSql = $@"
                    UPDATE files SET deleted=1
                    WHERE id IN (
                        SELECT f.id FROM files f
                        LEFT JOIN scanned_paths s ON f.path = s.path
                        WHERE {whereSql} AND s.path IS NULL
                    )";

                int markedCount;
                using (var cmd = new SQLiteCommand(updateSql, _connection, transaction))
                {
                    foreach (var p in cmdParams)
                        cmd.Parameters.Add(p);
                    markedCount = cmd.ExecuteNonQuery();
                }

                if (_ftsAvailable && markedCount > 0)
                {
                    try
                    {
                        var ftsWhere = whereSql.Replace("f.deleted=0", "f.deleted=1");
                        using (var cmd = new SQLiteCommand(
                            $@"DELETE FROM files_fts WHERE rowid IN (
                                SELECT f.id FROM files f
                                LEFT JOIN scanned_paths s ON f.path = s.path
                                WHERE {ftsWhere} AND s.path IS NULL)", _connection, transaction))
                        {
                            foreach (var p in cmdParams)
                                cmd.Parameters.Add(new SQLiteParameter(p.ParameterName, p.Value));
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch { _ftsAvailable = false; }
                }

                using (var cmd = new SQLiteCommand("DROP TABLE IF EXISTS scanned_paths", _connection, transaction))
                    cmd.ExecuteNonQuery();

                transaction.Commit();

                if (LoggingService.IsDebugMode) LoggingService.LogDebug($"MarkDeletedFiles: marked {markedCount} files as deleted");
            }
        }

        public void MarkFileAsDeleted(int fileId)
        {
            ThrowIfDisposed();
            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"MarkFileAsDeleted: fileId={fileId}");
            using (var cmd = new SQLiteCommand("UPDATE files SET deleted=1 WHERE id=@id", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.ExecuteNonQuery();
            }
            FtsDelete(fileId, null);
        }

        public void MarkPathAsDeleted(string folderPath)
        {
            ThrowIfDisposed();
            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"MarkPathAsDeleted: {folderPath}");
            var prefix = folderPath.EndsWith("\\") ? folderPath : folderPath + "\\";
            using (var cmd = new SQLiteCommand("UPDATE files SET deleted=1 WHERE path=@path OR path LIKE @prefix", _connection))
            {
                cmd.Parameters.AddWithValue("@path", folderPath);
                cmd.Parameters.AddWithValue("@prefix", prefix + "%");
                cmd.ExecuteNonQuery();
            }
            if (_ftsAvailable)
            {
                try
                {
                    using (var cmd = new SQLiteCommand(
                        @"DELETE FROM files_fts WHERE rowid IN (SELECT id FROM files WHERE path=@path OR path LIKE @prefix)", _connection))
                    {
                        cmd.Parameters.AddWithValue("@path", folderPath);
                        cmd.Parameters.AddWithValue("@prefix", prefix + "%");
                        cmd.ExecuteNonQuery();
                    }
                }
                catch { _ftsAvailable = false; }
            }
        }

        public void UpdateFolderPath(string oldPath, string newPath)
        {
            ThrowIfDisposed();
            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"UpdateFolderPath: {oldPath} -> {newPath}");
            var oldPrefix = oldPath.EndsWith("\\") ? oldPath : oldPath + "\\";
            var newPrefix = newPath.EndsWith("\\") ? newPath : newPath + "\\";
            using (var transaction = _connection.BeginTransaction())
            {
                using (var cmd = new SQLiteCommand(
                    @"UPDATE files SET path = CASE WHEN path = @oldPath THEN @newPath ELSE @newPrefix || SUBSTR(path, @oldLen + 1) END,
                      name = CASE WHEN path = @oldPath THEN @newName ELSE name END
                      WHERE path = @oldPath OR path LIKE @oldPrefix",
                    _connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@oldPath", oldPath);
                    cmd.Parameters.AddWithValue("@newPath", newPath);
                    cmd.Parameters.AddWithValue("@newPrefix", newPrefix);
                    cmd.Parameters.AddWithValue("@oldLen", oldPrefix.Length);
                    cmd.Parameters.AddWithValue("@oldPrefix", oldPrefix + "%");
                    cmd.Parameters.AddWithValue("@newName", Path.GetFileName(newPath));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        public List<FileEntry> SearchFiles(string query, string folderPath, CancellationToken ct = default, bool includePath = true)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(query))
            {
                if (LoggingService.IsDebugMode) LoggingService.LogDebug($"SearchFiles: empty query, returning all files under {folderPath}");
                return GetFilesInFolder(folderPath);
            }

            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"SearchFiles: query=\"{query}\" folderPath={folderPath}");
            var files = new List<FileEntry>();
            var prefix = folderPath.EndsWith("\\") ? folderPath : folderPath + "\\";

            if (_ftsAvailable)
            {
                try
                {
                    return SearchFilesFts(query, folderPath, prefix, ct, includePath);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _ftsAvailable = false;
                    LoggingService.Log($"SearchFiles: FTS failed, falling back to LIKE: {ex.Message}");
                    files = new List<FileEntry>();
                }
            }

            var pathCondition = includePath ? " OR f.path LIKE @q" : "";
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, char(0)) as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted=0 AND f.path LIKE @folderPrefix
                  AND (f.name LIKE @q" + pathCondition + @" OR n.note LIKE @q
                   OR EXISTS (SELECT 1 FROM file_tags ft2 JOIN tags t2 ON ft2.tag_id = t2.id
                              WHERE ft2.file_id = f.id AND t2.name LIKE @qPrefix))
                GROUP BY f.id";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@q", "%" + query + "%");
                cmd.Parameters.AddWithValue("@qPrefix", query + "%");
                cmd.Parameters.AddWithValue("@folderPrefix", prefix + "%");

                using (ct.Register(() => cmd.Cancel()))
                {
                    using (var r = cmd.ExecuteReader())
                    {
                        var ordinals = GetOrdinals(r);
                        while (r.Read())
                        {
                            ct.ThrowIfCancellationRequested();
                            files.Add(MapFileEntry(r, ordinals));
                        }
                    }
                }
            }
            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"SearchFiles: returned {files.Count} results for query=\"{query}\"");
            return files;
        }

        private List<FileEntry> SearchFilesFts(string query, string folderPath, string prefix, CancellationToken ct, bool includePath = true)
        {
            var files = new List<FileEntry>();
            var ftsQuery = includePath ? BuildFtsQuery(query) : BuildFtsQueryNameOnly(query);
            if (string.IsNullOrEmpty(ftsQuery))
                throw new ArgumentException("Empty FTS query");

            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, char(0)) as tags_text
                FROM files f
                JOIN files_fts fts ON fts.rowid = f.id
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted=0 AND f.path LIKE @folderPrefix AND files_fts MATCH @fts
                GROUP BY f.id";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@fts", ftsQuery);
                cmd.Parameters.AddWithValue("@folderPrefix", prefix + "%");

                using (ct.Register(() => cmd.Cancel()))
                {
                    using (var r = cmd.ExecuteReader())
                    {
                        var ordinals = GetOrdinals(r);
                        while (r.Read())
                        {
                            ct.ThrowIfCancellationRequested();
                            files.Add(MapFileEntry(r, ordinals));
                        }
                    }
                }
            }

            // Union with tag prefix matches (FTS only covers name/note)
            HashSet<int> ftsIds = null;
            var tagSql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, char(0)) as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted=0 AND f.path LIKE @folderPrefix
                  AND EXISTS (SELECT 1 FROM file_tags ft2 JOIN tags t2 ON ft2.tag_id = t2.id
                              WHERE ft2.file_id = f.id AND t2.name LIKE @qPrefix)
                GROUP BY f.id";

            using (var cmd = new SQLiteCommand(tagSql, _connection))
            {
                cmd.Parameters.AddWithValue("@qPrefix", query + "%");
                cmd.Parameters.AddWithValue("@folderPrefix", prefix + "%");

                using (ct.Register(() => cmd.Cancel()))
                {
                    using (var r = cmd.ExecuteReader())
                    {
                        var ordinals = GetOrdinals(r);
                        ftsIds = new HashSet<int>(files.Select(f => f.Id));
                        while (r.Read())
                        {
                            ct.ThrowIfCancellationRequested();
                            var entry = MapFileEntry(r, ordinals);
                            if (ftsIds.Add(entry.Id))
                                files.Add(entry);
                        }
                    }
                }
            }

            if (LoggingService.IsDebugMode) LoggingService.LogDebug($"SearchFiles (FTS): returned {files.Count} results for query=\"{query}\"");
            return files;
        }

        private static string BuildFtsQuery(string query)
        {
            var tokens = query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return null;
            var sb = new System.Text.StringBuilder();
            foreach (var token in tokens)
            {
                var sanitized = new string(token.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-').ToArray());
                if (string.IsNullOrEmpty(sanitized)) continue;
                if (sb.Length > 0) sb.Append(" OR ");
                sb.Append('"').Append(sanitized.Replace("\"", "\"\"")).Append("\"*");
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static string BuildFtsQueryNameOnly(string query)
        {
            var tokens = query.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return null;
            var sb = new System.Text.StringBuilder();
            foreach (var token in tokens)
            {
                var sanitized = new string(token.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-').ToArray());
                if (string.IsNullOrEmpty(sanitized)) continue;
                if (sb.Length > 0) sb.Append(" OR ");
                sb.Append("{name} : \"").Append(sanitized.Replace("\"", "\"\"")).Append("\"*");
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        public HashSet<string> GetDirectoriesWithTaggedFiles(string folderPath)
        {
            ThrowIfDisposed();
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var prefix = folderPath.EndsWith("\\") ? folderPath : folderPath + "\\";
            var sql = @"
                SELECT DISTINCT f.path
                FROM files f
                INNER JOIN file_tags ft ON f.id = ft.file_id
                WHERE f.deleted = 0 AND f.path LIKE @folderPrefix";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@folderPrefix", prefix + "%");
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var fullPath = r.GetString(0);
                        if (fullPath.Length <= prefix.Length)
                            continue;
                        var relative = fullPath.Substring(prefix.Length);
                        var slashIdx = relative.IndexOf('\\');
                        if (slashIdx > 0)
                        {
                            result.Add(prefix + relative.Substring(0, slashIdx));
                        }
                    }
                }
            }
            return result;
        }

        public void UpdateFilePathAndName(int fileId, string newPath, string newName)
        {
            ThrowIfDisposed();
            using (var cmd = new SQLiteCommand("UPDATE files SET path=@p,name=@n,extension=@e WHERE id=@id", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.Parameters.AddWithValue("@p", newPath);
                cmd.Parameters.AddWithValue("@n", newName);
                cmd.Parameters.AddWithValue("@e", Path.GetExtension(newName));
                cmd.ExecuteNonQuery();
            }
        }

        public List<string> GetAllTags()
        {
            ThrowIfDisposed();
            var tags = new List<string>();
            using (var cmd = new SQLiteCommand("SELECT name FROM tags ORDER BY name", _connection))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    tags.Add(r.GetString(0));
                }
            }
            return tags;
        }

        public void ClearDatabase()
        {
            ThrowIfDisposed();
            using (var cmd = new SQLiteCommand("DELETE FROM file_tags;DELETE FROM notes;DELETE FROM tags;DELETE FROM files;", _connection))
            {
                cmd.ExecuteNonQuery();
            }
            if (_ftsAvailable)
            {
                try
                {
                    using (var cmd = new SQLiteCommand("DELETE FROM files_fts", _connection))
                        cmd.ExecuteNonQuery();
                }
                catch { _ftsAvailable = false; }
            }
        }

        public void Dispose()
        {
            InvalidateUpsertCommand();
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
    }
}
