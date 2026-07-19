namespace KeeStashDock.Domain.Entities;

/// <summary>
/// A user-defined group of snippets (shown in the workbench sidebar).
/// Icon is a Segoe Fluent Icons glyph string; Color is an #AARRGGBB hex string.
/// Both are plain strings so the Domain layer stays UI-framework agnostic.
/// </summary>
public sealed class Category
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "\uE8B7";      // default: folder glyph
    public string Color { get; set; } = "#FF6C8EBF";
    public int SortOrder { get; set; }
}
