using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Infrastructure.Repositories;

public sealed partial class SnippetRepository
{
    public string DatabaseIdentity
    {
        get { using var connection = _db.Open(); return ReadIdentity(connection, null); }
    }

    private static string ReadIdentity(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT identity FROM database_metadata WHERE singleton=1";
        return command.ExecuteScalar() as string ?? throw new InvalidDataException("The database identity is missing.");
    }

    public SnippetPage QueryPage(SnippetQuery query, bool useFts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(query.Scope) || !Enum.IsDefined(query.Kind) || query.Offset < 0 || query.Limit is < 1 or > 500)
            throw new ArgumentException("Invalid query range or filter.", nameof(query));
        string text = (query.Text ?? string.Empty).Trim();
        if (text.Length > 4096 || query.Tag?.Length > 256) throw new ArgumentException("The search term is too long.", nameof(query));
        string predicate = query.Scope == QueryScope.Trash ? "s.deleted_at IS NOT NULL" : "s.deleted_at IS NULL AND (s.expire_at IS NULL OR s.expire_at > $now)";
        predicate += query.Scope switch
        {
            QueryScope.Uncategorized => " AND s.category_id IS NULL",
            QueryScope.Category => " AND s.category_id = $category",
            QueryScope.Pinned => " AND s.is_pinned = 1",
            _ => string.Empty,
        };
        predicate += query.Kind switch
        {
            SnippetKindFilter.Text => " AND s.type != 5 AND s.is_template = 0",
            SnippetKindFilter.Image => " AND s.type = 5",
            SnippetKindFilter.Template => " AND s.is_template = 1",
            _ => string.Empty,
        };
        string? tag = string.IsNullOrWhiteSpace(query.Tag) ? null : query.Tag.Trim();
        if (tag is not null)
            predicate += " AND EXISTS (SELECT 1 FROM snippet_tags st JOIN tags t ON t.id=st.tag_id WHERE st.snippet_id=s.id AND t.name=$tag)";
        bool fts = useFts && text.Length > 0;
        string from = "snippets s" + (fts ? " JOIN snippets_fts ON snippets_fts.rowid=s.id" : string.Empty);
        if (text.Length > 0)
            predicate += fts ? " AND snippets_fts MATCH $query" : " AND (s.content LIKE $query ESCAPE '\\' OR COALESCE(s.title,'') LIKE $query ESCAPE '\\')";
        string term = fts ? string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => $"\"{token.Replace("\"", "\"\"")}\"*"))
            : "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

        using var connection = _db.Open(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        string identity = ReadIdentity(connection, transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$now", UtcTimestamp.Format(DateTime.UtcNow));
        command.Parameters.AddWithValue("$category", (object?)query.CategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("$tag", (object?)tag ?? DBNull.Value);
        command.Parameters.AddWithValue("$query", term);
        command.Parameters.AddWithValue("$limit", query.Limit);
        command.Parameters.AddWithValue("$offset", query.Offset);
        using var registration = cancellationToken.Register(command.Cancel);
        command.CommandText = $"SELECT COUNT(*) FROM {from} WHERE {predicate}";
        int count = Convert.ToInt32(command.ExecuteScalar());
        cancellationToken.ThrowIfCancellationRequested();
        string order = fts ? "bm25(snippets_fts), " : string.Empty;
        order += query.Scope == QueryScope.Trash ? "s.deleted_at DESC, s.id DESC" : "s.is_pinned DESC, s.sort_weight DESC, s.updated_at DESC, s.id DESC";
        command.CommandText = $"SELECT s.* FROM {from} WHERE {predicate} ORDER BY {order} LIMIT $limit OFFSET $offset";
        var items = new List<Snippet>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) { cancellationToken.ThrowIfCancellationRequested(); items.Add(Map(reader)); }
        transaction.Commit();
        return new SnippetPage(items, count, identity, (long)query.Offset + items.Count < count);
    }
}
