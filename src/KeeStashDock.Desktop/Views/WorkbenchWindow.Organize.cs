using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop.Views;

public partial class WorkbenchWindow
{
    private bool _batchCheckboxGesture;

    private void OnBatchCheckboxPreviewDown(object sender, MouseButtonEventArgs e) => _batchCheckboxGesture = true;

    private void OnWorkbenchListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed || e.Handled) return;
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _handlingSelection = true;
            try { ItemList.SelectAll(); }
            finally { _handlingSelection = false; }
            _vm.SetBatchSelection(ItemList.SelectedItems.Cast<SnippetItemViewModel>());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        { _vm.ClearBatchSelection(); e.Handled = true; }
    }

    private void OnClearBatchSelection(object sender, RoutedEventArgs e) => _vm.ClearBatchSelection();

    private void OnBatchActionsClick(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasBatchSelection || _vm.IsBatchBusy) return;
        var loc = LocalizationService.Instance;
        var menu = new ContextMenu { PlacementTarget = BatchActionsButton };
        if (_vm.IsTrashView)
        {
            var restore = new MenuItem { Header = loc["Batch.Restore"] };
            restore.Click += async (_, _) =>
            {
                var failure = await _vm.ApplyBatchAsync(BatchAction.Restore);
                if (failure == BatchConflictReason.ExpiredItem && MessageBox.Show(this,
                    loc["Batch.RestoreExpired"], loc["Common.Confirm"], MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes)
                    await _vm.ApplyBatchAsync(BatchAction.Restore, resetExpired: true);
            };
            menu.Items.Add(restore);
        }
        else
        {
            var move = new MenuItem { Header = loc["Batch.Move"] };
            var ungrouped = new MenuItem { Header = loc["Workbench.Uncategorized"] };
            ungrouped.Click += async (_, _) => await _vm.ApplyBatchAsync(BatchAction.MoveToCategory);
            move.Items.Add(ungrouped);
            foreach (var category in _vm.Nav.Where(item => item.Id > 0))
            {
                var destination = new MenuItem { Header = category.Name };
                destination.Click += async (_, _) => await _vm.ApplyBatchAsync(BatchAction.MoveToCategory, category.Id);
                move.Items.Add(destination);
            }
            menu.Items.Add(move);
            foreach (var choice in new[] { ("Batch.AddTags", BatchAction.AddTags), ("Batch.RemoveTags", BatchAction.RemoveTags) })
            {
                var tags = new MenuItem { Header = loc[choice.Item1] };
                tags.Click += async (_, _) =>
                {
                    string? text = InputDialog.Prompt(this, loc[choice.Item1], loc["Batch.TagsPrompt"], string.Empty);
                    if (text is null) return;
                    string[] values = text.Split([',', '\uFF0C', ';', '\uFF1B'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.Ordinal).ToArray();
                    if (values.Length > 0) await _vm.ApplyBatchAsync(choice.Item2, tags: values);
                };
                menu.Items.Add(tags);
            }
            foreach (var choice in new[] { ("Batch.Pin", BatchAction.Pin), ("Batch.Unpin", BatchAction.Unpin) })
            {
                var item = new MenuItem { Header = loc[choice.Item1] };
                item.Click += async (_, _) => await _vm.ApplyBatchAsync(choice.Item2);
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var trash = new MenuItem { Header = loc["Batch.Trash"] };
            trash.Click += async (_, _) => await _vm.ApplyBatchAsync(BatchAction.Trash);
            menu.Items.Add(trash);
        }
        menu.IsOpen = true;
    }
}
