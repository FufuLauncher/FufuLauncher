/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers;

public class IntToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int i)
            return new GridLength(i, GridUnitType.Star);
        if (value is double d)
            return new GridLength(d, GridUnitType.Star);
        return new GridLength(1, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public class IntInverseToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double val = 0;
        if (value is int i) val = i;
        if (value is double d) val = d;

        var remain = 100.0 - val;
        if (remain < 0) remain = 0;

        return new GridLength(remain, GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}
