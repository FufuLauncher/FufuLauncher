/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml.Markup;

namespace FufuLauncher.Helpers;

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public class Loc : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue()
    {
        return string.IsNullOrEmpty(Key) ? string.Empty : Key.GetLocalized();
    }
}
