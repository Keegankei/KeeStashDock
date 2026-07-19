using System.Windows;
using System.Windows.Input;

namespace KeeStashDock.Desktop.Views;

/// <summary>One-line modal text prompt. Returns the trimmed value or null.</summary>
public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        SourceInitialized += (_, _) => ThemeManager.ApplyWindowChrome(this);
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
        Localization.LocalizationService.Instance.ApplyFlowDirection(this);
    }

    public string Value => ValueBox.Text.Trim();

    /// <summary>Shows the prompt modally; null when cancelled or left empty.</summary>
    public static string? Prompt(Window owner, string title, string prompt,
                                 string initialValue = "")
    {
        var dlg = new InputDialog(title, prompt, initialValue) { Owner = owner };
        bool ok = dlg.ShowDialog() == true;
        return ok && dlg.Value.Length > 0 ? dlg.Value : null;
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnValueKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { DialogResult = true; e.Handled = true; }
        else if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
    }
}
