using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace KeeStashDock.Infrastructure.Repositories;

public sealed partial class SnippetRepository
{
    public BatchSelection CaptureBatchSelection(IReadOnlyCollection<long> ids, string? expectedDatabaseIdentity = null)
    {
        long[] selected = ValidateIds(ids);
        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        string identity = ReadIdentity(connection, transaction);
        if (expectedDatabaseIdentity is not null && !string.Equals(identity, expectedDatabaseIdentity, StringComparison.Ordinal))
            throw new BatchConflictException(BatchConflictReason.DatabaseChanged);
        var rows = ReadBatchRows(connection, transaction, selected);
        if (rows.Count != selected.Length) throw new BatchConflictException(BatchConflictReason.MissingItem);
        var result = MakeSelection(identity, rows);
        transaction.Commit();
        return result;
    }

    public BatchResult ApplyBatch(BatchSelection selection, BatchAction action, long? targetCategoryId = null,
        IReadOnlyList<string>? tags = null, bool resetExpiredOnRestore = false)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(action)) throw new ArgumentException("Unsupported batch operation.", nameof(action));
        long[] ids = ValidateIds(selection.Items.Select(item => item.Id).ToArray());
        string[] cleanTags = (tags ?? []).Select(value => value?.Trim() ?? string.Empty).Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal).ToArray();
        if ((action is BatchAction.AddTags or BatchAction.RemoveTags) &&
            (cleanTags.Length is 0 or > 64 || cleanTags.Any(tag => tag.Length > 256)))
            throw new ArgumentException("Specify between one and 64 tags, each at most 256 characters.", nameof(tags));

        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        string identity = ReadIdentity(connection, transaction);
        if (!string.Equals(identity, selection.DatabaseIdentity, StringComparison.Ordinal))
            throw new BatchConflictException(BatchConflictReason.DatabaseChanged);
        var rows = ReadBatchRows(connection, transaction, ids);
        if (rows.Count != ids.Length) throw new BatchConflictException(BatchConflictReason.MissingItem);
        var expected = selection.Items.ToDictionary(item => item.Id, item => item.Revision);
        if (rows.Any(row => !string.Equals(expected[row.Snippet.Id], Revision(row), StringComparison.Ordinal)))
            throw new BatchConflictException(BatchConflictReason.ChangedItem);
        bool restoring = action == BatchAction.Restore;
        if (rows.Any(row => row.Snippet.IsDeleted != restoring))
            throw new BatchConflictException(BatchConflictReason.InvalidState);
        DateTime now = DateTime.UtcNow;
        if (restoring)
        {
            if (!resetExpiredOnRestore && rows.Any(row => row.Snippet.IsExpired(now)))
                throw new BatchConflictException(BatchConflictReason.ExpiredItem);
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!hashes.Add(row.Snippet.ContentHash) || FindOtherLiveId(connection, transaction, row.Snippet.ContentHash, row.Snippet.Id) is not null)
                    throw new BatchConflictException(BatchConflictReason.DuplicateContent);
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (action == BatchAction.MoveToCategory && targetCategoryId is long category)
        {
            command.CommandText = "SELECT COUNT(*) FROM categories WHERE id=$category";
            command.Parameters.AddWithValue("$category", category);
            if (Convert.ToInt32(command.ExecuteScalar()) != 1)
                throw new BatchConflictException(BatchConflictReason.MissingCategory);
            command.Parameters.Clear();
        }
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(ids));
        command.Parameters.AddWithValue("$now", UtcTimestamp.Format(now));
        command.Parameters.AddWithValue("$category", (object?)targetCategoryId ?? DBNull.Value);
        const string selected = "id IN (SELECT value FROM json_each($ids))";
        string assignment = action switch
        {
            BatchAction.MoveToCategory => "category_id=$category",
            BatchAction.Pin => "is_pinned=1",
            BatchAction.Unpin => "is_pinned=0",
            BatchAction.Trash => "deleted_at=$now",
            BatchAction.Restore when resetExpiredOnRestore => "deleted_at=NULL, expire_policy=CASE WHEN expire_at <= $now THEN 0 ELSE expire_policy END, expire_at=CASE WHEN expire_at <= $now THEN NULL ELSE expire_at END",
            BatchAction.Restore => "deleted_at=NULL",
            _ => "updated_at=$now",
        };
        string timestamp = action is BatchAction.AddTags or BatchAction.RemoveTags ? string.Empty : ", updated_at=$now";
        command.CommandText = $"UPDATE snippets SET {assignment}{timestamp} WHERE {selected}";
        command.ExecuteNonQuery();
        if (action is BatchAction.AddTags or BatchAction.RemoveTags)
            UpdateBatchTags(command, cleanTags, action == BatchAction.AddTags);
        BatchUndoToken? undo = action == BatchAction.Trash
            ? new BatchUndoToken(MakeSelection(identity, ReadBatchRows(connection, transaction, ids))) : null;
        transaction.Commit();
        return new BatchResult(ids.Length, undo);
    }

    public BatchResult UndoTrash(BatchUndoToken undo)
    {
        ArgumentNullException.ThrowIfNull(undo);
        return ApplyBatch(undo.Selection, BatchAction.Restore);
    }

    private static void UpdateBatchTags(SqliteCommand command, IReadOnlyList<string> tags, bool add)
    {
        command.Parameters.AddWithValue("$tag", string.Empty);
        foreach (string tag in tags)
        {
            command.Parameters["$tag"].Value = tag;
            if (add)
            {
                command.CommandText = "INSERT OR IGNORE INTO tags(name) VALUES($tag)";
                command.ExecuteNonQuery();
                command.CommandText = "INSERT OR IGNORE INTO snippet_tags(snippet_id, tag_id) SELECT value,t.id FROM json_each($ids) CROSS JOIN tags t WHERE t.name=$tag";
            }
            else command.CommandText = "DELETE FROM snippet_tags WHERE snippet_id IN (SELECT value FROM json_each($ids)) AND tag_id IN (SELECT id FROM tags WHERE name=$tag)";
            command.ExecuteNonQuery();
        }
        command.CommandText = "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM snippet_tags)";
        command.ExecuteNonQuery();
    }

    private static long[] ValidateIds(IReadOnlyCollection<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count is 0 or > 50000 || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Count)
            throw new ArgumentException("Select between one and 50,000 distinct entries.", nameof(ids));
        return ids.Order().ToArray();
    }

    private sealed record BatchRow(Snippet Snippet, IReadOnlyList<string> Tags);

    private static List<BatchRow> ReadBatchRows(SqliteConnection connection, SqliteTransaction transaction, long[] ids)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$ids", JsonSerializer.Serialize(ids));
        command.CommandText = "SELECT * FROM snippets WHERE id IN (SELECT value FROM json_each($ids)) ORDER BY id";
        var snippets = new List<Snippet>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) snippets.Add(Map(reader));
        command.CommandText = "SELECT st.snippet_id,t.name FROM snippet_tags st JOIN tags t ON t.id=st.tag_id WHERE st.snippet_id IN (SELECT value FROM json_each($ids)) ORDER BY st.snippet_id,t.name";
        var tags = new Dictionary<long, List<string>>();
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                long id = reader.GetInt64(0);
                if (!tags.TryGetValue(id, out var names)) tags.Add(id, names = []);
                names.Add(reader.GetString(1));
            }
        return snippets.Select(item => new BatchRow(item, tags.GetValueOrDefault(item.Id) ?? [])).ToList();
    }

    private static string Revision(BatchRow row)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row))));

    private static BatchSelection MakeSelection(string identity, List<BatchRow> rows)
        => new(identity, rows.Select(row => new SnippetVersion(row.Snippet.Id, Revision(row))).ToArray());
}
