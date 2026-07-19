using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KeeStashDock.Desktop.Views;

/// <summary>Null/empty -&gt; Collapsed, anything else -&gt; Visible.
/// Used by the quick panel toast and other transient hints.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null || (value is string s && s.Length == 0)
            ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
