using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using KeeStashDock.Desktop.Interop;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeeStashDock.Desktop;

/// <summary>
/// Runtime Light, Dark, and System theme switching. "System" reads the
/// Windows AppsUseLightTheme registry value; panel effects are applied by the
/// quick-panel appearance layer.
/// </summary>
public static class ThemeManager
{
    private static SettingsStore? _settings;
    private static Application? _application;
    private static string? _lastSelection;
    private static string? _lastMode;
    private static ThemeScheme? _scheme;
    private static ThemeScheme? _preview;
    private static bool _previewActive;
    private static string? _previewMode;
    private static readonly Dictionary<string, BitmapSource> Images = new(StringComparer.OrdinalIgnoreCase);
    public static ThemePackageStore Store { get; private set; } = new(Path.Combine(AppContext.BaseDirectory, "theme"), ValidateImage);
    public static ThemeScheme? ActiveScheme => _previewActive ? _preview : _scheme;
    public static ThemeVariant? ActiveVariant => SystemParameters.HighContrast ? null : IsDark ? ActiveScheme?.Dark : ActiveScheme?.Light;
    public static string? LoadError { get; private set; }

    /// <summary>Call once after settings load. Window hooks never affect screenshot pixels or QR image content.</summary>
    public static void Initialize(SettingsStore settings, string? themeRoot = null)
    {
        if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        _settings = settings;
        Store = new ThemePackageStore(themeRoot ?? Path.Combine(AppContext.BaseDirectory, "theme"), ValidateImage);
        _lastSelection = null; _lastMode = null;
        settings.Changed += OnSettingsChanged;
        ThemeWindowSurface.Register();
        SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
        SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        if (_application is not null) _application.Exit -= OnApplicationExit;
        _application = Application.Current;
        if (_application is not null) _application.Exit += OnApplicationExit;
        Apply(settings.Current.Theme);
    }

    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        => QueueSystemAppearanceRefresh(force: false);

    private static void QueueSystemAppearanceRefresh(bool force)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (_settings is null || dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        // Windows can send several preference notifications for one switch.
        // Read the mode on the UI thread and repaint only an actual change.
        try
        {
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (_settings is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
                string mode = (_previewActive ? _previewMode : _settings.Current.Theme) ?? "System";
                if (force || mode == "System" && IsSystemDark() != IsDark) Apply(mode);
            }), DispatcherPriority.Normal);
        }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) { }
    }

    private static void OnSystemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) QueueSystemAppearanceRefresh(force: true);
    }

    private static void OnApplicationExit(object? sender, ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
        if (_settings is not null) _settings.Changed -= OnSettingsChanged;
        if (_application is not null) _application.Exit -= OnApplicationExit;
        _application = null; _settings = null; Images.Clear();
    }

    private static void OnSettingsChanged()
    {
        if (_settings is null) return;
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settings.Current.ThemePresetId != _lastSelection || _settings.Current.Theme != _lastMode) Apply(_settings.Current.Theme);
        });
    }

    public static void RefreshSchemes()
    {
        _lastSelection = null; Images.Clear();
        Apply(_settings?.Current.Theme ?? "System");
    }

    public static void Preview(ThemeScheme? scheme, string mode)
    {
        _previewActive = true; _preview = scheme; _previewMode = mode;
        Apply(mode);
    }

    public static void EndPreview()
    {
        _previewActive = false; _preview = null; _previewMode = null;
        Apply(_settings?.Current.Theme ?? "System");
    }

    public static string? GetBackgroundPath(string role)
    {
        if (ActiveVariant?.Backgrounds.TryGetValue(role, out ThemeBackground? background) != true || background is null || ActiveScheme is null) return null;
        try { return ThemePackageStore.Resolve(ActiveScheme.Directory, background.Path); }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex)) { return null; }
    }

    /// <summary>Preset crop/opacity applies only while its background is effective, including live previews.</summary>
    public static ThemeBackground? GetPanelBackground(AppSettings saved) =>
        (_previewActive || !saved.ThemeUseCustomBackground) && ActiveVariant?.Backgrounds.TryGetValue("quickPanel", out ThemeBackground? background) == true
            ? background : null;

    /// <summary>Computes a disposable appearance snapshot; never rewrites the user's original wallpaper or layout.</summary>
    public static AppSettings CreatePanelAppearanceSettings(AppSettings saved)
    {
        if (SystemParameters.HighContrast)
        {
            AppSettings accessible = saved.Copy();
            accessible.BackgroundImagePath = null; accessible.CardOpacity = 1; accessible.SettingsSurfaceOpacity = 1;
            accessible.PanelTextColorMode = "Auto"; accessible.FrostedGlassEnabled = false;
            accessible.BubbleOpacity = 1;
            accessible.SettingsFrostedGlassEnabled = false;
            return accessible;
        }
        if (ActiveVariant is not ThemeVariant variant)
        {
            if (_previewActive)
            {
                AppSettings preview = saved.Copy(); preview.BackgroundImagePath = null; preview.CardOpacity = .9; preview.SettingsSurfaceOpacity = 1;
                preview.SettingsFrostedGlassEnabled = false; preview.SettingsFrostedGlassRadius = 8;
                preview.PanelTextColorMode = "Auto"; preview.PanelTextCustomColor = "#FFFFFF";
                return preview;
            }
            if (saved.ThemeUseCustomBackground || string.IsNullOrEmpty(saved.BackgroundImagePath)) return saved;
            AppSettings plain = saved.Copy(); plain.BackgroundImagePath = null; return plain;
        }
        AppSettings effective = saved.Copy();
        bool custom = !_previewActive && saved.ThemeAppearanceCustomized;
        if (_previewActive || !saved.ThemeUseCustomBackground)
        {
            effective.BackgroundImagePath = GetBackgroundPath("quickPanel");
            if (!custom && variant.Backgrounds.TryGetValue("quickPanel", out ThemeBackground? background))
            {
                effective.BackgroundOverlayOpacity = background.OverlayOpacity;
                effective.FrostedGlassEnabled = background.BlurRadius > 0;
                effective.FrostedGlassRadius = background.BlurRadius;
            }
        }
        if (!custom)
        {
            effective.CardOpacity = variant.CardOpacity;
            effective.SettingsSurfaceOpacity = variant.SettingsSurfaceOpacity;
            effective.SettingsFrostedGlassEnabled = variant.SettingsFrostedGlassEnabled;
            effective.SettingsFrostedGlassRadius = variant.SettingsFrostedGlassRadius;
            if (saved.SelectionBubbleStyle == "FollowTheme")
            {
                effective.BubbleOpacity = variant.BubbleOpacity;
            }
            effective.PanelTextColorMode = variant.PanelTextColorMode;
            effective.PanelTextCustomColor = variant.PanelTextCustomColor;
        }
        return effective;
    }

    public static ThemeVariant CreateDefaultVariant(bool dark)
    {
        var palette = new ResourceDictionary { Source = new Uri(dark ? "/KeeStashDock;component/Themes/Theme.Dark.xaml" :
            "/KeeStashDock;component/Themes/Theme.Light.xaml", UriKind.Relative) };
        var variant = new ThemeVariant();
        foreach (string key in ThemePackageStore.RequiredColors.Concat(ThemePackageStore.OptionalColors)) variant.Colors[key] = ((SolidColorBrush)palette[key]).Color.ToString();
        return variant;
    }

    public static BitmapSource? GetBackgroundImage(string role)
    {
        string? path = GetBackgroundPath(role);
        if (path is null) return null;
        try
        {
            string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
            if (Images.TryGetValue(key, out BitmapSource? cached)) return cached;
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path); bitmap.DecodePixelWidth = 1920; bitmap.EndInit(); bitmap.Freeze();
            if (Images.Count >= 8) Images.Clear();
            Images[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex)) { return null; }
    }

    internal static void ValidateImage(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth > 8192 || decoder.Frames[0].PixelHeight > 8192 ||
            (long)decoder.Frames[0].PixelWidth * decoder.Frames[0].PixelHeight > 32_000_000)
            throw new InvalidDataException("Unsupported theme image dimensions or animation.");
        // Force pixel decoding before anything is installed or previewed.
        BitmapSource frame = decoder.Frames[0];
        int stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
        byte[] row = new byte[stride];
        for (int y = 0; y < frame.PixelHeight; y++) frame.CopyPixels(new Int32Rect(0, y, frame.PixelWidth, 1), row, stride, 0);
    }
    /// <summary>Effective darkness of the ACTIVE palette (resolves "System").</summary>
    public static bool IsDark { get; private set; }

    /// <summary>Raised after every palette swap; chromed windows re-tint.</summary>
    public static event Action? Changed;

    public static void Apply(string theme)
    {
        string selection = _settings?.Current.ThemePresetId ?? "";
        if (!_previewActive && selection != _lastSelection)
        {
            _lastSelection = selection; _scheme = null; LoadError = null; Images.Clear();
            if (selection.Length > 0)
            {
                try
                {
                    _scheme = Store.Find(selection);
                    if (_scheme is null) LoadError = "Theme.Unavailable";
                }
                catch (Exception ex) when (ThemePackageStore.IsPackageFailure(ex)) { LoadError = "Theme.Unavailable"; }
            }
        }
        if (!_previewActive) _lastMode = theme;
        if (_previewActive) theme = _previewMode ?? theme;
        bool dark = theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => IsSystemDark(),
        };
        IsDark = dark;
        var uri = new Uri(dark ? "/KeeStashDock;component/Themes/Theme.Dark.xaml" : "/KeeStashDock;component/Themes/Theme.Light.xaml",
                          UriKind.Relative);
        var dicts = Application.Current.Resources.MergedDictionaries;
        // Slot 0 = palette, slot 1 = Styles.xaml (order fixed by App.xaml).
        var palette = new ResourceDictionary { Source = uri };
        if (ActiveVariant is ThemeVariant variant)
        {
            foreach ((string key, string value) in variant.Colors)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze();
                palette[key] = brush;
            }
            foreach ((string semantic, string fallback) in new[] { ("B.Focus", "B.Accent"), ("B.Link", "B.Accent"),
                ("B.Disabled", "B.TextMuted"), ("B.ScrollThumb", "B.Border"), ("B.SearchHighlight", "B.AccentSoft"),
                ("B.BubbleBg", "B.CardBg"), ("B.BubbleText", "B.TextPrimary"),
                ("B.BubbleAccent", "B.Accent"), ("B.BubbleAccentSoft", "B.AccentSoft"),
                ("B.BubbleBorder", "B.Border"), ("B.BubbleMuted", "B.TextMuted"), ("B.BubbleHighlight", "B.Accent") })
                if (!variant.Colors.ContainsKey(semantic)) palette[semantic] = palette[fallback];
            palette["C.Accent"] = ((SolidColorBrush)palette["B.Accent"]).Color;
        }
        if (SystemParameters.HighContrast)
        {
            palette["B.WindowBg"] = palette["B.CardBg"] = palette["B.PanelBg"] = palette["B.InputBg"] = SystemColors.WindowBrush;
            palette["B.TextPrimary"] = palette["B.TextMuted"] = palette["B.InputText"] = palette["B.InputTextMuted"] =
                palette["B.CardTextPrimary"] = palette["B.CardTextMuted"] = SystemColors.WindowTextBrush;
            palette["B.Accent"] = palette["B.AccentSoft"] = SystemColors.HighlightBrush;
            palette["B.OnAccent"] = SystemColors.HighlightTextBrush;
            palette["B.CardHover"] = palette["B.Border"] = palette["B.Focus"] = palette["B.ScrollThumb"] = SystemColors.WindowTextBrush;
            palette["B.Disabled"] = SystemColors.GrayTextBrush;
            palette["B.Link"] = palette["B.SearchHighlight"] = SystemColors.HighlightBrush;
            palette["B.BubbleBg"] = SystemColors.WindowBrush; palette["B.BubbleText"] = SystemColors.WindowTextBrush;
            palette["B.BubbleAccent"] = SystemColors.HighlightBrush; palette["B.BubbleAccentSoft"] = SystemColors.ControlBrush;
            palette["B.BubbleBorder"] = palette["B.BubbleMuted"] = SystemColors.WindowTextBrush;
            palette["B.BubbleHighlight"] = SystemColors.WindowTextBrush;
            palette["C.Accent"] = SystemColors.HighlightColor;
        }
        dicts[0] = palette;
        Changed?.Invoke();
    }

    /// <summary>Tints the NATIVE window frame (title bar) to match the active
    /// palette and requests Win11 rounded corners. Before this, chromed
    /// windows (workbench / settings / dialogs) kept a white title bar in
    /// dark mode - the biggest remaining "dark theme still off" offender.
    /// No-op silently on systems without these DWM attributes.</summary>
    public static void ApplyWindowChrome(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int dark = IsDark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int corner = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }
        catch (EntryPointNotFoundException) { /* pre-20H1 Windows: skip */ }
    }

    /// <summary>Hooks a window so its native frame follows theme switches.</summary>
    public static void TrackWindowChrome(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyWindowChrome(window);
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            ApplyWindowChrome(window);
        void OnThemeChanged() => ApplyWindowChrome(window);
        Changed += OnThemeChanged;
        window.Closed += (_, _) => Changed -= OnThemeChanged;
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
