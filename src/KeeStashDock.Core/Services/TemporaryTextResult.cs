namespace KeeStashDock.Core.Services;

public enum ResultTextKind { Source, Translation, Both }

/// <summary>Owns a result revision; late translation and save completion cannot replace a new source.</summary>
public sealed class TemporaryTextResult
{
    public long Revision { get; private set; }
    public string Source { get; private set; } = string.Empty;
    public string Translation { get; private set; } = string.Empty;
    public bool Saving { get; private set; }
    private readonly HashSet<(ResultTextKind Kind, long? Group, string Text)> _saved = [];

    public void Reset()
    {
        Revision++;
        Source = Translation = string.Empty;
        Saving = false;
        _saved.Clear();
    }

    public void SetSource(string source) => Source = source;
    public bool SetTranslation(long revision, string translation)
    {
        if (revision != Revision) return false;
        Translation = translation;
        return true;
    }

    public string? Content(ResultTextKind kind) => kind switch
    {
        ResultTextKind.Source when !string.IsNullOrWhiteSpace(Source) => Source,
        ResultTextKind.Translation when !string.IsNullOrWhiteSpace(Translation) => Translation,
        ResultTextKind.Both when !string.IsNullOrWhiteSpace(Source) && !string.IsNullOrWhiteSpace(Translation)
            => $"Source:\n{Source}\n\nTranslation:\n{Translation}",
        _ => null,
    };

    public sealed record SaveRequest(long Revision, ResultTextKind Kind, long? Group, string Text);

    public SaveRequest? BeginSave(ResultTextKind kind, long? categoryId)
    {
        string? text = Content(kind);
        if (Saving || text is null || _saved.Contains((kind, categoryId, text))) return null;
        Saving = true;
        return new(Revision, kind, categoryId, text);
    }

    public bool CompleteSave(SaveRequest request, bool success)
    {
        if (request.Revision != Revision) return false;
        Saving = false;
        if (success) _saved.Add((request.Kind, request.Group, request.Text));
        return true;
    }
}
