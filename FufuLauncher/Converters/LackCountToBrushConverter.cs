/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FufuLauncher.Converters
{
    public class LackCountToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is int lackCount && lackCount > 0)
            {
                return new SolidColorBrush(Color.FromArgb(255, 255, 150, 100));
            }
            return new SolidColorBrush(Color.FromArgb(255, 150, 255, 150));
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
