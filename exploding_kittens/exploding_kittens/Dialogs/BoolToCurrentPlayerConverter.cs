using System;
using System.Globalization;
using System.Windows.Data;

namespace exploding_kittens.Dialogs
{
    public class BoolToCurrentPlayerConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isCurrent && isCurrent)
            {
                return "👑";
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}