using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OcgStatus.Core;

namespace OcgStatus.App.Helpers;

public sealed class PercentToBrushConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___)
    {
        if (value is double pct)
        {
            if (pct >= 90) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0x3E, 0x3E));
            if (pct >= 60) return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDD, 0x6B, 0x20));
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0xA1, 0x69));
        }
        return new SolidColorBrush(Colors.Gray);
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type _, object __, CultureInfo ___)
    {
        // Used with ProgressBar: we just use native control, but keep helper for custom bar if needed
        return value;
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
