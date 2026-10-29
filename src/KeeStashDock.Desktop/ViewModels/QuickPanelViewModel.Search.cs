using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop.ViewModels;

public sealed partial class QuickPanelViewModel
{
    private CancellationTokenSource? _searchCancellation;
    private long _searchEpoch;
    private string? _searchDatabaseIdentity;
    private bool _reloadingSearchGroups;
    [ObservableProperty] private bool _isSearchMode;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _hasMoreSearchResults;
    [ObservableProperty] private string _searchStatus = string.Empty;
    [ObservableProperty] private string _searchTag = string.Empty;
    [ObservableProperty] private int _searchKindIndex;
    [ObservableProperty] private int _searchGroupIndex;
    public ObservableCollection<SearchGroupChoice> SearchGroups { get; } = new();
    public Task SearchCompletion { get; private set; } = Task.CompletedTask;

    public void BeginSearch()
    {
        if (_disposed || IsSearchMode) return;
        SearchGroupIndex = GroupIndex > 0 ? GroupIndex + 1 : 0;
        IsSearchMode = true;
        ScheduleSearch(immediate: true);
    }

    public void EndSearch()
    {
        if (_disposed || !IsSearchMode) return;
        IsSearchMode = false;
        _searchEpoch++;
        _searchCancellation?.Cancel();
        IsSearching = false;
        Reload();
    }

    partial void OnSearchTagChanged(string value) => ScheduleSearch();
    partial void OnSearchKindIndexChanged(int value) => ScheduleSearch(immediate: true);
    partial void OnSearchGroupIndexChanged(int value)
    {
        if (!_reloadingSearchGroups) ScheduleSearch(immediate: true);
    }

    private void ReloadSearchGroups()
    {
        long? selected = SearchGroups.ElementAtOrDefault(SearchGroupIndex)?.Id;
        _reloadingSearchGroups = true;
        try
        {
            SearchGroups.Clear();
            SearchGroups.Add(new(-1, LocalizationService.Instance["Search.Scope.All"]));
            SearchGroups.Add(new(-4, LocalizationService.Instance["Workbench.Uncategorized"]));
            foreach (var category in _cats) SearchGroups.Add(new(category.Id, category.Name));
            SearchGroupIndex = Math.Max(0, SearchGroups.ToList().FindIndex(choice => choice.Id == selected));
        }
        finally { _reloadingSearchGroups = false; }
    }

    private SnippetQuery CurrentSearchQuery(int offset = 0)
    {
        long scope = SearchGroups.ElementAtOrDefault(SearchGroupIndex)?.Id ?? -1;
        return new SnippetQuery
        {
            Text = Filter, Tag = SearchTag,
            Scope = scope == -4 ? QueryScope.Uncategorized : scope > 0 ? QueryScope.Category : QueryScope.All,
            CategoryId = scope > 0 ? scope : null,
            Kind = (SnippetKindFilter)Math.Clamp(SearchKindIndex, 0, 3),
            Offset = offset, Limit = 50,
        };
    }

    private void ScheduleSearch(bool immediate = false)
    {
        if (_disposed || !IsSearchMode) return;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        long epoch = ++_searchEpoch;
        HasMoreSearchResults = false;
        SearchCompletion = FetchSearchAsync(CurrentSearchQuery(), epoch, _searchCancellation.Token,
            append: false, immediate);
    }

    [RelayCommand]
    private async Task LoadMoreSearch()
    {
        if (_disposed || !IsSearchMode || IsSearching || !HasMoreSearchResults || _searchCancellation is null) return;
        SearchCompletion = FetchSearchAsync(CurrentSearchQuery(Items.Count), _searchEpoch,
            _searchCancellation.Token, append: true, immediate: true);
        await SearchCompletion;
    }

    private async Task FetchSearchAsync(SnippetQuery query, long epoch, CancellationToken token, bool append, bool immediate)
    {
        IsSearching = true;
        SearchStatus = LocalizationService.Instance["Search.Loading"];
        try
        {
            if (!immediate) await Task.Delay(150, token);
            SnippetPage page = await _search.QueryAsync(query, token);
            string currentIdentity = await Task.Run(() => _snippets.DatabaseIdentity, token);
            if (token.IsCancellationRequested || epoch != _searchEpoch || !IsSearchMode) return;
            if (page.DatabaseIdentity != currentIdentity ||
                (append && page.DatabaseIdentity != _searchDatabaseIdentity))
            { ScheduleSearch(immediate: true); return; }
            _searchDatabaseIdentity = page.DatabaseIdentity;
            if (!append) Items.Clear();
            var loaded = Items.Select(item => item.Id).ToHashSet();
            foreach (Snippet item in page.Items)
                if (loaded.Add(item.Id)) Items.Add(new SnippetItemViewModel(item, _snippets.GetImageThumbnail));
            HasMoreSearchResults = page.HasMore;
            SearchStatus = string.Format(LocalizationService.Instance["Search.Count"], Items.Count, page.TotalCount);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (epoch != _searchEpoch) return;
            SearchStatus = LocalizationService.Instance["Search.Failed"];
            App.Log?.Warn($"Search failed: {error.GetType().Name}");
        }
        finally { if (epoch == _searchEpoch) IsSearching = false; }
    }

    public sealed record SearchGroupChoice(long Id, string Name);
}
