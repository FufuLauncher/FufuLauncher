/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Helpers;

public static class AccountAvatarPendant
{
    public const string SettingKey = "IsAvatarPendantEnabled";

    public static string? NormalizeUrl(string? value)
    {
        var url = value?.Trim();
        if (url?.StartsWith("//", StringComparison.Ordinal) == true) url = "https:" + url;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.AbsoluteUri
            : null;
    }

    public static BitmapImage? CreateImage(string? value) =>
        NormalizeUrl(value) is { } url ? new BitmapImage(new Uri(url)) : null;
}
