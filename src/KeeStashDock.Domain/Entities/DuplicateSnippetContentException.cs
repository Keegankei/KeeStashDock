namespace KeeStashDock.Domain.Entities;

/// <summary>An edit would duplicate another live snippet's content.</summary>
public sealed class DuplicateSnippetContentException(long existingSnippetId)
    : InvalidOperationException("Another live snippet already has this content.")
{
    public long ExistingSnippetId { get; } = existingSnippetId;
}
