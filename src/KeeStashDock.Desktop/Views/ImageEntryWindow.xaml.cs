using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Domain.Entities;
using Microsoft.Win32;

namespace KeeStashDock.Desktop.Views;

/// <summary>Original screenshot and editable OCR belong to one entry. Translation is delegated.</summary>
public partial class ImageEntryWindow : Window
{
    private readonly SnippetService _snippets;
    private readonly Func<byte[], CancellationToken, Task<OcrRecognitionResult>> _recognize;
    private readonly long _id;
    private readonly byte[] _png;
    private string _savedText;
    private CancellationTokenSource? _recognition;
    private bool _closed;
    private bool _readOnly;
    private TaskCompletionSource? _recognitionDone;
    internal Task RecognitionCompletion => _recognitionDone?.Task ?? Task.CompletedTask;
    public event Action<TextBox>? SelectionRequested;
    public event Action? DismissActions;

    public ImageEntryWindow(SnippetService snippets, LocalOcrService ocr, Snippet snippet, byte[] png)
        : this(snippets, ocr.RecognizeAsync, snippet, png) { }

    internal ImageEntryWindow(SnippetService snippets,
        Func<byte[], CancellationToken, Task<OcrRecognitionResult>> recognize, Snippet snippet, byte[] png)
    {
        InitializeComponent();
        _snippets = snippets;
        _recognize = recognize;
        _id = snippet.Id;
        _png = png;
        _savedText = snippet.Content;
        OcrText.Text = _savedText;
        OriginalImage.Source = ImagePresentation.Decode(png);
        _readOnly = snippet.IsDeleted;
        RecognizeButton.IsEnabled = !_readOnly;
        OcrText.IsReadOnly = _readOnly;
        ThemeManager.TrackWindowChrome(this);
        LocalizationService.Instance.ApplyFlowDirection(this);
        OcrText.AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => OfferSelection()), true);
        OcrText.KeyUp += (_, e) => { if (e.Key != Key.Escape) OfferSelection(); };
        PreviewMouseDown += (_, _) => DismissActions?.Invoke();
        Deactivated += (_, _) => DismissActions?.Invoke();
        Closing += OnClosing;
        _snippets.Changed += OnSnippetChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _snippets.Changed -= OnSnippetChanged;
            _recognition?.Cancel();
            DismissActions?.Invoke();
        };
        UpdateButtons();
    }

    private void OnSnippetChanged()
    {
        if (Dispatcher.CheckAccess()) RefreshEntryState();
        else Dispatcher.BeginInvoke(RefreshEntryState);
    }

    private void RefreshEntryState()
    {
        if (_closed) return;
        Snippet? current;
        try { current = _snippets.GetById(_id); }
        catch (InvalidOperationException) { current = null; }
        _readOnly = current is not { IsDeleted: false };
        if (_readOnly) _recognition?.Cancel();
        // A clean editor follows edits in the workbench. A dirty editor keeps
        // its draft and SaveText still checks the original persisted content.
        if (current is not null && _recognition is null && OcrText.Text == _savedText)
        {
            _savedText = current.Content;
            OcrText.Text = _savedText;
        }
        RecognizeButton.IsEnabled = !_readOnly && _recognition is null;
        OcrText.IsReadOnly = _readOnly || _recognition is not null;
        UpdateButtons();
    }

    private void OfferSelection()
    {
        if (!string.IsNullOrWhiteSpace(OcrText.SelectedText)) SelectionRequested?.Invoke(OcrText);
        else DismissActions?.Invoke();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SaveTextButton is null) return;
        DismissActions?.Invoke();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        SaveTextButton.IsEnabled = !OcrText.IsReadOnly && _recognition is null && OcrText.Text != _savedText;
        CopyTextButton.IsEnabled = !string.IsNullOrWhiteSpace(OcrText.Text);
    }

    private void ShowStatus(string key) => Status.Text = LocalizationService.Instance[key];

    private async void OnRecognize(object sender, RoutedEventArgs e)
    {
        if (_recognition is not null) return;
        if (!string.IsNullOrWhiteSpace(OcrText.Text) && MessageBox.Show(this,
            LocalizationService.Instance["Screenshot.RecognitionReplace"], Title,
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        using var request = new CancellationTokenSource();
        _recognition = request;
        _recognitionDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecognizeButton.IsEnabled = false;
        OcrText.IsReadOnly = true;
        CancelRecognitionButton.Visibility = Visibility.Visible;
        UpdateButtons();
        ShowStatus("Screenshot.Recognizing");
        try
        {
            OcrRecognitionResult result = await _recognize(_png, request.Token);
            if (_closed || request.IsCancellationRequested) return;
            if (result.IsSuccess)
            {
                OcrText.Text = result.Text;
                OcrText.IsReadOnly = false;
                SaveText();
            }
            else ShowStatus($"Screenshot.Ocr{result.Failure}");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        { if (!_closed) ShowStatus("Screenshot.OcrRecognitionFailed"); }
        finally
        {
            _recognition = null;
            if (!_closed)
            {
                try { _readOnly = _snippets.GetById(_id) is not { IsDeleted: false }; }
                catch (InvalidOperationException) { _readOnly = true; }
                RecognizeButton.IsEnabled = !_readOnly;
                OcrText.IsReadOnly = _readOnly;
                CancelRecognitionButton.Visibility = Visibility.Collapsed;
                if (request.IsCancellationRequested) ShowStatus("Screenshot.OcrCancelled");
                UpdateButtons();
            }
            _recognitionDone.TrySetResult();
        }
    }

    private void OnCancelRecognition(object sender, RoutedEventArgs e) => _recognition?.Cancel();
    private void OnSaveText(object sender, RoutedEventArgs e) => SaveText();

    internal bool SaveText()
    {
        try
        {
            Snippet? current = _snippets.GetById(_id);
            if (current is null || current.IsDeleted || current.Content != _savedText)
            { ShowStatus("Workbench.Editor.ExternalConflict"); return false; }
            current.Content = OcrText.Text;
            _snippets.Update(current);
            _savedText = current.Content;
            OcrText.Text = _savedText;
            ShowStatus("Screenshot.TextSaved");
            UpdateButtons();
            return true;
        }
        catch (Exception)
        { ShowStatus("Screenshot.SaveFailed"); return false; }
    }

    private void OnCopyImage(object sender, RoutedEventArgs e)
    {
        try
        {
            ImagePresentation.Copy(_png);
            RegisterUse();
            ShowStatus("QuickPanel.Copied");
        }
        catch (System.Runtime.InteropServices.COMException) { ShowStatus("Screenshot.CopyFailed"); }
    }

    private void OnCopyText(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OcrText.Text)) return;
        try { ClipboardWriteGuard.Shared.Write(() => Clipboard.SetDataObject(OcrText.Text, true)); RegisterUse(); ShowStatus("QuickPanel.Copied"); }
        catch (System.Runtime.InteropServices.COMException) { ShowStatus("Screenshot.CopyFailed"); }
    }

    private void RegisterUse()
    {
        if (_snippets.GetById(_id) is { IsDeleted: false } model && _snippets.RegisterUse(model))
        {
            _readOnly = true;
            _recognition?.Cancel();
            RecognizeButton.IsEnabled = false;
            OcrText.IsReadOnly = true;
            UpdateButtons();
        }
    }

    private void OnSaveImage(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        { Filter = LocalizationService.Instance["Screenshot.ImageFilter"], FileName = $"Screenshot-{_id}.png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllBytes(dialog.FileName, _png); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowStatus("Screenshot.SaveFailed"); }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (OcrText.Text == _savedText) return;
        MessageBoxResult answer = MessageBox.Show(this, LocalizationService.Instance["Screenshot.Unsaved"],
            Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        e.Cancel = answer == MessageBoxResult.Cancel || (answer == MessageBoxResult.Yes && !SaveText());
    }
}
