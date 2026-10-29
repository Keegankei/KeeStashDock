using System.Globalization;
using System.Windows.Data;

namespace KeeStashDock.Desktop.Views;

/// <summary>Uses measured chrome heights so larger text does not reserve an oversized, mostly empty writing area.</summary>
public sealed class WorkbenchDetailsHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double height || !double.IsFinite(height)) return 96d;
        double scale = values[1] is double fontSize && double.IsFinite(fontSize) ? fontSize / 13d : 1d;
        if (values.Length >= 6 && values.Skip(2).Take(4).All(value => value is double measured && double.IsFinite(measured)))
        {
            double chrome = values.Skip(2).Take(4).Cast<double>().Sum();
            // Border/padding and the margins between the heading, details,
            // actions, status, and content toolbar total at most 94 DIPs.
            double writing = Math.Max(100, 70 * scale);
            return Math.Max(96, height - chrome - 94 - writing);
        }
        return Math.Max(96, height - 325 * scale);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
