using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Repositories;

namespace KeeStashDock.Core.Services;

/// <summary>
/// Search router (designed trade-off, documented in the schema migration):
///
/// * Space-delimited languages -> SQLite FTS5 MATCH with prefix queries
///   ("term*"), ranked by bm25.
/// * Queries containing CJK characters -> LIKE substring fallback, because
///   the built-in unicode61 tokenizer cannot segment Chinese/Japanese text.
///   Latency depends on the query and collection; measure representative data
///   before setting performance expectations. A segmenting tokenizer can be
///   introduced by a future schema migration without touching callers.
/// </summary>
public sealed class SearchService
{
    private readonly SnippetRepository _repo;
    public SearchService(SnippetRepository repo) => _repo = repo;

    public string DatabaseIdentity => _repo.DatabaseIdentity;

    public Task<SnippetPage> QueryAsync(SnippetQuery query, CancellationToken cancellationToken = default)
        => Task.Run(() => Query(query, cancellationToken), cancellationToken);

    public SnippetPage Query(SnippetQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        bool fts = !ContainsCjk(query.Text ?? string.Empty);
        try { return _repo.QueryPage(query, fts, cancellationToken); }
        catch (Microsoft.Data.Sqlite.SqliteException exception) when (fts && exception.SqliteErrorCode == 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _repo.QueryPage(query, useFts: false, cancellationToken);
        }
    }

    public List<Snippet> Search(string query, int limit = 200, bool trashOnly = false)
    {
        query = query.Trim();
        if (query.Length == 0) return trashOnly ? _repo.GetTrash().Take(limit).ToList() : _repo.GetRecent(limit);

        if (ContainsCjk(query))
            return _repo.SearchLike(query, limit, trashOnly);

        // Build a safe FTS expression: quote each token, add prefix wildcard.
        // Quoting neutralizes FTS operators (AND/OR/NEAR/"), preventing both
        // syntax errors and accidental query injection.
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => $"\"{t.Replace("\"", "\"\"")}\"*");
        string fts = string.Join(" ", tokens);
        try
        {
            return _repo.SearchFts(fts, limit, trashOnly);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Defensive: any residual FTS syntax edge case degrades to LIKE.
            return _repo.SearchLike(query, limit, trashOnly);
        }
    }

    /// <summary>True when the string contains CJK ideographs or kana.</summary>
    private static bool ContainsCjk(string s) => s.Any(c =>
        (c >= 0x4E00 && c <= 0x9FFF)  ||   // CJK Unified Ideographs
        (c >= 0x3400 && c <= 0x4DBF)  ||   // CJK Extension A
        (c >= 0x3040 && c <= 0x30FF)  ||   // Hiragana + Katakana
        (c >= 0xAC00 && c <= 0xD7AF));     // Hangul syllables
}
