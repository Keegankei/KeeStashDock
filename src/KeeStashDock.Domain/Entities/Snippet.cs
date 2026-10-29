namespace KeeStashDock.Domain.Entities;

/// <summary>
/// The core aggregate of KeeStashDock: one text or screenshot entry.
///
/// Design notes
/// ------------
/// * All timestamps are stored as UTC (converted for display only).
/// * <see cref="ContentHash"/> identifies text or the immutable original image,
///   used for duplicate detection at insert time.
/// * Soft delete: <see cref="DeletedAt"/> non-null means "in the recycle bin".
///   Rows are physically purged by the housekeeping service 30 days later.
/// * <see cref="SortWeight"/> allows manual re-ordering inside a category
///   without rewriting timestamps.
/// </summary>
public sealed class Snippet
{
    public long Id { get; set; }

    /// <summary>Optional human-friendly title. Null =&gt; UI shows a content preview.</summary>
    public string? Title { get; set; }

    /// <summary>Text content, or optional editable OCR text for an image entry.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Text SHA-256, or image: plus original PNG SHA-256. OCR edits retain image identity.</summary>
    public string ContentHash { get; set; } = string.Empty;

    public SnippetType Type { get; set; } = SnippetType.PlainText;
    public bool IsImage => Type == SnippetType.Image;
    /// <summary>Explicit plain-text template; ordinary brace-containing content never expands implicitly.</summary>
    public bool IsTemplate { get; set; }
    public SnippetSource Source { get; set; } = SnippetSource.Manual;

    /// <summary>FK to Category. Null =&gt; "Uncategorized" virtual group.</summary>
    public long? CategoryId { get; set; }

    /// <summary>Pinned snippets float to the top of the quick panel.</summary>
    public bool IsPinned { get; set; }

    public ExpirePolicy ExpirePolicy { get; set; } = ExpirePolicy.Never;

    /// <summary>Absolute UTC expiry instant, pre-computed from the policy. Null = never.</summary>
    public DateTime? ExpireAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Soft-delete marker. Non-null = sitting in the recycle bin.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Manual ordering weight (higher = earlier). Default 0.</summary>
    public int SortWeight { get; set; }

    /// <summary>How many times the snippet was copied out - powers "frequently used".</summary>
    public int UseCount { get; set; }

    /// <summary>UTC instant of the most recent copy-out. Null = never used.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Convenience flag consumed by view models.</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>True when the snippet's expiry instant has passed.</summary>
    public bool IsExpired(DateTime utcNow) => ExpireAt is not null && ExpireAt <= utcNow;
}
