/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.MiHoYo;

namespace FufuLauncher.Services;

public class CommunityCheckinService : ICommunityCheckinService
{
    private readonly AccountIdentityService _accountIdentityService;
    private readonly HttpClient _httpClient;

    public CommunityCheckinService(AccountIdentityService accountIdentityService)
        : this(accountIdentityService, new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = TimeSpan.FromSeconds(30) })
    {
    }

    internal CommunityCheckinService(AccountIdentityService accountIdentityService, HttpClient httpClient)
    {
        _accountIdentityService = accountIdentityService;
        _httpClient = httpClient;
    }

    public async Task<CheckinTypeResult> ExecuteCheckinAsync(AccountCredentials account,
        Func<Task<string?>> refreshCookie)
    {
        var result = new CheckinTypeResult { TypeName = "CheckinCommunity_Title".GetLocalized(), Executed = true };

        if (string.IsNullOrEmpty(account.Stoken) || string.IsNullOrEmpty(account.Mid))
        {
            result.FailCount = 1;
            result.Message = "CheckinCommunity_MissingToken".GetLocalized();
            return result;
        }

        try
        {
            var identity = await _accountIdentityService.BuildAsync(account.ConfigPath);
            var stokenCookie = account.GetStokenCookie();
            var (code, message) = await SignAsync(identity, stokenCookie);
            if (code is int retcode && MiyousheCheckinGame.IsLoginExpired(retcode))
            {
                if (await IsAlreadySignedAsync(identity.Cookies))
                {
                    result.SkippedCount = 1;
                    result.Message = "CheckinCommunity_SignDoneSkipped".GetLocalized();
                    result.Details.Add(result.Message);
                    return result;
                }

                if (await refreshCookie() != null)
                {
                    var refreshedCookie = account.GetStokenCookie();
                    if (refreshedCookie != stokenCookie)
                    {
                        identity = await _accountIdentityService.BuildAsync(account.ConfigPath);
                        (code, message) = await SignAsync(identity, refreshedCookie);
                    }
                }
            }

            result.Success = code == 0;
            result.Message = code switch
            {
                0 => "CheckinCommunity_GenshinSignSuccess".GetLocalized(),
                null => "CheckinCommunity_GenshinSignRequestFailed".GetLocalized(),
                1028 or 1034 => "CheckinCommunity_GenshinSignCaptcha".GetLocalized(),
                int error when MiyousheCheckinGame.IsLoginExpired(error) =>
                    FormatSignFailure(error, "Checkin_LoginExpired".GetLocalized()),
                int error => FormatSignFailure(error, message)
            };
        }
        catch (Exception ex)
        {
            result.Message = string.Format("CheckinCommunity_GenshinSignException".GetLocalized(), ex.Message);
            DebugLog.Info(nameof(CommunityCheckinService), result.Message);
        }

        if (result.Success) result.SuccessCount = 1;
        else result.FailCount = 1;
        result.Details.Add(result.Message);
        return result;
    }

    private static string FormatSignFailure(int code, string? message) =>
        string.Format("CheckinCommunity_GenshinSignFailed".GetLocalized(), $"[{code}] {message}".TrimEnd());

    private async Task<bool> IsAlreadySignedAsync(IReadOnlyDictionary<string, string> cookies)
    {
        // The website status endpoint accepts v2 values under the original web cookie names.
        var pairs = new List<string>();
        foreach (var name in new[] { "ltuid", "ltoken", "account_id", "cookie_token" })
        {
            var value = cookies.GetValueOrDefault(name + "_v2");
            if (string.IsNullOrEmpty(value)) value = cookies.GetValueOrDefault(name);
            if (!string.IsNullOrEmpty(value)) pairs.Add($"{name}={value}");
        }

        if (pairs.Count == 0) return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GenshinApiEndpoints.BbsSignStatusUrl);
            request.Headers.Referrer = new Uri(GenshinApiEndpoints.GenshinTavernUrl);
            request.Headers.Add(HeaderNames.Origin, "https://www.miyoushe.com");
            request.Headers.UserAgent.ParseAdd(UserAgents.WindowsBbs295);
            request.Headers.Add(HeaderNames.RpcClientType, ClientTypes.Web);
            request.Headers.Add(HeaderNames.Cookie, string.Join("; ", pairs));
            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   root.TryGetProperty("retcode", out var retcode) && retcode.ValueKind == JsonValueKind.Number &&
                   retcode.TryGetInt32(out var code) && code == 0 &&
                   root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                   data.TryGetProperty("is_signed", out var signed) && signed.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            DebugLog.Info(nameof(CommunityCheckinService), $"Unable to read community check-in status: {ex.Message}");
            return false;
        }
    }

    private async Task<(int? Code, string? Message)> SignAsync(
        AccountContext identity, string stokenCookie)
    {
        var device = identity.Device;
        var body = JsonSerializer.Serialize(new { gids = GenshinApiEndpoints.GenshinCommunityId });
        using var request = new HttpRequestMessage(HttpMethod.Post, GenshinApiEndpoints.BbsSignUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Referrer = new Uri(ApiEndpoints.AppMihoyoBase);
        request.Headers.UserAgent.ParseAdd(identity.UserAgent.OkHttp);
        request.Headers.Add(HeaderNames.Cookie, stokenCookie);
        request.Headers.Add(HeaderNames.DS, SignatureHelper.GetDsX6(body: body));
        request.Headers.Add(HeaderNames.RpcClientType, "2");
        request.Headers.Add(HeaderNames.RpcAppVersion, HeaderVersions.BbsCn);
        request.Headers.Add(HeaderNames.RpcChannel, Channels.MiyousheLuodi);
        request.Headers.Add(HeaderNames.RpcDeviceId, device.BbsDeviceId);
        request.Headers.Add(HeaderNames.RpcDeviceFp, device.DeviceFp);
        request.Headers.Add(HeaderNames.RpcDeviceName, Uri.EscapeDataString(device.DeviceName));
        request.Headers.Add(HeaderNames.RpcDeviceModel, Uri.EscapeDataString(device.Model));
        request.Headers.Add(HeaderNames.RpcSysVersion, device.SysVersion);
        request.Headers.Add(HeaderNames.RpcH265Supported, "1");
        request.Headers.Add(HeaderNames.RpcVerifyKey, GenshinApiEndpoints.PassportAppId);
        request.Headers.Add(HeaderNames.RpcCsmSource, "discussion");

        using var response = await _httpClient.SendAsync(request);
        if (response.Headers.Contains(HeaderNames.RpcAigis)) return (1034, null);

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("retcode", out var retcode) ||
            retcode.ValueKind != JsonValueKind.Number ||
            !retcode.TryGetInt32(out var code))
            return (null, null);

        var message = root.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return (code, message);
    }
}
