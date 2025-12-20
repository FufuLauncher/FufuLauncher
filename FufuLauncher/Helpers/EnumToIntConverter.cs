using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers
{
    public class EnumToIntConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (int)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return Enum.ToObject(targetType, value);
        }
    }
}