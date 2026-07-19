using Microsoft.Data.Sqlite;
using KeeStashDock.Infrastructure.Logging;

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
        // (2, "..."), <- future migrations are appended here, never edited.
    };

    public void Run()
    {
        using var conn = _db.Open();

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
        foreach (var (version, sql) in Migrations)
        {
            if (version <= current) continue;
            _log.Info($"Migrating database schema {current} -> {version}");
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
            cmd.CommandText = "UPDATE schema_version SET version = $v";
            cmd.Parameters.AddWithValue("$v", version);
            cmd.ExecuteNonQuery();
            tx.Commit();
            current = version;
        }
    }

    private static int GetVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
