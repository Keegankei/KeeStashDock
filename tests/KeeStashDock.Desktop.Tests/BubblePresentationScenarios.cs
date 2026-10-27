using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KeeStashDock.Desktop.Localization;
using KeeStashDock.Desktop.Views;
using KeeStashDock.Domain.Entities;
using KeeStashDock.Infrastructure.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KeeStashDock.Desktop.Tests;

/// <summary>Exercises bubble readability and real control layout without activating native windows.</summary>
internal static class BubblePresentationScenarios
{
    internal static void Verify(string isolatedRoot, SettingsStore previousSettings)
    {
        string previousRoot = ThemeManager.Store.Root;
        string language = LocalizationService.Instance.CurrentLanguage;
        int fontSize = TypographyManager.Percent;
        string source = Environment.GetEnvironmentVariable("KEESTASHDOCK_THEME_SOURCE") ?? Path.Combine(AppContext.BaseDirectory, "theme");
        string? output = Environment.GetEnvironmentVariable("KEESTASHDOCK_BUBBLE_PREVIEWS");
        if (!string.IsNullOrWhiteSpace(output)) Directory.CreateDirectory(Path.GetFullPath(output));
        var settings = new SettingsStore(Path.Combine(isolatedRoot, "bubble-presentation.json"));
        settings.Current.SelectionAction = "Both";
        settings.Current.ThemeAppearanceCustomized = true;
        try
        {
            LocalizationService.Instance.SetLanguage("en-US");
            ThemeManager.Initialize(settings, source);
            VerifyPaletteAndPreferences(settings);
            VerifyMissingOptionalColors(settings, isolatedRoot);
            foreach (string appearance in new[] { "default", "wisteria-light", "wisteria-dark" })
            {
                settings.Current.SelectionBubbleStyle = appearance == "default" ? "Default" : "FollowTheme";
                settings.Current.ThemePresetId = appearance == "default" ? "" : "builtin:wisteria-atelier";
                // Default deliberately runs on a dark application palette: its controls must remain light.
                settings.Current.Theme = appearance == "wisteria-light" ? "Light" : "Dark";
                settings.Save();
                BubblePalette palette = GlassBubble.Resolve(settings.Current);
                if (appearance != "default" && !SystemParameters.HighContrast)
                {
                    Assert.IsNotNull(ThemeManager.ActiveVariant, "The built-in theme must be present in the test output.");
                    Assert.AreEqual(ColorOf(ThemeManager.ActiveVariant.Colors["B.BubbleText"]), palette.Text);
                    Assert.AreEqual(ColorOf(ThemeManager.ActiveVariant.Colors["B.BubbleAccent"]), palette.Accent);
                    Assert.IsTrue(Contrast(palette.Surface, palette.Text) >= 7,
                        "Wisteria bubble body text must remain readable in both variants.");
                    Assert.IsTrue(Contrast(palette.Surface, palette.Muted) >= 4.5,
                        "Secondary labels must not disappear into decorative surfaces.");
                }
                foreach (int percent in new[] { 90, 100, 150 })
                {
                    TypographyManager.Apply(percent);
                    VerifyWindows(settings, appearance, percent, "en-US", output);
                    if (percent == 150) VerifyWindows(settings, appearance, percent, "ar-SA", output);
                }
            }
            VerifyCompactChineseResult(settings, output);
            if (!string.IsNullOrWhiteSpace(output))
                File.WriteAllText(Path.Combine(output, "bubble-preview-notes.txt"),
                    "Actual KeeStashDock WPF bubble controls rendered offscreen with fictional sample text.\n" +
                    "Default, Wisteria Light and Wisteria Dark; 100%/150% typography and Arabic RTL coverage.\n" +
                    "Additional Chinese short-result view exercises compact source/result copy controls and save actions.\n" +
                    "No desktop capture, clipboard read/write, provider call or native selection-hotkey registration.\n");
        }
        finally
        {
            ThemeManager.EndPreview();
            ThemeManager.Initialize(previousSettings, previousRoot);
            LocalizationService.Instance.SetLanguage(language);
            TypographyManager.Apply(fontSize);
        }
    }

    private static void VerifyPaletteAndPreferences(SettingsStore settings)
    {
        settings.Current.SelectionBubbleStyle = "Default";
        ThemeManager.Preview(null, "Light");
        BubblePalette light = GlassBubble.Resolve(settings.Current);
        ThemeManager.Preview(null, "Dark");
        BubblePalette dark = GlassBubble.Resolve(settings.Current);
        Assert.AreEqual(light.Surface, dark.Surface);
        Assert.AreEqual(light.Text, dark.Text);
        Assert.IsTrue(Contrast(dark.Surface, dark.Text) >= 7,
            "The fixed default bubble must not inherit white text from a dark main panel.");
        settings.Current.BubbleShadow = false;
        settings.Current.BubbleTextGlow = true;
        settings.Current.BubbleOpacity = 1;
        var surface = new Border();
        GlassBubble.Apply(settings.Current, surface);
        Assert.IsNull(surface.Effect, "The shadow toggle must disable the surface effect.");
        BubblePalette customized = GlassBubble.Resolve(settings.Current);
        Assert.AreEqual(!SystemParameters.HighContrast, customized.Glow,
            "High-contrast mode must suppress the optional text halo.");
        if (surface.Background is GradientBrush gradient)
            Assert.IsTrue(gradient.GradientStops.All(stop => stop.Color.A == 255),
                "100% opacity must not retain a translucent bottom gradient stop.");
        settings.Current.BubbleShadow = true;
        settings.Current.BubbleTextGlow = false;
        GlassBubble.Apply(settings.Current, surface);
        Assert.AreEqual(!SystemParameters.HighContrast, surface.Effect is not null);
        Assert.IsFalse(GlassBubble.Resolve(settings.Current).Glow);
        VerifyHighContrastChoices(customized);
        ThemeManager.EndPreview();
    }

    private static void VerifyHighContrastChoices(BubblePalette palette)
    {
        // Exercise the accessibility palette without changing the user's Windows settings.
        var scope = new Border();
        BubbleChrome.ApplyResources(scope, palette with { HighContrast = true });
        var choice = new ComboBoxItem
        {
            Content = "A readable language choice",
            Style = (Style)scope.Resources[typeof(ComboBoxItem)],
        };
        scope.Child = choice;
        try
        {
            foreach (bool selected in new[] { true, false })
            {
                choice.IsSelected = selected;
                scope.Measure(new Size(300, 80));
                scope.Arrange(new Rect(0, 0, 300, 80));
                scope.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                Brush foreground = selected ? SystemColors.HighlightTextBrush : SystemColors.WindowTextBrush;
                Brush background = selected ? SystemColors.HighlightBrush : SystemColors.WindowBrush;
                Assert.AreSame(foreground, choice.Foreground,
                    "A selected accessibility choice must pair highlight text with its highlight background.");
                Assert.AreSame(background, choice.Background);
                TextBlock label = VisualDescendants(choice).OfType<TextBlock>()
                    .Single(text => text.Text == "A readable language choice");
                Assert.AreSame(foreground, label.Foreground,
                    "The generated content label must inherit the selected-state text color, not the app's implicit text style.");
            }
        }
        finally { scope.Child = null; }

        static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                yield return child;
                foreach (DependencyObject descendant in VisualDescendants(child)) yield return descendant;
            }
        }
    }

    private static void VerifyMissingOptionalColors(SettingsStore settings, string root)
    {
        ThemeVariant light = ThemeManager.CreateDefaultVariant(false), dark = ThemeManager.CreateDefaultVariant(true);
        foreach (ThemeVariant variant in new[] { light, dark })
            foreach (string key in new[] { "B.BubbleAccent", "B.BubbleAccentSoft", "B.BubbleBorder", "B.BubbleMuted", "B.BubbleHighlight" })
                variant.Colors.Remove(key);
        var scheme = new ThemeScheme("legacy-preview", root,
            new ThemeManifest { Id = "legacy-preview", Name = "Legacy theme" }, light, dark);
        settings.Current.SelectionBubbleStyle = "FollowTheme";
        foreach (string mode in new[] { "Light", "Dark" })
        {
            ThemeManager.Preview(scheme, mode);
            foreach ((string key, string fallback) in new[] {
                ("B.BubbleAccent", "B.Accent"), ("B.BubbleAccentSoft", "B.AccentSoft"),
                ("B.BubbleBorder", "B.Border"), ("B.BubbleMuted", "B.TextMuted") })
                if (!SystemParameters.HighContrast)
                    Assert.AreEqual(((SolidColorBrush)Application.Current.FindResource(fallback)).Color,
                        ((SolidColorBrush)Application.Current.FindResource(key)).Color,
                        "An older theme must use its own matching semantic palette for missing bubble keys.");
            Assert.IsTrue(Contrast(GlassBubble.Resolve(settings.Current).Surface,
                GlassBubble.Resolve(settings.Current).Text) >= 4.5);
        }
        ThemeManager.EndPreview();
    }

    private static void VerifyWindows(SettingsStore settings, string appearance, int percent, string language, string? output)
    {
        LocalizationService.Instance.SetLanguage(language);
        var selection = new SelectionPopupWindow(settings);
        var result = new TranslationResultWindow(settings, _ => { });
        try
        {
            Assert.IsFalse(selection.ShowActivated);
            Assert.IsFalse(result.ShowActivated);
            Button copy = SelectionButton(selection, "_copy"), translate = SelectionButton(selection, "_translate");
            Assert.IsFalse(copy.Focusable, "Selection commands must preserve focus in the source application.");
            Assert.IsFalse(translate.Focusable);
            selection.SetBusy(true);
            Assert.IsFalse(copy.IsEnabled); Assert.IsFalse(translate.IsEnabled);
            selection.SetBusy(false);
            Assert.IsTrue(copy.IsEnabled); Assert.IsTrue(translate.IsEnabled);
            selection.MarkCaptured();
            Assert.IsFalse(copy.IsEnabled, "Copied text cannot be captured again in the same selection session.");
            Assert.IsTrue(translate.IsEnabled, "Copy must not consume the independent Translate action.");
            selection.HideBubble();
            // A settings refresh brings the newly cleared session state into the controls without showing a window.
            settings.Save();
            Assert.IsTrue(copy.IsEnabled); Assert.IsTrue(translate.IsEnabled);
            string original = language == "ar-SA" ? "\u0627\u062D\u062A\u0641\u0638 \u0628\u0623\u0641\u0643\u0627\u0631\u0643 \u0641\u064A \u0645\u0643\u0627\u0646 \u0642\u0631\u064A\u0628. \u064A\u0645\u0643\u0646\u0643 \u0646\u0633\u062E \u0627\u0644\u0646\u0635 \u0623\u0648 \u062A\u0631\u062C\u0645\u062A\u0647 \u062B\u0645 \u062D\u0641\u0638\u0647 \u0641\u064A \u0645\u062C\u0645\u0648\u0639\u062A\u0643."
                : "Keep ideas close. Capture a thought, give it room to grow, and return when inspiration arrives.";
            result.SetRecognizedSource(original);
            result.ShowResult(language == "ar-SA" ? "\u0627\u062D\u0641\u0638 \u0623\u0641\u0643\u0627\u0631\u0643 \u0628\u0633\u0647\u0648\u0644\u0629\u060C \u0648\u0627\u0633\u062A\u0639\u062F \u0644\u0644\u062E\u0637\u0648\u0629 \u0627\u0644\u062A\u0627\u0644\u064A\u0629."
                : "\u8BA9\u7075\u611F\u89E6\u624B\u53EF\u53CA\u3002\u8BB0\u5F55\u4E00\u4E2A\u60F3\u6CD5\uFF0C\u7ED9\u5B83\u6210\u957F\u7684\u7A7A\u95F4\uFF0C\u5728\u7075\u611F\u6765\u4E34\u65F6\u518D\u6B21\u56DE\u987E\u3002");
            result.SetResultContext([new(null, language == "ar-SA" ? "\u063A\u064A\u0631 \u0645\u0635\u0646\u0641" : "Uncategorized"), new(7, "Writing")],
                7, "zh-CN", _ => throw new AssertFailedException("Layout tests cannot request translation."),
                (_, _) => throw new AssertFailedException("Layout tests cannot save entries."));
            BubblePalette palette = GlassBubble.Resolve(settings.Current);
            Assert.AreEqual(palette.Text, ((SolidColorBrush)result.FindResource("B.InputText")).Color,
                "Input labels must use the bubble's palette, including Default on a dark application theme.");
            Assert.IsTrue(Contrast(((SolidColorBrush)result.FindResource("B.InputText")).Color,
                ((SolidColorBrush)result.FindResource("B.InputBg")).Color) >= 4.5);
            string suffix = appearance + "-" + percent + (language == "ar-SA" ? "-rtl" : "");
            bool capture = !string.IsNullOrWhiteSpace(output) && percent != 90;
            RenderAndCheck(selection, 440, capture ? Path.Combine(output!, "selection-" + suffix + ".png") : null);
            RenderAndCheck(result, 440, capture ? Path.Combine(output!, "result-" + suffix + ".png") : null);
            // A narrow result must keep the long source and controls within the window; body scrolling handles height.
            result.SetRecognizedSource(string.Join(" ", Enumerable.Repeat(original, 6)));
            RenderAndCheck(result, 320, null);
            if (language == "ar-SA")
                Assert.AreEqual(FlowDirection.RightToLeft, result.FlowDirection);
        }
        finally { selection.Close(); result.Close(); }
    }

    private static void VerifyCompactChineseResult(SettingsStore settings, string? output)
    {
        LocalizationService.Instance.SetLanguage("zh-CN");
        TypographyManager.Apply(100);
        var result = new TranslationResultWindow(settings, _ => { });
        try
        {
            result.SetRecognizedSource("Preview and apply");
            result.ShowResult("\u9884\u89c8\u4e0e\u5e94\u7528");
            result.SetResultContext([new(null, "\u672a\u5206\u7ec4")], null, "zh-CN",
                _ => throw new AssertFailedException("Layout tests cannot request translation."),
                (_, _) => throw new AssertFailedException("Layout tests cannot save entries."));
            result.SetActions(() => { }, null);
            Size compact = RenderAndCheck(result, 440,
                string.IsNullOrWhiteSpace(output) ? null : Path.Combine(output, "result-wisteria-dark-100-zh-CN.png"));
            Assert.IsTrue(compact.Height <= 370,
                $"A short result with source, target, group, all save actions and retry must stay compact; actual height {compact.Height}.");
            VerifyCompactToolSizing(result);
            foreach (string field in new[] { "_copySource", "_copyResult" })
            {
                var button = (Button)typeof(TranslationResultWindow).GetField(field,
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(result)!;
                Assert.IsTrue(button.MinWidth >= 28 && button.MinHeight >= 28,
                    "Compact copy actions must retain practical pointer targets.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)),
                    "Icon-only copy actions must retain an accessible label.");
            }
            var hint = (TextBlock)typeof(TranslationResultWindow).GetField("_hint",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(result)!;
            Assert.AreEqual(Visibility.Collapsed, hint.Visibility, "Success must not reserve a redundant copy-hint row.");
            Assert.IsFalse(result.TryCopyResult(_ => throw new System.Runtime.InteropServices.COMException("Simulated clipboard contention")));
            Assert.AreEqual(Visibility.Visible, hint.Visibility, "Compact mode must still explain clipboard failures.");
            result.ShowResult("\u9884\u89c8\u4e0e\u5e94\u7528");
            Assert.AreEqual(Visibility.Collapsed, hint.Visibility);
        }
        finally { result.Close(); }
    }

    private static void VerifyCompactToolSizing(TranslationResultWindow result)
    {
        var fields = LogicalDescendants((DependencyObject)result.Content).OfType<Grid>()
            .Single(item => item.Name == "ResultToolFields");
        var target = fields.Children.OfType<StackPanel>().Single(item => item.Name == "ResultTargetTools");
        var group = fields.Children.OfType<StackPanel>().Single(item => item.Name == "ResultGroupTools");
        // Short labels simulate the smaller natural width produced by other
        // language/font fallbacks without requiring those fonts on the host.
        ((TextBlock)target.Children[0]).Text = "To";
        ((TextBlock)group.Children[0]).Text = "Group";
        foreach (string field in new[] { "_saveSource", "_saveTranslation", "_saveBoth" })
        {
            var button = (Button)typeof(TranslationResultWindow).GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(result)!;
            ((TextBlock)button.Content).Text = "Save";
        }
        foreach (double dpi in new[] { 1d, 1.25d, 1.5d })
        {
            Size compact = RenderAndCheck(result, 440, null, dpi);
            Assert.AreEqual(1, Grid.GetColumn(group),
                "Short labels must retain side-by-side selectors when the available window width permits them.");
            Assert.IsTrue(compact.Height <= 370);
            Size repeated = RenderAndCheck(result, 440, null, dpi);
            Assert.AreEqual(1, Grid.GetColumn(group));
            Assert.IsTrue(Math.Abs(compact.Height - repeated.Height) <= 1 / dpi
                && Math.Abs(compact.Width - repeated.Width) <= 1 / dpi,
                "Repeated layout may round by one pixel but must not switch to an extra selector row.");
            RenderAndCheck(result, 320, null, dpi);
            Assert.AreEqual(0, Grid.GetColumn(group));
            Assert.AreEqual(1, Grid.GetRow(group), "A genuinely narrow work area must stack selectors without clipping.");
        }
        TypographyManager.Apply(150);
        try
        {
            RenderAndCheck(result, 440, null);
            Assert.AreEqual(1, Grid.GetRow(group), "Large text must retain the accessible stacked layout.");
            RenderAndCheck(result, 320, null);
        }
        finally { TypographyManager.Apply(100); }
    }

    private static Size RenderAndCheck(Window window, double width, string? destination, double dpiScale = 1)
    {
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        var frame = new Border { Child = content, FlowDirection = window.FlowDirection,
            Background = (Brush)Application.Current.FindResource("B.WindowBg"), UseLayoutRounding = true, SnapsToDevicePixels = true };
        frame.Resources.MergedDictionaries.Add(window.Resources);
        // Captures use one pixel per DIP; responsive regressions also exercise
        // higher DPI explicitly instead of inheriting the build host's display.
        VisualTreeHelper.SetRootDpi(frame, new DpiScale(dpiScale, dpiScale));
        try
        {
            frame.Measure(new Size(width, double.PositiveInfinity));
            double arrangedWidth = Math.Min(width, frame.DesiredSize.Width);
            frame.Arrange(new Rect(new Size(arrangedWidth, frame.DesiredSize.Height)));
            frame.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Assert.IsTrue(double.IsFinite(frame.ActualHeight) && frame.ActualHeight > 0);
            Assert.IsTrue(content.ActualWidth <= width + .5, "Bubble content must stay within the supplied work-area width.");
            foreach (Button button in LogicalDescendants(content).OfType<Button>().Where(button => button.Visibility == Visibility.Visible))
            {
                if (button.ActualWidth <= 0 || button.ActualHeight <= 0) continue;
                Rect bounds = button.TransformToAncestor(frame).TransformBounds(new Rect(button.RenderSize));
                Assert.IsTrue(bounds.Left >= -.5 && bounds.Right <= arrangedWidth + .5,
                    $"Action {button.Name} must not be clipped horizontally at {width} DIPs.");
            }
            if (destination is not null)
            {
                var image = new RenderTargetBitmap((int)Math.Ceiling(frame.ActualWidth), (int)Math.Ceiling(frame.ActualHeight),
                    96, 96, PixelFormats.Pbgra32);
                image.Render(frame);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(destination); encoder.Save(file);
            }
            return new Size(frame.ActualWidth, frame.ActualHeight);
        }
        finally { frame.Child = null; window.Content = content; }
    }

    private static Button SelectionButton(SelectionPopupWindow window, string name)
    {
        object action = typeof(SelectionPopupWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        return (Button)action.GetType().GetProperty("Button", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(action)!;
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        yield return root;
        foreach (DependencyObject child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (DependencyObject descendant in LogicalDescendants(child)) yield return descendant;
    }

    private static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte channel) { double value = channel / 255d; return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
