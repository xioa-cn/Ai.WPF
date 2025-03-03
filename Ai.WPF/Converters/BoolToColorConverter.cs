using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Ai.WPF.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isUser)
            {
                return isUser ? new SolidColorBrush(Color.FromRgb(232, 245, 253)) : new SolidColorBrush(Color.FromRgb(245, 245, 245));
            }
            return new SolidColorBrush(Color.FromRgb(245, 245, 245));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 