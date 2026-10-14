using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// A non-activating selection bubble with independent Copy and Translate actions.
/// Temporary keyboard shortcuts also preserve focus in the source application.
/// The text itself is owned by the application's transient selection session.
/// </summary>
public sealed class SelectionPopupWindow : Window
{
    private const int WsExNoActivate = 0x08000000, WsExToolWindow = 0x00000080, GwlExStyle = -20;
    private const int CopyHotkeyId = 0x4B31, TranslateHotkeyId = 0x4B32;
    private readonly DispatcherTimer _hideTimer;
    private readonly SettingsStore _settings;
    private readonly Border _surface;
    private readonly BubbleAction _copy;
    private readonly BubbleAction _translate;
    private readonly Border _separator;
    private bool _busy, _captured, _copyHotkey, _translateHotkey;
    private int _deviceX, _deviceY;

    public event Action? CaptureRequested;
    public event Action? TranslateRequested;
    public event Action? Dismissed;

    public SelectionPopupWindow(SettingsStore settings)
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
        _copy = new BubbleAction("\uE8C8");
        _translate = new BubbleAction("\uE774");
        _separator = new Border { Width = 1, Margin = new Thickness(0, 7, 0, 7), Opacity = 0.25 };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_copy.Button);
        actions.Children.Add(_separator);
        actions.Children.Add(_translate.Button);
        _surface = new Border
        {
            CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1),
            Child = actions, Margin = new Thickness(10), Padding = new Thickness(2),
        };
        Content = _surface;
        _copy.Button.Click += (_, _) => RequestCopy();
        _translate.Button.Click += (_, _) => RequestTranslate();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _hideTimer.Tick += (_, _) =>
        {
            if (!_busy && !IsMouseOver) HideBubble();
        };
        MouseEnter += (_, _) => _hideTimer.Stop();
        MouseLeave += (_, _) => RestartHideTimer();
        SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GwlExStyle, GetWindowLong(handle, GwlExStyle) | WsExNoActivate | WsExToolWindow);
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        };
        _settings.Changed += SettingsChanged;
        ThemeManager.Changed += ApplyStyle;
        LocalizationService.Instance.LanguageChanged += RefreshLabels;
        Closed += (_, _) =>
        {
            HideBubble();
            _settings.Changed -= SettingsChanged;
            ThemeManager.Changed -= ApplyStyle;
            LocalizationService.Instance.LanguageChanged -= RefreshLabels;
        };
        ApplyStyle();
        RefreshLabels();
    }

    private void SettingsChanged() => Dispatcher.Invoke(() =>
    {
        ApplyStyle(); RefreshLabels();
        if (IsVisible)
        {
            RegisterActionHotkeys(); FitToWorkArea();
        }
    });

    private void ApplyStyle()
    {
        var brush = new SolidColorBrush(GlassBubble.Apply(_settings.Current, _surface));
        _copy.ApplyStyle(brush, _settings.Current.BubbleTextGlow);
        _translate.ApplyStyle(brush, _settings.Current.BubbleTextGlow);
        _separator.Background = brush;
    }

    private void RefreshLabels()
    {
        var loc = LocalizationService.Instance;
        loc.ApplyFlowDirection(this);
        _copy.SetLabel(loc["Capture.Button"]);
        _translate.SetLabel(loc["Translate.Button"]);
        string mode = _settings.Current.SelectionAction;
        _copy.Button.Visibility = mode == "Translate" ? Visibility.Collapsed : Visibility.Visible;
        _translate.Button.Visibility = mode is "Translate" or "Both" ? Visibility.Visible : Visibility.Collapsed;
        _separator.Visibility = mode == "Both" ? Visibility.Visible : Visibility.Collapsed;
        UpdateEnabledState(); UpdateShortcutLabels();
        if (IsVisible)
        {
            FitToWorkArea();
        }
    }

    private void UpdateEnabledState()
    {
        _copy.Button.IsEnabled = !_busy && !_captured;
        _translate.Button.IsEnabled = !_busy;
        _copy.Button.Opacity = _captured ? 0.45 : 1;
    }

    public void SetBusy(bool busy)
    {
        _busy = busy; UpdateEnabledState(); RestartHideTimer();
    }

    public void MarkCaptured()
    {
        _captured = true; _busy = false;
        if (_settings.Current.SelectionAction != "Both") HideBubble();
        else { UpdateEnabledState(); RestartHideTimer(); }
    }

    private void RequestCopy()
    {
        if (!IsVisible || !_copy.Button.IsEnabled || _copy.Button.Visibility != Visibility.Visible) return;
        CaptureRequested?.Invoke();
    }

    private void RequestTranslate()
    {
        if (!IsVisible || !_translate.Button.IsEnabled || _translate.Button.Visibility != Visibility.Visible) return;
        TranslateRequested?.Invoke();
    }

    public void ShowAt(int deviceX, int deviceY)
    {
        (_deviceX, _deviceY) = (deviceX, deviceY);
        _busy = _captured = false;
        RefreshLabels();
        PopupPlacement.ShowNear(this, deviceX, deviceY, preferAbove: true);
        FitToWorkArea();
        RegisterActionHotkeys(); RestartHideTimer();
    }

    private void FitToWorkArea()
    {
        var work = System.Windows.Forms.Screen.FromPoint(new(_deviceX, _deviceY)).WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double room = work.Width / scale;
        int count = _settings.Current.SelectionAction == "Both" ? 2 : 1;
        // Split remaining text width between the actions after decorative margins,
        // button padding, and glyphs, so long translations wrap on small monitors.
        double textWidth = Math.Clamp((room - 26) / count - 45, 24, 240);
        _copy.SetTextWidth(textWidth);
        _translate.SetTextWidth(textWidth);
        UpdateLayout();
        PopupPlacement.PlaceNear(this, _deviceX, _deviceY, preferAbove: true);
    }

    public void HideBubble()
    {
        _hideTimer.Stop(); UnregisterActionHotkeys();
        if (IsVisible) Hide();
        _busy = _captured = false;
        Dismissed?.Invoke();
    }

    private void RestartHideTimer()
    {
        _hideTimer.Stop();
        if (IsVisible && !_busy && !IsMouseOver) _hideTimer.Start();
    }

    private void RegisterActionHotkeys()
    {
        UnregisterActionHotkeys();
        nint handle = new WindowInteropHelper(this).Handle;
        uint modifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_SHIFT | NativeMethods.MOD_NOREPEAT;
        if (_copy.Button.Visibility == Visibility.Visible)
            _copyHotkey = NativeMethods.RegisterHotKey(handle, CopyHotkeyId, modifiers, 0x43);
        if (_translate.Button.Visibility == Visibility.Visible)
            _translateHotkey = NativeMethods.RegisterHotKey(handle, TranslateHotkeyId, modifiers, 0x54);
        UpdateShortcutLabels();
    }

    private void UpdateShortcutLabels()
    {
        var loc = LocalizationService.Instance;
        _copy.Button.ToolTip = loc[_copyHotkey ? "Selection.CopyShortcut" : "Capture.Button"];
        _translate.Button.ToolTip = loc[_translateHotkey ? "Selection.TranslateShortcut" : "Translate.Button"];
        AutomationProperties.SetAcceleratorKey(_copy.Button, _copyHotkey ? "Ctrl+Alt+Shift+C" : "");
        AutomationProperties.SetAcceleratorKey(_translate.Button, _translateHotkey ? "Ctrl+Alt+Shift+T" : "");
    }

    private void UnregisterActionHotkeys()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (_copyHotkey) NativeMethods.UnregisterHotKey(handle, CopyHotkeyId);
        if (_translateHotkey) NativeMethods.UnregisterHotKey(handle, TranslateHotkeyId);
        _copyHotkey = _translateHotkey = false;
    }

    private nint WndProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WM_HOTKEY)
        {
            if ((int)wParam == CopyHotkeyId) { handled = true; RequestCopy(); }
            if ((int)wParam == TranslateHotkeyId) { handled = true; RequestTranslate(); }
        }
        return nint.Zero;
    }

    private sealed class BubbleAction
    {
        internal Button Button { get; }
        private readonly TextBlock _icon, _label, _iconGlow, _labelGlow;
        private readonly StackPanel _glow;

        internal BubbleAction(string glyph)
        {
            TextBlock Icon() => new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBlock Label() => new() { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 240, TextWrapping = TextWrapping.Wrap };
            _icon = Icon(); _iconGlow = Icon(); _label = Label(); _labelGlow = Label();
            static StackPanel Row(TextBlock icon, TextBlock label)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(icon); row.Children.Add(label); return row;
            }
            _glow = Row(_iconGlow, _labelGlow);
            Button = new Button
            {
                Content = GlassBubble.LayerGlow(Row(_icon, _label), _glow),
                Padding = new Thickness(11, 5, 11, 5), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Focusable = false, IsTabStop = false, Cursor = Cursors.Hand,
            };
            // Keep Button automation and clicks, without the opaque default chrome.
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(22, 128, 128, 128))));
            template.Triggers.Add(hover);
            Button.Template = template;
        }

        internal void SetLabel(string text)
        {
            _label.Text = _labelGlow.Text = text;
            AutomationProperties.SetName(Button, text);
        }

        internal void SetTextWidth(double width) => _label.MaxWidth = _labelGlow.MaxWidth = width;

        internal void ApplyStyle(Brush foreground, bool glow)
        {
            _icon.Foreground = _iconGlow.Foreground = _label.Foreground = _labelGlow.Foreground = foreground;
            GlassBubble.SetGlowEnabled(_glow, glow);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(nint window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(nint window, int index, int value);
}
