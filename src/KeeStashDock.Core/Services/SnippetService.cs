using System.Security.Cryptography;
using System.Text;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Logging;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Core.Services;

/// <summary>
/// Primary use-case service for stashing and retrieving snippets.
/// Owns the business rules that must not leak into the UI:
///   * duplicate detection by content hash (dedupe bumps the existing row),
///   * automatic type detection for new content,
///   * expiry timestamp computation from the chosen policy,
///   * burn-after-read semantics on copy-out.
/// Raises <see cref="Changed"/> so every open view refreshes consistently.
/// </summary>
public sealed class SnippetService
{
    private readonly SnippetRepository _repo;
    private readonly FileLogger _log;

    /// <summary>Fired after any mutation. Views subscribe and reload.</summary>
    public event Action? Changed;

    public SnippetService(SnippetRepository repo, FileLogger log)
    {
        _repo = repo; _log = log;
    }

    // ----------------------------------------------------------------- add

    /// <summary>
    /// Stash new content. Returns the affected snippet id.
    /// Duplicate rule: identical live content is NOT inserted twice - instead
    /// the existing row's UpdatedAt is bumped so it surfaces to the top.
    /// </summary>
    public long Add(string content, SnippetSource source,
                    ExpirePolicy expire = ExpirePolicy.Never,
                    string? title = null, long? categoryId = null,
                    SnippetType? explicitType = null)
    {
        if (explicitType == SnippetType.Image) throw new ArgumentException("Use AddImage for image entries.", nameof(explicitType));
        content = NormalizeContent(content);
        string hash = Sha256(content);

        var now = DateTime.UtcNow;
        var snippet = new Snippet
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title!.Trim(),
            Content = content,
            ContentHash = hash,
            Type = explicitType ?? TypeDetector.Detect(content),
            Source = source,
            CategoryId = categoryId,
            ExpirePolicy = expire,
            ExpireAt = ComputeExpireAt(expire, now),
            CreatedAt = now,
            UpdatedAt = now,
        };
        long id = _repo.InsertOrRefresh(snippet);
        _log.Debug($"Snippet #{id} recorded ({snippet.Type}, {content.Length} chars)");
        Changed?.Invoke();
        return id;
    }

    /// <summary>Stores an immutable original PNG and a bounded thumbnail. OCR remains optional text.</summary>
    public long AddImage(byte[] png, byte[] thumbnailPng, long? categoryId = null,
                         ExpirePolicy expire = ExpirePolicy.Never, string? title = null,
                         string ocrText = "")
    {
        var now = DateTime.UtcNow;
        var snippet = new Snippet
        {
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            Content = (ocrText ?? string.Empty).TrimEnd('\r', '\n'),
            ContentHash = PngImageValidator.ImageHash(png),
            Type = SnippetType.Image,
            Source = SnippetSource.Screenshot,
            CategoryId = categoryId,
            ExpirePolicy = expire,
            ExpireAt = ComputeExpireAt(expire, now),
            CreatedAt = now,
            UpdatedAt = now,
        };
        long id = _repo.InsertImageOrRefresh(snippet, png, thumbnailPng);
        _log.Debug($"Image snippet #{id} recorded");
        Changed?.Invoke();
        return id;
    }

    /// <summary>Commits a complete imported group before notifying open views.</summary>
    internal int ImportGroup(CategoryRepository categories, string targetName, long? overwriteCategoryId,
        IReadOnlyList<GroupIoService.GroupItem> items, bool detectTypes)
    {
        var entries = new List<SnippetImportEntry>();
        DateTime now = DateTime.UtcNow;
        foreach (var item in items)
        {
            bool image = item.Type == SnippetType.Image;
            if (!image && string.IsNullOrWhiteSpace(item.Content)) continue;
            string content = image ? item.Content.TrimEnd('\r', '\n') : NormalizeContent(item.Content);
            entries.Add(new SnippetImportEntry(new Snippet
            {
                Title = string.IsNullOrWhiteSpace(item.Title) ? null : item.Title.Trim(),
                Content = content,
                ContentHash = image ? PngImageValidator.ImageHash(item.ImagePng!) : Sha256(content),
                Type = image ? SnippetType.Image : detectTypes ? TypeDetector.Detect(content) : item.Type,
                Source = SnippetSource.Import,
                ExpirePolicy = ExpirePolicy.Never,
                CreatedAt = now,
                UpdatedAt = now,
            }, item.ImagePng, item.ThumbnailPng));
        }
        int placed = categories.ImportGroup(targetName, overwriteCategoryId, entries);
        Changed?.Invoke();
        return placed;
    }

    public byte[]? GetImage(long id) => _repo.GetImage(id);
    public byte[]? GetImageThumbnail(long id) => _repo.GetImageThumbnail(id);

    // ---------------------------------------------------------------- edit

    /// <summary>Rejects blank or duplicate text; image OCR may be empty. Editor fields and optional
    /// tags commit together, and failed saves leave the supplied model intact.</summary>
    public void Update(Snippet s, IReadOnlyList<string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        Snippet stored = _repo.FindById(s.Id)
            ?? throw new InvalidOperationException($"Snippet #{s.Id} no longer exists.");
        string content = stored.IsImage ? (s.Content ?? string.Empty).TrimEnd('\r', '\n') : NormalizeContent(s.Content);
        string hash = stored.IsImage ? stored.ContentHash : Sha256(content);
        var pending = new Snippet
        {
            Id = s.Id,
            Title = s.Title,
            Content = content,
            ContentHash = hash,
            ExpirePolicy = s.ExpirePolicy,
            ExpireAt = s.ExpireAt,
        };
        DateTime updatedAt = DateTime.UtcNow;
        _repo.UpdateContent(pending, updatedAt, tags);
        s.Content = content;
        s.ContentHash = hash;
        s.UpdatedAt = updatedAt;
        Changed?.Invoke();
    }

    public void TogglePin(Snippet s)
    {
        bool isPinned = !s.IsPinned;
        _repo.SetPinned(s.Id, isPinned);
        s.IsPinned = isPinned;
        Changed?.Invoke();
    }

    // -------------------------------------------------------------- consume

    /// <summary>
    /// Registers a copy-out: bumps the usage counter and enforces
    /// burn-after-read (the snippet moves to trash after its first use, which
    /// still leaves the 30-day recovery window as a safety net).
    /// Returns true when the snippet was burned (views should drop the card).
    /// </summary>
    public bool RegisterUse(Snippet s)
    {
        bool burn = s.ExpirePolicy == ExpirePolicy.BurnAfterRead;
        RegisterUseResult result = _repo.RegisterUse(s.Id, burn);
        s.UseCount = result.UseCount;
        s.LastUsedAt = result.LastUsedAt;
        s.DeletedAt = result.DeletedAt;
        if (burn)
        {
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- trash

    public void AssignCategory(long id, long? categoryId)
    { _repo.SetCategory(id, categoryId); Changed?.Invoke(); }

    public void MoveToTrash(long id) { _repo.MoveToTrash(id); Changed?.Invoke(); }
    public void RestoreFromTrash(long id) { _repo.Restore(id); Changed?.Invoke(); }
    public void DeleteForever(long id) { _repo.HardDelete(id); Changed?.Invoke(); }

    public void EmptyTrash()
    {
        _repo.EmptyTrash();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- reads

    public Snippet? GetById(long id) => _repo.FindById(id);
    public List<Snippet> Recent(int limit = 100) => _repo.GetRecent(limit);
    public List<Snippet> AllLive() => _repo.GetAllLive();
    public List<Snippet> ByCategory(long? categoryId) => _repo.GetByCategory(categoryId);
    public List<Snippet> Trash() => _repo.GetTrash();

    // ------------------------------------------------------------- plumbing

    /// <summary>The workbench editor recomputes ExpireAt when the user
    /// changes a snippet's retention policy after creation.</summary>
    public static DateTime? ComputeExpireAt(ExpirePolicy p, DateTime now) => p switch
    {
        ExpirePolicy.OneDay => now.AddDays(1),
        ExpirePolicy.SevenDays => now.AddDays(7),
        _ => null,   // Never & BurnAfterRead have no wall-clock expiry
    };

    private static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static string NormalizeContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Snippet content must not be empty.", nameof(content));
        return content.TrimEnd('\r', '\n');
    }
}
