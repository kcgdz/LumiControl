using System.Globalization;
using System.Windows.Data;

namespace LumiControl.Converters;

public class BrightnessToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[1] is double totalWidth)
        {
            double brightness = 0;
            if (values[0] is int intVal) brightness = intVal;
            else if (values[0] is double dblVal) brightness = dblVal;
            return Math.Max(0, totalWidth * brightness / 100.0);
        }
        return 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is System.Windows.Visibility.Visible;
}

public class ConnectionTypeToBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            Core.Models.ConnectionType.HDMI => "HDMI",
            Core.Models.ConnectionType.DisplayPort => "DP",
            Core.Models.ConnectionType.VGA => "VGA",
            Core.Models.ConnectionType.DVI => "DVI",
            Core.Models.ConnectionType.USB_C => "USB-C",
            Core.Models.ConnectionType.Thunderbolt => "TB",
            Core.Models.ConnectionType.Internal => "INT",
            _ => "?"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
