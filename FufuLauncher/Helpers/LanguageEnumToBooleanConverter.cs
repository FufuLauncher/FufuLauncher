using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers
{
    public class LanguageEnumToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null || parameter == null)
                return false;

            try
            {
                int enumValue = (int)value;
                int paramValue = System.Convert.ToInt32(parameter);
                return enumValue == paramValue;
            }
            catch
            {
                return false;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isChecked && isChecked)
            {
                try
                {
                    return (AppLanguage)System.Convert.ToInt32(parameter);
                }
                catch
                {
                    return AppLanguage.Default;
                }
            }
            return null;
        }
    }
}