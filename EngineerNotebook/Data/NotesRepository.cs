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
                    UpdatedAt TEXT NOT NULL,
                    CurrentVersionId INTEGER
                );

                CREATE INDEX IF NOT EXISTS IX_Notes_Title ON Notes(Title);
                """;
                cmd.ExecuteNonQuery();
            }

            // миграции колонок (на всякий)
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
            if (!ColumnExists(con, "Notes", "CurrentVersionId"))
            {
                using var cmd = con.CreateCommand();
                cmd.CommandText = "ALTER TABLE Notes ADD COLUMN CurrentVersionId INTEGER;";
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
                SELECT DISTINCT IFNULL(Category,'Без категории')
                FROM Notes
                WHERE IFNULL(TRIM(Category),'') <> '';
                """;
                cmd.ExecuteNonQuery();
            }

            // NoteVersions
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

            // ✅ миграция существующих данных:
            // 1) нормализуем Notes
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

            // 2) для каждой заметки гарантируем CurrentVersionId:
            //    - если есть версии → ставим последнюю
            //    - если нет версий → создаём версию из Notes и ставим её
            var noteIds = new List<long>();
            using (var cmd = con.CreateCommand())
            {
                cmd.CommandText = "SELECT Id FROM Notes;";
                using var r = cmd.ExecuteReader();
                while (r.Read()) noteIds.Add(r.GetInt64(0));
            }

            foreach (var nid in noteIds)
            {
                long? lastVid = null;

                using (var cmd = con.CreateCommand())
                {
                    cmd.CommandText =
                    """
                    SELECT VersionId
                    FROM NoteVersions
                    WHERE NoteId = $nid
                    ORDER BY datetime(SavedAt) DESC, VersionId DESC
                    LIMIT 1;
                    """;
                    cmd.Parameters.AddWithValue("$nid", nid);
                    var obj = cmd.ExecuteScalar();
                    if (obj != null && obj != DBNull.Value)
                        lastVid = Convert.ToInt64(obj);
                }

                if (lastVid == null)
                {
                    // создаём initial-версию из Notes
                    using var tx = con.BeginTransaction();

                    long newVid;
                    using (var cmd = con.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText =
                        """
                        INSERT INTO NoteVersions(NoteId, Title, Content, Category, Tags, SavedAt)
                        SELECT Id,
                               Title,
                               Content,
                               IFNULL(Category,'Без категории'),
                               IFNULL(Tags,''),
                               $saved
                        FROM Notes
                        WHERE Id = $nid
                        LIMIT 1;

                        SELECT last_insert_rowid();
                        """;
                        cmd.Parameters.AddWithValue("$nid", nid);
                        cmd.Parameters.AddWithValue("$saved", DateTime.Now.ToString("O"));
                        newVid = Convert.ToInt64(cmd.ExecuteScalar());
                    }

                    using (var cmd = con.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "UPDATE Notes SET CurrentVersionId=$vid WHERE Id=$nid;";
                        cmd.Parameters.AddWithValue("$vid", newVid);
                        cmd.Parameters.AddWithValue("$nid", nid);
                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }
                else
                {
                    // если CurrentVersionId пустой — проставим
                    using var cmd = con.CreateCommand();
                    cmd.CommandText =
                    """
                    UPDATE Notes
                    SET CurrentVersionId = COALESCE(CurrentVersionId, $vid)
                    WHERE Id = $nid;
                    """;
                    cmd.Parameters.AddWithValue("$vid", lastVid.Value);
                    cmd.Parameters.AddWithValue("$nid", nid);
                    cmd.ExecuteNonQuery();
                }
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
                   CreatedAt, UpdatedAt,
                   IFNULL(CurrentVersionId,0) as CurrentVersionId
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
                UpdatedAt = DateTime.Parse(r.GetString(6)),
                CurrentVersionId = r.GetInt64(7)
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
                   CreatedAt, UpdatedAt,
                   IFNULL(CurrentVersionId,0) as CurrentVersionId
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
                    UpdatedAt = DateTime.Parse(r.GetString(6)),
                    CurrentVersionId = r.GetInt64(7)
                });
            }
            return list;
        }

        /// <summary>
        /// ✅ Добавление: создаём заметку + сразу создаём initial-версию и делаем её текущей.
        /// </summary>
        public long Insert(Note note)
        {
            using var con = OpenConnection();
            using var tx = con.BeginTransaction();

            long noteId;
            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                """
                INSERT INTO Notes(Title, Content, Category, Tags, CreatedAt, UpdatedAt, CurrentVersionId)
                VALUES($title, $content, $cat, $tags, $created, $updated, 0);
                SELECT last_insert_rowid();
                """;
                cmd.Parameters.AddWithValue("$title", note.Title);
                cmd.Parameters.AddWithValue("$content", note.Content);
                cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(note.Category) ? "Без категории" : note.Category.Trim());
                cmd.Parameters.AddWithValue("$tags", note.Tags?.Trim() ?? "");
                cmd.Parameters.AddWithValue("$created", note.CreatedAt.ToString("O"));
                cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("O"));

                noteId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            long versionId;
            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                """
                INSERT INTO NoteVersions(NoteId, Title, Content, Category, Tags, SavedAt)
                VALUES($nid, $t, $c, $cat, $tags, $saved);
                SELECT last_insert_rowid();
                """;
                cmd.Parameters.AddWithValue("$nid", noteId);
                cmd.Parameters.AddWithValue("$t", note.Title);
                cmd.Parameters.AddWithValue("$c", note.Content);
                cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(note.Category) ? "Без категории" : note.Category.Trim());
                cmd.Parameters.AddWithValue("$tags", note.Tags?.Trim() ?? "");
                cmd.Parameters.AddWithValue("$saved", DateTime.Now.ToString("O"));

                versionId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE Notes SET CurrentVersionId=$vid WHERE Id=$nid;";
                cmd.Parameters.AddWithValue("$vid", versionId);
                cmd.Parameters.AddWithValue("$nid", noteId);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();

            // чистим до 50 (на всякий)
            TrimVersions(noteId, 50);

            return noteId;
        }

        /// <summary>
        /// ✅ Update = создаём новую версию (новые данные) -> делаем её текущей.
        /// </summary>
        public void Update(Note note)
        {
            using var con = OpenConnection();
            using var tx = con.BeginTransaction();

            long versionId;
            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                """
                INSERT INTO NoteVersions(NoteId, Title, Content, Category, Tags, SavedAt)
                VALUES($nid, $t, $c, $cat, $tags, $saved);
                SELECT last_insert_rowid();
                """;
                cmd.Parameters.AddWithValue("$nid", note.Id);
                cmd.Parameters.AddWithValue("$t", note.Title);
                cmd.Parameters.AddWithValue("$c", note.Content);
                cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(note.Category) ? "Без категории" : note.Category.Trim());
                cmd.Parameters.AddWithValue("$tags", note.Tags?.Trim() ?? "");
                cmd.Parameters.AddWithValue("$saved", DateTime.Now.ToString("O"));

                versionId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            using (var cmd = con.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                """
                UPDATE Notes
                SET Title = $title,
                    Content = $content,
                    Category = $cat,
                    Tags = $tags,
                    UpdatedAt = $updated,
                    CurrentVersionId = $vid
                WHERE Id = $id;
                """;
                cmd.Parameters.AddWithValue("$title", note.Title);
                cmd.Parameters.AddWithValue("$content", note.Content);
                cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(note.Category) ? "Без категории" : note.Category.Trim());
                cmd.Parameters.AddWithValue("$tags", note.Tags?.Trim() ?? "");
                cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("O"));
                cmd.Parameters.AddWithValue("$vid", versionId);
                cmd.Parameters.AddWithValue("$id", note.Id);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();

            // ✅ лимит 50 версий
            TrimVersions(note.Id, 50);
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
            ORDER BY datetime(SavedAt) DESC, VersionId DESC;
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

        /// <summary>
        /// ✅ Восстановление = просто делаем выбранную версию текущей (без создания новой версии).
        /// </summary>
        public void RestoreFromVersion(long versionId)
        {
            using var con = OpenConnection();

            // берём версию
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
                    UpdatedAt = $u,
                    CurrentVersionId = $vid
                WHERE Id = $nid;
                """;
                cmd.Parameters.AddWithValue("$nid", v!.NoteId);
                cmd.Parameters.AddWithValue("$vid", v.VersionId);
                cmd.Parameters.AddWithValue("$t", v.Title);
                cmd.Parameters.AddWithValue("$c", v.Content);
                cmd.Parameters.AddWithValue("$cat", string.IsNullOrWhiteSpace(v.Category) ? "Без категории" : v.Category.Trim());
                cmd.Parameters.AddWithValue("$tags", v.Tags?.Trim() ?? "");
                cmd.Parameters.AddWithValue("$u", DateTime.Now.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();

            // после восстановления тоже можно подчистить, но текущую не трогаем
            TrimVersions(v!.NoteId, 50);
        }

        /// <summary>
        /// ✅ Оставляем последние N версий + всегда сохраняем текущую, даже если она старая.
        /// </summary>
        private void TrimVersions(long noteId, int keep)
        {
            using var con = OpenConnection();

            using var cmd = con.CreateCommand();
            cmd.CommandText =
            $"""
            DELETE FROM NoteVersions
            WHERE NoteId = $nid
              AND VersionId NOT IN (
                    SELECT VersionId FROM (
                        SELECT VersionId
                        FROM NoteVersions
                        WHERE NoteId = $nid
                        ORDER BY datetime(SavedAt) DESC, VersionId DESC
                        LIMIT {keep}
                    )
                    UNION
                    SELECT IFNULL(CurrentVersionId, 0)
                    FROM Notes
                    WHERE Id = $nid
              );
            """;
            cmd.Parameters.AddWithValue("$nid", noteId);
            cmd.ExecuteNonQuery();
        }
    }
}
