using CommunityToolkit.Mvvm.ComponentModel;

namespace KeeStashDock.Desktop.ViewModels;

public sealed partial class SnippetItemViewModel
{
    [ObservableProperty] private bool _isEditing;
}
