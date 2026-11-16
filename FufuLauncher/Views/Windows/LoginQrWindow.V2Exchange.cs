/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using System.Text.Json.Nodes;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.MiHoYo;
using FufuLauncher.Services.MiHoYo.Passport;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow
{
    #region 扫码换取

    private async Task<Dictionary<string, string>?> ExchangeV2TokensAsync(string stoken, string mid, string aid,
        DeviceIdentity? device = null, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(aid))
        {
            UpdateStatus("登录失败: 响应缺少账号 ID", false);
            return null;
        }

        try
        {
            UpdateStatus("LoginQR_FetchingFullCredential".GetLocalized(), true);
            var finalCookies = new Dictionary<string, string>
            {
                ["stoken"] = stoken,
                ["mid"] = mid,
                ["stuid"] = aid
            };

            var passportClient = App.GetService<PassportClient>();
            device ??= await App.GetService<AccountIdentityService>().GetDeviceAsync(token);
            var webCookies = await passportClient.ExchangeWebCookiesAsync(stoken, mid, aid, device, token);
            if (webCookies != null)
            {
                foreach (var (name, value) in webCookies)
                    finalCookies[name] = value;
                return finalCookies;
            }

            string? webTicket = await CreateWebQrCodeAsync(device, token);
            if (string.IsNullOrEmpty(webTicket))
            {
                UpdateStatus("LoginQR_CannotCreateAuthToken".GetLocalized());
                return null;
            }

            string authCookie = $"stoken={stoken}; mid={mid}";

            bool scanResult = await SimulateAppActionAsync(ApiEndpoints.PassportScanQrLoginUrl, webTicket, authCookie,
                device, token);
            if (!scanResult)
            {
                UpdateStatus("LoginQR_ScanRejected".GetLocalized());
                return null;
            }

            await Task.Delay(1000, token);

            bool confirmResult =
                await SimulateAppActionAsync(ApiEndpoints.PassportConfirmQrLoginUrl, webTicket, authCookie, device,
                    token);
            if (!confirmResult)
            {
                UpdateStatus("LoginQR_RequestRejected".GetLocalized());
                return null;
            }

            var v2Cookies = await GetWebQrStatusAndExtractCookiesAsync(webTicket, device, token);
            if (v2Cookies != null)
            {
                bool hasLToken = CopyPair("ltoken_v2", "ltuid_v2", "ltmid_v2") | CopyPair("ltoken", "ltuid");
                bool hasCookieToken = CopyPair("cookie_token_v2", "account_id_v2", "account_mid_v2") |
                                      CopyPair("cookie_token", "account_id");
                if (hasLToken && hasCookieToken) return finalCookies;
            }

            UpdateStatus("LoginQR_ExtractCookieFromHeadersFailed".GetLocalized());
            return null;

            bool CopyPair(string tokenName, string uidName, string? midName = null)
            {
                var value = v2Cookies!.GetValueOrDefault(tokenName);
                if (string.IsNullOrEmpty(value) || v2Cookies.GetValueOrDefault(uidName) != aid) return false;
                finalCookies[tokenName] = value;
                finalCookies[uidName] = aid;
                if (midName != null) finalCookies[midName] = mid;
                return true;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            UpdateStatus(string.Format("LoginQR_CredentialExchangeException_Format".GetLocalized(), ex.Message));
            return null;
        }
    }

    private async Task ProcessAndExchangeV2TokensAsync(JsonNode dataNode, LoginSession session, DeviceIdentity device)
    {
        string stoken = "";
        string mid = dataNode["user_info"]?["mid"]?.GetValue<string>() ?? "";
        string aid = dataNode["user_info"]?["aid"]?.GetValue<string>() ?? "";

        if (dataNode["tokens"] is JsonArray tokens)
        {
            var token = tokens.FirstOrDefault(item => item?["name"]?.GetValue<string>() is "stoken" or "stoken_v2")
                        ?? tokens.FirstOrDefault(item => item?["token_type"] is JsonValue type &&
                                                         type.TryGetValue<int>(out var id) && id == 1);
            stoken = token?["token"]?.GetValue<string>() ?? "";
        }

        if (string.IsNullOrEmpty(stoken) || string.IsNullOrEmpty(mid))
        {
            UpdateStatus("LoginQR_ExtractRetry".GetLocalized(), false);
            return;
        }

        var cookies = await ExchangeV2TokensAsync(stoken, mid, aid, device, session.Cts.Token);
        if (cookies != null && IsSessionActive(session))
        {
            OnLoginSuccess(cookies, "cn");
        }
    }

    private async Task<string?> CreateWebQrCodeAsync(DeviceIdentity device, CancellationToken token)
    {
        string url = ApiEndpoints.PassportCreateQrLoginUrl;
        var body = new JsonObject();
        string bodyStr = body.ToJsonString(_jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

        AddCommonHeaders(request, device, bodyStr, "", "2", "bll8iq97cem8", "2.90.1");

        try
        {
            using var response = await _httpClient.SendAsync(request, token);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(token));
            if (result?["retcode"]?.GetValue<int>() == 0) return result["data"]?["ticket"]?.GetValue<string>();
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
        }

        return null;
    }

    private async Task<bool> SimulateAppActionAsync(string url, string ticket, string authCookie,
        DeviceIdentity device, CancellationToken token)
    {
        var tokenTypes = new JsonArray { "4" };
        var body = new JsonObject { ["ticket"] = ticket, ["token_types"] = tokenTypes };
        string bodyStr = body.ToJsonString(_jsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        AddCommonHeaders(request, device, bodyStr, "", "2", "bll8iq97cem8", "2.90.1", authCookie);

        try
        {
            using var response = await _httpClient.SendAsync(request, token);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(token));
            return result?["retcode"]?.GetValue<int>() == 0;
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
        }

        return false;
    }

    private async Task<Dictionary<string, string>?> GetWebQrStatusAndExtractCookiesAsync(string ticket,
        DeviceIdentity device, CancellationToken token)
    {
        string url = ApiEndpoints.PassportQueryQrLoginStatusUrl;
        var body = new JsonObject { ["ticket"] = ticket };
        string bodyStr = body.ToJsonString(_jsonOptions);

        for (int i = 0; i < 3; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
            AddCommonHeaders(request, device, bodyStr, "", "2", "bll8iq97cem8", "2.90.1");

            try
            {
                using var response = await _httpClient.SendAsync(request, token);
                var result = JsonNode.Parse(await response.Content.ReadAsStringAsync(token));

                if (result?["retcode"]?.GetValue<int>() == 0)
                {
                    string? status = result["data"]?["status"]?.GetValue<string>();
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
            catch (Exception) when (!token.IsCancellationRequested)
            {
            }

            await Task.Delay(1000, token);
        }

        return null;
    }

    #endregion
}
