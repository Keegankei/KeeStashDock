using Microsoft.Data.Sqlite;
using KeeStashDock.Infrastructure.Database;

namespace KeeStashDock.Infrastructure.Repositories;

/// <summary>
/// Tags per snippet, backed by the tags and snippet_tags tables and exposed
/// through the workbench editor's tag row.
/// Model: a snippet holds a SET of tag names; tags are global and deduplicated
/// by name; orphaned tags are pruned on every write so the table never grows
/// stale entries.
/// </summary>
public sealed class TagRepository
{
    private readonly Db _db;
    public TagRepository(Db db) => _db = db;

    /// <summary>Tag names attached to one snippet, alphabetical.</summary>
    public List<string> GetForSnippet(long snippetId)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.name FROM tags t
            JOIN snippet_tags st ON st.tag_id = t.id
            WHERE st.snippet_id = $id ORDER BY t.name
            """;
        cmd.Parameters.AddWithValue("$id", snippetId);
        using var rd = cmd.ExecuteReader();
        var list = new List<string>();
        while (rd.Read()) list.Add(rd.GetString(0));
        return list;
    }

    /// <summary>Replaces the snippet's tag set with <paramref name="names"/>.
    /// Names are trimmed, empties dropped, duplicates collapsed (case-sensitive,
    /// mirroring the UNIQUE constraint). Runs in one transaction.</summary>
    public void SetForSnippet(long snippetId, IEnumerable<string> names)
    {
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        SetForSnippet(conn, tx, snippetId, names);
        tx.Commit();
    }

    internal static void SetForSnippet(SqliteConnection conn, SqliteTransaction tx,
                                       long snippetId, IEnumerable<string> names)
    {
        var clean = names.Select(n => n.Trim())
                         .Where(n => n.Length > 0)
                         .Distinct()
                         .ToList();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = "DELETE FROM snippet_tags WHERE snippet_id = $id";
        cmd.Parameters.AddWithValue("$id", snippetId);
        cmd.ExecuteNonQuery();

        foreach (string name in clean)
        {
            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT OR IGNORE INTO tags (name) VALUES ($n)";
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();
            cmd.CommandText = """
                INSERT OR IGNORE INTO snippet_tags (snippet_id, tag_id)
                SELECT $id, id FROM tags WHERE name = $n
                """;
            cmd.Parameters.AddWithValue("$id", snippetId);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.ExecuteNonQuery();
        }

        // Prune tags no snippet references anymore.
        cmd.Parameters.Clear();
        cmd.CommandText =
            "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM snippet_tags)";
        cmd.ExecuteNonQuery();

    }
}
