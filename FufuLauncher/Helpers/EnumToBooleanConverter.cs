/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers;

public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is string enumString && value != null)
        {
            if (value.GetType().IsEnum)
            {
                try
                {
                    var parsedValue = Enum.Parse(value.GetType(), enumString);
                    return parsedValue.Equals(value);
                }
                catch { return false; }
            }
            if (value is int intValue)
            {
                try
                {
                    var enumValue = Enum.Parse(typeof(WindowBackdropType), enumString);
                    return (int)enumValue == intValue;
                }
                catch { return false; }
            }
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isChecked && isChecked && parameter is string enumString)
        {
            try
            {
                if (targetType.IsEnum)
                {
                    return Enum.Parse(targetType, enumString);
                }

                if (targetType == typeof(int) || targetType == typeof(object))
                {
                    var enumValue = Enum.Parse(typeof(WindowBackdropType), enumString);
                    return (int)enumValue;
                }
            }
            catch
            {
                return DependencyProperty.UnsetValue;
            }
        }
        return DependencyProperty.UnsetValue;
    }
}
