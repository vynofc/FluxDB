using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.SQLite;
using System.IO;
using FluxDB.Models;

namespace FluxDB.Services
{
    public class DatabaseService : IDisposable
    {
        private SQLiteConnection _connection;

        public DatabaseService(string databasePath)
        {
            var dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) 
                Directory.CreateDirectory(dir);
            LoggingService.LogDebug($"DatabaseService: opening DB at {databasePath}");
            _connection = new SQLiteConnection(string.Format("Data Source={0};Version=3;", databasePath));
            _connection.Open();
            using (var pragmaCmd = new SQLiteCommand("PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-8000;", _connection))
            {
                pragmaCmd.ExecuteNonQuery();
            }

            // Clean up orphaned WAL/SHM files from previous WAL mode
            try
            {
                var walPath = databasePath + "-wal";
                var shmPath = databasePath + "-shm";
                if (File.Exists(walPath)) File.Delete(walPath);
                if (File.Exists(shmPath)) File.Delete(shmPath);
            }
            catch { /* best effort */ }

            InitDb();
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
            ";
            using (var transaction = _connection.BeginTransaction())
            {
                using (var cmd = new SQLiteCommand(sql, _connection, transaction))
                {
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        public SQLiteTransaction BeginTransaction()
        {
            return _connection.BeginTransaction();
        }

        public int UpsertFile(FileEntry f, SQLiteTransaction transaction = null)
        {
            var sql = "INSERT INTO files (path,name,extension,size,created_at,modified_at,deleted,last_indexed_at) VALUES (@p,@n,@e,@s,@c,@m,@d,@l) ON CONFLICT(path) DO UPDATE SET name=@n,extension=@e,size=@s,modified_at=@m,deleted=@d,last_indexed_at=@l RETURNING id";
            using (var cmd = new SQLiteCommand(sql, _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@p", f.Path);
                cmd.Parameters.AddWithValue("@n", f.Name);
                cmd.Parameters.AddWithValue("@e", f.Extension ?? "");
                cmd.Parameters.AddWithValue("@s", f.Size);
                cmd.Parameters.AddWithValue("@c", f.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@m", f.ModifiedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@d", f.Deleted ? 1 : 0);
                cmd.Parameters.AddWithValue("@l", f.LastIndexedAt.ToString("o"));
                var r = cmd.ExecuteScalar();
                return r != null ? Convert.ToInt32(r) : 0;
            }
        }

        public List<FileEntry> GetAllFiles(bool includeDeleted = false)
        {
            var files = new List<FileEntry>();
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, '\0') as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE (f.deleted = 0 OR @includeDeleted = 1)
                GROUP BY f.id";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@includeDeleted", includeDeleted ? 1 : 0);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        files.Add(MapFileEntry(r));
                    }
                }
            }
            return files;
        }

        public List<FileEntry> GetFilesInFolder(string folderPath)
        {
            var files = new List<FileEntry>();
            var prefix = folderPath.EndsWith("\\") ? folderPath : folderPath + "\\";
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, '\0') as tags_text
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
                    while (r.Read())
                    {
                        files.Add(MapFileEntry(r));
                    }
                }
            }
            return files;
        }

        private FileEntry MapFileEntry(SQLiteDataReader r)
        {
            var f = new FileEntry
            {
                Id = r.GetInt32(0),
                Path = r.GetString(1),
                Name = r.GetString(2),
                Extension = r.IsDBNull(3) ? "" : r.GetString(3),
                Size = r.IsDBNull(4) ? 0 : r.GetInt64(4),
                CreatedAt = r.IsDBNull(5) ? DateTime.MinValue : (DateTime.TryParse(r.GetString(5), out var c) ? c : DateTime.MinValue),
                ModifiedAt = r.IsDBNull(6) ? DateTime.MinValue : (DateTime.TryParse(r.GetString(6), out var m) ? m : DateTime.MinValue),
                Deleted = r.GetInt32(7) == 1,
                LastIndexedAt = r.IsDBNull(8) ? DateTime.MinValue : (DateTime.TryParse(r.GetString(8), out var l) ? l : DateTime.MinValue),
                Note = r.IsDBNull(9) ? "" : r.GetString(9),
                TagsText = r.IsDBNull(10) ? "" : r.GetString(10)
            };
            if (!string.IsNullOrEmpty(f.TagsText))
            {
                f.Tags = new List<string>(f.TagsText.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries));
            }
            else
            {
                f.Tags = new List<string>();
            }
            return f;
        }

        public void SetTagsForFile(int fileId, List<string> tags, SQLiteTransaction transaction = null)
        {
            LoggingService.LogDebug($"SetTagsForFile: fileId={fileId} tags=[{string.Join(", ", tags)}]");
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

        public void SetNoteForFile(int fileId, string note, SQLiteTransaction transaction = null)
        {
            using (var cmd = new SQLiteCommand("INSERT INTO notes (file_id,note) VALUES (@id,@n) ON CONFLICT(file_id) DO UPDATE SET note=@n", _connection, transaction))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.Parameters.AddWithValue("@n", note ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public void MarkDeletedFiles(HashSet<string> existingPaths, string scopePath = null)
        {
            var toDelete = new List<int>();
            LoggingService.LogDebug($"MarkDeletedFiles: scope={scopePath ?? "(all)"}, existingPaths.Count={existingPaths.Count}");

            using (var transaction = _connection.BeginTransaction())
            {
                var sql = "SELECT id, path FROM files WHERE deleted=0";

                using (var cmd = new SQLiteCommand(sql, _connection, transaction))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var id = r.GetInt32(0);
                        var path = r.GetString(1);
                        if (scopePath != null && !path.StartsWith(scopePath.EndsWith("\\") ? scopePath : scopePath + "\\", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!existingPaths.Contains(path))
                        {
                            toDelete.Add(id);
                        }
                    }
                }

                if (toDelete.Count > 0)
                {
                    var ids = string.Join(",", toDelete);
                    using (var cmd = new SQLiteCommand($"UPDATE files SET deleted=1 WHERE id IN ({ids})", _connection, transaction))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
                transaction.Commit();
            }

            LoggingService.LogDebug($"MarkDeletedFiles: marked {toDelete.Count} files as deleted");
        }

        public int GetFileCount()
        {
            using (var cmd = new SQLiteCommand("SELECT COUNT(*) FROM files WHERE deleted=0", _connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public void MarkFileAsDeleted(int fileId)
        {
            LoggingService.LogDebug($"MarkFileAsDeleted: fileId={fileId}");
            using (var cmd = new SQLiteCommand("UPDATE files SET deleted=1 WHERE id=@id", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.ExecuteNonQuery();
            }
        }

        public void MarkPathAsDeleted(string folderPath)
        {
            LoggingService.LogDebug($"MarkPathAsDeleted: {folderPath}");
            var prefix = folderPath.EndsWith("\\") ? folderPath : folderPath + "\\";
            using (var cmd = new SQLiteCommand("UPDATE files SET deleted=1 WHERE path=@path OR path LIKE @prefix", _connection))
            {
                cmd.Parameters.AddWithValue("@path", folderPath);
                cmd.Parameters.AddWithValue("@prefix", prefix + "%");
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateFolderPath(string oldPath, string newPath)
        {
            LoggingService.LogDebug($"UpdateFolderPath: {oldPath} → {newPath}");
            var oldPrefix = oldPath.EndsWith("\\") ? oldPath : oldPath + "\\";
            var newPrefix = newPath.EndsWith("\\") ? newPath : newPath + "\\";
            using (var transaction = _connection.BeginTransaction())
            {
                using (var cmd = new SQLiteCommand(
                    @"UPDATE files SET path = @newPrefix || SUBSTR(path, @oldLen + 1), 
                      name = CASE WHEN path = @oldPath THEN @newName ELSE name END
                      WHERE path = @oldPath OR path LIKE @oldPrefix",
                    _connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@oldPath", oldPath);
                    cmd.Parameters.AddWithValue("@newPrefix", newPrefix);
                    cmd.Parameters.AddWithValue("@oldLen", oldPrefix.Length);
                    cmd.Parameters.AddWithValue("@oldPrefix", oldPrefix + "%");
                    cmd.Parameters.AddWithValue("@newName", Path.GetFileName(newPath));
                    cmd.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }

        public List<FileEntry> SearchFiles(string query, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                LoggingService.LogDebug($"SearchFiles: empty query, returning all files under {folderPath}");
                return GetFilesInFolder(folderPath);
            }

            LoggingService.LogDebug($"SearchFiles: query=\"{query}\" folderPath={folderPath}");
            var files = new List<FileEntry>();
            var prefix = folderPath.EndsWith("\\") ? folderPath : folderPath + "\\";
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, '\0') as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted=0 AND f.path LIKE @folderPrefix
                GROUP BY f.id
                HAVING f.name LIKE @q OR f.path LIKE @q OR tags_text LIKE @q OR n.note LIKE @q";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@q", "%" + query + "%");
                cmd.Parameters.AddWithValue("@folderPrefix", prefix + "%");
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        files.Add(MapFileEntry(r));
                    }
                }
            }
            LoggingService.LogDebug($"SearchFiles: returned {files.Count} results for query=\"{query}\"");
            return files;
        }

        public HashSet<string> GetDirectoriesWithTaggedFiles(string folderPath)
        {
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
            using (var cmd = new SQLiteCommand("UPDATE files SET path=@p,name=@n,extension=@e WHERE id=@id", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.Parameters.AddWithValue("@p", newPath);
                cmd.Parameters.AddWithValue("@n", newName);
                cmd.Parameters.AddWithValue("@e", Path.GetExtension(newName));
                cmd.ExecuteNonQuery();
            }
        }

        public void ClearDatabase()
        {
            using (var cmd = new SQLiteCommand("DELETE FROM file_tags;DELETE FROM notes;DELETE FROM tags;DELETE FROM files;", _connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
    }
}
