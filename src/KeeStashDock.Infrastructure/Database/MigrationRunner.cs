using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Logging;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Infrastructure.Database;

/// <summary>
/// Versioned schema migration framework.
///
/// How it works
/// ------------
/// * The `schema_version` table stores the single current version number.
/// * <see cref="Migrations"/> is an ordered list of (version, script) pairs.
/// * On startup every script with version &gt; current runs inside ONE
///   transaction; the version row is bumped in the same transaction, so a
///   failed migration rolls back atomically and the app refuses to start
///   against a half-migrated database (fail fast &gt; silent corruption).
/// * Adding a future schema change = append one entry here. Never edit an
///   already-shipped script.
/// </summary>
public sealed class MigrationRunner
{
    public const int LatestSupportedVersion = 3;

    private readonly Db _db;
    private readonly FileLogger _log;

    public MigrationRunner(Db db, FileLogger log) { _db = db; _log = log; }

    /// <summary>Ordered migration chain. Index 0 = schema version 1.</summary>
    private static readonly (int Version, string Sql)[] Migrations =
    {
        (1, /* -------- V1: initial schema -------- */ """
            CREATE TABLE categories (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                name       TEXT    NOT NULL,
                icon       TEXT    NOT NULL DEFAULT '',
                color      TEXT    NOT NULL DEFAULT '#FF6C8EBF',
                sort_order INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE snippets (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                title         TEXT    NULL,
                content       TEXT    NOT NULL,
                content_hash  TEXT    NOT NULL,
                type          INTEGER NOT NULL DEFAULT 0,
                source        INTEGER NOT NULL DEFAULT 0,
                category_id   INTEGER NULL REFERENCES categories(id) ON DELETE SET NULL,
                is_pinned     INTEGER NOT NULL DEFAULT 0,
                expire_policy INTEGER NOT NULL DEFAULT 0,
                expire_at     TEXT    NULL,   -- ISO-8601 UTC
                created_at    TEXT    NOT NULL,
                updated_at    TEXT    NOT NULL,
                deleted_at    TEXT    NULL,   -- non-null => recycle bin
                sort_weight   INTEGER NOT NULL DEFAULT 0,
                use_count     INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX idx_snippets_live    ON snippets(deleted_at, is_pinned, updated_at);
            CREATE INDEX idx_snippets_hash    ON snippets(content_hash);
            CREATE INDEX idx_snippets_cat     ON snippets(category_id);

            CREATE TABLE tags (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE
            );
            CREATE TABLE snippet_tags (
                snippet_id INTEGER NOT NULL REFERENCES snippets(id) ON DELETE CASCADE,
                tag_id     INTEGER NOT NULL REFERENCES tags(id)     ON DELETE CASCADE,
                PRIMARY KEY (snippet_id, tag_id)
            );

            -- FTS5 full-text index over title+content, kept in sync by triggers.
            -- Tokenizer note: unicode61 handles space-delimited languages well;
            -- CJK queries are routed to a LIKE fallback by SearchService because
            -- unicode61 cannot segment Chinese/Japanese (documented trade-off).
            CREATE VIRTUAL TABLE snippets_fts USING fts5(
                title, content,
                content='snippets', content_rowid='id',
                tokenize='unicode61'
            );
            CREATE TRIGGER trg_snip_ai AFTER INSERT ON snippets BEGIN
                INSERT INTO snippets_fts(rowid, title, content)
                VALUES (new.id, COALESCE(new.title,''), new.content);
            END;
            CREATE TRIGGER trg_snip_ad AFTER DELETE ON snippets BEGIN
                INSERT INTO snippets_fts(snippets_fts, rowid, title, content)
                VALUES ('delete', old.id, COALESCE(old.title,''), old.content);
            END;
            CREATE TRIGGER trg_snip_au AFTER UPDATE OF title, content ON snippets BEGIN
                INSERT INTO snippets_fts(snippets_fts, rowid, title, content)
                VALUES ('delete', old.id, COALESCE(old.title,''), old.content);
                INSERT INTO snippets_fts(rowid, title, content)
                VALUES (new.id, COALESCE(new.title,''), new.content);
            END;
            """),
        (2, /* -------- V2: atomic use tracking + UTC timestamps -------- */ """
            ALTER TABLE snippets ADD COLUMN last_used_at TEXT NULL;
            """),
        (3, /* -------- V3: normalized unique group names -------- */ """
            ALTER TABLE categories ADD COLUMN name_key TEXT NOT NULL DEFAULT '';
            """),
        // (4, "..."), <- future migrations are appended here, never edited.
    };

    public void Run()
    {
        EnsureVersionIsSupportedBeforeWrite();
        using var conn = _db.Open();

        int userVersion = GetUserVersion(conn);
        if (userVersion > LatestSupportedVersion)
        {
            throw new InvalidOperationException(
                $"Database user_version {userVersion} is newer than supported " +
                $"version {LatestSupportedVersion}.");
        }

        using (var init = conn.CreateCommand())
        {
            init.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);
                INSERT INTO schema_version(version)
                    SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM schema_version);
                """;
            init.ExecuteNonQuery();
        }

        int current = GetVersion(conn);
        if (current > LatestSupportedVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {current} is newer than supported " +
                $"version {LatestSupportedVersion}.");
        }

        foreach (var (version, sql) in Migrations)
        {
            if (version <= current) continue;
            _log.Info($"Migrating database schema {current} -> {version}");
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            if (version == 2)
            {
                NormalizeTimestampColumns(conn, tx);
            }
            if (version == 3)
            {
                NormalizeCategoryNames(conn, tx);
            }

            cmd.CommandText = "UPDATE schema_version SET version = $v";
            cmd.Parameters.AddWithValue("$v", version);
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();
            cmd.CommandText = $"PRAGMA user_version = {version}";
            cmd.ExecuteNonQuery();
            tx.Commit();
            current = version;
        }

        if (GetUserVersion(conn) != current)
        {
            using var command = conn.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {current}";
            command.ExecuteNonQuery();
        }
    }

    private static void NormalizeCategoryNames(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var rows = new List<(long Id, string Name)>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT id, name FROM categories ORDER BY id";
            using var reader = read.ExecuteReader();
            while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        // Reserve existing names so repairing a duplicate never takes the name
        // of a later group. Only names change: IDs and snippet references survive.
        var reserved = rows.Where(row => !string.IsNullOrWhiteSpace(row.Name))
            .Select(row => CategoryName.GetKey(row.Name))
            .ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            bool empty = string.IsNullOrWhiteSpace(row.Name);
            string name = empty ? "Group" : CategoryName.Normalize(row.Name);
            string key = CategoryName.GetKey(name);
            if (used.Contains(key) || (empty && reserved.Contains(key)))
                name = CategoryName.GetUnique(name, reserved);
            key = CategoryName.GetKey(name);
            used.Add(key);
            reserved.Add(key);

            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE categories SET name = $name, name_key = $key WHERE id = $id";
            update.Parameters.AddWithValue("$name", name);
            update.Parameters.AddWithValue("$key", key);
            update.Parameters.AddWithValue("$id", row.Id);
            update.ExecuteNonQuery();
        }

        using var constraints = connection.CreateCommand();
        constraints.Transaction = transaction;
        constraints.CommandText = """
            CREATE UNIQUE INDEX idx_categories_name_key ON categories(name_key);
            CREATE TRIGGER trg_category_name_insert BEFORE INSERT ON categories
            WHEN length(new.name) = 0 OR length(new.name_key) = 0 BEGIN
                SELECT RAISE(ABORT, 'Group name and name key must not be empty.');
            END;
            CREATE TRIGGER trg_category_name_update BEFORE UPDATE OF name, name_key ON categories
            WHEN length(new.name) = 0 OR length(new.name_key) = 0 BEGIN
                SELECT RAISE(ABORT, 'Group name and name key must not be empty.');
            END;
            """;
        constraints.ExecuteNonQuery();
    }

    private static int GetVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private void EnsureVersionIsSupportedBeforeWrite()
    {
        string path = _db.DatabaseFile;
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            return;
        }

        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();

        int userVersion = GetUserVersion(connection);
        if (userVersion > LatestSupportedVersion)
        {
            throw new InvalidOperationException(
                $"Database user_version {userVersion} is newer than supported " +
                $"version {LatestSupportedVersion}.");
        }

        using SqliteCommand tableCheck = connection.CreateCommand();
        tableCheck.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'schema_version'
            """;
        if (Convert.ToInt32(tableCheck.ExecuteScalar()) == 0)
        {
            return;
        }

        int schemaVersion = GetVersion(connection);
        if (schemaVersion > LatestSupportedVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {schemaVersion} is newer than supported " +
                $"version {LatestSupportedVersion}.");
        }
    }

    private void NormalizeTimestampColumns(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var rows = new List<TimestampRow>();
        using (SqliteCommand read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT id, created_at, updated_at, expire_at, deleted_at
                FROM snippets
                """;
            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new TimestampRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        foreach (TimestampRow row in rows)
        {
            string createdAt = NormalizeRequired(row.Id, "created_at", row.CreatedAt);
            string updatedAt = NormalizeRequired(row.Id, "updated_at", row.UpdatedAt);
            string? expireAt = NormalizeOptional(row.Id, "expire_at", row.ExpireAt);
            string? deletedAt = NormalizeOptional(row.Id, "deleted_at", row.DeletedAt);

            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE snippets SET
                    created_at = $created_at,
                    updated_at = $updated_at,
                    expire_at = $expire_at,
                    deleted_at = $deleted_at
                WHERE id = $id
                """;
            update.Parameters.AddWithValue("$created_at", createdAt);
            update.Parameters.AddWithValue("$updated_at", updatedAt);
            update.Parameters.AddWithValue(
                "$expire_at", (object?)expireAt ?? DBNull.Value);
            update.Parameters.AddWithValue(
                "$deleted_at", (object?)deletedAt ?? DBNull.Value);
            update.Parameters.AddWithValue("$id", row.Id);
            update.ExecuteNonQuery();
        }
    }

    private string NormalizeRequired(long id, string column, string value)
    {
        if (UtcTimestamp.TryNormalize(value, out string? normalized))
        {
            return normalized!;
        }

        _log.Warn(
            $"Snippet #{id} has invalid {column} '{value}'; " +
            $"using {UtcTimestamp.InvalidRequiredFallback}.");
        return UtcTimestamp.InvalidRequiredFallback;
    }

    private string? NormalizeOptional(long id, string column, string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (UtcTimestamp.TryNormalize(value, out string? normalized))
        {
            return normalized;
        }

        _log.Warn($"Snippet #{id} has invalid {column} '{value}'; using NULL.");
        return null;
    }

    private sealed record TimestampRow(
        long Id,
        string CreatedAt,
        string UpdatedAt,
        string? ExpireAt,
        string? DeletedAt);
}
