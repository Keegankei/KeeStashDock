namespace KeeStashDock.Domain.Entities;

/// <summary>
/// Free-form label attached to snippets (many-to-many via snippet_tags table).
/// Tags are created implicitly the first time they are used and garbage
/// collected by housekeeping when no snippet references them anymore.
/// </summary>
public sealed class Tag
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
