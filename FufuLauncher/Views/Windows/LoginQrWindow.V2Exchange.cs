/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using System.Text.Json.Nodes;
using FufuLauncher.Constants;
using FufuLauncher.Services.MiHoYo.Passport;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow
{
    #region 扫码换取

    private async Task<Dictionary<string, string>?> ExchangeV2TokensAsync(string stoken, string mid, string aid)
    {
        try
        {
            UpdateStatus("正在获取完整登录凭证...", true);
            var finalCookies = new Dictionary<string, string>
            {
                ["stoken"] = stoken,
                ["mid"] = mid,
                ["account_id"] = aid,
                ["ltuid"] = aid
            };

            var passportClient = App.GetService<PassportClient>();

            string cookieToken = await passportClient.GetCookieAccountInfoBySTokenAsync(stoken, mid, aid);
            if (!string.IsNullOrEmpty(cookieToken))
            {
                finalCookies["cookie_token"] = cookieToken;
            }

            string ltoken = await passportClient.GetLTokenBySTokenAsync(stoken, mid, aid);
            if (!string.IsNullOrEmpty(ltoken))
            {
                finalCookies["ltoken"] = ltoken;
            }

            string webTicket = await CreateWebQrCodeAsync();
            if (string.IsNullOrEmpty(webTicket))
            {
                UpdateStatus("无法创建验证凭据");
                return null;
            }

            string authCookie = $"stoken={stoken}; mid={mid}";

            bool scanResult = await SimulateAppActionAsync(ApiEndpoints.PassportScanQrLoginUrl, webTicket, authCookie);
            if (!scanResult)
            {
                UpdateStatus("扫描请求被拒绝");
                return null;
            }

            await Task.Delay(1000);

            bool confirmResult =
                await SimulateAppActionAsync(ApiEndpoints.PassportConfirmQrLoginUrl, webTicket, authCookie);
            if (!confirmResult)
            {
                UpdateStatus("请求被拒绝");
                return null;
            }

            var v2Cookies = await GetWebQrStatusAndExtractCookiesAsync(webTicket);
            if (v2Cookies != null && v2Cookies.Count > 0)
            {
                foreach (var kvp in v2Cookies)
                {
                    finalCookies[kvp.Key] = kvp.Value;
                }

                finalCookies["stoken"] = stoken;

                return finalCookies;
            }
            else
            {
                UpdateStatus("未能从响应头提取出完整Cookie");
                return null;
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"凭证换取异常: {ex.Message}");
            return null;
        }
    }

    private async Task ProcessAndExchangeV2TokensAsync(JsonNode dataNode)
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
            UpdateStatus("提取失败，请重试", false);
            return;
        }

        var cookies = await ExchangeV2TokensAsync(stoken, mid, aid);
        if (cookies != null)
        {
            OnLoginSuccess(cookies, "cn");
        }
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
            if (result["retcode"]?.GetValue<int>() == 0) return result["data"]["ticket"]?.GetValue<string>();
        }
        catch
        {
        }

        return null!;
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

    #endregion
}