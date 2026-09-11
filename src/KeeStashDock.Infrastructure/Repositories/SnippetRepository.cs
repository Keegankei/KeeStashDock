using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Infrastructure.Repositories;

/// <summary>
/// Data access for snippets. Hand-written ADO.NET (no ORM) keeps the
/// dependency graph tiny and every query auditable. All timestamps round-trip
/// as ISO-8601 UTC strings ("O" format) - SQLite has no native date type.
/// </summary>
public sealed class SnippetRepository
{
    private readonly Db _db;
    public SnippetRepository(Db db) => _db = db;

    // ---------------------------------------------------------------- write

    public long Insert(Snippet s)
    {
        using var conn = _db.Open();
        return Insert(conn, null, s);
    }

    /// <summary>Serializes duplicate lookup and insertion against other
    /// captures and edits so a concurrent capture cannot reintroduce duplicates.</summary>
    public long InsertOrRefresh(Snippet snippet)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction(deferred: false);
        long? existingId = FindOtherLiveId(conn, tx, snippet.ContentHash, 0);
        long id;
        if (existingId is long existing)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE snippets SET updated_at = $now WHERE id = $id";
            cmd.Parameters.AddWithValue("$now", UtcTimestamp.Format(snippet.UpdatedAt));
            cmd.Parameters.AddWithValue("$id", existing);
            cmd.ExecuteNonQuery();
            id = existing;
        }
        else
        {
            id = Insert(conn, tx, snippet);
        }
        tx.Commit();
        return id;
    }

    private static long Insert(SqliteConnection conn, SqliteTransaction? tx, Snippet s)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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

    /// <summary>Updates only fields owned by the workbench editor.</summary>
    public void UpdateContent(Snippet snippet, DateTime updatedAtUtc,
                              IReadOnlyList<string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(snippet.Content))
            throw new ArgumentException("Snippet content must not be empty.", nameof(snippet));
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction(deferred: false);
        long? duplicateId = FindOtherLiveId(conn, tx, snippet.ContentHash, snippet.Id);
        if (duplicateId is long existing)
            throw new DuplicateSnippetContentException(existing);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            UPDATE snippets SET
                title = $title,
                content = $content,
                content_hash = $hash,
                expire_policy = $expire_policy,
                expire_at = $expire_at,
                updated_at = $updated_at
            WHERE id = $id
            """;
        cmd.Parameters.AddWithValue("$title", (object?)snippet.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$content", snippet.Content);
        cmd.Parameters.AddWithValue("$hash", snippet.ContentHash);
        cmd.Parameters.AddWithValue("$expire_policy", (int)snippet.ExpirePolicy);
        cmd.Parameters.AddWithValue(
            "$expire_at",
            (object?)FormatOptional(snippet.ExpireAt) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$updated_at", UtcTimestamp.Format(updatedAtUtc));
        cmd.Parameters.AddWithValue("$id", snippet.Id);
        EnsureUpdated(cmd.ExecuteNonQuery(), snippet.Id);
        if (tags is not null)
            TagRepository.SetForSnippet(conn, tx, snippet.Id, tags);
        tx.Commit();
    }

    public void RefreshRecency(long id, DateTime updatedAtUtc) => ExecRequired(
        "UPDATE snippets SET updated_at = $updated_at WHERE id = $id",
        id,
        ("$updated_at", UtcTimestamp.Format(updatedAtUtc)),
        ("$id", id));

    public void SetPinned(long id, bool isPinned) => ExecRequired(
        "UPDATE snippets SET is_pinned = $pinned WHERE id = $id",
        id,
        ("$pinned", isPinned ? 1 : 0),
        ("$id", id));

    /// <summary>Soft delete: move to the recycle bin.</summary>
    public DateTime MoveToTrash(long id)
    {
        DateTime deletedAt = DateTime.UtcNow;
        ExecRequired(
            "UPDATE snippets SET deleted_at = $now WHERE id = $id",
            id,
            ("$now", UtcTimestamp.Format(deletedAt)),
            ("$id", id));
        return deletedAt;
    }

    /// <summary>Restore from the recycle bin.</summary>
    public void Restore(long id) => ExecRequired(
        "UPDATE snippets SET deleted_at = NULL WHERE id = $id",
        id,
        ("$id", id));

    /// <summary>Physical removal (FTS row is cleaned up by trigger).</summary>
    public void HardDelete(long id) => Exec(
        "DELETE FROM snippets WHERE id = $id", ("$id", id));

    /// <summary>Moves a snippet into a category (null = uncategorized).
    /// Group import uses this to force-place deduplicated rows: Add() may
    /// return an EXISTING snippet living in another group.</summary>
    public void SetCategory(long id, long? categoryId) => Exec(
        "UPDATE snippets SET category_id = $c, updated_at = $t WHERE id = $i",
        ("$c", (object?)categoryId ?? DBNull.Value),
        ("$t", UtcTimestamp.Format(DateTime.UtcNow)),
        ("$i", id));

    public RegisterUseResult RegisterUse(long id, bool moveToTrash)
    {
        DateTime usedAt = DateTime.UtcNow;
        string usedAtText = UtcTimestamp.Format(usedAt);
        using SqliteConnection connection = _db.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            UPDATE snippets SET
                use_count = CASE
                    WHEN use_count < $max_use_count THEN use_count + 1
                    ELSE $max_use_count
                END,
                last_used_at = $used_at,
                deleted_at = CASE
                    WHEN $move_to_trash = 1 THEN COALESCE(deleted_at, $used_at)
                    ELSE deleted_at
                END
            WHERE id = $id
            RETURNING use_count, last_used_at, deleted_at
            """;
        command.Parameters.AddWithValue("$used_at", usedAtText);
        command.Parameters.AddWithValue("$move_to_trash", moveToTrash ? 1 : 0);
        command.Parameters.AddWithValue("$max_use_count", int.MaxValue);
        command.Parameters.AddWithValue("$id", id);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Snippet #{id} no longer exists.");
        }

        return new RegisterUseResult(
            reader.GetInt32(0),
            UtcTimestamp.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : UtcTimestamp.Parse(reader.GetString(2)));
    }

    /// <summary>Housekeeping: purge trash older than the retention window.</summary>
    public int PurgeTrash(int retentionDays) => Exec(
        "DELETE FROM snippets WHERE deleted_at IS NOT NULL AND deleted_at < $cut",
        ("$cut", UtcTimestamp.Format(DateTime.UtcNow.AddDays(
            -NormalizeRetentionDays(retentionDays)))));

    /// <summary>Housekeeping: soft-delete everything past its expiry instant.</summary>
    public int MoveExpiredToTrash() => Exec(
        """
        UPDATE snippets SET deleted_at = $now
        WHERE deleted_at IS NULL AND expire_at IS NOT NULL AND expire_at <= $now
        """,
        ("$now", UtcTimestamp.Format(DateTime.UtcNow)));

    public int EmptyTrash() => Exec(
        "DELETE FROM snippets WHERE deleted_at IS NOT NULL");

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

    private static long? FindOtherLiveId(SqliteConnection conn, SqliteTransaction tx,
                                         string hash, long excludingId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id FROM snippets
            WHERE deleted_at IS NULL AND content_hash = $hash AND id != $id
            ORDER BY id LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.Parameters.AddWithValue("$id", excludingId);
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    private static void Bind(SqliteCommand cmd, Snippet s)
    {
        cmd.Parameters.AddWithValue("$t", (object?)s.Title ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$c", s.Content);
        cmd.Parameters.AddWithValue("$h", s.ContentHash);
        cmd.Parameters.AddWithValue("$ty", (int)s.Type);
        cmd.Parameters.AddWithValue("$src", (int)s.Source);
        cmd.Parameters.AddWithValue("$cat", (object?)s.CategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pin", s.IsPinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$ep", (int)s.ExpirePolicy);
        cmd.Parameters.AddWithValue("$ea", (object?)FormatOptional(s.ExpireAt) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ca", UtcTimestamp.Format(s.CreatedAt));
        cmd.Parameters.AddWithValue("$ua", UtcTimestamp.Format(s.UpdatedAt));
        cmd.Parameters.AddWithValue("$sw", s.SortWeight);
        cmd.Parameters.AddWithValue("$uc", s.UseCount);
    }

    private static Snippet Map(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        Title = r.IsDBNull(r.GetOrdinal("title")) ? null : r.GetString(r.GetOrdinal("title")),
        Content = r.GetString(r.GetOrdinal("content")),
        ContentHash = r.GetString(r.GetOrdinal("content_hash")),
        Type = (SnippetType)r.GetInt32(r.GetOrdinal("type")),
        Source = (SnippetSource)r.GetInt32(r.GetOrdinal("source")),
        CategoryId = r.IsDBNull(r.GetOrdinal("category_id")) ? null : r.GetInt64(r.GetOrdinal("category_id")),
        IsPinned = r.GetInt32(r.GetOrdinal("is_pinned")) == 1,
        ExpirePolicy = (ExpirePolicy)r.GetInt32(r.GetOrdinal("expire_policy")),
        ExpireAt = ReadDate(r, "expire_at"),
        CreatedAt = ReadDate(r, "created_at") ?? DateTime.UtcNow,
        UpdatedAt = ReadDate(r, "updated_at") ?? DateTime.UtcNow,
        DeletedAt = ReadDate(r, "deleted_at"),
        SortWeight = r.GetInt32(r.GetOrdinal("sort_weight")),
        UseCount = r.GetInt32(r.GetOrdinal("use_count")),
        LastUsedAt = ReadDate(r, "last_used_at"),
    };

    private static DateTime? ReadDate(SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null
            : UtcTimestamp.Parse(r.GetString(i));
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

    private void ExecRequired(
        string sql,
        long snippetId,
        params (string, object)[] args)
        => EnsureUpdated(Exec(sql, args), snippetId);

    private static void EnsureUpdated(int affectedRows, long snippetId)
    {
        if (affectedRows != 1)
        {
            throw new InvalidOperationException($"Snippet #{snippetId} no longer exists.");
        }
    }

    private static string? FormatOptional(DateTime? value)
        => value is null ? null : UtcTimestamp.Format(value.Value);

    private static int NormalizeRetentionDays(int retentionDays)
        => retentionDays is <= 0 or > AppSettings.MaxTrashRetentionDays
            ? AppSettings.DefaultTrashRetentionDays
            : retentionDays;
}

public sealed record RegisterUseResult(
    int UseCount,
    DateTime LastUsedAt,
    DateTime? DeletedAt);
