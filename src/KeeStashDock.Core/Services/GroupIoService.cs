using System.Text.Json;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Repositories;

namespace KeeStashDock.Core.Services;

/// <summary>
/// Group-level exchange. Two import granularities exist in the app:
///  - FULL package (.ksdpkg, BackupService): whole-database replace;
///  - GROUP package (.ksdgrp, THIS service): one category + its snippets as
///    portable JSON, imported ADDITIVELY. Name conflicts are decided by the
///    caller (overwrite the existing group's contents, or import under a
///    new name) - the service just executes the chosen mode.
/// Built-in presets (e.g. common git commands) reuse the same pipeline: a
/// preset is simply an in-memory GroupPackage.
/// Duplicate rule inherited from SnippetService.Add: identical live content
/// is never duplicated - the existing snippet is MOVED into the target group.
/// </summary>
public sealed class GroupIoService
{
    public sealed record GroupItem(string? Title, string Content);
    public sealed record GroupPackage(int Version, string Name, List<GroupItem> Items);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SnippetService _snippets;
    private readonly CategoryRepository _categories;

    public GroupIoService(SnippetService snippets, CategoryRepository categories)
    {
        _snippets = snippets;
        _categories = categories;
    }

    // ------------------------------------------------------------- export

    public void ExportGroup(long categoryId, string groupName, string targetFile)
    {
        var items = _snippets.ByCategory(categoryId)
            .Select(s => new GroupItem(s.Title, s.Content))
            .ToList();
        var pkg = new GroupPackage(1, groupName, items);
        File.WriteAllText(targetFile, JsonSerializer.Serialize(pkg, JsonOpts));
    }

    // ------------------------------------------------------------- import

    /// <summary>Parses a .ksdgrp file. Throws on malformed input; the caller
    /// turns that into a user-facing message.</summary>
    public GroupPackage ReadPackage(string file)
    {
        var pkg = JsonSerializer.Deserialize<GroupPackage>(
            File.ReadAllText(file), JsonOpts);
        pkg = ValidatePackage(pkg);
        return pkg with { Name = CategoryName.Normalize(pkg.Name) };
    }

    /// <summary>Case-insensitive category lookup for conflict detection.</summary>
    public Category? FindCategoryByName(string name)
        => _categories.FindByName(name);

    public string GetUniqueCategoryName(string baseName) => _categories.GetUniqueName(baseName);

    /// <summary>Imports a package into a category and returns the number of
    /// items placed. <paramref name="overwriteCategoryId"/> non-null =
    /// replace that group's contents (existing snippets go to the trash, so
    /// an accidental overwrite is recoverable); null = create a NEW category
    /// named <paramref name="targetName"/>.</summary>
    public int ImportGroup(GroupPackage pkg, string targetName,
                           long? overwriteCategoryId)
    {
        ValidatePackage(pkg);
        targetName = CategoryName.Normalize(targetName);
        long categoryId;
        if (overwriteCategoryId is long existing)
        {
            if (!_categories.GetAll().Any(category => category.Id == existing))
                throw new InvalidOperationException("The group selected for replacement no longer exists.");
            categoryId = existing;
            foreach (var old in _snippets.ByCategory(existing))
                _snippets.MoveToTrash(old.Id);   // recoverable, not destructive
        }
        else
        {
            categoryId = _categories.Insert(new Category { Name = targetName });
        }

        int placed = 0;
        foreach (var item in pkg.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Content)) continue;
            long id = _snippets.Add(item.Content, SnippetSource.Import,
                                    title: item.Title);
            _snippets.AssignCategory(id, categoryId);
            placed++;
        }
        return placed;
    }

    private static GroupPackage ValidatePackage(GroupPackage? pkg)
    {
        if (pkg is null || pkg.Version != 1 || string.IsNullOrWhiteSpace(pkg.Name) ||
            pkg.Items is null || pkg.Items.Any(item => item is null || item.Content is null))
            throw new InvalidDataException("Not a supported group package.");
        return pkg;
    }
}
