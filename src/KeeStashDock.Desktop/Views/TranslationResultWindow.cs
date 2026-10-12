using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// The selection-translation result card uses the same glass
/// family as the selection bubble via <see cref="GlassBubble"/>, sized for
/// paragraphs. Shows a "translating..." state first, then the result plus
/// a muted "click to copy" hint; clicking copies and dismisses. Same
/// WS_EX_NOACTIVATE + TOOLWINDOW treatment as the bubble - it must never
/// steal focus from the app the user is reading. One instance is reused;
/// it auto-hides after a grace period or on any outside click.
/// </summary>
public sealed class TranslationResultWindow : Window
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int GWL_EXSTYLE = -20;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _hideTimer;
    private readonly Border _surface;
    private readonly TextBlock _body;
    private readonly TextBlock _bodyGlow;
    private readonly TextBlock _hint;
    private string _resultText = string.Empty;
    private bool _copyable;
    private int _anchorX, _anchorY;

    public TranslationResultWindow(SettingsStore settings)
    {
        Localization.LocalizationService.Instance.ApplyFlowDirection(this);
        Localization.LocalizationService.Instance.LanguageChanged +=
            () => Localization.LocalizationService.Instance.ApplyFlowDirection(this);
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

        TextBlock MakeBody() => new()
        {
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 260,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _body = MakeBody(); _bodyGlow = MakeBody();

        _hint = new TextBlock
        {
            FontSize = 10.5,
            Margin = new Thickness(0, 6, 0, 0),
            Opacity = 0.75,
        };

        var stack = new StackPanel();
        stack.Children.Add(GlassBubble.LayerGlow(_body, _bodyGlow));
        stack.Children.Add(_hint);

        _surface = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 10, 14, 9),
            Child = stack,
            Cursor = System.Windows.Input.Cursors.Hand,
            // Transparent room for the outer drop shadow to render into.
            Margin = new Thickness(10),
        };
        _surface.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!_copyable) return;   // still translating / error state
            try { Clipboard.SetDataObject(_resultText, copy: true); } catch { }
            HideCard();
            MiniToast.Show(Localization.LocalizationService.Instance["QuickPanel.Copied"]);
        };
        Content = _surface;

        ApplyStyle();
        _settings.Changed += () => Dispatcher.Invoke(ApplyStyle);
        ThemeManager.Changed += ApplyStyle;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        _hideTimer.Tick += (_, _) => HideCard();

        SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE,
                GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    private void ApplyStyle()
    {
        Color text = GlassBubble.Apply(_settings.Current, _surface);
        var fg = new SolidColorBrush(text);
        _body.Foreground = fg;
        _bodyGlow.Foreground = fg;
        _hint.Foreground = fg;
        GlassBubble.SetGlowEnabled(_bodyGlow, _settings.Current.BubbleTextGlow);
    }

    /// <summary>Shows the in-progress state near a DEVICE-pixel point
    /// (straight from the mouse hook), clamped into the work area.</summary>
    public void ShowTranslating(int deviceX, int deviceY)
    {
        var loc = Localization.LocalizationService.Instance;
        SetBody(loc["Translate.InProgress"]);
        _hint.Text = string.Empty;
        _hint.Visibility = Visibility.Collapsed;
        _copyable = false;

        (_anchorX, _anchorY) = (deviceX, deviceY);
        PopupPlacement.ShowNear(this, deviceX, deviceY, preferAbove: false);
        _hideTimer.Stop();
        // The client owns a bounded request timeout. Do not dismiss the card
        // before it can display a slow response or a useful timeout message.
    }

    /// <summary>Swaps in the translation (click-to-copy armed).</summary>
    public void ShowResult(string translated)
    {
        _resultText = translated;
        _copyable = true;
        SetBody(translated);
        _hint.Text = Localization.LocalizationService.Instance["Translate.CopyHint"];
        _hint.Visibility = Visibility.Visible;
        ClampIntoWorkArea();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>Swaps in the failure message (not copyable).</summary>
    public void ShowError(string messageKey = "Translate.Failed")
    {
        _copyable = false;
        SetBody(Localization.LocalizationService.Instance[messageKey]);
        _hint.Visibility = Visibility.Collapsed;
        ClampIntoWorkArea();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void SetBody(string text) => _body.Text = _bodyGlow.Text = text;

    /// <summary>Growing from one line to a paragraph can push the card off
    /// the bottom/right edge; re-clamp after the content swap lays out.</summary>
    private void ClampIntoWorkArea()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (IsVisible) PopupPlacement.PlaceNear(this, _anchorX, _anchorY, preferAbove: false);
        });
    }

    public void HideCard()
    {
        _hideTimer.Stop();
        if (IsVisible) Hide();
    }
}
