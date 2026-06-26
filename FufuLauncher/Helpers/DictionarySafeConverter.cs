/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers
{
    public class DictionarySafeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            try
            {
                if (value is System.Collections.IDictionary dictionary && parameter is string key)
                {
                    if (dictionary.Contains(key))
                    {
                        return dictionary[key]?.ToString() ?? "0";
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
