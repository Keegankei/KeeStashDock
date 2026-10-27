using System.Windows;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop;

/// <summary>Prepares text only. Callers write the clipboard and register use after successful output.</summary>
internal static class TemplateUseCoordinator
{
    internal static Task<string?> PrepareAsync(Snippet snippet, string? actionLabel = null)
    {
        if (!snippet.IsTemplate) return Task.FromResult<string?>(snippet.Content);
        try
        {
            var document = TemplateParser.Parse(snippet.Content);
            var window = new TemplateFillWindow(document, actionLabel);
            Window? owner = Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(candidate => candidate.IsActive && candidate.IsVisible);
            if (owner is not null) window.Owner = owner;
            return Task.FromResult(window.ShowDialog() == true ? window.ResultText : null);
        }
        catch (TemplateParseException ex)
        {
            MessageBox.Show(string.Format(LocalizationService.Instance["Template.Invalid"], ex.Position + 1),
                LocalizationService.Instance["Template.Title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return Task.FromResult<string?>(null);
        }
    }
}
