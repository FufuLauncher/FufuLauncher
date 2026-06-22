using Microsoft.UI.Xaml.Data;

namespace FufuLauncher.Helpers;

/// <summary>
/// null → Opacity=0（占位但透明），not-null → Opacity=1（正常显示）
/// </summary>
public class NullToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value == null ? 0.0 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}