using System.Globalization;
using System.Text;

namespace KeeStashDock.Core.Services;

public sealed class TemplateParseException(string message, int position) : FormatException(message)
{
    /// <summary>Zero-based UTF-16 offset in the stored template.</summary>
    public int Position { get; } = position;
}

/// <summary>A bounded, purely textual template. It cannot evaluate expressions or access external state.</summary>
public sealed class TemplateDocument
{
    internal sealed record Part(string Text, bool Variable, int Position);
    private readonly IReadOnlyList<Part> _parts;
    internal TemplateDocument(IReadOnlyList<Part> parts, IReadOnlyList<string> variables)
    { _parts = parts; Variables = variables; }

    public IReadOnlyList<string> Variables { get; }

    public string Render(IReadOnlyDictionary<string, string> values, DateTime snapshot)
    {
        ArgumentNullException.ThrowIfNull(values);
        var output = new StringBuilder();
        foreach (Part part in _parts)
        {
            string value = part.Text;
            if (part.Variable)
            {
                value = part.Text switch
                {
                    "date" => snapshot.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "time" => snapshot.ToString("HH:mm", CultureInfo.InvariantCulture),
                    _ => values.TryGetValue(part.Text, out string? provided) && provided is not null ? provided
                        : throw new TemplateParseException($"Missing value for {part.Text}.", part.Position),
                };
            }
            if ((long)output.Length + value.Length > TemplateParser.MaxTextLength)
                throw new TemplateParseException("The expanded text exceeds the supported size.", part.Position);
            // Values are appended verbatim and are never parsed recursively.
            output.Append(value);
        }
        return output.ToString();
    }
}

public static class TemplateParser
{
    public const int MaxTextLength = 1_000_000;
    public const int MaxFields = 64;

    public static TemplateDocument Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Length > MaxTextLength) throw new TemplateParseException("The template exceeds the supported size.", 0);
        var parts = new List<TemplateDocument.Part>();
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int position = 0, literalStart = 0;
        while (position < template.Length)
        {
            if (!template.AsSpan(position).StartsWith("{{", StringComparison.Ordinal))
            {
                if (template.AsSpan(position).StartsWith("}}", StringComparison.Ordinal))
                    throw new TemplateParseException("Closing braces have no matching opening braces.", position);
                position++;
                continue;
            }
            if (literalStart < position) parts.Add(new(template[literalStart..position], false, literalStart));
            bool escaped = template.AsSpan(position).StartsWith("{{{{", StringComparison.Ordinal);
            int delimiterLength = escaped ? 4 : 2;
            int end = template.IndexOf(escaped ? "}}}}" : "}}", position + delimiterLength, StringComparison.Ordinal);
            if (end < 0) throw new TemplateParseException("Opening braces have no matching closing braces.", position);
            string name = template[(position + delimiterLength)..end];
            if (escaped) parts.Add(new("{{" + name + "}}", false, position));
            else
            {
                if (!ValidName(name)) throw new TemplateParseException("Use a variable name made of letters, digits and underscores.", position);
                parts.Add(new(name, true, position));
                if (name is not ("date" or "time") && seen.Add(name))
                {
                    if (fields.Count == MaxFields) throw new TemplateParseException("The template contains too many input fields.", position);
                    fields.Add(name);
                }
            }
            position = end + delimiterLength;
            literalStart = position;
        }
        if (literalStart < template.Length) parts.Add(new(template[literalStart..], false, literalStart));
        return new TemplateDocument(parts.AsReadOnly(), fields.AsReadOnly());
    }

    private static bool ValidName(string name)
    {
        if (name.Length is 0 or > 128) return false;
        bool first = true;
        foreach (Rune rune in name.EnumerateRunes())
        {
            if (rune.Value != '_' && !(first ? Rune.IsLetter(rune) : Rune.IsLetterOrDigit(rune))) return false;
            first = false;
        }
        return !first;
    }
}
