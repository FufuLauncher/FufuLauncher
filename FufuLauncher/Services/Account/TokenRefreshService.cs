/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Constants;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Helpers;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Services.MiHoYo.Passport;
using FufuLauncher.Models.MiHoYo.Identity;

namespace FufuLauncher.Services;

public class TokenRefreshService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly PassportClient _passportClient;

    public TokenRefreshService() : this(new HttpClient(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    }, App.GetService<PassportClient>())
    {
    }

    internal TokenRefreshService(HttpClient httpClient, PassportClient passportClient)
    {
        _httpClient = httpClient;
        _passportClient = passportClient;
    }

    public async Task<Dictionary<string, string>?> RefreshCookieAsync(Dictionary<string, string> currentCookies,
        bool isManual = false, bool forceRefresh = false)
    {
        try
        {
            if (currentCookies == null || currentCookies.Count == 0)
            {
                if (isManual) SendErrorNotification("Token_InvalidCookie".GetLocalized());
                return null;
            }


            string? stoken = ReadCookie(currentCookies, "stoken", "stoken_v2");
            string? mid = ReadCookie(currentCookies, "mid", "account_mid_v2", "ltmid_v2");
            if (stoken == null || mid == null)
            {
                DebugLog.Info("TokenRefreshService", "Provided cookies are missing stoken or mid, cannot refresh");
                if (isManual) SendErrorNotification("Token_MissingStokenOrMid".GetLocalized());
                return null;
            }

            string? uid = ReadCookie(currentCookies, "stuid", "ltuid", "ltuid_v2", "account_id", "account_id_v2");
            if (uid == null)
            {
                if (isManual) SendErrorNotification("Token_InvalidCookie".GetLocalized());
                return null;
            }

            var device = await _passportClient.GetDeviceAsync();
            if (!isManual && !forceRefresh)
            {
                bool? isValid = await CheckCookieValidAsync(currentCookies, device);
                if (isValid != false)
                {
                    DebugLog.Info("TokenRefreshService",
                        isValid == true
                            ? "Cookie still valid, no refresh needed"
                            : "Cannot confirm cookie validity, keeping current credentials");
                    return null;
                }
            }

            DebugLog.Info("TokenRefreshService",
                isManual ? "Manual cookie refresh triggered..." : "Cookie expired, starting refresh...");

            if (isManual)
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "Token_RefreshTitle".GetLocalized(),
                    "Token_ManualRefreshProgress".GetLocalized(), NotificationType.Warning, 3000));

            var webCookies = await _passportClient.ExchangeWebCookiesAsync(stoken, mid, uid, device);
            if (webCookies == null)
            {
                if (isManual) SendErrorNotification("Token_GetNewCookieFailed".GetLocalized());
                return null;
            }

            var refreshedCookies = new Dictionary<string, string>(currentCookies);
            foreach (string name in new[]
                     {
                         "ltoken", "ltuid", "ltoken_v2", "ltuid_v2", "ltmid_v2",
                         "cookie_token", "account_id", "cookie_token_v2", "account_id_v2", "account_mid_v2"
                     })
                refreshedCookies.Remove(name);

            foreach (var (name, value) in webCookies)
                refreshedCookies[name] = value;
            refreshedCookies["stuid"] = uid;
            refreshedCookies["mid"] = mid;

            DebugLog.Info("TokenRefreshService", "Cookie refresh succeeded");
            if (isManual)
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "Token_RefreshTitle".GetLocalized(),
                    "Token_ManualRefreshDone".GetLocalized(), NotificationType.Success, 3000));
            return refreshedCookies;
        }
        catch (Exception ex)
        {
            DebugLog.Info("TokenRefreshService", $"Cookie refresh error: {ex.Message}");
            if (isManual)
                SendErrorNotification(string.Format("Token_RefreshException".GetLocalized(), ex.Message));
            return null;
        }
    }

    private void SendErrorNotification(string message)
    {
        WeakReferenceMessenger.Default.Send(new NotificationMessage("Token_RefreshFailed".GetLocalized(), message,
            NotificationType.Error, 4000));
    }

    private async Task<bool?> CheckCookieValidAsync(IReadOnlyDictionary<string, string> cookies, DeviceIdentity device)
    {
        string? ltokenCookie = BuildCredentialCookie(cookies, "ltoken", "ltuid", "ltmid_v2");
        string? cookieTokenCookie = BuildCredentialCookie(cookies, "cookie_token", "account_id", "account_mid_v2");
        if (ltokenCookie == null || cookieTokenCookie == null) return false;

        bool? ltokenValid = await CheckCredentialValidAsync(ApiEndpoints.MihoyoBbsAccountInfoUrl, ltokenCookie, device);
        if (ltokenValid == false) return false;

        bool? cookieTokenValid = await CheckCredentialValidAsync(
            ApiEndpoints.MihoyoBbsRolesByCookieTokenUrl, cookieTokenCookie, device);
        if (cookieTokenValid == false) return false;

        return ltokenValid == true && cookieTokenValid == true ? true : null;
    }

    private static string? BuildCredentialCookie(IReadOnlyDictionary<string, string> cookies,
        string tokenName, string uidName, string midName)
    {
        string? token = ReadCookie(cookies, tokenName);
        string? uid = ReadCookie(cookies, uidName);
        if (token != null && uid != null) return $"{tokenName}={token}; {uidName}={uid}";

        token = ReadCookie(cookies, tokenName + "_v2");
        uid = ReadCookie(cookies, uidName + "_v2");
        if (token == null || uid == null) return null;

        string header = $"{tokenName}_v2={token}; {uidName}_v2={uid}";
        string? mid = ReadCookie(cookies, midName, "mid");
        return mid == null ? header : $"{header}; {midName}={mid}";
    }

    private async Task<bool?> CheckCredentialValidAsync(string url, string cookie, DeviceIdentity device)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("DS", MiHoYoHeaderFactory.CalculateDs2(HeaderSalts.CnX4));
            request.Headers.TryAddWithoutValidation("x-rpc-channel", "miyousheluodi");
            request.Headers.TryAddWithoutValidation("Origin", ApiEndpoints.ActMihoyoBase);
            request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.93.1");
            request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
            request.Headers.TryAddWithoutValidation("Referer", ApiEndpoints.ActMihoyoBase + "/");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            MiHoYoHeaderFactory.ApplyDeviceIdentityHeaders(request, device, device.BbsDeviceId);
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36 miHoYoBBS/2.93.1");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var responseText = await response.Content.ReadAsStringAsync();

            using var json = JsonDocument.Parse(responseText);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("retcode", out var code) ||
                code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out int retCode))
                return null;

            if (MiyousheCheckinGame.IsLoginExpired(retCode)) return false;
            if (retCode == 0 && json.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object && data.TryGetProperty("list", out var list) &&
                list.ValueKind == JsonValueKind.Array)
                return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("TokenRefreshService", $"Error while validating cookie status: {ex.Message}");
        }

        return null;
    }


    private static string? ReadCookie(IReadOnlyDictionary<string, string> cookies, params string[] names)
    {
        foreach (var name in names)
        {
            if (cookies.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    public void Dispose() => _httpClient.Dispose();
}
