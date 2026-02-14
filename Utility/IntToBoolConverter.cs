using System;
using System.Globalization;
using System.Windows.Data;

namespace ASG.EAT.Plugin.Utility
{
    /// <summary>
    /// Converts an integer value to a boolean for RadioButton binding.
    /// Used for Orientation selection (1-4).
    /// </summary>
    public class IntToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue && parameter is string strParam && int.TryParse(strParam, out int paramValue))
            {
                return intValue == paramValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter is string strParam && int.TryParse(strParam, out int paramValue))
            {
                return paramValue;
            }
            return Binding.DoNothing;
        }
    }
}
