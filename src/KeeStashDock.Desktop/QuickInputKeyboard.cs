using System.Windows.Controls;
using System.Windows.Input;

namespace KeeStashDock.Desktop;

internal enum QuickInputEnterAction
{
    None,
    Stash,
    NewLine,
}

/// <summary>Quick-input Enter handling shared by the panel and WPF tests.</summary>
internal static class QuickInputKeyboard
{
    public static QuickInputEnterAction HandleEnter(
        TextBox textBox, KeyEventArgs args, ModifierKeys modifiers)
    {
        // Do not recover ImeProcessedKey: Enter belongs to the IME while it
        // commits a composition. Alt+Enter, however, arrives as Key.System.
        if (args.Handled || args.Key == Key.ImeProcessed)
            return QuickInputEnterAction.None;

        Key key = args.Key == Key.System ? args.SystemKey : args.Key;
        if (key != Key.Enter)
            return QuickInputEnterAction.None;

        args.Handled = true;
        if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Shift)) == 0)
            return QuickInputEnterAction.Stash;

        // Editing SelectedText preserves bindings and undo history, replaces
        // the active selection, and inserts at the caret instead of appending.
        int start = textBox.SelectionStart;
        textBox.BeginChange();
        try
        {
            textBox.SelectedText = Environment.NewLine;
            textBox.Select(start + Environment.NewLine.Length, 0);
        }
        finally
        {
            textBox.EndChange();
        }
        return QuickInputEnterAction.NewLine;
    }
}
