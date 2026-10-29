using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KeeStashDock.Desktop.ViewModels;

namespace KeeStashDock.Desktop.Views;

public partial class QuickPanelWindow
{
    private bool _searchComposing;

    private void InitializeSearch()
    {
        PreviewKeyDown += OnPanelSearchKeyDown;
        TextCompositionManager.AddPreviewTextInputStartHandler(this, (_, _) => _searchComposing = true);
        TextCompositionManager.AddPreviewTextInputHandler(this, (_, _) =>
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _searchComposing = false));
        LostKeyboardFocus += (_, _) => _searchComposing = false;
    }

    private void RefreshSearchVisibility()
    {
        bool expanded = _state == PanelState.Expanded;
        InputRow.Visibility = ExpireRow.Visibility = expanded && !_vm.IsSearchMode ? Visibility.Visible : Visibility.Collapsed;
        SearchInputRow.Visibility = SearchFilterRow.Visibility = expanded && _vm.IsSearchMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenSearchClick(object sender, RoutedEventArgs e) => OpenSearch();
    private void OnCloseSearchClick(object sender, RoutedEventArgs e) => CloseSearch();

    private void OpenSearch()
    {
        _vm.BeginSearch();
        RefreshSearchVisibility();
        Activate(); // Only the user's explicit Search action activates the panel.
        PanelSearchBox.Focus();
        PanelSearchBox.SelectAll();
    }

    private void CloseSearch()
    {
        _vm.EndSearch();
        RefreshSearchVisibility();
        QuickBox.Focus();
    }

    private void OnPanelSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key == Key.ImeProcessed || _searchComposing) return;
        if (OwnedWindows.Cast<Window>().Any(window => window.IsVisible && window.IsActive)) return;
        if (PanelSearchScope.IsDropDownOpen || PanelSearchKind.IsDropDownOpen ||
            ContextMenu?.IsOpen == true || CardList.ContextMenu?.IsOpen == true) return;
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && IsKeyboardFocusWithin)
        { OpenSearch(); e.Handled = true; return; }
        if (!_vm.IsSearchMode) return;
        if (e.Key == Key.Escape)
        { CloseSearch(); e.Handled = true; return; }
        if ((e.Key is Key.Down or Key.Up) && PanelSearchBox.IsKeyboardFocusWithin && CardList.Items.Count > 0)
        {
            CardList.SelectedIndex = e.Key == Key.Down ? 0 : CardList.Items.Count - 1;
            CardList.ScrollIntoView(CardList.SelectedItem);
            CardList.UpdateLayout();
            (CardList.ItemContainerGenerator.ContainerFromIndex(CardList.SelectedIndex) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && PanelSearchBox.IsKeyboardFocusWithin)
        {
            if (!_vm.IsSearching && CardList.SelectedItem is SnippetItemViewModel selected)
                _vm.CopyCommand.Execute(selected);
            else if (!_vm.IsSearching && _vm.Items.FirstOrDefault() is { } first)
                _vm.CopyCommand.Execute(first);
            e.Handled = true;
        }
    }
}
