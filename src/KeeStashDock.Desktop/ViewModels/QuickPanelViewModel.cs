using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Repositories;

namespace KeeStashDock.Desktop.ViewModels;

/// <summary>
/// View model of the slide-out quick panel (the "peek" state).
/// Responsibilities: quick-add from the input box, live filtering of recent
/// cards, copy-out / pin / trash commands. Data reloads whenever the
/// SnippetService announces a change, keeping panel and workbench in sync.
/// </summary>
public sealed partial class QuickPanelViewModel : ObservableObject
{
    private readonly SnippetService _snippets;
    private readonly SearchService _search;
    private readonly CategoryRepository _categories;
    private List<Category> _cats = new();
    private CancellationTokenSource? _toastCancellation;

    public ObservableCollection<SnippetItemViewModel> Items { get; } = new();

    [ObservableProperty] private string _quickInput = string.Empty;
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _isLocked;          // lock = pin current panel state
    [ObservableProperty] private string? _toast;          // transient "Copied ✓" feedback

    /// <summary>Expiry policy for the NEXT quick stash. Index maps 1:1 to
    /// <see cref="ExpirePolicy"/>: 0 Never, 1 OneDay, 2 SevenDays, 3 BurnAfterRead.</summary>
    [ObservableProperty] private int _expireIndex;

    /// <summary>Group filter combo: index 0 = all groups, then real
    /// categories in sidebar order. New stashes land in the selected group.</summary>
    public ObservableCollection<string> GroupNames { get; } = new();
    [ObservableProperty] private int _groupIndex;

    /// <summary>Category id behind the current selection (null = all).</summary>
    public long? SelectedCategoryId =>
        GroupIndex > 0 && GroupIndex <= _cats.Count ? _cats[GroupIndex - 1].Id : null;

    public event Action<string>? DraftChanged;            // wired to DraftGuard

    public QuickPanelViewModel(SnippetService snippets, SearchService search,
                               CategoryRepository categories)
    {
        _snippets = snippets;
        _search = search;
        _categories = categories;
        _snippets.Changed += () =>
            Application.Current.Dispatcher.BeginInvoke(Reload);
        // Group hot-update: rename/add/delete/import of groups
        // reflects in the combo IMMEDIATELY, even while the panel is open.
        _categories.Changed += () =>
            Application.Current.Dispatcher.BeginInvoke(() =>
            { ReloadCategories(); Reload(); });

        // Crash recovery: restore an orphaned draft into the input box.
        string? orphan = DraftGuard.RecoverOrphan();
        if (!string.IsNullOrEmpty(orphan)) QuickInput = orphan;
    }

    partial void OnQuickInputChanged(string value) => DraftChanged?.Invoke(value);
    partial void OnFilterChanged(string value) => Reload();
    partial void OnGroupIndexChanged(int value) => Reload();

    /// <summary>Rebuilds the group combo (called on every panel summon so
    /// groups created/renamed in the workbench appear without a restart).
    /// The selection survives by category ID when possible.</summary>
    public void ReloadCategories()
    {
        long? keep = SelectedCategoryId;
        _cats = _categories.GetAll();
        GroupNames.Clear();
        GroupNames.Add(Localization.LocalizationService.Instance["QuickPanel.Group.All"]);
        foreach (var c in _cats) GroupNames.Add(c.Name);
        int idx = keep is null ? 0 : _cats.FindIndex(c => c.Id == keep) + 1;
        GroupIndex = Math.Max(0, idx);
    }

    public void Reload()
    {
        // Search overrides the group filter (search is global by design);
        // otherwise the list is either "recent across everything" or the
        // selected group's contents.
        var data = !string.IsNullOrWhiteSpace(Filter)
            ? _search.Search(Filter, 100)
            : SelectedCategoryId is long cat
                ? _snippets.ByCategory(cat)
                : _snippets.Recent(100);
        Items.Clear();
        foreach (var s in data) Items.Add(new SnippetItemViewModel(s, _snippets.GetImageThumbnail));
    }

    /// <summary>Enter in the quick input box: stash and clear.</summary>
    [RelayCommand]
    private void QuickAdd()
    {
        if (string.IsNullOrWhiteSpace(QuickInput)) return;
        long id = _snippets.Add(QuickInput, SnippetSource.Manual,
                                expire: (ExpirePolicy)Math.Clamp(ExpireIndex, 0, 3),
                                categoryId: SelectedCategoryId);
        // Dedupe may have bumped an EXISTING snippet in another group; when a
        // group is explicitly selected, force the card into it.
        if (SelectedCategoryId is long cat) _snippets.AssignCategory(id, cat);
        QuickInput = string.Empty;   // triggers DraftChanged("") -> draft file cleared
    }

    /// <summary>Card click: copy content to clipboard, count the use,
    /// honor burn-after-read, flash a confirmation toast.</summary>
    [RelayCommand]
    private async Task Copy(SnippetItemViewModel? item)
    {
        if (item is null) return;
        try
        {
            Action writeClipboard;
            if (item.IsImage)
            {
                if (_snippets.GetImage(item.Id) is not byte[] png)
                { ShowToast("Screenshot.ImageUnavailable"); return; }
                writeClipboard = () => ImagePresentation.Copy(png);
            }
            else writeClipboard = () => ClipboardWriteGuard.Shared.Write(() => Clipboard.SetDataObject(item.Model.Content, copy: true));
            if (!await TryWriteClipboardAsync(writeClipboard))
            { ShowToast("Screenshot.CopyFailed"); return; }
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or
            ArgumentException or NotSupportedException)
        { ShowToast("Screenshot.CopyFailed"); return; }
        _snippets.RegisterUse(item.Model);
        DirectPasteResult pasteResult = item.IsImage ? DirectPasteResult.Disabled
            : await DirectPaste.TryPasteToTargetAsync();
        string toastKey = pasteResult is DirectPasteResult.Success or DirectPasteResult.Disabled
            ? "QuickPanel.Copied"
            : "QuickPanel.DirectPasteFailed";
        ShowToast(toastKey);
    }

    /// <summary>A busy clipboard gets one delayed retry; a second lock is a normal copy failure.</summary>
    internal static async Task<bool> TryWriteClipboardAsync(Action write, Func<Task>? retryDelay = null)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try { write(); return true; }
            catch (System.Runtime.InteropServices.COMException)
            {
                if (attempt == 1) return false;
                await (retryDelay?.Invoke() ?? Task.Delay(80));
            }
        }
        return false;
    }

    [RelayCommand]
    private void TogglePin(SnippetItemViewModel? item)
    {
        if (item is null) return;
        _snippets.TogglePin(item.Model);
        item.NotifyPinChanged();   // instant visual; the reload follows
    }

    [RelayCommand]
    private void MoveToTrash(SnippetItemViewModel? item)
    {
        if (item is null) return;
        _snippets.MoveToTrash(item.Id);
    }

    public void ShowToast(string? key = null)
    {
        _toastCancellation?.Cancel();
        _toastCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _toastCancellation = cancellation;
        Toast = Localization.LocalizationService.Instance[key ?? "QuickPanel.Copied"];
        _ = ClearToastAsync(cancellation);
    }

    private async Task ClearToastAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(1200, cancellation.Token);
            if (ReferenceEquals(_toastCancellation, cancellation))
                Toast = null;
        }
        catch (OperationCanceledException)
        {
            // A newer toast owns the timer now.
        }
        finally
        {
            if (ReferenceEquals(_toastCancellation, cancellation))
                _toastCancellation = null;
            cancellation.Dispose();
        }
    }
}
