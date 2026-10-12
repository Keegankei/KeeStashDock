using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

[TestClass]
public sealed class QuickInputKeyboardTests
{
    [STATestMethod]
    public void AltEnter_SystemKeyInsertsAtCaretAndPreservesUndo()
    {
        var box = new TextBox { Text = "abcd", AcceptsReturn = true };
        box.Select(2, 0);
        using var source = CreateSource(box);
        KeyEventArgs args = CreateKey(source, Key.Enter, "MarkSystem");

        QuickInputEnterAction action = QuickInputKeyboard.HandleEnter(box, args, ModifierKeys.Alt);

        Assert.AreEqual(Key.System, args.Key);
        Assert.AreEqual(Key.Enter, args.SystemKey);
        Assert.AreEqual(QuickInputEnterAction.NewLine, action);
        Assert.AreEqual("ab" + Environment.NewLine + "cd", box.Text);
        Assert.AreEqual(2 + Environment.NewLine.Length, box.CaretIndex);
        Assert.AreEqual(0, box.SelectionLength);
        Assert.IsTrue(args.Handled);
        Assert.IsTrue(box.CanUndo, "The hosted TextBox must record the newline as an undo unit.");
        box.Undo();
        Assert.AreEqual("abcd", box.Text);
        Assert.IsTrue(box.CanRedo);
        box.Redo();
        Assert.AreEqual("ab" + Environment.NewLine + "cd", box.Text);
    }

    [STATestMethod]
    [DataRow(ModifierKeys.Alt, "MarkSystem")]
    [DataRow(ModifierKeys.Shift, null)]
    public void ModifiedEnter_ReplacesSelectionWithSingleNewline(
        ModifierKeys modifiers, string? markMethod)
    {
        var box = new TextBox { Text = "before selected after", AcceptsReturn = true };
        box.Select(7, 8);
        using var source = CreateSource(box);
        KeyEventArgs args = CreateKey(source, Key.Enter, markMethod);

        Assert.AreEqual(QuickInputEnterAction.NewLine,
            QuickInputKeyboard.HandleEnter(box, args, modifiers));
        Assert.AreEqual("before " + Environment.NewLine + " after", box.Text);
        Assert.AreEqual(7 + Environment.NewLine.Length, box.CaretIndex);
        Assert.AreEqual(0, box.SelectionLength);
    }

    [STATestMethod]
    public void PlainEnter_RequestsStashWithoutInsertingText()
    {
        var box = new TextBox { Text = "draft", AcceptsReturn = true };
        using var source = CreateSource(box);
        KeyEventArgs args = CreateKey(source, Key.Enter);

        Assert.AreEqual(QuickInputEnterAction.Stash,
            QuickInputKeyboard.HandleEnter(box, args, ModifierKeys.None));
        Assert.AreEqual("draft", box.Text);
        Assert.IsTrue(args.Handled);
    }

    [STATestMethod]
    [DataRow(ModifierKeys.None)]
    [DataRow(ModifierKeys.Alt)]
    public void ImeCommitEnter_IsNeverTreatedAsStashOrNewline(ModifierKeys modifiers)
    {
        var box = new TextBox { Text = "draft", AcceptsReturn = true };
        using var source = CreateSource(box);
        KeyEventArgs args = CreateKey(source, Key.Enter, "MarkImeProcessed");

        Assert.AreEqual(QuickInputEnterAction.None,
            QuickInputKeyboard.HandleEnter(box, args, modifiers));
        Assert.AreEqual("draft", box.Text);
        Assert.IsFalse(args.Handled);
    }

    [STATestMethod]
    public void AltOtherKey_IsLeftForNormalWpfRouting()
    {
        var box = new TextBox { Text = "draft", AcceptsReturn = true };
        using var source = CreateSource(box);
        KeyEventArgs args = CreateKey(source, Key.A, "MarkSystem");

        Assert.AreEqual(QuickInputEnterAction.None,
            QuickInputKeyboard.HandleEnter(box, args, ModifierKeys.Alt));
        Assert.AreEqual("draft", box.Text);
        Assert.IsFalse(args.Handled);
    }

    private static HwndSource CreateSource(TextBox box)
    {
        // WPF creates the text editor's render scope when its template is
        // applied. A detached, untemplated TextBox accepts SelectedText edits
        // but cannot execute Undo/Redo as an on-screen editor does.
        var source = new HwndSource(new HwndSourceParameters("QuickInputKeyboardTest")
        {
            WindowStyle = 0,
            Width = 400,
            Height = 120,
        });
        source.RootVisual = box;
        box.Measure(new Size(400, 120));
        box.Arrange(new Rect(0, 0, 400, 120));
        box.UpdateLayout();
        box.IsUndoEnabled = false;
        box.IsUndoEnabled = true;
        return source;
    }

    private static KeyEventArgs CreateKey(HwndSource source, Key key, string? markMethod = null)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        if (markMethod is not null)
        {
            // WPF's native keyboard provider marks these internally. Recreate
            // its actual event shape without sending input to the user's apps.
            MethodInfo? mark = typeof(KeyEventArgs).GetMethod(
                markMethod, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(mark);
            mark.Invoke(args, null);
        }
        return args;
    }
}
