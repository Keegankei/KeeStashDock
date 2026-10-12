using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using System.Windows.Threading;
using KeeStashDock.Core.Services;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.ViewModels;
using KeeStashDock.Infrastructure.Storage;

namespace KeeStashDock.Desktop.Views;

/// <summary>
/// Code-behind for the three-state quick panel.
///
/// State machine
/// -------------
///   Hidden   --(hotkey / tray)-->                last visible state
///   Expanded --(unlocked, mouse away AND no keyboard focus for
///               CollapseDelaySeconds)-->        Shrunk
///   Shrunk   --(unlocked, hover or focus for
///               ExpandDelaySeconds)-->          Expanded
///   any      --(minimize button / Esc / hotkey toggle)--> Hidden
///
/// Lock pins the CURRENT state: no auto transitions in either direction
/// (manual buttons still work). A 200 ms dispatcher timer drives the clock;
/// engagement = IsMouseOver || IsKeyboardFocusWithin, so typing with the
/// mouse elsewhere never collapses the panel (spec item 3), and the input
/// box is NOT focused automatically on summon.
///
/// The window never activates itself (ShowActivated=false) so summoning it
/// does not steal keyboard focus from the app the user is working in.
/// Both visible states are draggable by the top bar; the position persists.
/// </summary>
public partial class QuickPanelWindow : Window
{
    private enum PanelState { Expanded, Shrunk }

    private readonly QuickPanelViewModel _vm;
    private readonly SettingsStore _settings;
    private readonly DraftGuard _draft = new();
    private readonly Action _onExpandRequested;
    private readonly Action _onSettingsRequested;
    private readonly Action? _onCaptureRequested;
    private readonly Action<long>? _onImageRequested;
    private readonly DispatcherTimer _clock;

    private PanelState _state = PanelState.Expanded;
    private int _visibilityTransition;
    private DateTime? _idleSince;    // expanded: engagement lost at this instant
    private DateTime? _hoverSince;   // shrunk: engagement gained at this instant
    private bool _expandArmed;       // shrunk must lose engagement once before
                                     // hover can re-expand (prevents instant
                                     // bounce-back after the shrink button)
    private System.Windows.Point _dragStart;

    private const double ShrunkHeight = 44;   // top bar only
    private const double MinExpandedWidth = 280;
    private const double MinExpandedHeight = 340;
    /// <summary>Shrunk bar expands when the cursor comes within this distance
    /// of the window (DIPs), not only when it is directly over it.</summary>
    private const double ProximityDip = 48;
    private const int ResizeBorderPx = 6;      // edge-drag hit band, device px
    private const int CornerBandPx = 14;       // corner grab zone (bigger target)

    public QuickPanelWindow(SnippetService snippets, SearchService search,
                            SettingsStore settings,
                            Infrastructure.Repositories.CategoryRepository categories,
                            Action onExpandRequested, Action onSettingsRequested,
                            Action? onCaptureRequested = null, Action<long>? onImageRequested = null)
    {
        InitializeComponent();
        _settings = settings;
        _onExpandRequested = onExpandRequested;
        _onSettingsRequested = onSettingsRequested;
        _onCaptureRequested = onCaptureRequested;
        _onImageRequested = onImageRequested;
        _vm = new QuickPanelViewModel(snippets, search, categories);
        _vm.DraftChanged += _draft.OnTextChanged;
        DataContext = _vm;

        // Drag-in: dropping text or files onto the panel stashes them instantly.
        AllowDrop = true;
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.UnicodeText) is string text &&
                !string.IsNullOrWhiteSpace(text))
                snippets.Add(text, Domain.Entities.SnippetSource.DragDrop);
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                foreach (string f in files)
                    snippets.Add(f, Domain.Entities.SnippetSource.DragDrop,
                                 explicitType: Domain.Entities.SnippetType.FilePath);
        };

        // Auto shrink/expand clock. 200 ms granularity is imperceptible
        // against the 1 s / 5 s default thresholds.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _clock.Tick += (_, _) => TickStateMachine();

        LocalizationService.Instance.LanguageChanged +=
            () => LocalizationService.Instance.ApplyFlowDirection(this);
        LocalizationService.Instance.ApplyFlowDirection(this);

        // Borderless windows get no system resize frame; WM_NCHITTEST turns
        // a 6px edge band into live resize handles (item: drag-to-resize).
        SourceInitialized += (_, _) =>
        {
            var source = (HwndSource)PresentationSource.FromVisual(this)!;
            DirectPaste.RegisterPanel(source.Handle);
            source.AddHook(WndProc);
        };

        // Per-region tint engine (see ApplyLocalTints): re-sample on layout
        // or content movement, debounced so scrolling costs one pass, not N.
        _tintTimer.Tick += (_, _) =>
        {
            _tintTimer.Stop();
            if (_tintRebuild) { RebuildBgSampler(); _tintRebuild = false; }
            ApplyLocalTints();
        };
        SizeChanged += (_, _) => ScheduleLocalTints(rebuild: true);
        CardList.AddHandler(ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => ScheduleLocalTints()));
        ((System.Collections.Specialized.INotifyCollectionChanged)CardList.Items)
            .CollectionChanged += (_, _) => ScheduleLocalTints();

        // Scrim color derives from the active palette - refresh on theme swap.
        ThemeManager.Changed += () => { _visualSig = null; ApplyBackground(); };
        // Hot-apply: picking an image / moving the opacity or frost
        // sliders takes effect immediately, no re-summon needed (the
        // signature guard below makes unrelated saves - e.g. geometry - free).
        _settings.Changed += () => Dispatcher.Invoke(ApplyBackground);
        Closed += (_, _) =>
        {
            _visibilityTransition++;
            _clock.Stop();
            _tintTimer.Stop();
        };
    }

    /// <summary>Signature of the last applied visual settings; skips the
    /// (image-decoding) reapply when a Save() didn't touch anything visual.</summary>
    private string? _visualSig;

    // ------------------------------------------------- custom background

    /// <summary>Applies the user's background image (Settings > appearance):
    /// the image fills the rounded panel (UniformToFill crop) and a
    /// theme-colored scrim with user-chosen opacity sits between image and
    /// content so text stays readable. Falls back to the plain themed
    /// background when no image is set or the file is gone. Decode width is
    /// capped so a 20-megapixel wallpaper cannot balloon memory.</summary>
    private void ApplyBackground()
    {
        var cfg = _settings.Current;
        string sig = $"{cfg.BackgroundImagePath}|{cfg.BackgroundOverlayOpacity:0.##}|" +
                     $"{cfg.FrostedGlassEnabled}|{cfg.FrostedGlassRadius:0.#}|" +
                     $"{cfg.CardOpacity:0.##}|" +
                     $"{cfg.PanelTextColorMode}|{cfg.PanelTextCustomColor}";
        if (sig == _visualSig) return;   // unrelated save (e.g. geometry)
        _visualSig = sig;

        // ---- background image + frost --------------------------------
        string? path = cfg.BackgroundImagePath;
        bool haveImage = false;
        Color? imageAvg = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // release the file
                bmp.DecodePixelWidth = 1600;                  // memory cap
                bmp.EndInit();
                bmp.Freeze();
                BgImage.Background = new ImageBrush(bmp)
                { Stretch = Stretch.UniformToFill };
                BgImage.Effect = cfg.FrostedGlassEnabled
                    ? new BlurEffect
                      { Radius = Math.Clamp(cfg.FrostedGlassRadius, 1, 18) }
                    : null;
                imageAvg = AverageColor(bmp);
                haveImage = true;
            }
            catch { /* unreadable image -> plain background below */ }
        }
        if (haveImage)
        {
            var baseColor = ((SolidColorBrush)FindResource("B.PanelBg")).Color;
            byte a = (byte)(Math.Clamp(cfg.BackgroundOverlayOpacity, 0.0, 1.0) * 255);
            Scrim.Background = new SolidColorBrush(
                Color.FromArgb(a, baseColor.R, baseColor.G, baseColor.B));
        }
        else
        {
            BgImage.Background = Brushes.Transparent;
            BgImage.Effect = null;
            Scrim.Background = Brushes.Transparent;
        }

        // ---- card translucency ----------------------------------------
        // A WINDOW-scoped B.CardBg override wins the DynamicResource lookup
        // inside this panel only, so cards float over the wallpaper without
        // touching the workbench's cards.
        var cardColor = ((SolidColorBrush)Application.Current.FindResource("B.CardBg")).Color;
        byte ca = (byte)(Math.Clamp(cfg.CardOpacity, 0.1, 1.0) * 255);
        Resources["B.CardBg"] = new SolidColorBrush(
            Color.FromArgb(ca, cardColor.R, cardColor.G, cardColor.B));
        // The SAME slider also governs the quick input box and the two
        // combos (expiry / group): they all sit on B.InputBg, so one
        // window-scoped override keeps the whole panel visually coherent.
        var inputColor = ((SolidColorBrush)Application.Current
            .FindResource("B.InputBg")).Color;
        Resources["B.InputBg"] = new SolidColorBrush(
            Color.FromArgb(ca, inputColor.R, inputColor.G, inputColor.B));

        // ---- auto-contrast foreground over the wallpaper ---------------
        // Whole-average resources first (baseline for anything the local
        // pass doesn't reach: combo internals, lock glyph, toasts...).
        ApplyForegroundScheme(cfg, haveImage ? imageAvg : null);
        // ...then the per-region pass refines individual text elements.
        _hasBgImage = haveImage;
        ScheduleLocalTints(rebuild: true);

    }

    // ------------------------------------------- auto-contrast foreground

    /// <summary>Mean COLOR of a bitmap (full RGB, not just luminance),
    /// sampled on a 32px thumbnail so a wallpaper-sized image costs ~nothing.</summary>
    private static Color AverageColor(BitmapSource src)
    {
        double scale = 32.0 / Math.Max(1, Math.Max(src.PixelWidth, src.PixelHeight));
        var small = new TransformedBitmap(src, new ScaleTransform(scale, scale));
        var conv = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight;
        var px = new byte[w * h * 4];
        conv.CopyPixels(px, w * 4, 0);
        double r = 0, g = 0, b = 0;
        for (int i = 0; i < px.Length; i += 4)
        { b += px[i]; g += px[i + 1]; r += px[i + 2]; }
        int n = w * h;
        return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>Alpha-composites TOP over UNDER (channel-wise, no gamma).</summary>
    private static Color Over(Color top, Color under, double alpha) => Color.FromRgb(
        (byte)(top.R * alpha + under.R * (1 - alpha)),
        (byte)(top.G * alpha + under.G * (1 - alpha)),
        (byte)(top.B * alpha + under.B * (1 - alpha)));

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    private static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 0) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = ((int)(h / 60) % 6) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255),
                             (byte)((b + m) * 255));
    }

    /// <summary>Full-RGB auto foreground: instead of snapping to one of two
    /// fixed gray sets, the text color is DERIVED from the surface it sits
    /// on - it keeps the surface's hue (capped saturation, so it still reads
    /// as text) and pushes value to the far end for contrast. A lavender
    /// wallpaper thus gets warm off-lavender text rather than plain white.
    /// Muted = primary pulled ~1/3 back toward the surface.</summary>
    private static (Color P, Color M) DeriveForeground(Color surface)
    {
        RgbToHsv(surface, out double h, out double s, out _);
        bool lightText = Luminance(surface) < 0.5;
        double ts = Math.Min(s, 0.60) * (lightText ? 0.38 : 0.55);
        double tv = lightText ? 0.97 : 0.13;
        Color primary = HsvToRgb(h, ts, tv);
        return (primary, Blend(primary, surface, 0.32));
    }

    private static double Luminance(Color c)
        => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    /// <summary>Auto-contrast recolors the panel's text and
    /// icon buttons so they stay readable over the user's wallpaper.
    ///
    /// Two independent decisions, because two different surfaces sit under
    /// text:
    ///
    ///   A named preset or Custom palette (see <see cref="TextPalettes"/>)
    ///   short-circuits everything: one palette on all three surfaces.
    ///
    ///   In "Auto": CARD text follows the card's own effective background
    ///   (theme card color at the opacity setting over what shows through);
    ///   INPUT-surface text does the same with the theme input color; and
    ///   CHROME text (title, labels, hints, icon buttons) follows the
    ///   image-blended-with-scrim luminance. No image = theme defaults.</summary>
    // --------------------------------------- per-region local tinting
    //
    // The whole-image average (above) picks ONE foreground per surface,
    // which fails on split wallpapers (bright sky over dark ground). This
    // engine samples the background LOCALLY BEHIND EACH TEXT ELEMENT:
    //   1. BgImage + Scrim (frost blur included) are baked into a low-res
    //      RenderTargetBitmap (~15% scale) - one cheap bake per layout/bg
    //      change, cached as raw pixels;
    //   2. each target element maps its on-screen rect into that bitmap,
    //      averages its own patch, composites the card/input layer color
    //      at the shared opacity, and derives its own foreground (same
    //      full-RGB DeriveForeground as the global pass);
    //   3. scroll / resize / list changes re-run the pass through a 120 ms
    //      debounce, so a fling costs one pass, not one per pixel.
    // Targets: title, top-bar icon buttons, quick-input text+placeholder,
    // expiry label, empty hint, and EVERY VISIBLE CARD individually (its
    // TextBlocks are marked Tag="TintP"/"TintM" in the template). Presets/
    // Custom palettes and image-less Auto disable the pass and restore the
    // resource-driven colors.

    private readonly DispatcherTimer _tintTimer =
        new() { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _tintRebuild;
    private bool _hasBgImage;
    private bool _localTintActive;
    private byte[]? _bgPixels;
    private int _bgW, _bgH;

    private void ScheduleLocalTints(bool rebuild = false)
    {
        _tintRebuild |= rebuild;
        _tintTimer.Stop();
        _tintTimer.Start();
    }

    /// <summary>Bakes BgImage+Scrim (blur and all) into cached low-res
    /// pixels. Rendering the actual visuals means the sampler sees exactly
    /// what the eye sees under the text - crop mode, frost, scrim opacity.</summary>
    private void RebuildBgSampler()
    {
        _bgPixels = null;
        if (!_hasBgImage) return;
        double w = BgImage.ActualWidth, h = BgImage.ActualHeight;
        if (w < 8 || h < 8) return;
        try
        {
            const double k = 0.15;
            _bgW = Math.Max(2, (int)Math.Ceiling(w * k));
            _bgH = Math.Max(2, (int)Math.Ceiling(h * k));
            var rtb = new RenderTargetBitmap(_bgW, _bgH, 96 * k, 96 * k,
                                             PixelFormats.Pbgra32);
            rtb.Render(BgImage);
            rtb.Render(Scrim);
            _bgPixels = new byte[_bgW * _bgH * 4];
            rtb.CopyPixels(_bgPixels, _bgW * 4, 0);
        }
        catch { _bgPixels = null; }   // sampling is polish, never a crash
    }

    /// <summary>Average color of the baked background directly behind an
    /// element. Null when the element is collapsed, detached, or lands on
    /// too few opaque pixels (rounded-corner edges).</summary>
    private Color? SampleBehind(FrameworkElement el)
    {
        if (_bgPixels is null || !el.IsVisible) return null;
        Rect r;
        try
        {
            r = el.TransformToVisual(BgImage)
                  .TransformBounds(new Rect(el.RenderSize));
        }
        catch (InvalidOperationException) { return null; }

        double kx = _bgW / Math.Max(1.0, BgImage.ActualWidth);
        double ky = _bgH / Math.Max(1.0, BgImage.ActualHeight);
        int x0 = Math.Clamp((int)(r.X * kx), 0, _bgW - 1);
        int x1 = Math.Clamp((int)Math.Ceiling(r.Right * kx), x0 + 1, _bgW);
        int y0 = Math.Clamp((int)(r.Y * ky), 0, _bgH - 1);
        int y1 = Math.Clamp((int)Math.Ceiling(r.Bottom * ky), y0 + 1, _bgH);

        double sr = 0, sg = 0, sb = 0; int n = 0;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = (y * _bgW + x) * 4;
                if (_bgPixels[i + 3] < 40) continue;   // corner transparency
                sb += _bgPixels[i]; sg += _bgPixels[i + 1]; sr += _bgPixels[i + 2];
                n++;
            }
        if (n < 3) return null;
        return Color.FromRgb((byte)(sr / n), (byte)(sg / n), (byte)(sb / n));
    }

    private void ApplyLocalTints()
    {
        var cfg = _settings.Current;
        bool eligible = _hasBgImage && _bgPixels is not null &&
                        !TextPalettes.TryResolve(cfg, out _, out _);
        if (!eligible)
        {
            if (_localTintActive) RestoreLocalTints();
            return;
        }
        _localTintActive = true;
        double co = Math.Clamp(cfg.CardOpacity, 0.1, 1.0);
        var inputBg = ((SolidColorBrush)Application.Current
            .FindResource("B.InputBg")).Color;
        var cardBg = ((SolidColorBrush)Application.Current
            .FindResource("B.CardBg")).Color;

        // ---- fixed chrome, each from its own patch ----------------------
        if (SampleBehind(TitleText) is Color tBg)
            TitleText.Foreground = new SolidColorBrush(DeriveForeground(tBg).P);
        if (SampleBehind(TopBarButtons) is Color bBg)
        {
            var brush = new SolidColorBrush(DeriveForeground(bBg).M);
            foreach (var child in TopBarButtons.Children)
                if (child is Button btn) btn.Foreground = brush;
        }
        if (SampleBehind(ExpireLabel) is Color eBg)
            ExpireLabel.Foreground = new SolidColorBrush(DeriveForeground(eBg).M);
        if (EmptyHint.IsVisible && SampleBehind(EmptyHint) is Color hBg)
            EmptyHint.Foreground = new SolidColorBrush(DeriveForeground(hBg).M);

        // ---- input surface: composite the input color at shared opacity --
        if (SampleBehind(InputRow) is Color iBg)
        {
            var pair = DeriveForeground(Over(inputBg, iBg, co));
            QuickBox.Foreground = new SolidColorBrush(pair.P);
            PlaceholderText.Foreground = new SolidColorBrush(pair.M);
        }

        // ---- every visible card, individually ---------------------------
        for (int i = 0; i < CardList.Items.Count; i++)
        {
            if (CardList.ItemContainerGenerator.ContainerFromIndex(i)
                is not ListBoxItem item || !item.IsVisible) continue;
            if (SampleBehind(item) is not Color cBg) continue;
            var pair = DeriveForeground(Over(cardBg, cBg, co));
            var p = new SolidColorBrush(pair.P);
            var m = new SolidColorBrush(pair.M);
            foreach (var tb in FindTinted(item))
                tb.Foreground = (string?)tb.Tag == "TintP" ? p : m;
        }
    }

    /// <summary>Puts every locally tinted element back on its resource-
    /// driven color (preset picked, image removed, or Auto lost the image).</summary>
    private void RestoreLocalTints()
    {
        _localTintActive = false;
        TitleText.SetResourceReference(TextBlock.ForegroundProperty, "B.TextPrimary");
        foreach (var child in TopBarButtons.Children)
            if (child is Button btn) btn.ClearValue(ForegroundProperty);
        ExpireLabel.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
        EmptyHint.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
        QuickBox.ClearValue(ForegroundProperty);
        PlaceholderText.SetResourceReference(TextBlock.ForegroundProperty,
                                             "B.InputTextMuted");
        for (int i = 0; i < CardList.Items.Count; i++)
        {
            if (CardList.ItemContainerGenerator.ContainerFromIndex(i)
                is not ListBoxItem item) continue;
            foreach (var tb in FindTinted(item))
                tb.SetResourceReference(TextBlock.ForegroundProperty,
                    (string?)tb.Tag == "TintP" ? "B.CardTextPrimary"
                                               : "B.CardTextMuted");
        }
    }

    private static System.Collections.Generic.IEnumerable<TextBlock>
        FindTinted(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb &&
                ((string?)tb.Tag == "TintP" || (string?)tb.Tag == "TintM"))
                yield return tb;
            foreach (var sub in FindTinted(child))
                yield return sub;
        }
    }

    private void ApplyForegroundScheme(Domain.Entities.AppSettings cfg, Color? imageAvg)
    {
        // ---- preset / custom palette: one palette everywhere ------------
        if (TextPalettes.TryResolve(cfg, out Color pp, out Color pm))
        {
            SetForegroundPair("B.TextPrimary", "B.TextMuted", pp, pm);
            SetForegroundPair("B.CardTextPrimary", "B.CardTextMuted", pp, pm);
            SetForegroundPair("B.InputText", "B.InputTextMuted", pp, pm);
            return;
        }

        // ---- Auto: full-RGB derivation per surface -----------------------
        if (imageAvg is Color img)
        {
            var panel = ((SolidColorBrush)Application.Current
                .FindResource("B.PanelBg")).Color;
            double a = Math.Clamp(cfg.BackgroundOverlayOpacity, 0.0, 1.0);
            Color effPanel = Over(panel, img, a);

            double co = Math.Clamp(cfg.CardOpacity, 0.1, 1.0);
            Color effCard = Over(((SolidColorBrush)Application.Current
                .FindResource("B.CardBg")).Color, effPanel, co);
            Color effInput = Over(((SolidColorBrush)Application.Current
                .FindResource("B.InputBg")).Color, effPanel, co);

            var chrome = DeriveForeground(effPanel);
            var card = DeriveForeground(effCard);
            var input = DeriveForeground(effInput);
            SetForegroundPair("B.TextPrimary", "B.TextMuted", chrome.P, chrome.M);
            SetForegroundPair("B.CardTextPrimary", "B.CardTextMuted", card.P, card.M);
            SetForegroundPair("B.InputText", "B.InputTextMuted", input.P, input.M);
        }
        else
        {
            // No image: the theme's own foregrounds already match.
            foreach (string key in new[] { "B.TextPrimary", "B.TextMuted",
                                           "B.CardTextPrimary", "B.CardTextMuted",
                                           "B.InputText", "B.InputTextMuted" })
                Resources.Remove(key);
        }
    }

    private void SetForegroundPair(string primaryKey, string mutedKey,
                                   Color primary, Color muted)
    {
        Resources[primaryKey] = new SolidColorBrush(primary);
        Resources[mutedKey] = new SolidColorBrush(muted);
    }

    // ----------------------------------------- native resize / persistence

    private const int WM_NCHITTEST = 0x0084, WM_EXITSIZEMOVE = 0x0232,
                      WM_MOUSEACTIVATE = 0x0021;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13,
                      HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16,
                      HTBOTTOMRIGHT = 17;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam,
                           ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                // The click that is ABOUT to activate the panel: the window
                // losing the foreground right now is the direct-paste target.
                DirectPaste.RememberForeground();
                break;

            case WM_NCHITTEST:
            {
                int hit = HitTestResizeEdge(lParam);
                if (hit != 0) { handled = true; return (IntPtr)hit; }
                break;
            }
            case WM_EXITSIZEMOVE:
                // Fires once after every completed drag OR resize loop:
                // one place persists both position and user-chosen size.
                PersistGeometry();
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Maps a screen point (WM_NCHITTEST lParam, device px) onto the
    /// window's 6px edge bands. The shrunk bar has a fixed height, so it only
    /// exposes left/right handles; the expanded panel exposes all eight.</summary>
    private int HitTestResizeEdge(IntPtr lParam)
    {
        int x = unchecked((short)((long)lParam & 0xFFFF));
        int y = unchecked((short)(((long)lParam >> 16) & 0xFFFF));

        var t = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (t is null) return 0;
        var m = t.TransformToDevice;
        double l = Left * m.M11, tp = Top * m.M22;
        double r = (Left + ActualWidth) * m.M11;
        double b = (Top + ActualHeight) * m.M22;

        bool left   = x >= l && x < l + ResizeBorderPx;
        bool right  = x <= r && x > r - ResizeBorderPx;
        bool top    = y >= tp && y < tp + ResizeBorderPx;
        bool bottom = y <= b && y > b - ResizeBorderPx;

        // Corner zones first, with a WIDER band (14px vs 6px edges): the
        // rounded corner leaves little visible surface, so the grab target
        // must be generous to be usable.
        bool cLeft   = x >= l && x < l + CornerBandPx;
        bool cRight  = x <= r && x > r - CornerBandPx;
        bool cTop    = y >= tp && y < tp + CornerBandPx;
        bool cBottom = y <= b && y > b - CornerBandPx;

        if (_state == PanelState.Shrunk)
            return left ? HTLEFT : right ? HTRIGHT : 0;   // height is fixed

        if (cTop && cLeft) return HTTOPLEFT;
        if (cTop && cRight) return HTTOPRIGHT;
        if (cBottom && cLeft) return HTBOTTOMLEFT;
        if (cBottom && cRight) return HTBOTTOMRIGHT;
        if (left) return HTLEFT;
        if (right) return HTRIGHT;
        if (top) return HTTOP;
        if (bottom) return HTBOTTOM;
        return 0;
    }

    private void PersistGeometry()
    {
        var cfg = _settings.Current;
        cfg.PanelPosX = Left;
        cfg.PanelPosY = Top;
        cfg.PanelUserWidth = ActualWidth;                 // both states share width
        if (_state == PanelState.Expanded)
            cfg.PanelUserHeight = ActualHeight;
        _settings.Save();
    }

    /// <summary>The group new captures land in (null = uncategorized);
    /// mirrors the panel's group combo for the selection-capture flow.</summary>
    public long? CurrentCategoryId => _vm.SelectedCategoryId;
    public Domain.Entities.ExpirePolicy CurrentExpirePolicy => (Domain.Entities.ExpirePolicy)Math.Clamp(_vm.ExpireIndex, 0, 3);
    private void OnCaptureClick(object sender, RoutedEventArgs e) => _onCaptureRequested?.Invoke();

    // ------------------------------------------------------------ geometry

    private double ExpandedWidth  =>
        _settings.Current.PanelUserWidth
        ?? Math.Max(MinExpandedWidth, SystemParameters.WorkArea.Width * 0.15);
    private double ExpandedHeight =>
        _settings.Current.PanelUserHeight
        ?? Math.Max(MinExpandedHeight, SystemParameters.WorkArea.Height * 0.50);

    /// <summary>The compact window is exactly the height of its visible bar.</summary>
    private double ShrunkWindowHeight => ShrunkHeight;

    /// <summary>Applies the persisted (or default) top-left position, clamped
    /// into the current work area so a monitor change can never lose the window.</summary>
    private void ApplyPosition()
    {
        var wa = SystemParameters.WorkArea;
        double w = ExpandedWidth;   // both states share the same width
        double h = _state == PanelState.Expanded ? ExpandedHeight : ShrunkWindowHeight;

        double x = _settings.Current.PanelPosX
                   ?? (_settings.Current.DockEdge == "Left"
                       ? wa.Left + 12 : wa.Right - w - 12);
        double y = _settings.Current.PanelPosY
                   ?? wa.Top + (wa.Height - ExpandedHeight) / 2;

        Left = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - w));
        Top  = Math.Clamp(y, wa.Top,  Math.Max(wa.Top,  wa.Bottom - h));
    }

    private void ApplyStateVisuals()
    {
        bool expanded = _state == PanelState.Expanded;
        InputRow.Visibility  = expanded ? Visibility.Visible : Visibility.Collapsed;
        ExpireRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ListArea.Visibility  = expanded ? Visibility.Visible : Visibility.Collapsed;

        MinWidth = MinExpandedWidth;
        Width    = ExpandedWidth;
        if (expanded)
        {
            MinHeight = 300;
            MaxHeight = double.PositiveInfinity;
            Height = ExpandedHeight;
        }
        else
        {
            MinHeight = ShrunkWindowHeight;
            Height = ShrunkWindowHeight;
            MaxHeight = ShrunkWindowHeight;
        }

        // ChevronDown = "shrink", ChevronUp = "expand"
        ShrinkExpandBtn.Content = expanded ? "\uE70D" : "\uE70E";
        ShrinkExpandBtn.ToolTip = LocalizationService.Instance[
            expanded ? "QuickPanel.Btn.Shrink" : "QuickPanel.Btn.ExpandPanel"];
    }

    // -------------------------------------------------------- show / hide

    /// <summary>Shows the panel (fade-in) in its last visible state at the
    /// persisted position. Does NOT focus the input box and does NOT steal
    /// activation (spec item 3).</summary>
    public void ShowPanel()
    {
        _vm.ReloadCategories();   // groups may have changed in the workbench
        _vm.Reload();
        ApplyBackground();   // picks up settings changes made while hidden
        ApplyStateVisuals();
        ApplyPosition();
        int transition = ++_visibilityTransition;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) =>
        {
            if (transition != _visibilityTransition) return;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        };
        BeginAnimation(OpacityProperty, fade);

        ResetTimers();
        _clock.Start();
    }

    /// <summary>Fades out into the hidden (tray-only) state.</summary>
    public void HidePanel()
    {
        _clock.Stop();
        int transition = ++_visibilityTransition;
        double opacity = Opacity;
        BeginAnimation(OpacityProperty, null);
        Opacity = opacity;
        var fade = new DoubleAnimation(opacity, 0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) =>
        {
            // A completion queued by a replaced fade must not hide a newer summon.
            if (transition != _visibilityTransition) return;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            Hide();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    // ------------------------------------------------------- state machine

    private void TickStateMachine()
    {
        if (!IsVisible) return;
        // Engagement includes PROXIMITY (cursor within ~48 DIP of the window)
        // in BOTH states: it makes the thin shrunk bar forgiving to aim at,
        // and it must symmetrically keep the expanded panel open - otherwise
        // "idle just outside the panel" would collapse it and the proximity
        // zone would immediately re-expand it, oscillating forever.
        bool engaged = IsMouseOver || IsKeyboardFocusWithin ||
                       CursorNearWindow(ProximityDip);
        var now = DateTime.UtcNow;

        if (_vm.IsLocked) { ResetTimers(); return; }   // lock pins the state

        if (_state == PanelState.Expanded)
        {
            if (engaged) { _idleSince = null; return; }
            _idleSince ??= now;
            if ((now - _idleSince.Value).TotalSeconds >=
                Math.Max(0.5, _settings.Current.CollapseDelaySeconds))
                SetState(PanelState.Shrunk);
        }
        else // Shrunk
        {
            if (!engaged) { _hoverSince = null; _expandArmed = true; return; }
            if (!_expandArmed) return;   // wait until the mouse left once
            _hoverSince ??= now;
            if ((now - _hoverSince.Value).TotalSeconds >=
                Math.Max(0.1, _settings.Current.ExpandDelaySeconds))
                SetState(PanelState.Expanded);
        }
    }

    private void SetState(PanelState s)
    {
        if (_state == s) return;
        _state = s;
        // Manual shrink leaves the cursor on the window; require it to leave
        // once before hover can auto-expand again.
        _expandArmed = s != PanelState.Shrunk || !IsMouseOver;
        ResetTimers();
        ApplyStateVisuals();
        // Keep the panel inside the work area after the size change.
        var wa = SystemParameters.WorkArea;
        Top = Math.Clamp(Top, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
    }

    private void ResetTimers() { _idleSince = null; _hoverSince = null; }

    /// <summary>True when the physical cursor lies within <paramref name="marginDip"/>
    /// of the window bounds (converted to device pixels for the comparison).</summary>
    private bool CursorNearWindow(double marginDip)
    {
        if (!Interop.NativeMethods.GetCursorPos(out var pt)) return false;
        var t = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (t is null) return false;
        var m = t.TransformToDevice;
        double marginX = marginDip * m.M11, marginY = marginDip * m.M22;
        double l = Left * m.M11 - marginX;
        double tp = Top * m.M22 - marginY;
        double r = (Left + ActualWidth) * m.M11 + marginX;
        double b = (Top + ActualHeight) * m.M22 + marginY;
        return pt.X >= l && pt.X <= r && pt.Y >= tp && pt.Y <= b;
    }

    // ------------------------------------------------------------- top bar

    private void OnTopBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        try { DragMove(); }   // geometry is persisted by WM_EXITSIZEMOVE
        catch (InvalidOperationException) { }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => HidePanel();

    private void OnShrinkExpandClick(object sender, RoutedEventArgs e)
        => SetState(_state == PanelState.Expanded ? PanelState.Shrunk
                                                  : PanelState.Expanded);

    private void OnExpandClick(object sender, RoutedEventArgs e)
        => _onExpandRequested();   // panel stays; unlocked it will auto-shrink

    private void OnSettingsClick(object sender, RoutedEventArgs e)
        => _onSettingsRequested();

    // ----------------------------------------------------------- keyboard

    /// <summary>Quick input box. Enter stashes; Alt+Enter and Shift+Enter
    /// insert a newline. Down on the last line jumps into the card list;
    /// Esc hides the panel. IME-processed keys remain with the IME.</summary>
    private void OnQuickBoxKeyDown(object sender, KeyEventArgs e)
    {
        QuickInputEnterAction enter = QuickInputKeyboard.HandleEnter(
            QuickBox, e, Keyboard.Modifiers);
        if (enter == QuickInputEnterAction.Stash)
        {
            _vm.QuickAddCommand.Execute(null);
            _draft.Clear();
        }
        if (enter != QuickInputEnterAction.None || e.Key == Key.ImeProcessed)
            return;

        switch (e.Key)
        {
            case Key.Down when CardList.Items.Count > 0 && CaretOnLastLine():
                CardList.SelectedIndex = 0;
                ((ListBoxItem)CardList.ItemContainerGenerator
                    .ContainerFromIndex(0))?.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                HidePanel();
                e.Handled = true;
                break;
        }
    }

    /// <summary>With a multi-line box, Down must keep navigating text until
    /// the caret reaches the last visual line - only then jump to the list.</summary>
    private bool CaretOnLastLine()
    {
        int line = QuickBox.GetLineIndexFromCharacterIndex(QuickBox.CaretIndex);
        return line < 0 || line >= QuickBox.LineCount - 1;
    }

    /// <summary>Card list: Enter copies the selection, Esc hides.</summary>
    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when CardList.SelectedItem is SnippetItemViewModel item:
                _vm.CopyCommand.Execute(item);
                e.Handled = true;
                break;
            case Key.Escape:
                HidePanel();
                e.Handled = true;
                break;
        }
    }

    // -------------------------------------------------------------- mouse

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: SnippetItemViewModel item }) return;

        // File-path cards open their target on click (copy stays available
        // via Enter in the keyboard flow); other cards copy as before.
        if (item.IsImage) _onImageRequested?.Invoke(item.Id);
        else if (item.Model.Type == Domain.Entities.SnippetType.FilePath)
            TryOpenPath(item.Model.Content);
        else
            _vm.CopyCommand.Execute(item);
    }

    // ---------------------------------------------- drag = tear out note

    private const double TearThresholdDip = 10;   // > click jitter, < a real drag

    private SnippetItemViewModel? _pressItem;
    private ListBoxItem? _pressContainer;
    private bool _tearFired;

    /// <summary>Drag-to-tear: press a card
    /// and PULL - once the pointer travels past a small threshold the card
    /// "tears out of its slot": a sticky-note twin pops in exactly over the
    /// card (scale+fade entrance) and immediately takes over the drag, so
    /// it rides the cursor until the button is released wherever the user
    /// wants it. A plain click (below the threshold) still copies, and
    /// presses starting on the pin/delete buttons never tear.</summary>
    private void OnCardPressDown(object sender, MouseButtonEventArgs e)
    {
        _tearFired = false;
        if (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(
                e.OriginalSource as DependencyObject) is not null) return;
        var container = ItemsControl.ContainerFromElement(
            CardList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container?.DataContext is not SnippetItemViewModel item) return;

        if (item.IsImage) return;
        _pressItem = item;
        _pressContainer = container;
        _dragStart = e.GetPosition(this);
    }

    private void OnCardPressUp(object sender, MouseButtonEventArgs e)
    {
        _pressItem = null;
        _pressContainer = null;
        if (_tearFired)
        {
            // The tear already happened this press; the release must not
            // ALSO copy the card (swallow the paired bubbling ButtonUp).
            _tearFired = false;
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T hit) return hit;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>Card right-click: QR transfer / copy (tear-out lives on the
    /// drag gesture). Menu is code-built (Style-embedded ContextMenus with
    /// handlers corrupt BAML connection ids - same caveat as the workbench
    /// sidebar).</summary>
    private void OnCardRightClick(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(
            CardList, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container?.DataContext is not SnippetItemViewModel item) return;

        var loc = LocalizationService.Instance;
        var menu = new ContextMenu();

        if (item.IsImage)
        {
            var openImage = new MenuItem { Header = loc["Screenshot.Open"] };
            openImage.Click += (_, _) => _onImageRequested?.Invoke(item.Id);
            var copyImage = new MenuItem { Header = loc["Screenshot.CopyImage"] };
            copyImage.Click += (_, _) => _vm.CopyCommand.Execute(item);
            menu.Items.Add(openImage);
            menu.Items.Add(copyImage);
            menu.PlacementTarget = container;
            menu.IsOpen = true;
            e.Handled = true;
            return;
        }

        var qr = new MenuItem { Header = loc["QuickPanel.Menu.Qr"] };
        qr.Click += (_, _) =>
        {
            if (item.Model.Content.Length > QrWindow.MaxChars)
            {
                _vm.ShowToast("Qr.TooLong");
                return;
            }
            new QrWindow(item.Model.Content) { Owner = null, Topmost = true }.Show();
        };

        var copy = new MenuItem { Header = loc["QuickPanel.Menu.Copy"] };
        copy.Click += (_, _) => _vm.CopyCommand.Execute(item);

        menu.Items.Add(qr);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.PlacementTarget = container;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Shell-opens a stored file or folder path; missing targets get
    /// a toast instead of an exception.</summary>
    private void TryOpenPath(string raw)
    {
        string path = raw.Trim().Trim('"');
        if (File.Exists(path) || Directory.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                _vm.ShowToast("QuickPanel.Opened");
                return;
            }
            catch { /* fall through to failure toast */ }
        }
        _vm.ShowToast("QuickPanel.OpenFailed");
    }

    /// <summary>Drag-out: dragging a card starts an OLE text drag so the user
    /// can drop snippet content straight into any target application.</summary>
    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragStart = e.GetPosition(this);
            return;
        }
        if (_tearFired || _pressItem is null || _pressContainer is null) return;

        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < TearThresholdDip &&
            Math.Abs(pos.Y - _dragStart.Y) < TearThresholdDip)
            return;

        // Threshold crossed: tear the card out FROM ITS OWN SLOT. The note
        // spawns aligned with the card's on-screen rect (so it visually
        // detaches rather than teleporting) and takes over the drag.
        _tearFired = true;
        Point deviceTL = _pressContainer.PointToScreen(new Point(0, 0));
        Point dipTL = deviceTL;
        var ct = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (ct is not null) dipTL = ct.TransformFromDevice.Transform(deviceTL);

        var note = new StickyNoteWindow(_pressItem, _settings.Current.CardOpacity);
        _pressItem = null;
        _pressContainer = null;
        Mouse.Capture(null);   // the ListBoxItem must let go before DragMove
        note.ShowTornOutAt(dipTL.X, dipTL.Y);
    }
}
