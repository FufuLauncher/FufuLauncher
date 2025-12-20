using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers
{
    public class WindowModeEnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is WindowModeType enumValue && parameter is string parameterString)
            {
                if (Enum.TryParse<WindowModeType>(parameterString, out var paramValue))
                {
                    return enumValue == paramValue;
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {

            if (value is bool boolValue && boolValue && parameter is string parameterString)
            {
                if (Enum.TryParse<WindowModeType>(parameterString, out var paramValue))
                {
                    return paramValue;
                }
            }

            return WindowModeType.Normal;
        }
    }
}