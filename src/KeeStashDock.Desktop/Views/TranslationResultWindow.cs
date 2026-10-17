using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Shared non-activating translation/OCR card. Source text remains available when
/// translation fails; clipboard writes happen only in response to an explicit click.
/// </summary>
public sealed class TranslationResultWindow : Window
{
    private const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x00000080, GWL_EXSTYLE = -20;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _hideTimer;
    private readonly Border _surface;
    private readonly TextBlock _body, _bodyGlow, _hint, _source;
    private readonly StackPanel _sourcePanel;
    private readonly ScrollViewer _sourceScroll, _bodyScroll;
    private readonly Button _copySource, _retryButton, _settingsButton, _closeButton;
    private string _resultText = string.Empty, _sourceText = string.Empty;
    private bool _copyable, _persistentResult;
    private int _anchorX, _anchorY;
    private Action? _retry, _openSettings;

    public TranslationResultWindow(SettingsStore settings)
    {
        _settings = settings;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 420;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _hideTimer.Tick += (_, _) => HideCard();

        TextBlock MakeBody() => new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        _body = MakeBody();
        _bodyGlow = MakeBody();
        _source = MakeBody();
        _hint = new TextBlock { FontSize = 10.5, Margin = new Thickness(0, 6, 0, 0), Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap };

        Button MakeButton(Action action)
        {
            var button = new Button { Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 4, 5, 0),
                Cursor = System.Windows.Input.Cursors.Arrow, MaxWidth = 240 };
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

        _sourcePanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
        _sourcePanel.Children.Add(_sourceScroll = new ScrollViewer { Content = _source, MaxHeight = 130,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        _sourcePanel.Children.Add(_copySource);
        _bodyScroll = new ScrollViewer { Content = GlassBubble.LayerGlow(_body, _bodyGlow), MaxHeight = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        // Copy only from the result text area; source, scroll bars, and buttons have independent actions.
        _body.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!TryCopyResult(text => Clipboard.SetDataObject(text, copy: true))) return;
            HideCard();
            MiniToast.Show(LocalizationService.Instance["QuickPanel.Copied"]);
        };
        var actions = new WrapPanel();
        actions.Children.Add(_retryButton);
        actions.Children.Add(_settingsButton);
        actions.Children.Add(_closeButton);
        var stack = new StackPanel();
        stack.Children.Add(_sourcePanel);
        stack.Children.Add(_bodyScroll);
        stack.Children.Add(_hint);
        stack.Children.Add(actions);
        _surface = new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 9), Child = stack,
            Margin = new Thickness(10)
        };
        Content = _surface;
        ApplyStyle();
        ApplyLanguage();
        SetActions(null, null);
        _settings.Changed += ApplySettings;
        ThemeManager.Changed += ApplyStyle;
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
        Closed += (_, _) =>
        {
            ClearContent();
            _settings.Changed -= ApplySettings;
            ThemeManager.Changed -= ApplyStyle;
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

    private void ApplySettings() => Dispatcher.Invoke(ApplyStyle);

    private void ApplyLanguage()
    {
        var loc = LocalizationService.Instance;
        loc.ApplyFlowDirection(this);
        void Label(Button button, string key)
        {
            string label = loc[key];
            button.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(button, label);
        }
        Label(_copySource, "Screenshot.CopySource");
        Label(_retryButton, "Screenshot.RetryTranslation");
        Label(_settingsButton, "Screenshot.OpenComponents");
        Label(_closeButton, "Screenshot.CloseResult");
    }

    internal bool TryCopyResult(Action<string> copy) => _copyable && TryCopy(_resultText, copy);
    internal bool TryCopySource(Action<string> copy) => _sourceText.Length > 0 && TryCopy(_sourceText, copy);

    private bool TryCopy(string text, Action<string> copy)
    {
        try { ClipboardWriteGuard.Shared.Write(() => copy(text)); return true; }
        catch (System.Runtime.InteropServices.COMException)
        {
            _hint.Text = LocalizationService.Instance["Screenshot.CopyFailed"];
            _hint.Visibility = Visibility.Visible;
            StartHideTimer();
            return false;
        }
    }

    private void ApplyStyle()
    {
        Color text = GlassBubble.Apply(_settings.Current, _surface);
        var fg = new SolidColorBrush(text);
        _body.Foreground = _bodyGlow.Foreground = _hint.Foreground = _source.Foreground = fg;
        GlassBubble.SetGlowEnabled(_bodyGlow, _settings.Current.BubbleTextGlow);
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
        SetBody(LocalizationService.Instance[key]);
        (_anchorX, _anchorY) = (deviceX, deviceY);
        PopupPlacement.ShowNear(this, deviceX, deviceY, preferAbove: false);
        _hideTimer.Stop();
    }

    public void SetRecognizedSource(string text)
    {
        _sourceText = _source.Text = text;
        _sourcePanel.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _persistentResult = text.Length > 0;
        ClampIntoWorkArea();
    }

    public void ShowRecognizedText(string text)
    {
        _persistentResult = true;
        ShowResult(text);
    }

    public void SetActions(Action? retry, Action? openSettings)
    {
        _retry = retry;
        _openSettings = openSettings;
        _retryButton.Visibility = retry is null ? Visibility.Collapsed : Visibility.Visible;
        _settingsButton.Visibility = openSettings is null ? Visibility.Collapsed : Visibility.Visible;
        ClampIntoWorkArea();
    }

    public void ShowResult(string translated)
    {
        _resultText = translated;
        _copyable = true;
        SetBody(translated);
        _body.Cursor = System.Windows.Input.Cursors.Hand;
        _hint.Text = LocalizationService.Instance["Translate.CopyHint"];
        _hint.Visibility = Visibility.Visible;
        ClampIntoWorkArea();
        StartHideTimer();
    }

    public void ShowError(string messageKey = "Translate.Failed")
    {
        _resultText = string.Empty;
        _copyable = false;
        SetBody(LocalizationService.Instance[messageKey]);
        _body.Cursor = System.Windows.Input.Cursors.Arrow;
        _hint.Visibility = Visibility.Collapsed;
        ClampIntoWorkArea();
        StartHideTimer();
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
            MaxWidth = Math.Max(120, Math.Min(420, work.Width / dpi.DpiScaleX));
            double contentRoom = Math.Max(80, work.Height / dpi.DpiScaleY - 190);
            _sourceScroll.MaxHeight = Math.Min(130, contentRoom * 0.35);
            _bodyScroll.MaxHeight = Math.Min(240, contentRoom * 0.65);
            UpdateLayout();
            PopupPlacement.PlaceNear(this, _anchorX, _anchorY, preferAbove: false);
        });
    }

    private void ClearContent()
    {
        _hideTimer.Stop();
        _copyable = _persistentResult = false;
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
