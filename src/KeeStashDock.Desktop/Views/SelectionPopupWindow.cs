using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// The action bubble that appears beside a text selection offers
/// either "copy to KeeStashDock" or "translate", per the SelectionAction
/// setting - its label, icon and raised event follow the setting live. Critical property: WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW - the
/// bubble must NEVER take focus, because the capture flow synthesizes
/// Ctrl+C into the application that still owns the selection; if clicking
/// the bubble activated us, the copy would land in the wrong window.
/// One instance is reused; it auto-hides after a short grace period or on
/// any click elsewhere (driven by SelectionWatcher.AnyMouseDown).
/// </summary>
public sealed class SelectionPopupWindow : Window
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int GWL_EXSTYLE = -20;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly DispatcherTimer _hideTimer;
    private readonly SettingsStore _settings;
    private readonly TextBlock _icon;
    private readonly TextBlock _label;
    private readonly TextBlock _iconGlow;
    private readonly TextBlock _labelGlow;
    private readonly StackPanel _glowHost;
    private readonly Border _button;

    /// <summary>Raised when the user clicks the bubble while the selection
    /// action is "Capture" / "Translate" respectively.</summary>
    public event Action? CaptureRequested;
    public event Action? TranslateRequested;

    public SelectionPopupWindow(SettingsStore settings)
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

        // Crisp text sits ON TOP of a separate blurred twin that provides
        // the glow: a BitmapEffect directly on the text rasterizes it and
        // was why the label looked out of focus. The top copy renders with
        // Display formatting (pixel-snapped), the bottom copy carries all
        // the blur.
        TextBlock MakeIcon() => new()
        {
            Text = "\uE8C8",   // copy glyph
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        TextBlock MakeLabel() => new()
        { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };

        _icon = MakeIcon(); _iconGlow = MakeIcon();
        _label = MakeLabel(); _labelGlow = MakeLabel();
        _glowHost = MakeRowHost(_iconGlow, _labelGlow);
        var row = GlassBubble.LayerGlow(
            MakeRowHost(_icon, _label), _glowHost);

        static StackPanel MakeRowHost(TextBlock i, TextBlock l)
        {
            var r = new StackPanel { Orientation = Orientation.Horizontal };
            r.Children.Add(i); r.Children.Add(l);
            return r;
        }

        _button = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(13, 7, 13, 7),
            BorderThickness = new Thickness(1),
            Child = row,
            Cursor = System.Windows.Input.Cursors.Hand,
            // Margin gives the outer drop shadow transparent room to render.
            Margin = new Thickness(10),
        };
        _button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            HideBubble();
            if (_settings.Current.SelectionAction == "Translate")
                TranslateRequested?.Invoke();
            else
                CaptureRequested?.Invoke();
        };
        Content = _button;

        ApplyStyle();
        SetLabel();
        // Restyle live: text-color scheme / theme / selection-action changes
        // must not need an app restart to reach the bubble.
        _settings.Changed += () => Dispatcher.Invoke(() => { ApplyStyle(); SetLabel(); });
        ThemeManager.Changed += ApplyStyle;
        Localization.LocalizationService.Instance.LanguageChanged += SetLabel;

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _hideTimer.Tick += (_, _) => HideBubble();

        SourceInitialized += (_, _) =>
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            SetWindowLong(h, GWL_EXSTYLE,
                GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    /// <summary>Frosted-glass look via the shared <see cref="GlassBubble"/>
    /// builder (also used by the translation card and MiniToast): surface
    /// derived from the settings-driven text color, glow as a blurred twin
    /// under the crisp text.</summary>
    private void ApplyStyle()
    {
        Color text = GlassBubble.Apply(_settings.Current, _button);
        var fg = new SolidColorBrush(text);
        _icon.Foreground = fg;
        _label.Foreground = fg;
        _iconGlow.Foreground = fg;
        _labelGlow.Foreground = fg;
        GlassBubble.SetGlowEnabled(_glowHost, _settings.Current.BubbleTextGlow);
    }

    /// <summary>Label and glyph follow the configured selection action:
    /// copy glyph + "copy to..." for Capture, globe + "translate" for
    /// Translate (both copies of each, crisp and glow twin).</summary>
    private void SetLabel()
    {
        bool translate = _settings.Current.SelectionAction == "Translate";
        _icon.Text = _iconGlow.Text = translate ? "\uE774" : "\uE8C8";
        _label.Text = _labelGlow.Text = Localization.LocalizationService
            .Instance[translate ? "Translate.Button" : "Capture.Button"];
    }

    /// <summary>Shows the bubble near a screen point given in DEVICE pixels
    /// (straight from the mouse hook), clamped into the work area.</summary>
    public void ShowAt(int deviceX, int deviceY)
    {
        double dpi = 1.0;
        var t = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (t is not null) dpi = t.TransformToDevice.M11;
        else
        {
            var main = Application.Current?.Windows.Count > 0
                ? Application.Current.Windows[0] : null;
            var mt = main is null ? null
                : PresentationSource.FromVisual(main)?.CompositionTarget;
            if (mt is not null) dpi = mt.TransformToDevice.M11;
        }

        var wa = SystemParameters.WorkArea;
        // Above-right of the release point; flip below when out of room.
        double x = deviceX / dpi + 10;
        double y = deviceY / dpi - 44;
        if (y < wa.Top) y = deviceY / dpi + 18;
        Left = Math.Clamp(x, wa.Left, wa.Right - 170);
        Top = Math.Clamp(y, wa.Top, wa.Bottom - 44);

        Show();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void HideBubble()
    {
        _hideTimer.Stop();
        if (IsVisible) Hide();
    }
}
