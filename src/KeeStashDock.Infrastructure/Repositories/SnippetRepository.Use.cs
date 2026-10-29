using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;

namespace KeeStashDock.Infrastructure.Repositories;

public sealed partial class SnippetRepository
{
    /// <summary>Captures the displayed item before a template dialog or clipboard retry can yield.</summary>
    public BatchSelection? CaptureUse(Snippet expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var rows = ReadBatchRows(connection, transaction, [expected.Id]);
        if (rows.Count != 1) return null;
        Snippet current = rows[0].Snippet;
        if (current.IsDeleted || current.IsExpired(DateTime.UtcNow) || current.CreatedAt != expected.CreatedAt ||
            current.ContentHash != expected.ContentHash || current.Content != expected.Content ||
            current.Type != expected.Type || current.IsTemplate != expected.IsTemplate) return null;
        BatchSelection snapshot = MakeSelection(ReadIdentity(connection, transaction), rows);
        transaction.Commit();
        return snapshot;
    }

    /// <summary>Records one completed output only if its original database and item still match.</summary>
    public RegisterUseResult? TryRegisterUse(BatchSelection snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Items.Count != 1) return null;
        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction(deferred: false);
        if (!string.Equals(ReadIdentity(connection, transaction), snapshot.DatabaseIdentity, StringComparison.Ordinal)) return null;
        SnippetVersion expected = snapshot.Items[0];
        var rows = ReadBatchRows(connection, transaction, [expected.Id]);
        DateTime now = DateTime.UtcNow;
        if (rows.Count != 1 || rows[0].Snippet.IsDeleted || rows[0].Snippet.IsExpired(now) ||
            !string.Equals(Revision(rows[0]), expected.Revision, StringComparison.Ordinal)) return null;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE snippets SET
                use_count = CASE WHEN use_count < $maximum THEN use_count + 1 ELSE $maximum END,
                last_used_at = $now,
                deleted_at = CASE WHEN expire_policy = $burn THEN $now ELSE deleted_at END
            WHERE id = $id
            RETURNING use_count, last_used_at, deleted_at
            """;
        command.Parameters.AddWithValue("$maximum", int.MaxValue);
        command.Parameters.AddWithValue("$now", UtcTimestamp.Format(now));
        command.Parameters.AddWithValue("$burn", (int)ExpirePolicy.BurnAfterRead);
        command.Parameters.AddWithValue("$id", expected.Id);
        RegisterUseResult result;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return null;
            result = new RegisterUseResult(reader.GetInt32(0), UtcTimestamp.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : UtcTimestamp.Parse(reader.GetString(2)));
        }
        transaction.Commit();
        return result;
    }
}
