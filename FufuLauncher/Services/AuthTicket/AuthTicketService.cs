/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Services.MiHoYo.Passport;
using FufuLauncher.Services.MiHoYo.Transport;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.AuthTicket;

public sealed class AuthTicketService : IAuthTicketService
{
    private readonly AccountManager _accountManager;

    public AuthTicketService(AccountManager accountManager)
    {
        _accountManager = accountManager;
    }

    public async Task<AuthTicketResult> CreateAuthTicketAsync(string accountId)
    {
        var result = new AuthTicketResult();

        try
        {
            var cookies = await _accountManager.LoadCookiesAsync(accountId);
            if (cookies == null || cookies.Count == 0)
            {
                result.ErrorMessage = "AuthTicket_NotLoggedIn".GetLocalized();
                return result;
            }

            cookies.TryGetValue("stoken", out var stoken);
            cookies.TryGetValue("mid", out var mid);

            string stuid = GetFirstNonEmpty(cookies, "stuid", "account_id", "ltuid_v2", "account_id_v2");

            if (string.IsNullOrEmpty(stoken))
            {
                result.ErrorMessage = "AuthTicket_SessionExpired".GetLocalized();
                return result;
            }

            if (string.IsNullOrEmpty(mid))
            {
                result.ErrorMessage = "AuthTicket_SessionExpired".GetLocalized();
                return result;
            }

            bool isOversea = accountId.StartsWith("os", StringComparison.OrdinalIgnoreCase);

            string url;
            string bodyJson;

            if (isOversea)
            {
                url = ApiEndpoints.CreateAuthTicketBySTokenUrl;
                var request = new AuthTicketRequestOversea
                {
                    BizName = "hk4e_global",
                    Mid = mid,
                    SToken = stoken
                };
                bodyJson = JsonSerializer.Serialize(request);
            }
            else
            {
                url = ApiEndpoints.CreateAuthTicketByGameBizUrl;

                if (string.IsNullOrEmpty(stuid) || !int.TryParse(stuid, CultureInfo.InvariantCulture, out var uid))
                {
                    result.ErrorMessage = "AuthTicket_SessionExpired".GetLocalized();
                    return result;
                }

                var request = new AuthTicketRequest
                {
                    GameBiz = "hk4e_cn",
                    Mid = mid,
                    SToken = stoken,
                    Uid = uid
                };
                bodyJson = JsonSerializer.Serialize(request);
            }

            using var httpClient = new HttpClient();
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");


            ApplyHoyoPlayHeaders(httpRequest, cookies, isOversea);

            var response = await httpClient.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            DebugLog.Info("AuthTicketService", $"[AuthTicket] Response: {responseBody}");

            if (!response.IsSuccessStatusCode)
            {
                result.ErrorMessage = string.Format("AuthTicket_HttpError_Format".GetLocalized(), (int)response.StatusCode);
                return result;
            }

            var apiResponse = JsonSerializer.Deserialize<MihoyoApiResponse<AuthTicketData>>(responseBody);
            if (apiResponse == null)
            {
                result.ErrorMessage = "AuthTicket_ParseError".GetLocalized();
                return result;
            }

            if (apiResponse.RetCode != 0)
            {
                result.ErrorMessage = string.Format("AuthTicket_ApiError_Format".GetLocalized(), apiResponse.RetCode, apiResponse.Message);
                return result;
            }

            if (apiResponse.Data == null || string.IsNullOrEmpty(apiResponse.Data.Ticket))
            {
                result.ErrorMessage = "AuthTicket_NoTicket".GetLocalized();
                return result;
            }

            result.Success = true;
            result.Ticket = apiResponse.Data.Ticket;
            DebugLog.Info("AuthTicketService", $"[AuthTicket] Got ticket (length: {result.Ticket.Length})");
            return result;
        }
        catch (Exception ex)
        {
            DebugLog.Info("AuthTicketService", $"[AuthTicket] Exception: {ex.Message}");
            result.ErrorMessage = string.Format("AuthTicket_RequestException_Format".GetLocalized(), ex.Message);
            return result;
        }
    }

    private static void ApplyHoyoPlayHeaders(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string> cookies,
        bool isOversea)
    {
        request.Headers.UserAgent.ParseAdd(UserAgents.HoyoPlay);
        request.Headers.Accept.ParseAdd("application/json");

        if (isOversea)
        {
            request.Headers.Add(HeaderNames.RpcAppId, AppIds.HoyoPlayOversea);
            request.Headers.Add(HeaderNames.RpcClientType, "3");
            request.Headers.Add(HeaderNames.RpcDeviceId, PassportDeviceId.Generate53());
        }
        else
        {
            request.Headers.Add(HeaderNames.RpcAppId, AppIds.GameCombo);
            request.Headers.Add(HeaderNames.RpcClientType, "3");

            string cookieStr = BbsRequestBuilder.BuildCookieString(cookies, BbsRequestBuilder.CookieMode.CnWeb);
            if (!string.IsNullOrEmpty(cookieStr))
                request.Headers.Add(HeaderNames.Cookie, cookieStr);
        }
    }

    private static string GetFirstNonEmpty(IReadOnlyDictionary<string, string> dict, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (dict.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                return value;
        }

        return string.Empty;
    }
}