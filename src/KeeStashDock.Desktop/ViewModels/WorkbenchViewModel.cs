using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Repositories;

namespace KeeStashDock.Desktop.ViewModels;

/// <summary>
/// View model of the full workbench (the "expanded" state):
/// sidebar navigation (All / Pinned / per-category / Trash), full-text search,
/// the snippet editor pane, and the built-in text-transform actions.
/// </summary>
public sealed partial class WorkbenchViewModel : ObservableObject
{
    private readonly SnippetService _snippets;
    private readonly SearchService _search;
    private readonly CategoryRepository _categories;
    private readonly TagRepository _tags;
    private readonly WorkbenchEditorSession _editor = new();
    private readonly Func<CancellationToken, Task<WorkbenchUnsavedChoice>> _promptAsync;
    private readonly Action<Action> _dispatch;
    private SnippetItemViewModel? _selectedItem;
    private Snippet? _editingModel;
    private bool _hasSaveError;
    private string _saveErrorKey = "Workbench.Editor.SaveFailed";

    /// <summary>Sidebar entries. Virtual ids: -1 All, -2 Pinned, -3 Trash, -4 Uncategorized.</summary>
    public ObservableCollection<NavEntry> Nav { get; } = new();
    public ObservableCollection<SnippetItemViewModel> Items { get; } = new();

    [ObservableProperty] private NavEntry? _selectedNav;
    [ObservableProperty] private string _searchText = string.Empty;

    public SnippetItemViewModel? SelectedItem
    {
        get => _selectedItem;
        private set => SetProperty(ref _selectedItem, value);
    }

    public string EditTitle
    {
        get => _editor.EditTitle;
        set => SetEditorValue(_editor.EditTitle, value, v => _editor.EditTitle = v);
    }

    public string EditContent
    {
        get => _editor.EditContent;
        set => SetEditorValue(_editor.EditContent, value, v => _editor.EditContent = v);
    }

    /// <summary>Editor expiry policy; index maps 1:1 to <see cref="ExpirePolicy"/>.</summary>
    public int EditExpireIndex
    {
        get => (int)_editor.EditExpirePolicy;
        set
        {
            int normalized = Math.Clamp(value, 0, 3);
            if ((int)_editor.EditExpirePolicy == normalized) return;
            _editor.EditExpirePolicy = (ExpirePolicy)normalized;
            OnPropertyChanged();
            NotifyEditorStateChanged();
        }
    }

    /// <summary>Editor tag row, comma-separated (Chinese comma accepted too).</summary>
    public string EditTags
    {
        get => _editor.EditTags;
        set => SetEditorValue(_editor.EditTags, value, v => _editor.EditTags = v);
    }

    public bool IsTrashView => SelectedNav?.Id == -3;
    public bool IsDirty => _editor.IsDirty;
    public bool HasExternalConflict =>
        _editor.ExternalConflict != WorkbenchExternalConflict.None;
    public bool HasSaveError
    {
        get => _hasSaveError;
        private set => SetProperty(ref _hasSaveError, value);
    }
    public bool IsRefreshingItems { get; private set; }
    public string SaveErrorMessage => LocalizationService.Instance[_saveErrorKey];

    public WorkbenchViewModel(SnippetService snippets, SearchService search,
                              CategoryRepository categories, TagRepository tags,
                              Func<CancellationToken, Task<WorkbenchUnsavedChoice>>? promptAsync = null,
                              Action<Action>? dispatch = null)
    {
        _snippets = snippets;
        _search = search;
        _categories = categories;
        _tags = tags;
        _promptAsync = promptAsync ??
            (_ => Task.FromResult(WorkbenchUnsavedChoice.Cancel));
        _dispatch = dispatch ?? DispatchOnUiThread;
        _snippets.Changed += () => _dispatch(ReloadItems);
        // Sidebar follows the category repo (single source of truth for
        // group hot-updates; panel-side or import-side changes land here too).
        _categories.Changed += () => _dispatch(ReloadNav);
        ReloadNav();
    }

    // ------------------------------------------------------------- loading

    /// <summary>Rebuilds the sidebar; the selection survives BY ID when the
    /// entry still exists (rename keeps you in place, delete falls back to
    /// "All"). Safe to call repeatedly - it is the Changed-event handler.</summary>
    public void ReloadNav()
    {
        long? keep = SelectedNav?.Id;
        var loc = LocalizationService.Instance;
        Nav.Clear();
        Nav.Add(new NavEntry(-1, loc["Workbench.AllCategories"], "\uE8FD"));
        Nav.Add(new NavEntry(-2, loc["Workbench.Pinned"], "\uE718"));
        Nav.Add(new NavEntry(-4, loc["Workbench.Uncategorized"], "\uE8B7"));
        foreach (var c in _categories.GetAll())
            Nav.Add(new NavEntry(c.Id, c.Name, c.Icon));
        Nav.Add(new NavEntry(-3, loc["Workbench.Trash"], "\uE74D"));
        SelectedNav = Nav.FirstOrDefault(n => n.Id == keep) ?? Nav[0];
    }

    partial void OnSelectedNavChanged(NavEntry? value)
    {
        OnPropertyChanged(nameof(IsTrashView));
        ReloadItems();
    }

    partial void OnSearchTextChanged(string value) => ReloadItems();

    public void ReloadItems()
    {
        IsRefreshingItems = true;
        try
        {
            List<Snippet> data;
            if (!string.IsNullOrWhiteSpace(SearchText))
                data = _search.Search(SearchText);
            else
                data = SelectedNav?.Id switch
                {
                    -2 => _snippets.AllLive().Where(s => s.IsPinned).ToList(),
                    -3 => _snippets.Trash(),
                    -4 => _snippets.ByCategory(null),
                    > 0 => _snippets.ByCategory(SelectedNav.Id),
                    _ => _snippets.AllLive(),
                };

            long? editingId = _editor.ItemId;
            Items.Clear();
            foreach (Snippet snippet in data)
                Items.Add(new SnippetItemViewModel(snippet, _snippets.GetImageThumbnail));

            if (editingId is long id)
            {
                SnippetItemViewModel? refreshed = Items.FirstOrDefault(item => item.Id == id);
                if (refreshed is null)
                {
                    _editor.MarkExternalDeleted();
                    SelectedItem = null;
                }
                else
                {
                    _editingModel = refreshed.Model;
                    _editor.MergeExternal(CreateSnapshot(refreshed.Model));
                    SelectedItem = refreshed;
                }

                NotifyEditorBufferChanged();
                return;
            }

            CommitSelection(Items.FirstOrDefault());
        }
        finally
        {
            IsRefreshingItems = false;
        }
    }

    public async Task<bool> RequestSelectionChangeAsync(
        SnippetItemViewModel? requested,
        CancellationToken cancellationToken = default)
    {
        WorkbenchEditSnapshot? requestedSnapshot = requested is null
            ? null
            : CreateSnapshot(requested.Model);
        WorkbenchLeaveResult result = await _editor.RequestSelectionChangeAsync(
            requestedSnapshot,
            _promptAsync,
            PersistSnapshotAsync,
            cancellationToken);
        HasSaveError = result == WorkbenchLeaveResult.SaveFailed;
        NotifyEditorBufferChanged();
        if (result != WorkbenchLeaveResult.Proceed)
            return false;

        SelectedItem = requested;
        _editingModel = requested?.Model;
        return true;
    }

    public async Task<WorkbenchLeaveResult> RequestLeaveAsync(
        CancellationToken cancellationToken = default)
    {
        WorkbenchLeaveResult result = await _editor.ResolveUnsavedChangesAsync(
            _promptAsync,
            PersistSnapshotAsync,
            cancellationToken);
        HasSaveError = result == WorkbenchLeaveResult.SaveFailed;
        NotifyEditorBufferChanged();
        return result;
    }

    // ------------------------------------------------------------ commands

    [RelayCommand]
    private void NewSnippet()
    {
        long? cat = SelectedNav?.Id > 0 ? SelectedNav.Id : null;
        _snippets.Add(LocalizationService.Instance["Workbench.NewSnippetBody"],
                      SnippetSource.Manual, categoryId: cat);
    }

    [RelayCommand]
    private async Task Save()
    {
        WorkbenchLeaveResult result = await _editor.SaveAsync(PersistSnapshotAsync);
        HasSaveError = result == WorkbenchLeaveResult.SaveFailed;
        NotifyEditorBufferChanged();
    }

    [RelayCommand] private void Delete()
    { if (SelectedItem is not null) _snippets.MoveToTrash(SelectedItem.Id); }

    [RelayCommand] private void Restore()
    { if (SelectedItem is not null) _snippets.RestoreFromTrash(SelectedItem.Id); }

    [RelayCommand] private void DeleteForever()
    { if (SelectedItem is not null) _snippets.DeleteForever(SelectedItem.Id); }

    [RelayCommand] private void EmptyTrash() => _snippets.EmptyTrash();

    [RelayCommand]
    private void AddCategory()
    {
        var loc = LocalizationService.Instance;
        try
        {
            _categories.Insert(new Category
            { Name = _categories.GetUniqueName(loc["Workbench.NewCategoryName"]) });
        }
        catch (Exception ex)
        {
            ShowCategoryError(ex);
        }
        // Changed event rebuilds the sidebar (here AND in the quick panel).
    }

    /// <summary>Sidebar context menu: rename a REAL category (virtual ids &lt;= 0
    /// are ignored). The view collects the new name via InputDialog.</summary>
    public void RenameCategory(long id, string newName)
    {
        if (id <= 0) return;
        try { _categories.Rename(id, newName); }
        catch (Exception ex) { ShowCategoryError(ex); }
    }

    private static void ShowCategoryError(Exception error)
    {
        var loc = LocalizationService.Instance;
        string message = error switch
        {
            DuplicateCategoryNameException duplicate =>
                string.Format(loc["Workbench.Category.DuplicateName"], duplicate.Name),
            ArgumentException => loc["Workbench.Category.EmptyName"],
            _ => loc["Workbench.Category.OperationFailed"],
        };
        App.Log?.Warn($"Group save failed: {error.GetType().Name}");
        MessageBox.Show(message, loc["Common.Confirm"],
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Post-import refresh: rebuild the sidebar and select the
    /// imported/overwritten group so the result is immediately visible.</summary>
    public void RefreshAfterGroupImport(string groupName)
    {
        long? categoryId = _categories.FindByName(groupName)?.Id;
        ReloadNav();
        SelectedNav = Nav.FirstOrDefault(n => n.Id == categoryId) ?? Nav[0];
        ReloadItems();
    }

    /// <summary>Sidebar context menu: delete a category. Snippets survive -
    /// the FK is ON DELETE SET NULL, so they land in "Uncategorized".</summary>
    public void DeleteCategory(long id)
    {
        if (id <= 0) return;
        _categories.Delete(id);   // Changed event refreshes nav; the deleted
        ReloadItems();            // id is gone, so selection falls back to All
    }

    /// <summary>Deletes a group TOGETHER with its snippets. The snippets go
    /// to the TRASH (recoverable), never straight to oblivion - "delete with
    /// contents" should still survive a slip of the mouse.</summary>
    public void DeleteCategoryWithContents(long id)
    {
        if (id <= 0) return;
        foreach (var sn in _snippets.ByCategory(id))
            _snippets.MoveToTrash(sn.Id);
        _categories.Delete(id);
        ReloadItems();
    }

    /// <summary>Built-in text transforms - parameter selects the action.
    /// Transforms operate on the editor buffer; user saves explicitly.</summary>
    [RelayCommand]
    private void Transform(string action)
    {
        string src = EditContent;
        string? result = action switch
        {
            "upper"      => TextTransforms.ToUpper(src),
            "lower"      => TextTransforms.ToLower(src),
            "collapse"   => TextTransforms.CollapseWhitespace(src),
            "nolinebrk"  => TextTransforms.RemoveLineBreaks(src),
            "json"       => TextTransforms.FormatJson(src),
            "urlenc"     => TextTransforms.UrlEncode(src),
            "urldec"     => TextTransforms.UrlDecode(src),
            "b64enc"     => TextTransforms.Base64Encode(src),
            "b64dec"     => TextTransforms.Base64Decode(src),
            _            => src,
        };
        if (result is not null) EditContent = result;
        // null result (invalid JSON / Base64) leaves the buffer untouched.
    }

    private void CommitSelection(SnippetItemViewModel? item)
    {
        SelectedItem = item;
        _editingModel = item?.Model;
        HasSaveError = false;
        if (item is null)
            _editor.Clear();
        else
            _editor.Load(CreateSnapshot(item.Model));
        NotifyEditorBufferChanged();
    }

    private WorkbenchEditSnapshot CreateSnapshot(Snippet model) => new(
        model.Id,
        model.Title,
        model.Content,
        model.ExpirePolicy,
        _tags.GetForSnippet(model.Id));

    private Task PersistSnapshotAsync(
        WorkbenchEditSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_editingModel is null || _editingModel.Id != snapshot.ItemId)
            throw new InvalidOperationException("The edited snippet is no longer selected.");

        Snippet pending = CloneSnippet(_editingModel);
        pending.Title = snapshot.Title;
        pending.Content = snapshot.Content;
        if (snapshot.ExpirePolicy != pending.ExpirePolicy)
        {
            pending.ExpirePolicy = snapshot.ExpirePolicy;
            pending.ExpireAt = SnippetService.ComputeExpireAt(
                snapshot.ExpirePolicy,
                DateTime.UtcNow);
        }

        _saveErrorKey = "Workbench.Editor.SaveFailed";
        try
        {
            _snippets.Update(pending, snapshot.Tags);
        }
        catch (DuplicateSnippetContentException)
        {
            _saveErrorKey = "Workbench.Editor.DuplicateContent";
            throw;
        }
        catch (ArgumentException ex) when (ex.ParamName == "content")
        {
            _saveErrorKey = "Workbench.Editor.EmptyContent";
            throw;
        }
        finally
        {
            OnPropertyChanged(nameof(SaveErrorMessage));
        }
        _editingModel = pending;
        return Task.CompletedTask;
    }

    private static Snippet CloneSnippet(Snippet source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Content = source.Content,
        ContentHash = source.ContentHash,
        Type = source.Type,
        Source = source.Source,
        CategoryId = source.CategoryId,
        IsPinned = source.IsPinned,
        ExpirePolicy = source.ExpirePolicy,
        ExpireAt = source.ExpireAt,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
        DeletedAt = source.DeletedAt,
        SortWeight = source.SortWeight,
        UseCount = source.UseCount,
        LastUsedAt = source.LastUsedAt,
    };

    private void SetEditorValue(
        string current,
        string value,
        Action<string> assign,
        [CallerMemberName] string? propertyName = null)
    {
        value ??= string.Empty;
        if (current == value) return;
        assign(value);
        OnPropertyChanged(propertyName);
        NotifyEditorStateChanged();
    }

    private void NotifyEditorStateChanged()
    {
        OnPropertyChanged(nameof(SaveErrorMessage));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasExternalConflict));
    }

    private void NotifyEditorBufferChanged()
    {
        OnPropertyChanged(nameof(EditTitle));
        OnPropertyChanged(nameof(EditContent));
        OnPropertyChanged(nameof(EditExpireIndex));
        OnPropertyChanged(nameof(EditTags));
        NotifyEditorStateChanged();
    }

    private static void DispatchOnUiThread(Action action)
    {
        System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    public sealed record NavEntry(long Id, string Name, string Icon);
}
