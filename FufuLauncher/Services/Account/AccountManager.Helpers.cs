/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class AccountManager
{
    #region 通用解析工具

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string propertyName, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static Dictionary<string, string> ReadStringDictionary(JsonElement obj)
    {
        var dict = new Dictionary<string, string>();
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
                continue;

            dict[prop.Name] = prop.Value.GetString() ?? string.Empty;
        }

        return dict;
    }

    private static Dictionary<string, string> ParseCookieString(string cookieString)
    {
        var cookieDict = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(cookieString))
            return cookieDict;

        var parts = cookieString.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex > 0)
            {
                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim();
                if (!string.IsNullOrEmpty(key))
                    cookieDict[key] = value;
            }
        }

        return cookieDict;
    }

    private static string DetermineServerTypeByFileName(string fileName)
    {
        return fileName.Contains(".lab", StringComparison.OrdinalIgnoreCase) ? "os" : "cn";
    }

    private string ResolveCookiePath(string cookieFilePath)
    {
        if (string.IsNullOrWhiteSpace(cookieFilePath))
            throw new InvalidOperationException("Account_CookieFilenameEmpty".GetLocalized());

        string root = Path.GetFullPath(CookiesDir);
        string path = Path.GetFullPath(Path.Combine(root, cookieFilePath));
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Account_CookiePathOutOfBounds".GetLocalized());

        return path;
    }

    #endregion
}