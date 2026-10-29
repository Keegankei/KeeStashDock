using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Core.Services;

public sealed record WorkbenchEditSnapshot(
    long ItemId,
    string? Title,
    string Content,
    ExpirePolicy ExpirePolicy,
    IReadOnlyList<string> Tags,
    bool IsTemplate = false);

public enum WorkbenchUnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

public enum WorkbenchLeaveResult
{
    Proceed,
    Cancelled,
    SaveFailed,
}

public enum WorkbenchExternalConflict
{
    None,
    Changed,
    Deleted,
}

/// <summary>
/// Window-independent editor state used by the workbench ViewModel. The
/// session owns the loaded snapshot, dirty comparison and leave decisions so
/// list refreshes and WPF lifetime events cannot silently replace the buffer.
/// </summary>
public sealed class WorkbenchEditorSession
{
    private string? _originalTitle;
    private string _originalContent = string.Empty;
    private ExpirePolicy _originalExpirePolicy;
    private string[] _originalTags = [];
    private bool _originalIsTemplate;
    private WorkbenchEditSnapshot? _latestExternalSnapshot;

    public long? ItemId { get; private set; }
    public string EditTitle { get; set; } = string.Empty;
    public string EditContent { get; set; } = string.Empty;
    public ExpirePolicy EditExpirePolicy { get; set; }
    public string EditTags { get; set; } = string.Empty;
    public bool EditIsTemplate { get; set; }
    public WorkbenchExternalConflict ExternalConflict { get; private set; }

    public bool IsDirty => ItemId is not null &&
        (NormalizeTitle(EditTitle) != _originalTitle ||
         EditContent != _originalContent ||
         EditExpirePolicy != _originalExpirePolicy ||
         EditIsTemplate != _originalIsTemplate ||
         !NormalizeTags(EditTags).SequenceEqual(_originalTags));

    public void Load(WorkbenchEditSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string[] tags = NormalizeTags(snapshot.Tags);
        ItemId = snapshot.ItemId;
        _originalTitle = NormalizeTitle(snapshot.Title);
        _originalContent = snapshot.Content ?? string.Empty;
        _originalExpirePolicy = snapshot.ExpirePolicy;
        _originalTags = tags;
        _originalIsTemplate = snapshot.IsTemplate;
        _latestExternalSnapshot = null;
        EditTitle = _originalTitle ?? string.Empty;
        EditContent = _originalContent;
        EditExpirePolicy = _originalExpirePolicy;
        EditTags = string.Join(", ", tags);
        EditIsTemplate = _originalIsTemplate;
        ExternalConflict = WorkbenchExternalConflict.None;
    }

    public void Clear()
    {
        ItemId = null;
        _originalTitle = null;
        _originalContent = string.Empty;
        _originalExpirePolicy = ExpirePolicy.Never;
        _originalTags = [];
        _originalIsTemplate = false;
        _latestExternalSnapshot = null;
        EditTitle = string.Empty;
        EditContent = string.Empty;
        EditExpirePolicy = ExpirePolicy.Never;
        EditTags = string.Empty;
        EditIsTemplate = false;
        ExternalConflict = WorkbenchExternalConflict.None;
    }

    /// <summary>
    /// Applies a refreshed database snapshot. Dirty buffers remain untouched;
    /// only a conflicting external editor-field change is recorded.
    /// </summary>
    public void MergeExternal(WorkbenchEditSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (ItemId != snapshot.ItemId)
            throw new InvalidOperationException("External snapshot does not match the editor item.");

        if (!IsDirty)
        {
            Load(snapshot);
            return;
        }

        _latestExternalSnapshot = snapshot;
        ExternalConflict = MatchesOriginal(snapshot)
            ? WorkbenchExternalConflict.None
            : WorkbenchExternalConflict.Changed;
    }

    public void MarkExternalDeleted()
    {
        if (ItemId is not null)
        {
            _latestExternalSnapshot = null;
            ExternalConflict = WorkbenchExternalConflict.Deleted;
        }
    }

    public WorkbenchEditSnapshot CreateSaveSnapshot()
    {
        if (ItemId is not long itemId)
            throw new InvalidOperationException("No workbench item is loaded.");

        return new WorkbenchEditSnapshot(
            itemId,
            NormalizeTitle(EditTitle),
            EditContent.TrimEnd('\r', '\n'),
            EditExpirePolicy,
            NormalizeTags(EditTags), EditIsTemplate);
    }

    public async Task<WorkbenchLeaveResult> SaveAsync(
        Func<WorkbenchEditSnapshot, CancellationToken, Task> saveAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saveAsync);
        if (!IsDirty)
            return WorkbenchLeaveResult.Proceed;

        WorkbenchEditSnapshot pending = CreateSaveSnapshot();
        try
        {
            await saveAsync(pending, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WorkbenchLeaveResult.Cancelled;
        }
        catch
        {
            return WorkbenchLeaveResult.SaveFailed;
        }

        Load(pending);
        return WorkbenchLeaveResult.Proceed;
    }

    public async Task<WorkbenchLeaveResult> ResolveUnsavedChangesAsync(
        Func<CancellationToken, Task<WorkbenchUnsavedChoice>> promptAsync,
        Func<WorkbenchEditSnapshot, CancellationToken, Task> saveAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(promptAsync);
        ArgumentNullException.ThrowIfNull(saveAsync);
        if (!IsDirty)
            return WorkbenchLeaveResult.Proceed;

        WorkbenchUnsavedChoice choice;
        try
        {
            choice = await promptAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return WorkbenchLeaveResult.Cancelled;
        }

        switch (choice)
        {
            case WorkbenchUnsavedChoice.Save:
                return await SaveAsync(saveAsync, cancellationToken).ConfigureAwait(false);
            case WorkbenchUnsavedChoice.Discard:
                Discard();
                return WorkbenchLeaveResult.Proceed;
            default:
                return WorkbenchLeaveResult.Cancelled;
        }
    }

    public async Task<WorkbenchLeaveResult> RequestSelectionChangeAsync(
        WorkbenchEditSnapshot? requested,
        Func<CancellationToken, Task<WorkbenchUnsavedChoice>> promptAsync,
        Func<WorkbenchEditSnapshot, CancellationToken, Task> saveAsync,
        CancellationToken cancellationToken = default)
    {
        if (requested?.ItemId == ItemId)
            return WorkbenchLeaveResult.Proceed;

        WorkbenchLeaveResult result = await ResolveUnsavedChangesAsync(
            promptAsync,
            saveAsync,
            cancellationToken).ConfigureAwait(false);
        if (result != WorkbenchLeaveResult.Proceed)
            return result;

        if (requested is null)
            Clear();
        else
            Load(requested);
        return WorkbenchLeaveResult.Proceed;
    }

    private void Discard()
    {
        if (_latestExternalSnapshot is not null)
        {
            Load(_latestExternalSnapshot);
            return;
        }

        EditTitle = _originalTitle ?? string.Empty;
        EditContent = _originalContent;
        EditExpirePolicy = _originalExpirePolicy;
        EditTags = string.Join(", ", _originalTags);
        EditIsTemplate = _originalIsTemplate;
    }

    private bool MatchesOriginal(WorkbenchEditSnapshot snapshot)
        => NormalizeTitle(snapshot.Title) == _originalTitle &&
           (snapshot.Content ?? string.Empty) == _originalContent &&
           snapshot.ExpirePolicy == _originalExpirePolicy &&
           snapshot.IsTemplate == _originalIsTemplate &&
           NormalizeTags(snapshot.Tags).SequenceEqual(_originalTags);

    private static string? NormalizeTitle(string? title)
        => string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    private static string[] NormalizeTags(string tags)
        => NormalizeTags(tags.Split(
            [',', '\uFF0C', '\u3001'],
            StringSplitOptions.RemoveEmptyEntries));

    private static string[] NormalizeTags(IEnumerable<string> tags)
        => tags.Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
