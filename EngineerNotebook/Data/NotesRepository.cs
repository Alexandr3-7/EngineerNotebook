using System;
using System.Collections.Generic;
using System.IO;
using EngineerNotebook.Models;
using Microsoft.Data.Sqlite;

namespace EngineerNotebook.Data
{
    public class NotesRepository
    {
        private readonly string _dbPath;

        public NotesRepository(string? dbPath = null)
        {
            _dbPath = dbPath ?? Path.Combine(AppContext.BaseDirectory, "notes.db");
            EnsureDatabase();
        }

        private string ConnectionString => $"Data Source={_dbPath}";

        private SqliteConnection OpenConnection()
        {
            var con = new SqliteConnection(ConnectionString);
            con.Open();
            using var pragma = con.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
            return con;
        }

        private void EnsureDatabase()
        {
            using var con = OpenConnection();

            // Notes
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Notes(
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    Category TEXT,
                    Tags TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS IX_Notes_Title ON Notes(Title);
                """;
                cmd.ExecuteNonQuery();
            }

            // Миграции колонок
            if (!ColumnExists(con, "Notes", "Category"))
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = "ALTER TABLE Notes ADD COLUMN Category TEXT;";
                cmd.ExecuteNonQuery();
            }

            if (!ColumnExists(con, "Notes", "Tags"))
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = "ALTER TABLE Notes ADD COLUMN Tags TEXT;";
                cmd.ExecuteNonQuery();
            }

            // Нормализация
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText =
                """
                UPDATE Notes
                SET Category='Без категории'
                WHERE Category IS NULL OR TRIM(Category)='';

                UPDATE Notes
                SET Tags=''
                WHERE Tags IS NULL;
                """;
                cmd.ExecuteNonQuery();
            }

            // Categories
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Categories(
                    Name TEXT PRIMARY KEY
                );

                INSERT OR IGNORE INTO Categories(Name) VALUES('Без категории');

                INSERT OR IGNORE INTO Categories(Name)
                SELECT DISTINCT Category
                FROM Notes
                WHERE Category IS NOT NULL AND TRIM(Category) <> '';
                """;
                cmd.ExecuteNonQuery();
            }

            // NoteVersions (история изменений)
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS NoteVersions(
                    VersionId INTEGER PRIMARY KEY AUTOINCREMENT,
                    NoteId INTEGER NOT NULL,
                    Title TEXT NOT NULL,
                    Content TEXT NOT NULL,
                    Category TEXT,
                    Tags TEXT,
                    SavedAt TEXT NOT NULL,
                    FOREIGN KEY(NoteId) REFERENCES Notes(Id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS IX_NoteVersions_NoteId_SavedAt
                ON NoteVersions(NoteId, SavedAt DESC);
                """;
                cmd.ExecuteNonQuery();
            }
        }

        private static bool ColumnExists(SqliteConnection con, string table, string column)
        {
            using var cmd = con.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(1);
                if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ------------------ Categories ------------------

        public List<string> GetCategories()
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText =
            """
            SELECT Name
            FROM Categories
            ORDER BY CASE WHEN Name='Без категории' THEN 0 ELSE 1 END, Name;
            """;

            var list = new List<string>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetString(0));

            return list;
        }

        public void AddCategory(string name)
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO Categories(Name) VALUES($n);";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }

        public void DeleteCategory(string name)
        {
            using var con = OpenConnection();
            using var tx = con.BeginTransaction();

            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE Notes SET Category='Без категории' WHERE Category=$n;";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM Categories WHERE Name=$n;";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // ------------------ Notes ------------------

        public Note? GetById(long id)
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText =
            """
            SELECT Id, Title, Content,
                   IFNULL(Category,'Без категории') as Category,
                   IFNULL(Tags,'') as Tags,
                   CreatedAt, UpdatedAt
            FROM Notes
            WHERE Id = $id
            LIMIT 1;
            """;
            cmd.Parameters.AddWithValue("$id", id);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new Note
            {
                Id = r.GetInt64(0),
                Title = r.GetString(1),
                Content = r.GetString(2),
                Category = r.GetString(3),
                Tags = r.GetString(4),
                CreatedAt = DateTime.Parse(r.GetString(5)),
                UpdatedAt = DateTime.Parse(r.GetString(6))
            };
        }

        public List<Note> Search(string? query, string? categoryFilter = null)
        {
            query = (query ?? "").Trim();
            categoryFilter = (categoryFilter ?? "").Trim();

            using var con = OpenConnection();
            using var cmd = con.CreateCommand();

            var where = "WHERE 1=1 ";
            if (query.Length > 0)
            {
                where += "AND (Title LIKE $q OR Content LIKE $q OR Tags LIKE $q) ";
                cmd.Parameters.AddWithValue("$q", $"%{query}%");
            }

            if (categoryFilter.Length > 0 && categoryFilter != "Все")
            {
                where += "AND Category = $cat ";
                cmd.Parameters.AddWithValue("$cat", categoryFilter);
            }

            cmd.CommandText =
            $"""
            SELECT Id, Title, Content,
                   IFNULL(Category, 'Без категории') as Category,
                   IFNULL(Tags, '') as Tags,
                   CreatedAt, UpdatedAt
            FROM Notes
            {where}
            ORDER BY datetime(UpdatedAt) DESC;
            """;

            var list = new List<Note>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Note
                {
                    Id = r.GetInt64(0),
                    Title = r.GetString(1),
                    Content = r.GetString(2),
                    Category = r.GetString(3),
                    Tags = r.GetString(4),
                    CreatedAt = DateTime.Parse(r.GetString(5)),
                    UpdatedAt = DateTime.Parse(r.GetString(6))
                });
            }
            return list;
        }

        public long Insert(Note note)
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText =
            """
            INSERT INTO Notes(Title, Content, Category, Tags, CreatedAt, UpdatedAt)
            VALUES($title, $content, $cat, $tags, $created, $updated);
            SELECT last_insert_rowid();
            """;
            cmd.Parameters.AddWithValue("$title", note.Title);
            cmd.Parameters.AddWithValue("$content", note.Content);
            cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(note.Category) ? "Без категории" : note.Category.Trim());
            cmd.Parameters.AddWithValue("$tags", note.Tags?.Trim() ?? "");
            cmd.Parameters.AddWithValue("$created", note.CreatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("O"));

            return (long)(cmd.ExecuteScalar() ?? 0L);
        }

        /// <summary>Сохраняет текущую версию заметки в NoteVersions.</summary>
        public void SaveCurrentVersion(long noteId)
        {
            using var con = OpenConnection();

            // читаем текущую заметку
            Note? cur;
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText =
                """
                SELECT Id, Title, Content,
                       IFNULL(Category,'Без категории') as Category,
                       IFNULL(Tags,'') as Tags
                FROM Notes
                WHERE Id = $id
                LIMIT 1;
                """;
                cmd.Parameters.AddWithValue("$id", noteId);

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return;

                cur = new Note
                {
                    Id = r.GetInt64(0),
                    Title = r.GetString(1),
                    Content = r.GetString(2),
                    Category = r.GetString(3),
                    Tags = r.GetString(4)
                };
            }

            // записываем версию
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText =
                """
                INSERT INTO NoteVersions(NoteId, Title, Content, Category, Tags, SavedAt)
                VALUES($nid, $t, $c, $cat, $tags, $saved);
                """;
                cmd.Parameters.AddWithValue("$nid", cur!.Id);
                cmd.Parameters.AddWithValue("$t", cur.Title);
                cmd.Parameters.AddWithValue("$c", cur.Content);
                cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(cur.Category) ? "Без категории" : cur.Category.Trim());
                cmd.Parameters.AddWithValue("$tags", cur.Tags?.Trim() ?? "");
                cmd.Parameters.AddWithValue("$saved", DateTime.Now.ToString("O"));
                cmd.ExecuteNonQuery();
            }
        }

        public void Update(Note note)
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText =
            """
            UPDATE Notes
            SET Title = $title,
                Content = $content,
                Category = $cat,
                Tags = $tags,
                UpdatedAt = $updated
            WHERE Id = $id;
            """;
            cmd.Parameters.AddWithValue("$title", note.Title);
            cmd.Parameters.AddWithValue("$content", note.Content);
            cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(note.Category) ? "Без категории" : note.Category.Trim());
            cmd.Parameters.AddWithValue("$tags", note.Tags?.Trim() ?? "");
            cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("O"));
            cmd.Parameters.AddWithValue("$id", note.Id);

            cmd.ExecuteNonQuery();
        }

        public void Delete(long id)
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText = "DELETE FROM Notes WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        // ------------------ Versions ------------------

        public List<NoteVersion> GetVersions(long noteId)
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText =
            """
            SELECT VersionId, NoteId, Title, Content,
                   IFNULL(Category,'Без категории') as Category,
                   IFNULL(Tags,'') as Tags,
                   SavedAt
            FROM NoteVersions
            WHERE NoteId = $nid
            ORDER BY datetime(SavedAt) DESC;
            """;
            cmd.Parameters.AddWithValue("$nid", noteId);

            var list = new List<NoteVersion>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new NoteVersion
                {
                    VersionId = r.GetInt64(0),
                    NoteId = r.GetInt64(1),
                    Title = r.GetString(2),
                    Content = r.GetString(3),
                    Category = r.GetString(4),
                    Tags = r.GetString(5),
                    SavedAt = DateTime.Parse(r.GetString(6))
                });
            }
            return list;
        }

        public NoteVersion? GetVersion(long versionId)
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText =
            """
            SELECT VersionId, NoteId, Title, Content,
                   IFNULL(Category,'Без категории') as Category,
                   IFNULL(Tags,'') as Tags,
                   SavedAt
            FROM NoteVersions
            WHERE VersionId = $vid
            LIMIT 1;
            """;
            cmd.Parameters.AddWithValue("$vid", versionId);

            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;

            return new NoteVersion
            {
                VersionId = r.GetInt64(0),
                NoteId = r.GetInt64(1),
                Title = r.GetString(2),
                Content = r.GetString(3),
                Category = r.GetString(4),
                Tags = r.GetString(5),
                SavedAt = DateTime.Parse(r.GetString(6))
            };
        }

        /// <summary>Восстановить заметку из версии. Перед восстановлением сохраняем текущую версию (чтобы можно было откатить назад).</summary>
        public void RestoreFromVersion(long versionId)
        {
            using var con = OpenConnection();

            // берем версию
            NoteVersion? v;
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText =
                """
                SELECT VersionId, NoteId, Title, Content,
                       IFNULL(Category,'Без категории') as Category,
                       IFNULL(Tags,'') as Tags
                FROM NoteVersions
                WHERE VersionId = $vid
                LIMIT 1;
                """;
                cmd.Parameters.AddWithValue("$vid", versionId);

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return;

                v = new NoteVersion
                {
                    VersionId = r.GetInt64(0),
                    NoteId = r.GetInt64(1),
                    Title = r.GetString(2),
                    Content = r.GetString(3),
                    Category = r.GetString(4),
                    Tags = r.GetString(5),
                    SavedAt = DateTime.Now
                };
            }

            using var tx = con.BeginTransaction();

            // сохраняем текущую версию заметки (перед откатом)
            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                """
                INSERT INTO NoteVersions(NoteId, Title, Content, Category, Tags, SavedAt)
                SELECT Id, Title, Content,
                       IFNULL(Category,'Без категории'),
                       IFNULL(Tags,''),
                       $saved
                FROM Notes
                WHERE Id = $nid
                LIMIT 1;
                """;
                cmd.Parameters.AddWithValue("$nid", v!.NoteId);
                cmd.Parameters.AddWithValue("$saved", DateTime.Now.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            // обновляем Notes из версии
            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                """
                UPDATE Notes
                SET Title = $t,
                    Content = $c,
                    Category = $cat,
                    Tags = $tags,
                    UpdatedAt = $u
                WHERE Id = $nid;
                """;
                cmd.Parameters.AddWithValue("$nid", v!.NoteId);
                cmd.Parameters.AddWithValue("$t", v.Title);
                cmd.Parameters.AddWithValue("$c", v.Content);
                cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(v.Category) ? "Без категории" : v.Category.Trim());
                cmd.Parameters.AddWithValue("$tags", v.Tags?.Trim() ?? "");
                cmd.Parameters.AddWithValue("$u", DateTime.Now.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }
}
