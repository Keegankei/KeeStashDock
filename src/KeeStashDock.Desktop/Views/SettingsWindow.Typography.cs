using System.Globalization;
using System.Windows;

namespace KeeStashDock.Desktop.Views;

public partial class SettingsWindow
{
    private void PopulateFontSize()
    {
        FontSizeSlider.Value = _settings.Current.FontSizePercent;
        RefreshFontSizeValue();
    }

    private void RefreshFontSizeValue()
        => FontSizeValue.Text = FontSizeSlider.Value.ToString("0", CultureInfo.CurrentCulture) + "%";

    private void OnFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Slider initialization raises ValueChanged before the following label exists.
        if (_loading || FontSizeValue is null) return;
        RefreshFontSizeValue();
        int percent = (int)Math.Round(FontSizeSlider.Value);
        if (_settings.Current.FontSizePercent == percent) return;
        _settings.Current.FontSizePercent = percent;
        _settings.Save();
        TypographyManager.Apply(_settings.Current.FontSizePercent);
    }

    private void OnResetFontSizeClick(object sender, RoutedEventArgs e)
        => FontSizeSlider.Value = 100;
}
