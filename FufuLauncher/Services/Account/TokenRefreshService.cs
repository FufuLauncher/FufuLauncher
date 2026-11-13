/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using FufuLauncher.Constants;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using MihoyoBBS;
using FufuLauncher.Helpers;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Services.MiHoYo.Networking;

namespace FufuLauncher.Services;

public class TokenRefreshService : IDisposable
{
    private const string Salt = "dDIQHbKOdaPaLuvQKVzUzqdeCaxjtaPV";
    private const string WebSalt = "G1ktdwFL4IyGkHuuWSmz0wUe9Db9scyK";
    private readonly string _deviceId;
    private readonly string _deviceFp;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public TokenRefreshService() : this(new HttpClient(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    })
    {
    }

    internal TokenRefreshService(HttpClient httpClient)
    {
        _deviceId = Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();
        _deviceFp = GenerateDeviceFingerprint();

        _httpClient = httpClient;
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


            currentCookies.TryGetValue("stoken", out string stoken);
            currentCookies.TryGetValue("mid", out string mid);

            if (string.IsNullOrEmpty(stoken) || string.IsNullOrEmpty(mid))
            {
                DebugLog.Info("TokenRefreshService", "Provided cookies are missing stoken or mid, cannot refresh");
                if (isManual) SendErrorNotification("Token_MissingStokenOrMid".GetLocalized());
                return null;
            }


            if (!isManual && !forceRefresh)
            {
                bool? isValid = await CheckCookieValidAsync(currentCookies);
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

            string authCookie = $"stoken={stoken}; mid={mid}";

            string webTicket = await CreateWebQrCodeAsync();
            if (string.IsNullOrEmpty(webTicket))
            {
                if (isManual) SendErrorNotification("Token_WebTicketFailed".GetLocalized());
                return null;
            }

            bool scanResult = await SimulateAppActionAsync(ApiEndpoints.PassportScanQrLoginUrl, webTicket, authCookie);
            if (!scanResult)
            {
                if (isManual) SendErrorNotification("Token_QRScanFailed".GetLocalized());
                return null;
            }

            await Task.Delay(500);

            bool confirmResult =
                await SimulateAppActionAsync(ApiEndpoints.PassportConfirmQrLoginUrl, webTicket, authCookie);
            if (!confirmResult)
            {
                if (isManual) SendErrorNotification("Token_ConfirmLoginFailed".GetLocalized());
                return null;
            }

            var v2Cookies = await GetWebQrStatusAndExtractCookiesAsync(webTicket);
            if (v2Cookies != null &&
                (v2Cookies.TryGetValue("cookie_token_v2", out var tokenV2) && !string.IsNullOrEmpty(tokenV2) ||
                 v2Cookies.TryGetValue("cookie_token", out var tokenV1) && !string.IsNullOrEmpty(tokenV1)))
            {
                string? expectedUid = currentCookies.GetValueOrDefault("stuid") ??
                                      currentCookies.GetValueOrDefault("ltuid") ??
                                      currentCookies.GetValueOrDefault("account_id") ??
                                      currentCookies.GetValueOrDefault("account_id_v2");
                string? refreshedUid = v2Cookies.GetValueOrDefault("account_id_v2") ??
                                       v2Cookies.GetValueOrDefault("account_id");
                if (!string.IsNullOrEmpty(expectedUid) && !string.IsNullOrEmpty(refreshedUid) &&
                    expectedUid != refreshedUid)
                {
                    if (isManual) SendErrorNotification("Token_InvalidCookie".GetLocalized());
                    return null;
                }

                var refreshedCookies = new Dictionary<string, string>(currentCookies);
                foreach (var kvp in v2Cookies)
                {
                    refreshedCookies[kvp.Key] = kvp.Value;
                }

                if (!string.IsNullOrEmpty(expectedUid)) refreshedCookies["stuid"] = expectedUid;


                if (v2Cookies.ContainsKey("cookie_token_v2") && v2Cookies.ContainsKey("account_id_v2"))
                {
                    refreshedCookies.Remove("cookie_token");
                    refreshedCookies.Remove("account_id");
                }

                if (v2Cookies.ContainsKey("ltoken_v2") && v2Cookies.ContainsKey("ltuid_v2"))
                {
                    refreshedCookies.Remove("ltoken");
                    refreshedCookies.Remove("ltuid");
                }

                DebugLog.Info("TokenRefreshService", "Cookie refresh succeeded");
                if (isManual)
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "Token_RefreshTitle".GetLocalized(),
                        "Token_ManualRefreshDone".GetLocalized(), NotificationType.Success, 3000));
                return refreshedCookies;
            }
            else
            {
                if (isManual) SendErrorNotification("Token_GetNewCookieFailed".GetLocalized());
                return null;
            }
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

    private async Task<bool?> CheckCookieValidAsync(IReadOnlyDictionary<string, string> cookies)
    {
        
        string? ltokenCookie = BuildCredentialCookie(cookies, "ltoken", "ltuid");
        string? cookieTokenCookie = BuildCredentialCookie(cookies, "cookie_token", "account_id");
        if (ltokenCookie == null || cookieTokenCookie == null) return false;

        bool? ltokenValid = await CheckCredentialValidAsync(ApiEndpoints.MihoyoBbsAccountInfoUrl, ltokenCookie);
        if (ltokenValid == false) return false;

        bool? cookieTokenValid = await CheckCredentialValidAsync(
            ApiEndpoints.MihoyoBbsRolesByCookieTokenUrl, cookieTokenCookie);
        if (cookieTokenValid == false) return false;

        return ltokenValid == true && cookieTokenValid == true ? true : null;
    }

    private static string? BuildCredentialCookie(IReadOnlyDictionary<string, string> cookies,
        string tokenName, string uidName)
    {
        string suffix = !string.IsNullOrEmpty(cookies.GetValueOrDefault(tokenName)) ? "" : "_v2";
        string? token = cookies.GetValueOrDefault(tokenName + suffix);
        string? uid = cookies.GetValueOrDefault(uidName + suffix);
        return string.IsNullOrEmpty(token) || string.IsNullOrEmpty(uid)
            ? null
            : $"{tokenName}{suffix}={token}; {uidName}{suffix}={uid}";
    }

    private async Task<bool?> CheckCredentialValidAsync(string url, string cookie)
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
            request.Headers.TryAddWithoutValidation("x-rpc-device_id", _deviceId);
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


    private Dictionary<string, string> ParseCookieString(string cookieString)
    {
        var dict = new Dictionary<string, string>();
        var pairs = cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2)
            {
                dict[kv[0].Trim()] = kv[1].Trim();
            }
        }

        return dict;
    }

    private string BuildCookieString(Dictionary<string, string> cookieDict)
    {
        var list = new List<string>();
        foreach (var kvp in cookieDict)
        {
            list.Add($"{kvp.Key}={kvp.Value}");
        }

        return string.Join("; ", list);
    }


    private async Task<string> CreateWebQrCodeAsync()
    {
        string url = ApiEndpoints.PassportCreateQrLoginUrl;
        var body = new JsonObject();
        string bodyStr = body.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

        AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            if (result["retcode"]?.GetValue<int>() == 0)
                return result["data"]["ticket"]?.GetValue<string>();
        }
        catch
        {
        }

        return null;
    }

    private async Task<bool> SimulateAppActionAsync(string url, string ticket, string authCookie)
    {
        var tokenTypes = new JsonArray { "4" };
        var body = new JsonObject { ["ticket"] = ticket, ["token_types"] = tokenTypes };
        string bodyStr = body.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1", authCookie);

        try
        {
            var response = await _httpClient.SendAsync(request);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            return result["retcode"]?.GetValue<int>() == 0;
        }
        catch
        {
        }

        return false;
    }

    private async Task<Dictionary<string, string>> GetWebQrStatusAndExtractCookiesAsync(string ticket)
    {
        string url = ApiEndpoints.PassportQueryQrLoginStatusUrl;
        var body = new JsonObject { ["ticket"] = ticket };
        string bodyStr = body.ToJsonString(_jsonOptions);

        for (int i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
            AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1");

            try
            {
                var response = await _httpClient.SendAsync(request);
                var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());

                if (result["retcode"]?.GetValue<int>() == 0)
                {
                    string status = result["data"]["status"]?.GetValue<string>();
                    if (status == "Confirmed" || status == "confirmed")
                    {
                        var cookieDict = new Dictionary<string, string>();
                        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                        {
                            foreach (var cookieStr in setCookies)
                            {
                                var mainPart = cookieStr.Split(';')[0];
                                var kv = mainPart.Split('=', 2);
                                if (kv.Length == 2) cookieDict[kv[0].Trim()] = kv[1].Trim();
                            }
                        }

                        return cookieDict;
                    }
                }
            }
            catch
            {
            }

            await Task.Delay(1000);
        }

        return null;
    }

    private void AddCommonHeaders(HttpRequestMessage request, string body, string query, string clientType,
        string appId, string sdkVersion, string cookie = "")
    {
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 miHoYoBBS/2.90.1 Capture/2.2.0");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-cn");

        if (!string.IsNullOrEmpty(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);

        request.Headers.TryAddWithoutValidation("x-rpc-client_type", clientType);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.90.1");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", _deviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_fp", _deviceFp);
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", appId);
        request.Headers.TryAddWithoutValidation("x-rpc-sdk_version", sdkVersion);

        request.Headers.TryAddWithoutValidation("DS", GenerateDS(body, query));
    }

    private string GenerateDeviceFingerprint()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string seedId = GenerateRandomString(16, "0123456789abcdef");

        var deviceInfo = new
        {
            device_id = _deviceId,
            seed_id = seedId,
            seed_time = timestamp,
            platform = "2",
            device_fp = "",
            app_name = "bbs_cn"
        };

        string fpStr = JsonSerializer.Serialize(deviceInfo, _jsonOptions);
        return CreateMD5(fpStr);
    }

    private string GenerateDS(string body, string query)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GenerateRandomString(6, "abcdefghijklmnopqrstuvwxyz0123456789");

        string b = string.IsNullOrEmpty(body) ? "" : body;
        string q = string.IsNullOrEmpty(query) ? "" : query;

        string signStr = $"salt={Salt}&t={t}&r={r}&b={b}&q={q}";
        string sign = CreateMD5(signStr);

        return $"{t},{r},{sign}";
    }

    private string GenerateWebDS()
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GenerateRandomString(6, "abcdefghijklmnopqrstuvwxyz0123456789");
        string c = CreateMD5($"salt={WebSalt}&t={t}&r={r}");

        return $"{t},{r},{c}";
    }

    private string GenerateRandomString(int length, string chars)
    {
        var random = new Random();
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }

        return new string(result);
    }

    private string CreateMD5(string input)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            StringBuilder sb = new();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }

            return sb.ToString();
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
