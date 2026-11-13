/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Services.MiHoYo.Networking;

namespace FufuLauncher.Services;

internal sealed class MiyousheQrLoginService : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _deviceId = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
    private readonly string _deviceFp;

    internal MiyousheQrLoginService(HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
            { Timeout = TimeSpan.FromSeconds(20) };
        _deviceFp = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            device_id = _deviceId, seed_id = Guid.NewGuid().ToString("N")[..16],
            seed_time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), platform = "2", device_fp = "", app_name = "bbs_cn"
        })))).ToLowerInvariant();
    }


    internal async Task<MiyousheLoginQr> PrepareAsync(MiyousheLoginQr qr, CancellationToken cancellationToken)
    {
        if (qr.IsExpired) throw new MiyousheQrException("MiyousheQr_Expired");
        if (qr.GameBiz == null) return qr;
        string url = ApiEndpoints.GetGameQrCodeScanUrl(qr.GameBiz);
        var result = await PostAsync(url, new
        {
            passport_app_id = "bll8iq97cem8", ticket = qr.Ticket, app_id = qr.AppId,
            device = _deviceId, ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        }, null, cancellationToken);
        string? passportUrl = result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                                                                          && data.TryGetProperty("passport_qr_url",
                                                                              out var urlElement) &&
                                                                          urlElement.ValueKind == JsonValueKind.String
            ? urlElement.GetString()
            : null;
        if (passportUrl == null || !MiyousheLoginQr.TryParse(passportUrl, out var passport) ||
            passport!.GameBiz != null)
            throw new MiyousheQrException("MiyousheQr_Unsupported");
        return passport;
    }

    internal async Task ConfirmAsync(MiyousheLoginQr passport, IReadOnlyDictionary<string, string> cookies,
        Func<bool> isCurrentAccount, CancellationToken cancellationToken)
    {
        if (passport.GameBiz != null) throw new MiyousheQrException("MiyousheQr_Unsupported");
        string stoken = FindCookie(cookies, "stoken", "stoken_v2");
        string mid = FindCookie(cookies, "mid", "account_mid_v2", "ltmid_v2");
        if (stoken.Length == 0 || mid.Length == 0 || !IsCookieValue(stoken) || !IsCookieValue(mid))
            throw new MiyousheQrException("MiyousheQr_CredentialsMissing");
        string cookie = $"stoken={stoken}; mid={mid}";
        var body = new { ticket = passport.Ticket, token_types = new[] { passport.TokenTypes } };
        CheckAccountAndExpiry();
        await PostAsync(ApiEndpoints.PassportScanQrLoginUrl, body, cookie, cancellationToken);
        CheckAccountAndExpiry();
        await PostAsync(ApiEndpoints.PassportConfirmQrLoginUrl, body, cookie, cancellationToken);

        void CheckAccountAndExpiry()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!isCurrentAccount()) throw new MiyousheQrException("MiyousheQr_AccountChanged");
            if (passport.IsExpired) throw new MiyousheQrException("MiyousheQr_Expired");
        }
    }

    private async Task<JsonElement> PostAsync(string url, object body, string? cookie,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", "bll8iq97cem8");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", _deviceId);
        if (cookie != null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 miHoYoBBS/2.90.1 Capture/2.2.0");
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-cn");
            request.Headers.TryAddWithoutValidation("x-rpc-client_type", "2");
            request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.90.1");
            request.Headers.TryAddWithoutValidation("x-rpc-device_fp", _deviceFp);
            request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
            request.Headers.TryAddWithoutValidation("x-rpc-sdk_version", "2.90.1");
            request.Headers.TryAddWithoutValidation("x-rpc-account_version", "2.90.1");
            request.Headers.TryAddWithoutValidation("x-rpc-device_model", "Mi 14");
            request.Headers.TryAddWithoutValidation("x-rpc-device_name", "Mihoyo Capture");
            request.Headers.TryAddWithoutValidation("DS",
                MiHoYoHeaderFactory.CalculateDsGen2("dDIQHbKOdaPaLuvQKVzUzqdeCaxjtaPV", json));
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new MiyousheQrException("MiyousheQr_HttpError", (int)response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("retcode", out var code) || !code.TryGetInt32(out int retcode))
            throw new MiyousheQrException("MiyousheQr_ResponseError");
        if (retcode != 0) throw new MiyousheQrException("MiyousheQr_Rejected", retcode);
        return doc.RootElement.Clone();
    }

    private static string FindCookie(IReadOnlyDictionary<string, string> cookies, params string[] keys)
        => keys.Select(k => cookies.TryGetValue(k, out var value) ? value : "")
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

    private static bool IsCookieValue(string value) => value.All(c => c is >= '!' and <= '~' && c != ';');
    public void Dispose() => _http.Dispose();
}

internal sealed class MiyousheQrException(string resourceKey, int? code = null) : Exception(resourceKey)
{
    internal string ResourceKey
    {
        get;
    } = resourceKey;

    internal int? Code
    {
        get;
    } = code;
}