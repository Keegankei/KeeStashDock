using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop.ViewModels;

public sealed partial class WorkbenchViewModel
{
    private string? _queryDatabaseIdentity;
    private long _workbenchQueryEpoch;
    private CancellationTokenSource? _queryCancellation;
    private BatchSelection? _batchSelection;
    private BatchUndoToken? _batchUndo;
    [ObservableProperty] private int _filterKindIndex;
    [ObservableProperty] private string _filterTag = string.Empty;
    [ObservableProperty] private bool _hasMoreResults;
    [ObservableProperty] private int _totalResults;
    [ObservableProperty] private bool _isBatchBusy;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _batchStatus = string.Empty;
    public bool HasBatchSelection => _batchSelection?.Items.Count > 0;
    public int BatchSelectedCount => _batchSelection?.Items.Count ?? 0;
    public bool CanUndoBatch => _batchUndo is not null;
    public bool ShowBatchBar => HasBatchSelection || CanUndoBatch || !string.IsNullOrEmpty(BatchStatus);
    public string BatchSelectionLabel => string.Format(LocalizationService.Instance["Batch.Selected"], BatchSelectedCount);
    public string ResultCountLabel => IsSearching ? LocalizationService.Instance["Search.Loading"] :
        string.Format(LocalizationService.Instance["Search.Count"], Items.Count, TotalResults);
    public Task QueryCompletion { get; private set; } = Task.CompletedTask;
    public event Action? BatchSelectionCleared;

    partial void OnFilterKindIndexChanged(int value) => ScheduleQuery(immediate: true);
    partial void OnFilterTagChanged(string value) => ScheduleQuery();
    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(ResultCountLabel));
    partial void OnTotalResultsChanged(int value) => OnPropertyChanged(nameof(ResultCountLabel));
    partial void OnBatchStatusChanged(string value) => OnPropertyChanged(nameof(ShowBatchBar));

    private SnippetQuery CurrentQuery(int offset = 0) => new()
    {
        Text = SearchText, Tag = FilterTag,
        Scope = SelectedNav?.Id switch
        { -2 => QueryScope.Pinned, -3 => QueryScope.Trash, -4 => QueryScope.Uncategorized, > 0 => QueryScope.Category, _ => QueryScope.All },
        CategoryId = SelectedNav?.Id > 0 ? SelectedNav.Id : null,
        Kind = (SnippetKindFilter)Math.Clamp(FilterKindIndex, 0, 3), Offset = offset, Limit = 50,
    };

    private void ScheduleQuery(bool immediate = false)
    {
        ClearBatchSelection();
        _queryCancellation?.Cancel();
        _queryCancellation?.Dispose();
        _queryCancellation = new CancellationTokenSource();
        long epoch = ++_workbenchQueryEpoch;
        HasMoreResults = false;
        QueryCompletion = FetchQueryAsync(CurrentQuery(), epoch, _queryCancellation.Token, immediate);
    }

    private async Task FetchQueryAsync(SnippetQuery query, long epoch, CancellationToken token, bool immediate)
    {
        IsSearching = true;
        try
        {
            if (!immediate) await Task.Delay(150, token);
            SnippetPage page = await _search.QueryAsync(query, token);
            string currentIdentity = await Task.Run(() => _snippets.DatabaseIdentity, token);
            if (token.IsCancellationRequested || epoch != _workbenchQueryEpoch) return;
            if (page.DatabaseIdentity != currentIdentity)
            { ScheduleQuery(immediate: true); return; }
            ApplyQueryPage(page);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (epoch != _workbenchQueryEpoch) return;
            BatchStatus = LocalizationService.Instance["Search.Failed"];
            App.Log?.Warn($"Workbench search failed: {error.GetType().Name}");
        }
        finally { if (epoch == _workbenchQueryEpoch) IsSearching = false; }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (!HasMoreResults || IsRefreshingItems || IsSearching) return;
        SnippetQuery query = CurrentQuery(Items.Count);
        string? identity = _queryDatabaseIdentity;
        long epoch = _workbenchQueryEpoch;
        try
        {
            SnippetPage page = await _search.QueryAsync(query);
            string currentIdentity = await Task.Run(() => _snippets.DatabaseIdentity);
            if (epoch != _workbenchQueryEpoch || (query with { Offset = 0 }) != CurrentQuery() || identity != _queryDatabaseIdentity) return;
            if (page.DatabaseIdentity != identity || page.DatabaseIdentity != currentIdentity)
            { ReloadItems(); return; }
            var existing = Items.Select(item => item.Id).ToHashSet();
            foreach (Snippet snippet in page.Items)
                if (existing.Add(snippet.Id)) Items.Add(new SnippetItemViewModel(snippet, _snippets.GetImageThumbnail));
            HasMoreResults = page.HasMore;
            TotalResults = page.TotalCount;
            OnPropertyChanged(nameof(ResultCountLabel));
        }
        catch (Exception error)
        {
            BatchStatus = LocalizationService.Instance["Search.Failed"];
            App.Log?.Warn($"Workbench page failed: {error.GetType().Name}");
        }
    }

    public void SetBatchSelection(IEnumerable<SnippetItemViewModel> selected)
    {
        if (IsRefreshingItems || IsBatchBusy) return;
        long[] ids = selected.Select(item => item.Id).Distinct().ToArray();
        if (ids.Length == 0) { ClearBatchSelection(); return; }
        try
        {
            var captured = _snippets.CaptureBatchSelection(ids, _queryDatabaseIdentity);
            // Retain earlier revisions for entries that stayed selected.
            // Adding another checkbox must not silently accept external edits.
            var previous = _batchSelection?.Items.ToDictionary(item => item.Id);
            _batchSelection = captured with
            { Items = captured.Items.Select(item => previous?.GetValueOrDefault(item.Id) ?? item).ToArray() };
            BatchStatus = string.Empty;
        }
        catch (BatchConflictException error)
        { _batchSelection = null; BatchStatus = LocalizationService.Instance["Batch.Conflict." + error.Reason]; }
        NotifyBatchSelection();
    }

    public void ClearBatchSelection()
    {
        _batchSelection = null;
        NotifyBatchSelection();
        BatchSelectionCleared?.Invoke();
    }

    private void NotifyBatchSelection()
    {
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(BatchSelectedCount));
        OnPropertyChanged(nameof(BatchSelectionLabel));
        OnPropertyChanged(nameof(CanUndoBatch));
        OnPropertyChanged(nameof(ShowBatchBar));
        OnPropertyChanged(nameof(ResultCountLabel));
    }

    public async Task<BatchConflictReason?> ApplyBatchAsync(BatchAction action, long? categoryId = null,
        IReadOnlyList<string>? tags = null, bool resetExpired = false)
    {
        if (IsBatchBusy || _batchSelection is not { Items.Count: > 0 } selection) return null;
        long saveVersion = _editorSaveVersion;
        long? editingId = _editor.ItemId;
        IsBatchBusy = true;
        try
        {
            if (await RequestLeaveAsync() != WorkbenchLeaveResult.Proceed) return null;
            if (_editorSaveVersion != saveVersion && editingId is long changedId && selection.Items.Any(item => item.Id == changedId))
            {
                var saved = _snippets.CaptureBatchSelection([changedId], selection.DatabaseIdentity).Items.Single();
                selection = selection with { Items = selection.Items.Select(item => item.Id == changedId ? saved : item).ToArray() };
            }
            BatchResult result = await Task.Run(() => _snippets.ApplyBatch(selection, action, categoryId, tags, resetExpired));
            if (result.Undo is not null) _batchUndo = result.Undo;
            BatchStatus = string.Format(LocalizationService.Instance["Batch.Done"], result.AffectedCount);
            ClearBatchSelection();
            ReloadItems();
            return null;
        }
        catch (BatchConflictException error)
        {
            BatchStatus = LocalizationService.Instance["Batch.Conflict." + error.Reason];
            return error.Reason;
        }
        catch (Exception error)
        {
            App.Log?.Warn($"Batch operation failed: {error.GetType().Name}");
            BatchStatus = LocalizationService.Instance["Batch.Failed"];
            return null;
        }
        finally { IsBatchBusy = false; NotifyBatchSelection(); }
    }

    [RelayCommand]
    private async Task UndoBatch()
    {
        if (IsBatchBusy || _batchUndo is not { } undo) return;
        IsBatchBusy = true;
        try
        {
            if (await RequestLeaveAsync() != WorkbenchLeaveResult.Proceed) return;
            BatchResult result = await Task.Run(() => _snippets.UndoTrash(undo));
            _batchUndo = null;
            BatchStatus = string.Format(LocalizationService.Instance["Batch.Done"], result.AffectedCount);
            ReloadItems();
        }
        catch (BatchConflictException)
        { BatchStatus = LocalizationService.Instance["Batch.UndoConflict"]; }
        catch (Exception error)
        { App.Log?.Warn($"Batch undo failed: {error.GetType().Name}"); BatchStatus = LocalizationService.Instance["Batch.Failed"]; }
        finally { IsBatchBusy = false; NotifyBatchSelection(); }
    }
}
