using System;
using System.Collections.Generic;
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
            _connection = new SQLiteConnection(string.Format("Data Source={0};Version=3;", databasePath));
            _connection.Open();
            InitDb();
        }

        private void InitDb()
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS files (id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT UNIQUE NOT NULL, name TEXT NOT NULL, extension TEXT, size INTEGER, created_at TEXT, modified_at TEXT, deleted INTEGER DEFAULT 0, last_indexed_at TEXT);
                CREATE TABLE IF NOT EXISTS tags (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT UNIQUE NOT NULL);
                CREATE TABLE IF NOT EXISTS file_tags (file_id INTEGER, tag_id INTEGER, PRIMARY KEY (file_id, tag_id));
                CREATE TABLE IF NOT EXISTS notes (file_id INTEGER PRIMARY KEY, note TEXT);
            ";
            using (var cmd = new SQLiteCommand(sql, _connection)) { cmd.ExecuteNonQuery(); }
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
                SELECT f.*, n.note, GROUP_CONCAT(t.name, ', ') as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE " + (includeDeleted ? "1=1" : "f.deleted=0") + @"
                GROUP BY f.id";

            using (var cmd = new SQLiteCommand(sql, _connection))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var f = new FileEntry
                    {
                        Id = r.GetInt32(0),
                        Path = r.GetString(1),
                        Name = r.GetString(2),
                        Extension = r.IsDBNull(3) ? "" : r.GetString(3),
                        Size = r.GetInt64(4),
                        CreatedAt = DateTime.Parse(r.GetString(5)),
                        ModifiedAt = DateTime.Parse(r.GetString(6)),
                        Deleted = r.GetInt32(7) == 1,
                        LastIndexedAt = DateTime.Parse(r.GetString(8)),
                        Note = r.IsDBNull(9) ? "" : r.GetString(9),
                        TagsText = r.IsDBNull(10) ? "" : r.GetString(10)
                    };
                    if (!string.IsNullOrEmpty(f.TagsText))
                    {
                        f.Tags = new List<string>(f.TagsText.Split(new[] { ", " }, StringSplitOptions.None));
                    }
                    else
                    {
                        f.Tags = new List<string>();
                    }
                    files.Add(f);
                }
            }
            return files;
        }

        public List<FileEntry> SearchFiles(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return GetAllFiles();
            var files = new List<FileEntry>();
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, ', ') as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                LEFT JOIN file_tags ft ON f.id = ft.file_id
                LEFT JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted=0 
                GROUP BY f.id
                HAVING f.name LIKE @q OR f.path LIKE @q OR tags_text LIKE @q OR n.note LIKE @q";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@q", "%" + query + "%");
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var f = new FileEntry
                        {
                            Id = r.GetInt32(0),
                            Path = r.GetString(1),
                            Name = r.GetString(2),
                            Extension = r.IsDBNull(3) ? "" : r.GetString(3),
                            Size = r.GetInt64(4),
                            CreatedAt = DateTime.Parse(r.GetString(5)),
                            ModifiedAt = DateTime.Parse(r.GetString(6)),
                            Deleted = r.GetInt32(7) == 1,
                            LastIndexedAt = DateTime.Parse(r.GetString(8)),
                            Note = r.IsDBNull(9) ? "" : r.GetString(9),
                            TagsText = r.IsDBNull(10) ? "" : r.GetString(10)
                        };
                        if (!string.IsNullOrEmpty(f.TagsText))
                        {
                            f.Tags = new List<string>(f.TagsText.Split(new[] { ", " }, StringSplitOptions.None));
                        }
                        else
                        {
                            f.Tags = new List<string>();
                        }
                        files.Add(f);
                    }
                }
            }
            return files;
        }

        public List<FileEntry> SearchByTag(string tagName)
        {
            var files = new List<FileEntry>();
            var sql = @"
                SELECT f.*, n.note, GROUP_CONCAT(t.name, ', ') as tags_text
                FROM files f
                LEFT JOIN notes n ON f.id = n.file_id
                INNER JOIN file_tags ft ON f.id = ft.file_id
                INNER JOIN tags t ON ft.tag_id = t.id
                WHERE f.deleted = 0 AND t.name LIKE @t
                GROUP BY f.id";

            using (var cmd = new SQLiteCommand(sql, _connection))
            {
                cmd.Parameters.AddWithValue("@t", "%" + tagName + "%");
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var f = new FileEntry
                        {
                            Id = r.GetInt32(0),
                            Path = r.GetString(1),
                            Name = r.GetString(2),
                            Extension = r.IsDBNull(3) ? "" : r.GetString(3),
                            Size = r.GetInt64(4),
                            CreatedAt = DateTime.Parse(r.GetString(5)),
                            ModifiedAt = DateTime.Parse(r.GetString(6)),
                            Deleted = r.GetInt32(7) == 1,
                            LastIndexedAt = DateTime.Parse(r.GetString(8)),
                            Note = r.IsDBNull(9) ? "" : r.GetString(9),
                            TagsText = r.IsDBNull(10) ? "" : r.GetString(10)
                        };
                        if (!string.IsNullOrEmpty(f.TagsText))
                        {
                            f.Tags = new List<string>(f.TagsText.Split(new[] { ", " }, StringSplitOptions.None));
                        }
                        else
                        {
                            f.Tags = new List<string>();
                        }
                        files.Add(f);
                    }
                }
            }
            return files;
        }

        public int GetOrCreateTag(string name)
        {
            name = name.Trim().ToLower();
            using (var cmd = new SQLiteCommand("SELECT id FROM tags WHERE name=@n", _connection))
            {
                cmd.Parameters.AddWithValue("@n", name);
                var r = cmd.ExecuteScalar();
                if (r != null) return Convert.ToInt32(r);
            }
            using (var cmd = new SQLiteCommand("INSERT INTO tags (name) VALUES (@n); SELECT last_insert_rowid();", _connection))
            {
                cmd.Parameters.AddWithValue("@n", name);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public void SetTagsForFile(int fileId, List<string> tags)
        {
            using (var cmd = new SQLiteCommand("DELETE FROM file_tags WHERE file_id=@id", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.ExecuteNonQuery();
            }
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                var tagId = GetOrCreateTag(tag);
                using (var cmd = new SQLiteCommand("INSERT OR IGNORE INTO file_tags (file_id,tag_id) VALUES (@f,@t)", _connection))
                {
                    cmd.Parameters.AddWithValue("@f", fileId);
                    cmd.Parameters.AddWithValue("@t", tagId);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public List<string> GetTagsForFile(int fileId)
        {
            var tags = new List<string>();
            using (var cmd = new SQLiteCommand("SELECT t.name FROM tags t INNER JOIN file_tags ft ON t.id=ft.tag_id WHERE ft.file_id=@id", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) tags.Add(r.GetString(0));
                }
            }
            return tags;
        }

        public List<Tag> GetAllTags()
        {
            var tags = new List<Tag>();
            using (var cmd = new SQLiteCommand("SELECT id,name FROM tags", _connection))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) tags.Add(new Tag { Id = r.GetInt32(0), Name = r.GetString(1) });
            }
            return tags;
        }

        public void SetNoteForFile(int fileId, string note)
        {
            using (var cmd = new SQLiteCommand("INSERT INTO notes (file_id,note) VALUES (@id,@n) ON CONFLICT(file_id) DO UPDATE SET note=@n", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.Parameters.AddWithValue("@n", note ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        public string GetNoteForFile(int fileId)
        {
            using (var cmd = new SQLiteCommand("SELECT note FROM notes WHERE file_id=@id", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                var r = cmd.ExecuteScalar();
                return r != null ? r.ToString() : "";
            }
        }

        public void MarkDeletedFiles(HashSet<string> existingPaths)
        {
            using (var transaction = _connection.BeginTransaction())
            {
                var sql = "SELECT id, path FROM files WHERE deleted=0";
                var toDelete = new List<int>();

                using (var cmd = new SQLiteCommand(sql, _connection, transaction))
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var id = r.GetInt32(0);
                        var path = r.GetString(1);
                        if (!existingPaths.Contains(path))
                        {
                            toDelete.Add(id);
                        }
                    }
                }

                if (toDelete.Count > 0)
                {
                    using (var cmd = new SQLiteCommand("UPDATE files SET deleted=1 WHERE id=@id", _connection, transaction))
                    {
                        var param = cmd.Parameters.Add("@id", System.Data.DbType.Int32);
                        foreach (var id in toDelete)
                        {
                            param.Value = id;
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                transaction.Commit();
            }
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
            using (var cmd = new SQLiteCommand("UPDATE files SET deleted=1 WHERE id=@id", _connection))
            {
                cmd.Parameters.AddWithValue("@id", fileId);
                cmd.ExecuteNonQuery();
            }
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
        }
    }
}
