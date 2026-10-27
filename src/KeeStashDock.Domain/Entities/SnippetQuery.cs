namespace KeeStashDock.Domain.Entities;

public enum QueryScope { All, Uncategorized, Category, Pinned, Trash }
public enum SnippetKindFilter { All, Text, Image, Template }

/// <summary>Filters are applied in the database before stable ordering and pagination.</summary>
public sealed record SnippetQuery
{
    public string Text { get; init; } = string.Empty;
    public QueryScope Scope { get; init; }
    public long? CategoryId { get; init; }
    public string? Tag { get; init; }
    public SnippetKindFilter Kind { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 50;
}

public sealed record SnippetPage(IReadOnlyList<Snippet> Items, int TotalCount, string DatabaseIdentity, bool HasMore);
