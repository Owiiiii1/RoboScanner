using System;
using System.Globalization;
using System.Windows.Data;

namespace RoboScanner.Converters
{
    public class NullableDoubleConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double d ? d.ToString(culture) : "";

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value as string)?.Trim();
            if (string.IsNullOrEmpty(s)) return null;
            return double.TryParse(s, NumberStyles.Float, culture, out var d) ? d : null;
        }
    }
}
