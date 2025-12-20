using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers
{

    public class BoolToPanelGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
            => (value is bool b && b) ? "\uE00E" : "\uE00F";

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}