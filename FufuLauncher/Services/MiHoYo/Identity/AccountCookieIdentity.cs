/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services.MiHoYo;

internal static class AccountCookieIdentity
{
    internal static string? GetUid(IEnumerable<KeyValuePair<string, string>> cookies)
    {
        var uids = cookies
            .Where(cookie => cookie.Key is "stuid" or "account_id_v2" or "account_id" or "ltuid_v2" or "ltuid")
            .Select(cookie => cookie.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();

        return uids.Length == 1 ? uids[0] : null;
    }
}
