using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Storage;
using KeeStashDock.Core.Services;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Shared non-activating translation/OCR card. Source text remains available when
/// translation fails; clipboard writes happen only in response to an explicit click.
/// </summary>
public sealed partial class TranslationResultWindow : Window
{
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, GWL_EXSTYLE = -20;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly SettingsStore _settings;
    private readonly Action<string> _notifySaved;
    private readonly DispatcherTimer _hideTimer;
    private readonly Border _surface;
    private readonly TextBlock _body, _bodyGlow, _hint, _source;
    private readonly StackPanel _sourcePanel;
    private readonly ScrollViewer _sourceScroll, _bodyScroll;
    private readonly Button _copySource, _retryButton, _settingsButton, _closeButton;
    private string _resultText = string.Empty, _sourceText = string.Empty;
    private bool _copyable, _persistentResult, _inProgress;
    private int _anchorX, _anchorY;
    private Action? _retry, _openSettings;
    private readonly TemporaryTextResult _textResult = new();
    private readonly ComboBox _targetCombo, _groupCombo;
    private readonly TextBlock _targetLabel, _groupLabel, _saveStatus;
    private readonly StackPanel _resultTools, _targetTools, _saveTools;
    private readonly Button _saveSource, _saveTranslation, _saveBoth;
    private Func<string, long?, long>? _save;
    private Action<string>? _translateTarget;
    private bool _updatingTools;
    private readonly List<Task> _pendingSaves = [];
    public Task SaveCompletion => Task.WhenAll(_pendingSaves.ToArray());
    internal long? SelectedGroupId => (_groupCombo.SelectedItem as GroupChoice)?.Id;
    internal bool HasResultContext => _save is not null;
    public sealed record GroupChoice(long? Id, string Name);

    public TranslationResultWindow(SettingsStore settings, Action<string>? notifySaved = null)
    {
        _settings = settings;
        _notifySaved = notifySaved ?? MiniToast.Show;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 440;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _hideTimer.Tick += (_, _) => HideCard();

        TextBlock MakeBody()
        {
            var body = new TextBlock { TextWrapping = TextWrapping.Wrap };
            TypographyManager.SetFontSize(body, 13);
            return body;
        }
        _body = MakeBody();
        _bodyGlow = MakeBody();
        _source = MakeBody();
        _hint = new TextBlock { Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        TypographyManager.SetFontSize(_hint, 10.5);

        Button MakeButton(Action action)
        {
            var button = new Button { Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 6, 6, 0),
                MinHeight = 30, MaxWidth = 240 };
            button.SetResourceReference(StyleProperty, "S.Button");
            button.SetResourceReference(BackgroundProperty, "B.CardBg");
            button.Click += (_, e) => { e.Handled = true; action(); };
            return button;
        }
        _copySource = MakeButton(() =>
        {
            if (TryCopySource(text => Clipboard.SetDataObject(text, copy: true)))
                MiniToast.Show(LocalizationService.Instance["QuickPanel.Copied"]);
        });
        _retryButton = MakeButton(() => _retry?.Invoke());
        _settingsButton = MakeButton(() =>
        {
            Action? open = _openSettings;
            HideCard();
            open?.Invoke();
        });
        _closeButton = MakeButton(HideCard);
        _copyResult = MakeButton(CopyResultAndDismiss);

        _targetLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        _targetCombo = new ComboBox { Name = "ResultTargetCombo", HorizontalAlignment = HorizontalAlignment.Stretch };
        _targetCombo.SelectionChanged += (_, _) =>
        {
            if (!_updatingTools && _targetCombo.SelectedItem is ComboBoxItem { Tag: string target })
                _translateTarget?.Invoke(target);
        };
        _groupLabel = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        _groupCombo = new ComboBox { Name = "ResultGroupCombo", DisplayMemberPath = nameof(GroupChoice.Name) };
        _saveSource = MakeButton(async () => await SaveResultAsync(ResultTextKind.Source));
        _saveTranslation = MakeButton(async () => await SaveResultAsync(ResultTextKind.Translation));
        _saveBoth = MakeButton(async () => await SaveResultAsync(ResultTextKind.Both));
        _saveStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var saves = new WrapPanel();
        saves.Children.Add(_saveSource);
        saves.Children.Add(_saveTranslation);
        saves.Children.Add(_saveBoth);
        _targetTools = new StackPanel { Name = "ResultTargetTools", Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom };
        _targetTools.Children.Add(_targetLabel);
        _targetTools.Children.Add(_targetCombo);
        _groupTools = new StackPanel { Name = "ResultGroupTools", Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom };
        _groupTools.Children.Add(_groupLabel);
        _groupTools.Children.Add(_groupCombo);
        _saveTools = new StackPanel { Name = "ResultSaveTools", Visibility = Visibility.Collapsed };
        _saveTools.Children.Add(saves);
        _saveTools.Children.Add(_saveStatus);
        _resultTools = new StackPanel { Name = "ResultTools", Visibility = Visibility.Collapsed };
        _toolFields = new ResultToolFieldsGrid(_targetTools, _groupTools) { Name = "ResultToolFields" };
        _resultTools.Children.Add(_toolFields);
        _resultTools.Children.Add(_saveTools);

        _sourcePanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 8) };
        _sourceScroll = new ScrollViewer { Content = _source, MaxHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _bodyScroll = new ScrollViewer { Content = GlassBubble.LayerGlow(_body, _bodyGlow), MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // Copy only from the result text area; source, scroll bars, and buttons have independent actions.
        _body.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CopyResultAndDismiss();
        };
        _surface = new Border
        {
            Name = "ResultSurface", Margin = new Thickness(12)
        };
        BuildPresentation();
        // Target/group controls and wrapped labels can exceed a short, scaled
        // work area even after the text panes shrink. Keep every action reachable.
        Content = new ScrollViewer { Content = _surface,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        ApplyStyle();
        ApplyLanguage();
        SetActions(null, null);
        _settings.Changed += ApplySettings;
        ThemeManager.Changed += ApplyStyle;
        TypographyManager.Changed += ClampIntoWorkArea;
        TypographyManager.Changed += RefreshToolLayout;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
        Closed += (_, _) =>
        {
            ClearContent();
            _settings.Changed -= ApplySettings;
            ThemeManager.Changed -= ApplyStyle;
            TypographyManager.Changed -= ClampIntoWorkArea;
            TypographyManager.Changed -= RefreshToolLayout;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            LocalizationService.Instance.LanguageChanged -= ApplyLanguage;
        };
        MouseEnter += (_, _) => _hideTimer.Stop();
        MouseLeave += (_, _) => StartHideTimer();
        SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    private void ApplySettings() => Dispatcher.Invoke(() =>
    {
        ApplyStyle();
        // Visibility is independent of the retained source and any request in flight.
        // Toggling preferences must not recreate the context or change the chosen group.
        RefreshToolVisibility();
    });

    internal bool TryCopyResult(Action<string> copy) => _copyable && TryCopy(_resultText, copy);
    internal bool TryCopySource(Action<string> copy) => _sourceText.Length > 0 && TryCopy(_sourceText, copy);

    private bool TryCopy(string text, Action<string> copy)
    {
        try { ClipboardWriteGuard.Shared.Write(() => copy(text)); _hint.Visibility = Visibility.Collapsed; return true; }
        catch (System.Runtime.InteropServices.COMException)
        {
            _hint.Text = LocalizationService.Instance["Screenshot.CopyFailed"];
            _hint.Visibility = Visibility.Visible;
            StartHideTimer();
            return false;
        }
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.Invoke(ApplyStyle);
    }

    public void ShowRecognizing(int deviceX, int deviceY)
    {
        ShowProgress(deviceX, deviceY, "Screenshot.Recognizing");
        _persistentResult = true;
    }

    public void ShowTranslating(int deviceX, int deviceY)
        => ShowProgress(deviceX, deviceY, "Translate.InProgress");

    private void ShowProgress(int deviceX, int deviceY, string key)
    {
        ClearContent();
        _inProgress = true;
        _headingKey = key == "Screenshot.Recognizing" ? "Screenshot.ActionExtract" : "Translate.Button";
        RefreshHeading();
        RefreshCopyAction();
        SetBody(LocalizationService.Instance[key]);
        (_anchorX, _anchorY) = (deviceX, deviceY);
        PopupPlacement.ShowNear(this, deviceX, deviceY, preferAbove: false);
        _hideTimer.Stop();
    }

    public void SetRecognizedSource(string text)
    {
        _textResult.SetSource(text);
        _sourceText = _source.Text = text;
        _sourcePanel.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _persistentResult = text.Length > 0;
        RefreshSaveButtons();
        ClampIntoWorkArea();
    }

    public void ShowRecognizedText(string text)
    {
        _persistentResult = true;
        _headingKey = "Screenshot.ActionExtract";
        RefreshHeading();
        ShowResult(text);
        _textResult.SetSource(text);
        _textResult.SetTranslation(_textResult.Revision, string.Empty);
        RefreshSaveButtons();
    }

    /// <summary>The target here belongs to this retained source only; Settings owns global preferences.</summary>
    public void SetResultContext(IReadOnlyList<GroupChoice> groups, long? currentGroup, string target,
        Action<string> translateTarget, Func<string, long?, long> save)
    {
        _save = save;
        _translateTarget = translateTarget;
        _updatingTools = true;
        try
        {
            _targetCombo.Items.Clear();
            var cfg = _settings.Current;
            bool managed = TranslationService.IsManagedOfflineEndpoint(cfg.OfflineTranslationEndpoint);
            foreach (var language in TranslationTargets.Languages.Where(language =>
                         TranslationTargets.IsOffered(cfg.TranslationEngine, language.Tag, managed) || language.Tag == target))
                _targetCombo.Items.Add(new ComboBoxItem { Tag = language.Tag, Content = language.Name,
                    IsEnabled = TranslationTargets.IsOffered(cfg.TranslationEngine, language.Tag, managed) });
            _targetCombo.SelectedItem = _targetCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == target);
            _groupCombo.ItemsSource = groups;
            _groupCombo.SelectedItem = groups.FirstOrDefault(group => group.Id == currentGroup) ?? groups.FirstOrDefault();
        }
        finally { _updatingTools = false; }
        RefreshToolVisibility();
        RefreshSaveButtons();
        ClampIntoWorkArea();
    }

    internal Task<bool> SaveResultAsync(ResultTextKind kind)
    {
        Task<bool> task = SaveCoreAsync(kind);
        if (!task.IsCompleted)
        {
            _pendingSaves.Add(task);
            _ = RemoveCompletedSaveAsync(task);
        }
        return task;
    }

    private async Task RemoveCompletedSaveAsync(Task task)
    {
        try { await task; }
        finally { _pendingSaves.Remove(task); }
    }

    private async Task<bool> SaveCoreAsync(ResultTextKind kind)
    {
        var save = _save;
        if (save is null || _textResult.BeginSave(kind, SelectedGroupId) is not { } request) return false;
        RefreshSaveButtons();
        try
        {
            // A database snapshot or maintenance can hold the storage gate.
            // Keep the popup responsive while the explicit save waits for it.
            long id = await Task.Run(() => save(request.Text, request.Group));
            if (id <= 0) throw new InvalidOperationException("No saved entry returned.");
            if (!_textResult.CompleteSave(request, success: true)) return false;
            _saveStatus.Text = LocalizationService.Instance["Result.Saved"];
            _saveStatus.Visibility = Visibility.Visible;
            _notifySaved(_saveStatus.Text);
            return true;
        }
        catch (Exception)
        {
            if (_textResult.CompleteSave(request, success: false))
            {
                _saveStatus.Text = LocalizationService.Instance["Result.SaveFailed"];
                _saveStatus.Visibility = Visibility.Visible;
            }
            return false;
        }
        finally { RefreshSaveButtons(); ClampIntoWorkArea(); }
    }

    private void RefreshSaveButtons()
    {
        _saveSource.IsEnabled = !_textResult.Saving && _textResult.Content(ResultTextKind.Source) is not null;
        _saveTranslation.IsEnabled = !_textResult.Saving && _textResult.Content(ResultTextKind.Translation) is not null;
        _saveBoth.IsEnabled = !_textResult.Saving && _textResult.Content(ResultTextKind.Both) is not null;
        _targetCombo.IsEnabled = !_textResult.Saving && _textResult.Source.Length > 0;
        _groupCombo.IsEnabled = !_textResult.Saving;
        RefreshToolVisibility();
    }

    private void RefreshToolVisibility()
    {
        bool available = !_inProgress && _save is not null && _textResult.Content(ResultTextKind.Source) is not null;
        Visibility target = available && _settings.Current.TranslationResultShowTargetSelector
            ? Visibility.Visible : Visibility.Collapsed;
        Visibility save = available && _settings.Current.TranslationResultShowSavePanel
            ? Visibility.Visible : Visibility.Collapsed;
        bool changed = _targetTools.Visibility != target || _saveTools.Visibility != save;
        _targetTools.Visibility = target;
        _groupTools.Visibility = save;
        _saveTools.Visibility = save;
        _resultTools.Visibility = target == Visibility.Visible || save == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshToolLayout();
        _toolsCard.Visibility = _resultTools.Visibility;
        if (changed) ClampIntoWorkArea();
    }

    public void SetActions(Action? retry, Action? openSettings)
    {
        _retry = retry;
        _openSettings = openSettings;
        _retryButton.Visibility = retry is null ? Visibility.Collapsed : Visibility.Visible;
        _settingsButton.Visibility = openSettings is null ? Visibility.Collapsed : Visibility.Visible;
        _recoveryActions.Visibility = retry is null && openSettings is null ? Visibility.Collapsed : Visibility.Visible;
        ClampIntoWorkArea();
    }

    public void ShowResult(string translated)
    {
        _inProgress = false;
        _resultText = translated;
        _textResult.SetTranslation(_textResult.Revision, translated);
        _copyable = true;
        RefreshCopyAction();
        SetBody(translated);
        _body.Cursor = System.Windows.Input.Cursors.Hand;
        _hint.Visibility = Visibility.Collapsed;
        ClampIntoWorkArea();
        StartHideTimer();
        RefreshSaveButtons();
    }

    public void ShowError(string messageKey = "Translate.Failed")
    {
        _inProgress = false;
        _resultText = string.Empty;
        _textResult.SetTranslation(_textResult.Revision, string.Empty);
        _copyable = false;
        RefreshCopyAction();
        SetBody(LocalizationService.Instance[messageKey]);
        _body.Cursor = System.Windows.Input.Cursors.Arrow;
        _hint.Visibility = Visibility.Collapsed;
        ClampIntoWorkArea();
        StartHideTimer();
        RefreshSaveButtons();
    }

    private void StartHideTimer()
    {
        _hideTimer.Stop();
        if (!_persistentResult && IsVisible && !IsMouseOver && _copyable) _hideTimer.Start();
    }

    private void SetBody(string text) => _body.Text = _bodyGlow.Text = text;

    private void ClampIntoWorkArea()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!IsVisible) return;
            var work = System.Windows.Forms.Screen.FromPoint(new(_anchorX, _anchorY)).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            MaxWidth = Math.Max(120, Math.Min(440, work.Width / dpi.DpiScaleX));
            MaxHeight = Math.Max(1, work.Height / dpi.DpiScaleY);
            double contentRoom = Math.Max(80, work.Height / dpi.DpiScaleY - 350);
            _sourceScroll.MaxHeight = Math.Min(130, contentRoom * 0.35);
            _bodyScroll.MaxHeight = Math.Min(240, contentRoom * 0.65);
            UpdateLayout();
            PopupPlacement.PlaceNear(this, _anchorX, _anchorY, preferAbove: false);
        });
    }

    private void ClearContent()
    {
        _textResult.Reset();
        _inProgress = false;
        _save = null;
        _translateTarget = null;
        _resultTools.Visibility = Visibility.Collapsed;
        _toolsCard.Visibility = Visibility.Collapsed;
        _targetTools.Visibility = _groupTools.Visibility = _saveTools.Visibility = Visibility.Collapsed;
        _saveStatus.Text = string.Empty;
        _saveStatus.Visibility = Visibility.Collapsed;
        _hideTimer.Stop();
        _copyable = _persistentResult = false;
        _headingKey = "Translate.Button";
        RefreshHeading();
        RefreshCopyAction();
        _resultText = _sourceText = _source.Text = string.Empty;
        _sourcePanel.Visibility = _hint.Visibility = Visibility.Collapsed;
        _hint.Text = string.Empty;
        _body.Cursor = System.Windows.Input.Cursors.Arrow;
        SetActions(null, null);
    }

    public void HideCard()
    {
        ClearContent();
        SetBody(string.Empty);
        if (IsVisible) Hide();
    }
}
