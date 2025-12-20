
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers;

public class StringFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value == null) return "";

        var format = parameter as string;
        if (string.IsNullOrEmpty(format))
            return value.ToString();

        return string.Format(format, value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}