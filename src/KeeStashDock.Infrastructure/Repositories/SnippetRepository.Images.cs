using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Infrastructure.Repositories;

public sealed partial class SnippetRepository
{
    public long InsertImageOrRefresh(Snippet snippet, byte[] png, byte[] thumbnailPng)
    {
        var (width, height) = PngImageValidator.Validate(png);
        PngImageValidator.Validate(thumbnailPng, thumbnail: true);
        if (!snippet.IsImage || snippet.ContentHash != PngImageValidator.ImageHash(png))
            throw new ArgumentException("The image entry has inconsistent metadata.", nameof(snippet));
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction(deferred: false);
        long id;
        if (FindOtherLiveId(conn, tx, snippet.ContentHash, 0) is long existing)
        {
            using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE snippets SET updated_at = $now WHERE id = $id";
            update.Parameters.AddWithValue("$now", Database.UtcTimestamp.Format(snippet.UpdatedAt));
            update.Parameters.AddWithValue("$id", existing);
            update.ExecuteNonQuery();
            id = existing;
        }
        else
        {
            id = Insert(conn, tx, snippet);
            using var asset = conn.CreateCommand();
            asset.Transaction = tx;
            asset.CommandText = """
                INSERT INTO snippet_images(snippet_id, png, thumbnail_png, width, height)
                VALUES($id, $png, $thumbnail, $width, $height)
                """;
            asset.Parameters.AddWithValue("$id", id);
            asset.Parameters.AddWithValue("$png", png);
            asset.Parameters.AddWithValue("$thumbnail", thumbnailPng);
            asset.Parameters.AddWithValue("$width", width);
            asset.Parameters.AddWithValue("$height", height);
            asset.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    public byte[]? GetImage(long id) => ReadImageAsset(id, thumbnail: false);
    public byte[]? GetImageThumbnail(long id) => ReadImageAsset(id, thumbnail: true);

    private byte[]? ReadImageAsset(long id, bool thumbnail)
    {
        using var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        string column = thumbnail ? "thumbnail_png" : "png";
        cmd.CommandText = $"SELECT length({column}) FROM snippet_images WHERE snippet_id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        object? length = cmd.ExecuteScalar();
        if (length is null) return null;
        int limit = thumbnail ? PngImageValidator.MaxThumbnailBytes : PngImageValidator.MaxImageBytes;
        if (Convert.ToInt64(length) > limit) throw new InvalidDataException("The stored image exceeds its size limit.");
        cmd.CommandText = $"SELECT {column} FROM snippet_images WHERE snippet_id = $id";
        return cmd.ExecuteScalar() as byte[];
    }
}
