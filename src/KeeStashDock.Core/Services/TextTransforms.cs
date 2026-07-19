using System.Text;
using System.Text.Json;

namespace KeeStashDock.Core.Services;

/// <summary>
/// Built-in text processing actions surfaced in the snippet context menu.
/// Each transform is a pure string-to-string function, except operations that
/// return null to report invalid input without mutating the snippet.
/// </summary>
public static class TextTransforms
{
    public static string ToUpper(string s) => s.ToUpperInvariant();
    public static string ToLower(string s) => s.ToLowerInvariant();

    /// <summary>Collapse all whitespace runs to single spaces and trim.</summary>
    public static string CollapseWhitespace(string s)
        => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Remove line breaks (joins wrapped text copied from PDFs etc.).</summary>
    public static string RemoveLineBreaks(string s)
        => s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>Pretty-print JSON; returns null when input is not valid JSON
    /// (caller shows a localized toast instead of mutating the snippet).</summary>
    public static string? FormatJson(string s)
    {
        try
        {
            using var doc = JsonDocument.Parse(s);
            return JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException) { return null; }
    }

    public static string UrlEncode(string s) => Uri.EscapeDataString(s);
    public static string UrlDecode(string s) => Uri.UnescapeDataString(s);
    public static string Base64Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    public static string? Base64Decode(string s)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s.Trim())); }
        catch (FormatException) { return null; }
    }

    /// <summary>Characters / non-whitespace chars / words / lines, for the "word count" action.</summary>
    public static (int Chars, int NonWs, int Words, int Lines) Count(string s)
    {
        int words = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        int lines = s.Length == 0 ? 0 : s.Split('\n').Length;
        int nonWs = s.Count(c => !char.IsWhiteSpace(c));
        return (s.Length, nonWs, words, lines);
    }
}
