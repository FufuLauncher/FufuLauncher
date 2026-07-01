/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var param = parameter?.ToString().ToLower();
            var isNull = value == null;
            var invert = param is "inverse" or "true";
            return (isNull ^ invert) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
