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
        _separator = new Border { Width = 1, Margin = new Thickness(4, 10, 4, 10) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(_copy.Button);
        actions.Children.Add(_separator);
        actions.Children.Add(_translate.Button);
        _surface = new Border
        {
            BorderThickness = new Thickness(1), Margin = new Thickness(12), Padding = new Thickness(5),
        };
        BubbleChrome.Decorate(_surface, actions);
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
        TypographyManager.Changed += OnTypographyChanged;
        LocalizationService.Instance.LanguageChanged += RefreshLabels;
        Closed += (_, _) =>
        {
            HideBubble();
            _settings.Changed -= SettingsChanged;
            ThemeManager.Changed -= ApplyStyle;
            TypographyManager.Changed -= OnTypographyChanged;
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

    private void OnTypographyChanged()
    {
        if (IsVisible) FitToWorkArea();
    }

    private void ApplyStyle()
    {
        BubblePalette palette = BubbleChrome.Apply(_surface, _settings.Current);
        BubbleChrome.ApplyResources(this, palette);
        _copy.ApplyStyle(palette);
        _translate.ApplyStyle(palette);
        _separator.Background = BubbleChrome.Brush(palette.Border);
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
        // Disabled feedback belongs to the button template, including busy states.
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
        double textWidth = Math.Clamp((room - 36 - (count - 1) * 9) / count - 60, 24, 240);
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
        private readonly TextBlock _icon, _label, _labelGlow;
        private readonly Border _iconChip;

        internal BubbleAction(string glyph)
        {
            _icon = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _iconChip = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(8),
                Child = _icon, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            TextBlock Label()
            {
                var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold, MaxWidth = 240, TextWrapping = TextWrapping.Wrap };
                TypographyManager.SetFontSize(label, 13);
                return label;
            }
            _label = Label(); _labelGlow = Label();
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(_iconChip);
            var text = GlassBubble.LayerGlow(_label, _labelGlow);
            Grid.SetColumn(text, 1); row.Children.Add(text);
            Button = new Button
            {
                Content = row, Padding = new Thickness(9, 6, 12, 6),
                Focusable = false, IsTabStop = false, Cursor = Cursors.Hand,
            };
        }

        internal void SetLabel(string text)
        {
            _label.Text = _labelGlow.Text = text;
            AutomationProperties.SetName(Button, text);
        }

        internal void SetTextWidth(double width) => _label.MaxWidth = _labelGlow.MaxWidth = width;

        internal void ApplyStyle(BubblePalette palette)
        {
            BubbleChrome.ApplyButton(Button, palette);
            Button.Background = Brushes.Transparent;
            Button.BorderThickness = new Thickness(0);
            _iconChip.Background = BubbleChrome.Brush(palette.Hover);
            _icon.Foreground = BubbleChrome.Brush(palette.HighContrast ? palette.OnAccent : palette.Accent);
            _label.Foreground = _labelGlow.Foreground = BubbleChrome.Brush(palette.Text);
            GlassBubble.SetGlowEnabled(_labelGlow, palette.Glow);
        }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(nint window, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(nint window, int index, int value);
}
