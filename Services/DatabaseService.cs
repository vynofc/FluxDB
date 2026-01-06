using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using FluxDB.Models;

namespace FluxDB.Services
{
    /// <summary>
    /// SQLite database service for storing files, tags, and notes
    /// </summary>
    public class DatabaseService : IDisposable
    {
        private readonly string _connectionString;
        private SQLiteConnection _connection;

        public DatabaseService(string databasePath)
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={databasePath};Version=3;";
            _connection = new SQLiteConnection(_connectionString);
            _connection.Open();
            
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            var createTablesScript = @"
                CREATE TABLE IF NOT EXISTS files (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    path TEXT UNIQUE NOT NULL,
                    name TEXT NOT NULL,
                    extension TEXT,
                    size INTEGER,
                    created_at TEXT,
                    modified_at TEXT,
                    deleted INTEGER DEFAULT 0,
                    last_indexed_at TEXT
                );

                CREATE TABLE IF NOT EXISTS tags (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT UNIQUE NOT NULL
                );

                CREATE TABLE IF NOT EXISTS file_tags (
                    file_id INTEGER,
                    tag_id INTEGER,
                    PRIMARY KEY (file_id, tag_id),
                    FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE,
                    FOREIGN KEY (tag_id) REFERENCES tags(id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS notes (
                    file_id INTEGER PRIMARY KEY,
                    note TEXT,
                    FOREIGN KEY (file_id) REFERENCES files(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_files_path ON files(path);
                CREATE INDEX IF NOT EXISTS idx_files_name ON files(name);
                CREATE INDEX IF NOT EXISTS idx_tags_name ON tags(name);
            ";

            using (var cmd = new SQLiteCommand(createTablesScript, _connection))
            {
                cmd.ExecuteNonQuery();
            }

            // Create FTS virtual table if it doesn't exist
            var createFtsScript = @"
                CREATE VIRTUAL TABLE IF NOT EXISTS files_fts USING fts5(
                    name, 
                    path, 
                    tags_text, 
                    note_text,
                    content='',
                    contentless_delete=1
                );
            ";

            try
            {
                using (var cmd = new SQLiteCommand(createFtsScript, _connection))
                {
                    cmd.ExecuteNonQuery();
                }
            }
            catch (SQLiteException)
            {
                // FTS5 might not be available, continue without it
            }
        }

        /// <summary>
        /// Insert or update a file entry
        /// </summary>
        public int UpsertFile(FileEntry file)
        {
            var sql = @"
                INSERT INTO files (path, name, extension, size, created_at, modified_at, deleted, last_indexed_at)
                VALUES (@path, @name, @extension, @size, @created_at, @modified_at, @deleted, @last_indexed_at)
                ON CONFLICT(path) DO UPDATE SET
                    name = @name,
                    extension = @extension,
                    size = @size,
                    created_at = @created_at,
                    modified_at = @modified_at,
                    deleted = @deleted,
                    last_indexed_at = @last_indexed_at
                RETURNING id;
            ";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@path", file.Path);
                cmd.Parameters.AddWithValue("@name", file.Name);
                cmd.Parameters.AddWithValue("@extension", file.Extension ?? "");
                cmd.Parameters.AddWithValue("@size", file.Size);
                cmd.Parameters.AddWithValue("@created_at", file.CreatedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@modified_at", file.ModifiedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@deleted", file.Deleted ? 1 : 0);
                cmd.Parameters.AddWithValue("@last_indexed_at", file.LastIndexedAt.ToString("o"));

                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : GetFileIdByPath(file.Path);
            }
        }

        private int GetFileIdByPath(string path)
        {
            var sql = "SELECT id FROM files WHERE path = @path";
            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@path", path);
                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 0;
            }
        }

        /// <summary>
        /// Get all files
        /// </summary>
        public List<FileEntry> GetAllFiles(bool includeDeleted = false)
        {
            var files = new List<FileEntry>();
            var sql = includeDeleted 
                ? "SELECT * FROM files ORDER BY name" 
                : "SELECT * FROM files WHERE deleted = 0 ORDER BY name";

            using (var cmd = new SQLiteCommand(sql, _connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var file = ReadFileEntry(reader);
                    file.Tags = GetTagsForFile(file.Id);
                    file.TagsText = string.Join(", ", file.Tags);
                    file.Note = GetNoteForFile(file.Id);
                    files.Add(file);
                }
            }

            return files;
        }

        /// <summary>
        /// Search files using full-text search or LIKE
        /// </summary>
        public List<FileEntry> SearchFiles(string query)
        {
            var files = new List<FileEntry>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return GetAllFiles();
            }

            // Try FTS first, fall back to LIKE search
            var sql = @"
                SELECT DISTINCT f.* FROM files f
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                LEFT JOIN notes n ON f.id = n.file_id
                WHERE f.deleted = 0 AND (
                    f.name LIKE @query OR 
                    f.path LIKE @query OR
                    t.name LIKE @query OR
                    n.note LIKE @query
                )
                ORDER BY f.name
            ";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@query", $"%{query}%");
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var file = ReadFileEntry(reader);
                        file.Tags = GetTagsForFile(file.Id);
                        file.TagsText = string.Join(", ", file.Tags);
                        file.Note = GetNoteForFile(file.Id);
                        files.Add(file);
                    }
                }
            }

            return files;
        }

        /// <summary>
        /// Search files by specific tag
        /// </summary>
        public List<FileEntry> SearchByTag(string tagName)
        {
            var files = new List<FileEntry>();
            var sql = @"
                SELECT f.* FROM files f
                INNER JOIN file_tags ft ON f.id = ft.file_id
                INNER JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted = 0 AND t.name LIKE @tag
                ORDER BY f.name
            ";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@tag", $"%{tagName}%");
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var file = ReadFileEntry(reader);
                        file.Tags = GetTagsForFile(file.Id);
                        file.TagsText = string.Join(", ", file.Tags);
                        file.Note = GetNoteForFile(file.Id);
                        files.Add(file);
                    }
                }
            }

            return files;
        }

        private FileEntry ReadFileEntry(SQLiteDataReader reader)
        {
            return new FileEntry
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Path = reader.GetString(reader.GetOrdinal("path")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Extension = reader.IsDBNull(reader.GetOrdinal("extension")) ? "" : reader.GetString(reader.GetOrdinal("extension")),
                Size = reader.GetInt64(reader.GetOrdinal("size")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
                ModifiedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("modified_at"))),
                Deleted = reader.GetInt32(reader.GetOrdinal("deleted")) == 1,
                LastIndexedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("last_indexed_at")))
            };
        }

        /// <summary>
        /// Get or create a tag
        /// </summary>
        public int GetOrCreateTag(string tagName)
        {
            tagName = tagName.Trim().ToLower();
            
            // Check if exists
            var selectSql = "SELECT id FROM tags WHERE name = @name";
            using (var cmd = new SQLiteCommand(selectSql, _connection))
            {
                cmd.Parameters.AddWithValue("@name", tagName);
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    return Convert.ToInt32(result);
                }
            }

            // Create new
            var insertSql = "INSERT INTO tags (name) VALUES (@name); SELECT last_insert_rowid();";
            using (var cmd = new SQLiteCommand(insertSql, _connection))
            {
                cmd.Parameters.AddWithValue("@name", tagName);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// Add tags to a file
        /// </summary>
        public void SetTagsForFile(int fileId, List<string> tags)
        {
            // Remove existing tags
            var deleteSql = "DELETE FROM file_tags WHERE file_id = @file_id";
            using (var cmd = new SQLiteCommand(deleteSql, _connection))
            {
                cmd.Parameters.AddWithValue("@file_id", fileId);
                cmd.ExecuteNonQuery();
            }

            // Add new tags
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                
                var tagId = GetOrCreateTag(tag);
                var insertSql = "INSERT OR IGNORE INTO file_tags (file_id, tag_id) VALUES (@file_id, @tag_id)";
                using (var cmd = new SQLiteCommand(insertSql, _connection))
                {
                    cmd.Parameters.AddWithValue("@file_id", fileId);
                    cmd.Parameters.AddWithValue("@tag_id", tagId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>
        /// Get tags for a file
        /// </summary>
        public List<string> GetTagsForFile(int fileId)
        {
            var tags = new List<string>();
            var sql = @"
                SELECT t.name FROM tags t
                INNER JOIN file_tags ft ON t.id = ft.tag_id
                WHERE ft.file_id = @file_id
                ORDER BY t.name
            ";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@file_id", fileId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tags.Add(reader.GetString(0));
                    }
                }
            }

            return tags;
        }

        /// <summary>
        /// Get all tags
        /// </summary>
        public List<Tag> GetAllTags()
        {
            var tags = new List<Tag>();
            var sql = "SELECT id, name FROM tags ORDER BY name";

            using (var cmd = new SQLiteCommand(sql, _connection))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    tags.Add(new Tag
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1)
                    });
                }
            }

            return tags;
        }

        /// <summary>
        /// Set note for a file
        /// </summary>
        public void SetNoteForFile(int fileId, string note)
        {
            var sql = @"
                INSERT INTO notes (file_id, note) VALUES (@file_id, @note)
                ON CONFLICT(file_id) DO UPDATE SET note = @note
            ";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@file_id", fileId);
                cmd.Parameters.AddWithValue("@note", note ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Get note for a file
        /// </summary>
        public string GetNoteForFile(int fileId)
        {
            var sql = "SELECT note FROM notes WHERE file_id = @file_id";
            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@file_id", fileId);
                var result = cmd.ExecuteScalar();
                return result?.ToString() ?? "";
            }
        }

        /// <summary>
        /// Mark files as deleted that are no longer in the file system
        /// </summary>
        public void MarkDeletedFiles(HashSet<string> existingPaths)
        {
            var allFiles = GetAllFiles(true);
            foreach (var file in allFiles)
            {
                if (!existingPaths.Contains(file.Path))
                {
                    var sql = "UPDATE files SET deleted = 1 WHERE id = @id";
                    using (var cmd = new SQLiteCommand(sql, _connection))
                    {
                        cmd.Parameters.AddWithValue("@id", file.Id);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        /// <summary>
        /// Get file count
        /// </summary>
        public int GetFileCount()
        {
            var sql = "SELECT COUNT(*) FROM files WHERE deleted = 0";
            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        /// <summary>
        /// Clear all data
        /// </summary>
        public void ClearDatabase()
        {
            var sql = @"
                DELETE FROM file_tags;
                DELETE FROM notes;
                DELETE FROM tags;
                DELETE FROM files;
            ";
            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
