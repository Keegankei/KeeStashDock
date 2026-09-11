using System.Globalization;

namespace KeeStashDock.Domain.Entities;

/// <summary>Shared, culture-independent naming rules for stored and imported groups.</summary>
public static class CategoryName
{
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim();
    }

    public static string GetKey(string name) => Normalize(name).ToUpperInvariant();

    public static string GetUnique(string baseName, ISet<string> existingKeys)
    {
        string normalized = Normalize(baseName);
        string candidate = normalized;
        int suffix = 2;
        while (existingKeys.Contains(GetKey(candidate)))
        {
            candidate = normalized + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")";
            suffix++;
        }

        return candidate;
    }
}

public sealed class DuplicateCategoryNameException : InvalidOperationException
{
    public DuplicateCategoryNameException(string name, long existingCategoryId)
        : base($"A group named '{name}' already exists.")
    {
        Name = name;
        ExistingCategoryId = existingCategoryId;
    }

    public string Name { get; }
    public long ExistingCategoryId { get; }
}
