using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers
{
    public class DictionarySafeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try
            {
                if (value is Dictionary<string, object> dictionary && parameter is string key)
                {
                    if (dictionary.TryGetValue(key, out object result))
                    {
                        return result?.ToString() ?? "0";
                    }
                }
                return "0";
            }
            catch
            {
                return "0";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}