namespace KeeStashDock.Domain.Entities;

public enum BatchAction { MoveToCategory, AddTags, RemoveTags, Pin, Unpin, Trash, Restore }
public enum BatchConflictReason { DatabaseChanged, MissingItem, ChangedItem, InvalidState, MissingCategory, DuplicateContent, ExpiredItem }
public sealed class BatchConflictException(BatchConflictReason reason) : InvalidOperationException(reason.ToString())
{
    public BatchConflictReason Reason { get; } = reason;
}

/// <summary>Opaque optimistic concurrency token captured when a user selects entries.</summary>
public sealed record SnippetVersion(long Id, string Revision);
public sealed record BatchSelection(string DatabaseIdentity, IReadOnlyList<SnippetVersion> Items);
public sealed record BatchUndoToken(BatchSelection Selection);
public sealed record BatchResult(int AffectedCount, BatchUndoToken? Undo = null);
