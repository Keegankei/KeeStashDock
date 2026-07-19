using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using KeeStashDock.Core.Services;
using Microsoft.Win32;
using KeeStashDock.Desktop.Localization;
using System.IO;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Infrastructure.Repositories;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Workbench code-behind: window lifetime (hide-on-close), RTL application
/// and the transform dropdown trigger. Everything else is bound to
/// <see cref="WorkbenchViewModel"/>.
/// </summary>
public partial class WorkbenchWindow : Window
{
    private readonly WorkbenchViewModel _vm;
    private readonly GroupIoService _groupIo;

    public WorkbenchWindow(SnippetService snippets, SearchService search,
                           CategoryRepository categories, TagRepository tags,
                           SettingsStore settings, GroupIoService groupIo)
    {
        InitializeComponent();
        _groupIo = groupIo;
        _vm = new WorkbenchViewModel(snippets, search, categories, tags);
        DataContext = _vm;

        // Tray-resident app: closing the workbench only hides it.
        Closing += (s, e) => { e.Cancel = true; Hide(); };
        ThemeManager.TrackWindowChrome(this);   // dark title bar follows theme

        LocalizationService.Instance.LanguageChanged += () =>
        {
            LocalizationService.Instance.ApplyFlowDirection(this);
            _vm.ReloadNav();   // virtual nav entry names are localized
        };
        LocalizationService.Instance.ApplyFlowDirection(this);
    }

    public void ShowAndActivate()
    {
        _vm.ReloadItems();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // The workbench deliberately ignores the custom
    // background image. Wallpapers belong to the quick panel; the workbench
    // is the "get work done" surface and stays on the plain theme background
    // for maximum readability of the three dense columns.

    // ------------------------------------------ group import / presets

    private void OnImportGroupClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var dlg = new OpenFileDialog
        { Filter = "KeeStashDock group (*.ksdgrp;*.json)|*.ksdgrp;*.json" };
        if (dlg.ShowDialog(this) != true) return;

        GroupIoService.GroupPackage pkg;
        try { pkg = _groupIo.ReadPackage(dlg.FileName); }
        catch
        {
            MessageBox.Show(this, loc["Workbench.Category.ImportBad"],
                loc["Common.Confirm"], MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RunGroupImport(pkg);
    }

    /// <summary>Preset button: code-built flyout listing built-in templates
    /// (one click imports through the normal conflict-aware pipeline).</summary>
    private void OnPresetsClick(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationService.Instance;
        var menu = new ContextMenu { PlacementTarget = PresetsBtn };
        foreach (var (nameKey, build) in GroupPresets.All)
        {
            var mi = new MenuItem { Header = loc[nameKey] };
            mi.Click += (_, _) => RunGroupImport(build());
            menu.Items.Add(mi);
        }
        menu.IsOpen = true;
    }

    /// <summary>Shared import flow: resolve name conflicts (overwrite the
    /// existing group / import under a new name / cancel), execute, report.</summary>
    private void RunGroupImport(GroupIoService.GroupPackage pkg)
    {
        var loc = LocalizationService.Instance;
        string targetName = pkg.Name;
        long? overwriteId = null;

        var existing = _groupIo.FindCategoryByName(targetName);
        if (existing is not null)
        {
            var choice = MessageBox.Show(this,
                string.Format(loc["Workbench.Category.Conflict"], targetName),
                loc["Common.Confirm"],
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;
            if (choice == MessageBoxResult.Yes)
            {
                overwriteId = existing.Id;   // replace contents (old -> trash)
            }
            else
            {
                string? renamed = InputDialog.Prompt(this,
                    loc["Workbench.Category.Import"],
                    loc["Workbench.Category.RenamePrompt"],
                    $"{targetName} (2)");
                if (renamed is null) return;
                if (_groupIo.FindCategoryByName(renamed) is not null)
                {
                    MessageBox.Show(this, string.Format(
                        loc["Workbench.Category.Conflict"], renamed),
                        loc["Common.Confirm"], MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                targetName = renamed;
            }
        }

        int placed = _groupIo.ImportGroup(pkg, targetName, overwriteId);
        _vm.RefreshAfterGroupImport(targetName);
        MessageBox.Show(this,
            string.Format(loc["Workbench.Category.ImportDone"], placed, targetName),
            loc["Common.Confirm"], MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // --------------------------------------------- category context menu

    /// <summary>Right click on the sidebar: builds the rename/delete menu in
    /// code (a Style-embedded ContextMenu with Click handlers corrupts BAML
    /// connection ids - the InvalidCastException you hit). Virtual views
    /// (All / Pinned / Uncategorized / Trash, Id &lt;= 0) get no menu.</summary>
    private void OnNavRightClick(object sender, MouseButtonEventArgs e)
    {
        var container = System.Windows.Controls.ItemsControl.ContainerFromElement(
            NavList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container?.DataContext is not WorkbenchViewModel.NavEntry entry ||
            entry.Id <= 0) return;

        var loc = LocalizationService.Instance;
        var menu = new ContextMenu();

        var rename = new MenuItem { Header = loc["Workbench.Category.Rename"] };
        rename.Click += (_, _) =>
        {
            string? name = InputDialog.Prompt(this,
                loc["Workbench.Category.Rename"],
                loc["Workbench.Category.RenamePrompt"], entry.Name);
            if (name is not null) _vm.RenameCategory(entry.Id, name);
        };

        var delete = new MenuItem { Header = loc["Workbench.Category.Delete"] };
        delete.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                    string.Format(loc["Workbench.Category.Delete.Confirm"], entry.Name),
                    loc["Common.Confirm"],
                    MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.Yes)
                _vm.DeleteCategory(entry.Id);
        };

        // "Delete with contents": the group AND its snippets go in one
        // move - snippets land in the trash (recoverable), mirroring the
        // conflict-overwrite path rather than a destructive purge.
        var deleteAll = new MenuItem { Header = loc["Workbench.Category.DeleteAll"] };
        deleteAll.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                    string.Format(loc["Workbench.Category.DeleteAll.Confirm"], entry.Name),
                    loc["Common.Confirm"],
                    MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.Yes)
                _vm.DeleteCategoryWithContents(entry.Id);
        };

        var export = new MenuItem { Header = loc["Workbench.Category.Export"] };
        export.Click += (_, _) =>
        {
            var dlg = new SaveFileDialog
            {
                Filter = "KeeStashDock group (*.ksdgrp)|*.ksdgrp",
                FileName = $"{entry.Name}.ksdgrp",
            };
            if (dlg.ShowDialog(this) != true) return;
            _groupIo.ExportGroup(entry.Id, entry.Name, dlg.FileName);
        };

        menu.Items.Add(rename);
        menu.Items.Add(delete);
        menu.Items.Add(deleteAll);
        menu.Items.Add(new Separator());
        menu.Items.Add(export);
        menu.PlacementTarget = container;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnTransformMenuClick(object sender, RoutedEventArgs e)
    {
        // Open the context menu on left click (dropdown-button behavior).
        if (sender is Button { ContextMenu: ContextMenu menu } btn)
        {
            menu.PlacementTarget = btn;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }
}

/// <summary>True -&gt; Collapsed, False -&gt; Visible (inverse of the stock converter).</summary>
public sealed class InverseBoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type t, object? p, System.Globalization.CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type t, object? p, System.Globalization.CultureInfo c)
        => throw new NotSupportedException();
}
