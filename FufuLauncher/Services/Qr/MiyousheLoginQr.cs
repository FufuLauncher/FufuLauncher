/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;

namespace FufuLauncher.Services;

internal sealed record MiyousheLoginQr(
    string Ticket,
    string TokenTypes,
    string? GameBiz,
    int AppId,
    long? ExpiresAt)
{
    internal string TargetKey => GameBiz switch
    {
        "hk4e_cn" => "MiyousheQr_Genshin",
        "hkrpg_cn" => "MiyousheQr_StarRail",
        "nap_cn" => "MiyousheQr_Zzz",
        "bh3_cn" => "MiyousheQr_Honkai3",
        _ => "MiyousheQr_Passport"
    };

    internal bool IsExpired => ExpiresAt.HasValue
                               && ExpiresAt.Value <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    internal static bool TryParse(string text, out MiyousheLoginQr? qr)
    {
        qr = null;
        if (text.Length > 4096 || text.Any(char.IsControl)
                               || !Uri.TryCreate(text, UriKind.Absolute, out var uri)
                               || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0
                               || !uri.Host.Equals(ApiEndpoints.UserMihoyoHost, StringComparison.OrdinalIgnoreCase)) return false;
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0]);
            string value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
            if (value.Any(char.IsControl) || !parameters.TryAdd(key, value)) return false;
        }

        long? expiry = null;
        if (parameters.TryGetValue("expire", out var expire))
        {
            if (!long.TryParse(expire, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)) return false;
            expiry = seconds;
        }

        if (uri.AbsolutePath == ApiEndpoints.PassportQrMobileLoginPath)
        {
            if (!parameters.TryGetValue("tk", out var ticket) || !IsTicket(ticket)
                                                              || !parameters.TryGetValue("token_types", out var types)
                                                              || types.Length > 32 || types.Split(',').Any(t =>
                                                                  t.Length != 1 || t[0] is < '1' or > '9'))
                return false;
            qr = new(ticket, types, null, 0, expiry);
            return true;
        }

        if (uri.AbsolutePath != ApiEndpoints.PassportQrInGamePath
            || !parameters.TryGetValue("ticket", out var gameTicket) || !IsTicket(gameTicket)
            || !parameters.TryGetValue("app_id", out var appIdText)
            || !int.TryParse(appIdText, out int appId)
            || !parameters.TryGetValue("biz_key", out var biz)) return false;
        int expectedId = biz switch { "bh3_cn" => 1, "hk4e_cn" => 4, "hkrpg_cn" => 8, "nap_cn" => 12, _ => 0 };
        if (expectedId == 0 || appId != expectedId) return false;
        qr = new(gameTicket, "", biz, appId, expiry);
        return true;
    }

    private static bool IsTicket(string ticket) => ticket.Length is >= 8 and <= 256
                                                   && ticket.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}