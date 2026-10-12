using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Database;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Infrastructure.Repositories;

public sealed record SnippetImportEntry(Snippet Snippet, byte[]? ImagePng, byte[]? ThumbnailPng);

public sealed partial class CategoryRepository
{
    /// <summary>Creates or replaces a group in one transaction, including deduplicated entries
    /// moved from other groups. Failed writes leave every affected group unchanged.</summary>
    public int ImportGroup(string targetName, long? overwriteCategoryId, IReadOnlyList<SnippetImportEntry> entries)
    {
        targetName = CategoryName.Normalize(targetName);
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            if (entry.Snippet.IsImage)
            {
                if (entry.ImagePng is null || entry.ThumbnailPng is null)
                    throw new InvalidDataException("An imported image is missing its assets.");
                PngImageValidator.Validate(entry.ImagePng);
                PngImageValidator.Validate(entry.ThumbnailPng, thumbnail: true);
                if (entry.Snippet.ContentHash != PngImageValidator.ImageHash(entry.ImagePng))
                    throw new InvalidDataException("An imported image has inconsistent metadata.");
            }
            else if (string.IsNullOrWhiteSpace(entry.Snippet.Content) ||
                     entry.ImagePng is not null || entry.ThumbnailPng is not null)
                throw new InvalidDataException("An imported text entry is invalid.");
        }

        using (var connection = _db.Open())
        using (var transaction = connection.BeginTransaction(deferred: false))
        {
            long categoryId;
            if (overwriteCategoryId is long existing)
            {
                using var category = connection.CreateCommand();
                category.Transaction = transaction;
                category.CommandText = "SELECT COUNT(*) FROM categories WHERE id = $id";
                category.Parameters.AddWithValue("$id", existing);
                if (Convert.ToInt64(category.ExecuteScalar()) != 1)
                    throw new InvalidOperationException("The group selected for replacement no longer exists.");
                categoryId = existing;

                using var trash = connection.CreateCommand();
                trash.Transaction = transaction;
                trash.CommandText = "UPDATE snippets SET deleted_at = $now WHERE category_id = $id AND deleted_at IS NULL";
                trash.Parameters.AddWithValue("$now", UtcTimestamp.Format(DateTime.UtcNow));
                trash.Parameters.AddWithValue("$id", categoryId);
                trash.ExecuteNonQuery();
            }
            else
            {
                string key = CategoryName.GetKey(targetName);
                ThrowIfDuplicate(connection, transaction, targetName, key);
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO categories(name, name_key, icon, color, sort_order) VALUES($name, $key, $icon, $color, $sort);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$name", targetName);
                insert.Parameters.AddWithValue("$key", key);
                var defaults = new Category();
                insert.Parameters.AddWithValue("$icon", defaults.Icon);
                insert.Parameters.AddWithValue("$color", defaults.Color);
                insert.Parameters.AddWithValue("$sort", defaults.SortOrder);
                categoryId = (long)insert.ExecuteScalar()!;
            }

            foreach (var entry in entries)
            {
                Snippet snippet = entry.Snippet;
                if (SnippetRepository.FindOtherLiveId(connection, transaction, snippet.ContentHash, 0) is long duplicateId)
                {
                    using var move = connection.CreateCommand();
                    move.Transaction = transaction;
                    move.CommandText = "UPDATE snippets SET category_id = $category, updated_at = $now WHERE id = $id";
                    move.Parameters.AddWithValue("$category", categoryId);
                    move.Parameters.AddWithValue("$now", UtcTimestamp.Format(snippet.UpdatedAt));
                    move.Parameters.AddWithValue("$id", duplicateId);
                    move.ExecuteNonQuery();
                    continue;
                }

                // The import owns these new models; no live view is changed before commit.
                snippet.CategoryId = categoryId;
                long id = SnippetRepository.Insert(connection, transaction, snippet);
                if (!snippet.IsImage) continue;
                var (width, height) = PngImageValidator.Validate(entry.ImagePng!);
                using var asset = connection.CreateCommand();
                asset.Transaction = transaction;
                asset.CommandText = """
                    INSERT INTO snippet_images(snippet_id, png, thumbnail_png, width, height)
                    VALUES($id, $png, $thumbnail, $width, $height)
                    """;
                asset.Parameters.AddWithValue("$id", id);
                asset.Parameters.AddWithValue("$png", entry.ImagePng!);
                asset.Parameters.AddWithValue("$thumbnail", entry.ThumbnailPng!);
                asset.Parameters.AddWithValue("$width", width);
                asset.Parameters.AddWithValue("$height", height);
                asset.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        Changed?.Invoke();
        return entries.Count;
    }
}
