using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {

            var invert = parameter?.ToString().ToLower() is "inverse" or "true";
            return (boolValue ^ invert) ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            var invert = parameter?.ToString().ToLower() is "inverse" or "true";
            var result = visibility == Visibility.Visible;
            return result ^ invert;
        }
        return false;
    }
}