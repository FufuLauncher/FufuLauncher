namespace FufuLauncher.Services.Miyoushe;

public sealed partial class MiyousheClient
{
    private string[] LTokenCookies => LTokens(_context?.Cookies, AccountUid);

    private string[] CookieTokenCookies => CookieTokens(_context?.Cookies, AccountUid);

    private static string[] LTokens(IReadOnlyDictionary<string, string>? cookies, string uid) =>
        CredentialCookies(cookies, uid, "ltoken", "ltuid", "ltoken_v2", "ltuid_v2", "ltmid_v2");

    private static string[] CookieTokens(IReadOnlyDictionary<string, string>? cookies, string uid) =>
        CredentialCookies(cookies, uid, "cookie_token", "account_id", "cookie_token_v2", "account_id_v2",
            "account_mid_v2");

    internal static string BuildShopCookies(IReadOnlyDictionary<string, string> cookies, string uid) =>
        string.Join("; ", CookieTokens(cookies, uid).Concat(LTokens(cookies, uid)));

    private static string[] CredentialCookies(IReadOnlyDictionary<string, string>? cookies, string accountUid,
        string tokenV1, string uidV1, string tokenV2, string uidV2, string midV2)
    {
        if (cookies == null || accountUid.Length == 0) return [];
        string Cookie(string key) => cookies.GetValueOrDefault(key) ?? "";
        string token = Cookie(tokenV2), uid = Cookie(uidV2), mid = Cookie(midV2);
        if (token.Length > 0 && mid.Length > 0 && uid == accountUid)
            return [tokenV2 + "=" + token, uidV2 + "=" + uid, midV2 + "=" + mid];
        token = Cookie(tokenV1);
        uid = Cookie(uidV1);
        if (token.Length > 0 && uid == accountUid) return [tokenV1 + "=" + token, uidV1 + "=" + uid];
        return [];
    }

    private string BuildCookies(bool ltokenOnly) => string.Join("; ",
        ltokenOnly ? LTokenCookies : CookieTokenCookies.Concat(LTokenCookies));
}