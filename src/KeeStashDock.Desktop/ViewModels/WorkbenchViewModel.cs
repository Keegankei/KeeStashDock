using System.Collections.ObjectModel;
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

    /// <summary>Sidebar entries. Virtual ids: -1 All, -2 Pinned, -3 Trash, -4 Uncategorized.</summary>
    public ObservableCollection<NavEntry> Nav { get; } = new();
    public ObservableCollection<SnippetItemViewModel> Items { get; } = new();

    [ObservableProperty] private NavEntry? _selectedNav;
    [ObservableProperty] private SnippetItemViewModel? _selectedItem;
    [ObservableProperty] private string _searchText = string.Empty;

    // Editor pane fields (bound two-way; committed by SaveCommand)
    [ObservableProperty] private string _editTitle = string.Empty;
    [ObservableProperty] private string _editContent = string.Empty;

    /// <summary>Editor expiry policy; index maps 1:1 to <see cref="ExpirePolicy"/>.</summary>
    [ObservableProperty] private int _editExpireIndex;

    /// <summary>Editor tag row, comma-separated (Chinese comma accepted too).</summary>
    [ObservableProperty] private string _editTags = string.Empty;

    public bool IsTrashView => SelectedNav?.Id == -3;

    public WorkbenchViewModel(SnippetService snippets, SearchService search,
                              CategoryRepository categories, TagRepository tags)
    {
        _snippets = snippets;
        _search = search;
        _categories = categories;
        _tags = tags;
        _snippets.Changed += () =>
            Application.Current.Dispatcher.BeginInvoke(ReloadItems);
        // Sidebar follows the category repo (single source of truth for
        // group hot-updates; panel-side or import-side changes land here too).
        _categories.Changed += () =>
            Application.Current.Dispatcher.BeginInvoke(ReloadNav);
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

    partial void OnSelectedItemChanged(SnippetItemViewModel? value)
    {
        // Load the selection into the editor pane.
        EditTitle = value?.Model.Title ?? string.Empty;
        EditContent = value?.Model.Content ?? string.Empty;
        EditExpireIndex = (int)(value?.Model.ExpirePolicy ?? ExpirePolicy.Never);
        EditTags = value is null
            ? string.Empty
            : string.Join(", ", _tags.GetForSnippet(value.Id));
    }

    public void ReloadItems()
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
        long? keep = SelectedItem?.Id;
        Items.Clear();
        foreach (var s in data) Items.Add(new SnippetItemViewModel(s));
        SelectedItem = Items.FirstOrDefault(i => i.Id == keep) ?? Items.FirstOrDefault();
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
    private void Save()
    {
        if (SelectedItem is null) return;
        var m = SelectedItem.Model;
        m.Title = string.IsNullOrWhiteSpace(EditTitle) ? null : EditTitle.Trim();
        m.Content = EditContent;

        // Expiry: recompute the wall-clock instant only when the POLICY changed,
        // so re-saving content never silently extends an existing countdown.
        var policy = (ExpirePolicy)Math.Clamp(EditExpireIndex, 0, 3);
        if (policy != m.ExpirePolicy)
        {
            m.ExpirePolicy = policy;
            m.ExpireAt = SnippetService.ComputeExpireAt(policy, DateTime.UtcNow);
        }

        // Tags: comma-separated row -> set (both ASCII and CJK commas accepted).
        _tags.SetForSnippet(m.Id,
            EditTags.Split(new[] { ',', '\uFF0C', '\u3001' },
                           StringSplitOptions.RemoveEmptyEntries));

        _snippets.Update(m);
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
        _categories.Insert(new Category { Name = loc["Workbench.NewCategoryName"] });
        // Changed event rebuilds the sidebar (here AND in the quick panel).
    }

    /// <summary>Sidebar context menu: rename a REAL category (virtual ids &lt;= 0
    /// are ignored). The view collects the new name via InputDialog.</summary>
    public void RenameCategory(long id, string newName)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(newName)) return;
        _categories.Rename(id, newName.Trim());   // Changed event refreshes nav
    }

    /// <summary>Post-import refresh: rebuild the sidebar and select the
    /// imported/overwritten group so the result is immediately visible.</summary>
    public void RefreshAfterGroupImport(string groupName)
    {
        ReloadNav();
        SelectedNav = Nav.FirstOrDefault(n =>
                string.Equals(n.Name, groupName, StringComparison.OrdinalIgnoreCase))
            ?? Nav[0];
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

    public sealed record NavEntry(long Id, string Name, string Icon);
}
