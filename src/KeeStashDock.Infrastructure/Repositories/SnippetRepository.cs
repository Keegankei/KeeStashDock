using Microsoft.Data.Sqlite;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;

namespace KeeStashDock.Infrastructure.Repositories;

/// <summary>
/// Data access for snippets. Hand-written ADO.NET (no ORM) keeps the
/// dependency graph tiny and every query auditable. All timestamps round-trip
/// as ISO-8601 UTC strings ("o" format) - SQLite has no native date type.
/// </summary>
public sealed class SnippetRepository
{
    private readonly Db _db;
    public SnippetRepository(Db db) => _db = db;

    // ---------------------------------------------------------------- write

    public long Insert(Snippet s)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO snippets
                (title, content, content_hash, type, source, category_id, is_pinned,
                 expire_policy, expire_at, created_at, updated_at, sort_weight, use_count)
            VALUES ($t, $c, $h, $ty, $src, $cat, $pin, $ep, $ea, $ca, $ua, $sw, $uc);
            SELECT last_insert_rowid();
            """;
        Bind(cmd, s);
        return (long)cmd.ExecuteScalar()!;
    }

    public void Update(Snippet s)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE snippets SET
                title = $t, content = $c, content_hash = $h, type = $ty, source = $src,
                category_id = $cat, is_pinned = $pin, expire_policy = $ep, expire_at = $ea,
                created_at = $ca, updated_at = $ua, sort_weight = $sw, use_count = $uc
            WHERE id = $id
            """;
        Bind(cmd, s);
        cmd.Parameters.AddWithValue("$id", s.Id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Soft delete: move to the recycle bin.</summary>
    public void SoftDelete(long id) => Exec(
        "UPDATE snippets SET deleted_at = $now WHERE id = $id",
        ("$now", DateTime.UtcNow.ToString("o")), ("$id", id));

    /// <summary>Restore from the recycle bin.</summary>
    public void Restore(long id) => Exec(
        "UPDATE snippets SET deleted_at = NULL WHERE id = $id", ("$id", id));

    /// <summary>Physical removal (FTS row is cleaned up by trigger).</summary>
    public void HardDelete(long id) => Exec(
        "DELETE FROM snippets WHERE id = $id", ("$id", id));

    /// <summary>Moves a snippet into a category (null = uncategorized).
    /// Group import uses this to force-place deduplicated rows: Add() may
    /// return an EXISTING snippet living in another group.</summary>
    public void SetCategory(long id, long? categoryId) => Exec(
        "UPDATE snippets SET category_id = $c, updated_at = $t WHERE id = $i",
        ("$c", (object?)categoryId ?? DBNull.Value), ("$t", DateTime.UtcNow), ("$i", id));

    public void IncrementUseCount(long id) => Exec(
        "UPDATE snippets SET use_count = use_count + 1 WHERE id = $id", ("$id", id));

    /// <summary>Housekeeping: purge trash older than the retention window.</summary>
    public int PurgeTrash(int retentionDays) => Exec(
        "DELETE FROM snippets WHERE deleted_at IS NOT NULL AND deleted_at < $cut",
        ("$cut", DateTime.UtcNow.AddDays(-retentionDays).ToString("o")));

    /// <summary>Housekeeping: soft-delete everything past its expiry instant.</summary>
    public int MoveExpiredToTrash() => Exec(
        """
        UPDATE snippets SET deleted_at = $now
        WHERE deleted_at IS NULL AND expire_at IS NOT NULL AND expire_at <= $now
        """,
        ("$now", DateTime.UtcNow.ToString("o")));

    // ----------------------------------------------------------------- read

    /// <summary>Recent live snippets for the quick panel (pinned first).</summary>
    public List<Snippet> GetRecent(int limit)
        => Query("""
            SELECT * FROM snippets WHERE deleted_at IS NULL
            ORDER BY is_pinned DESC, sort_weight DESC, updated_at DESC LIMIT $lim
            """, ("$lim", limit));

    public List<Snippet> GetByCategory(long? categoryId)
        => categoryId is null
            ? Query("""
                SELECT * FROM snippets WHERE deleted_at IS NULL AND category_id IS NULL
                ORDER BY is_pinned DESC, sort_weight DESC, updated_at DESC
                """)
            : Query("""
                SELECT * FROM snippets WHERE deleted_at IS NULL AND category_id = $cat
                ORDER BY is_pinned DESC, sort_weight DESC, updated_at DESC
                """, ("$cat", categoryId.Value));

    public List<Snippet> GetAllLive()
        => Query("""
            SELECT * FROM snippets WHERE deleted_at IS NULL
            ORDER BY is_pinned DESC, sort_weight DESC, updated_at DESC
            """);

    public List<Snippet> GetTrash()
        => Query("SELECT * FROM snippets WHERE deleted_at IS NOT NULL ORDER BY deleted_at DESC");

    public Snippet? FindLiveByHash(string hash)
        => Query("SELECT * FROM snippets WHERE deleted_at IS NULL AND content_hash = $h LIMIT 1",
                 ("$h", hash)).FirstOrDefault();

    /// <summary>FTS5 MATCH search (space-delimited languages).</summary>
    public List<Snippet> SearchFts(string ftsQuery, int limit)
        => Query("""
            SELECT s.* FROM snippets_fts f
            JOIN snippets s ON s.id = f.rowid
            WHERE snippets_fts MATCH $q AND s.deleted_at IS NULL
            ORDER BY rank LIMIT $lim
            """, ("$q", ftsQuery), ("$lim", limit));

    /// <summary>LIKE fallback for CJK queries (unicode61 cannot segment CJK).</summary>
    public List<Snippet> SearchLike(string term, int limit)
        => Query("""
            SELECT * FROM snippets
            WHERE deleted_at IS NULL
              AND (content LIKE $q ESCAPE '\' OR COALESCE(title,'') LIKE $q ESCAPE '\')
            ORDER BY is_pinned DESC, updated_at DESC LIMIT $lim
            """,
            ("$q", "%" + term.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%"),
            ("$lim", limit));

    // ------------------------------------------------------------- plumbing

    private static void Bind(SqliteCommand cmd, Snippet s)
    {
        cmd.Parameters.AddWithValue("$t",   (object?)s.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c",   s.Content);
        cmd.Parameters.AddWithValue("$h",   s.ContentHash);
        cmd.Parameters.AddWithValue("$ty",  (int)s.Type);
        cmd.Parameters.AddWithValue("$src", (int)s.Source);
        cmd.Parameters.AddWithValue("$cat", (object?)s.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pin", s.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$ep",  (int)s.ExpirePolicy);
        cmd.Parameters.AddWithValue("$ea",  (object?)s.ExpireAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ca",  s.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$ua",  s.UpdatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$sw",  s.SortWeight);
        cmd.Parameters.AddWithValue("$uc",  s.UseCount);
    }

    private static Snippet Map(SqliteDataReader r) => new()
    {
        Id           = r.GetInt64(r.GetOrdinal("id")),
        Title        = r.IsDBNull(r.GetOrdinal("title")) ? null : r.GetString(r.GetOrdinal("title")),
        Content      = r.GetString(r.GetOrdinal("content")),
        ContentHash  = r.GetString(r.GetOrdinal("content_hash")),
        Type         = (SnippetType)r.GetInt32(r.GetOrdinal("type")),
        Source       = (SnippetSource)r.GetInt32(r.GetOrdinal("source")),
        CategoryId   = r.IsDBNull(r.GetOrdinal("category_id")) ? null : r.GetInt64(r.GetOrdinal("category_id")),
        IsPinned     = r.GetInt32(r.GetOrdinal("is_pinned")) == 1,
        ExpirePolicy = (ExpirePolicy)r.GetInt32(r.GetOrdinal("expire_policy")),
        ExpireAt     = ReadDate(r, "expire_at"),
        CreatedAt    = ReadDate(r, "created_at") ?? DateTime.UtcNow,
        UpdatedAt    = ReadDate(r, "updated_at") ?? DateTime.UtcNow,
        DeletedAt    = ReadDate(r, "deleted_at"),
        SortWeight   = r.GetInt32(r.GetOrdinal("sort_weight")),
        UseCount     = r.GetInt32(r.GetOrdinal("use_count")),
    };

    private static DateTime? ReadDate(SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null
            : DateTime.Parse(r.GetString(i), null, System.Globalization.DateTimeStyles.RoundtripKind);
    }

    private List<Snippet> Query(string sql, params (string, object)[] args)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        using var rd = cmd.ExecuteReader();
        var list = new List<Snippet>();
        while (rd.Read()) list.Add(Map(rd));
        return list;
    }

    private int Exec(string sql, params (string, object)[] args)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in args) cmd.Parameters.AddWithValue(k, v);
        return cmd.ExecuteNonQuery();
    }
}
