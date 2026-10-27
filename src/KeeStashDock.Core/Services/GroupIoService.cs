using System.Text.Json;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Core.Services;

/// <summary>Portable group exchange: v1 text, v2 image assets, and v3 explicit template identity.</summary>
public sealed class GroupIoService
{
    public sealed record GroupItem(string? Title, string Content,
        SnippetType Type = SnippetType.PlainText, byte[]? ImagePng = null, byte[]? ThumbnailPng = null, bool IsTemplate = false);
    public sealed record GroupPackage(int Version, string Name, List<GroupItem> Items);
    public const long MaxPackageBytes = 128L * 1024 * 1024;
    private const long MaxAssetBytes = 80L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
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

    public void ExportGroup(long categoryId, string groupName, string targetFile)
    {
        var items = new List<GroupItem>();
        long assetBytes = 0;
        foreach (var snippet in _snippets.ByCategory(categoryId))
        {
            byte[]? image = snippet.IsImage ? _snippets.GetImage(snippet.Id) : null;
            byte[]? thumbnail = snippet.IsImage ? _snippets.GetImageThumbnail(snippet.Id) : null;
            assetBytes += (image?.LongLength ?? 0) + (thumbnail?.LongLength ?? 0);
            if (assetBytes > MaxAssetBytes)
                throw new InvalidDataException("The group is too large for a group package. Use a full database package.");
            items.Add(new GroupItem(snippet.Title, snippet.Content, snippet.Type, image, thumbnail, snippet.IsTemplate));
        }
        var pkg = ValidatePackage(new GroupPackage(items.Any(item => item.IsTemplate) ? 3 : 2, groupName, items));
        string destination = Path.GetFullPath(targetFile);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = File.Create(temporary)) JsonSerializer.Serialize(output, pkg, JsonOpts);
            if (new FileInfo(temporary).Length > MaxPackageBytes)
                throw new InvalidDataException("The group package exceeds the supported size.");
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public GroupPackage ReadPackage(string file)
    {
        if (new FileInfo(file).Length > MaxPackageBytes)
            throw new InvalidDataException("The group package exceeds the supported size.");
        using var input = File.OpenRead(file);
        var pkg = ValidatePackage(JsonSerializer.Deserialize<GroupPackage>(input, JsonOpts));
        return NormalizeLegacy(pkg) with { Name = CategoryName.Normalize(pkg.Name) };
    }

    public Category? FindCategoryByName(string name) => _categories.FindByName(name);
    public string GetUniqueCategoryName(string baseName) => _categories.GetUniqueName(baseName);

    /// <summary>Validates every asset before mutating a group, including overwrite imports.</summary>
    public int ImportGroup(GroupPackage pkg, string targetName, long? overwriteCategoryId)
    {
        pkg = NormalizeLegacy(ValidatePackage(pkg));
        return _snippets.ImportGroup(_categories, targetName, overwriteCategoryId,
            pkg.Items, detectTypes: pkg.Version == 1);
    }

    private static GroupPackage ValidatePackage(GroupPackage? pkg)
    {
        if (pkg is null || pkg.Version is not (1 or 2 or 3) || string.IsNullOrWhiteSpace(pkg.Name) ||
            pkg.Items is null || pkg.Items.Count > 10000 ||
            pkg.Items.Any(item => item is null || item.Content is null))
            throw new InvalidDataException("Not a supported group package.");
        long assetBytes = 0;
        foreach (var item in pkg.Items)
        {
            if (!Enum.IsDefined(item.Type)) throw new InvalidDataException("Unsupported entry type.");
            if (pkg.Version == 3 && item.IsTemplate && (item.Type != SnippetType.PlainText || string.IsNullOrWhiteSpace(item.Content)))
                throw new InvalidDataException("A template must contain plain text.");
            if (item.Type == SnippetType.Image)
            {
                if (pkg.Version is not (2 or 3) || item.ImagePng is null || item.ThumbnailPng is null)
                    throw new InvalidDataException("The image entry is missing its image asset.");
                assetBytes += item.ImagePng.LongLength + item.ThumbnailPng.LongLength;
                if (assetBytes > MaxAssetBytes) throw new InvalidDataException("The group image assets exceed the supported size.");
                PngImageValidator.Validate(item.ImagePng);
                PngImageValidator.Validate(item.ThumbnailPng, thumbnail: true);
            }
            else if (item.ImagePng is not null || item.ThumbnailPng is not null)
                throw new InvalidDataException("Text entries cannot contain image assets.");
        }
        return pkg;
    }

    private static GroupPackage NormalizeLegacy(GroupPackage pkg)
        => pkg.Version < 3 ? pkg with { Items = pkg.Items.Select(item => item with { IsTemplate = false }).ToList() } : pkg;
}
