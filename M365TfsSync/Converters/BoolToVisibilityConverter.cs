using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace M365TfsSync.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;

        // 支援 ConverterParameter="Inverse" 反轉邏輯
        if (parameter is string param && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            boolValue = !boolValue;

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool result = value is Visibility v && v == Visibility.Visible;

        if (parameter is string param && param.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            result = !result;

        return result;
    }
}
