using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers
{
    public class DateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dateTime && dateTime > DateTime.MinValue)
            {
                return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return "从未使用";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}