using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using KeeStashDock.Domain.Entities;

namespace KeeStashDock.Desktop;

/// <summary>
/// Scales typography resources without scaling windows, images or icon glyphs.
/// Resource references update existing controls and are inherited by new controls.
/// </summary>
public static class TypographyManager
{
    private static readonly double[] FractionalSizes = [10.5, 12.5, 13.5];
    private static ResourceDictionary? _appliedResources;

    public static int Percent { get; private set; } = AppSettings.DefaultFontSizePercent;

    /// <summary>Raised only when the effective percentage changes, for text-dependent window geometry.</summary>
    public static event Action? Changed;

    public static double Scale(double baseline) => baseline * Percent / 100d;

    public static void Apply(int percent)
    {
        Application? application = Application.Current;
        if (application is null || application.Dispatcher.HasShutdownStarted) return;
        if (!application.Dispatcher.CheckAccess())
        {
            application.Dispatcher.Invoke(() => Apply(percent));
            return;
        }

        int normalized = Math.Clamp(percent, AppSettings.MinFontSizePercent, AppSettings.MaxFontSizePercent);
        bool changed = Percent != normalized;
        if (!changed && ReferenceEquals(_appliedResources, application.Resources)) return;

        Percent = normalized;
        _appliedResources = application.Resources;
        for (int baseline = 9; baseline <= 36; baseline++)
            application.Resources[FontKey(baseline)] = Scale(baseline);
        foreach (double baseline in FractionalSizes)
            application.Resources[FontKey(baseline)] = Scale(baseline);
        application.Resources["T.Line18"] = Scale(18);
        application.Resources["T.Line19"] = Scale(19);
        application.Resources["T.Length34"] = Scale(34);
        application.Resources["T.Length52"] = Scale(52);
        application.Resources["T.Length54"] = Scale(54);
        application.Resources["T.EditorMinHeight"] = 400d + Math.Max(0, Percent - 100) * 2;
        if (changed) Changed?.Invoke();
    }

    public static string FontKey(double baseline) =>
        "T.Font" + baseline.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', '_');

    /// <summary>Sets a live font-size reference for controls constructed in code.</summary>
    public static void SetFontSize(FrameworkElement element, double baseline)
    {
        ArgumentNullException.ThrowIfNull(element);
        DependencyProperty property = element switch
        {
            TextBlock => TextBlock.FontSizeProperty,
            Control => Control.FontSizeProperty,
            _ => TextElement.FontSizeProperty,
        };
        element.SetResourceReference(property, FontKey(baseline));
    }

    public static void SetFontSize(FrameworkContentElement element, double baseline)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetResourceReference(TextElement.FontSizeProperty, FontKey(baseline));
    }
}
