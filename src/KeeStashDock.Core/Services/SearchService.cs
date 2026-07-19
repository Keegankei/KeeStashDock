using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Repositories;

namespace KeeStashDock.Core.Services;

/// <summary>
/// Search router (designed trade-off, documented in the schema migration):
///
/// * Space-delimited languages -> SQLite FTS5 MATCH with prefix queries
///   ("term*"), ranked by bm25, fast at 10k+ rows.
/// * Queries containing CJK characters -> LIKE substring fallback, because
///   the built-in unicode61 tokenizer cannot segment Chinese/Japanese text.
///   LIKE over ~10k rows of short snippets is still well inside the 100 ms
///   budget; if the corpus outgrows it, a segmenting tokenizer extension can
///   be introduced by a future schema migration without touching callers.
/// </summary>
public sealed class SearchService
{
    private readonly SnippetRepository _repo;
    public SearchService(SnippetRepository repo) => _repo = repo;

    public List<Snippet> Search(string query, int limit = 200)
    {
        query = query.Trim();
        if (query.Length == 0) return _repo.GetRecent(limit);

        if (ContainsCjk(query))
            return _repo.SearchLike(query, limit);

        // Build a safe FTS expression: quote each token, add prefix wildcard.
        // Quoting neutralizes FTS operators (AND/OR/NEAR/"), preventing both
        // syntax errors and accidental query injection.
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => $"\"{t.Replace("\"", "\"\"")}\"*");
        string fts = string.Join(" ", tokens);
        try
        {
            return _repo.SearchFts(fts, limit);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Defensive: any residual FTS syntax edge case degrades to LIKE.
            return _repo.SearchLike(query, limit);
        }
    }

    /// <summary>True when the string contains CJK ideographs or kana.</summary>
    private static bool ContainsCjk(string s) => s.Any(c =>
        (c >= 0x4E00 && c <= 0x9FFF)  ||   // CJK Unified Ideographs
        (c >= 0x3400 && c <= 0x4DBF)  ||   // CJK Extension A
        (c >= 0x3040 && c <= 0x30FF)  ||   // Hiragana + Katakana
        (c >= 0xAC00 && c <= 0xD7AF));     // Hangul syllables
}
