using System.Text.RegularExpressions;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Core.Services;

/// <summary>
/// Heuristic content-type detection for freshly stashed text.
/// Order matters: cheap and specific checks first. Users can always override
/// the detected type in the workbench editor, so heuristics only need to be
/// "usually right", never perfect.
/// </summary>
public static partial class TypeDetector
{
    [GeneratedRegex(@"^https?://\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^(?:[a-zA-Z]:\\|\\\\)[^\r\n]+$")]
    private static partial Regex WindowsPathRegex();

    public static SnippetType Detect(string content)
    {
        string trimmed = content.Trim();

        // Single-line URL -> Link
        if (!trimmed.Contains('\n') && UrlRegex().IsMatch(trimmed))
            return SnippetType.Link;

        // Single-line absolute Windows path (drive or UNC) that exists -> FilePath
        if (!trimmed.Contains('\n') && WindowsPathRegex().IsMatch(trimmed)
            && (File.Exists(trimmed) || Directory.Exists(trimmed)))
            return SnippetType.FilePath;

        // Markdown signals: headings, fenced blocks, list markers at line start
        if (Regex.IsMatch(trimmed, @"(?m)^(#{1,6}\s|```|\*\s|-\s\[|\d+\.\s)"))
            return SnippetType.Markdown;

        // Code signals: braces/semicolons density or common keywords
        int structural = trimmed.Count(c => c is '{' or '}' or ';');
        if (structural >= 3 ||
            Regex.IsMatch(trimmed, @"\b(function|def |class |public |private |import |using |var |let |const )"))
            return SnippetType.Code;

        return SnippetType.PlainText;
    }
}
