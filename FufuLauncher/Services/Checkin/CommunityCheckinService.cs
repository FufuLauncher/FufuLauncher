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
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services.MiHoYo;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Services.Miyoushe;
using FufuLauncher.Views;

namespace FufuLauncher.Services;

public class CommunityCheckinService : ICommunityCheckinService
{
    private const int AlreadySignedCode = 1008;
    private readonly AccountIdentityService _accountIdentityService;
    private readonly HttpClient _httpClient;
    private readonly ILocalSettingsService _settings;
    private readonly Func<JsonElement, string?, Task<JsonElement?>> _showCaptcha;

    public CommunityCheckinService(AccountIdentityService accountIdentityService, ILocalSettingsService settings)
        : this(accountIdentityService, new HttpClient(new HttpClientHandler
        {
            UseCookies = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        }) { Timeout = TimeSpan.FromSeconds(30) }, settings, MiyousheVerificationWindow.ShowOnMainThreadAsync)
    {
    }

    internal CommunityCheckinService(AccountIdentityService accountIdentityService, HttpClient httpClient,
        ILocalSettingsService settings, Func<JsonElement, string?, Task<JsonElement?>> showCaptcha)
    {
        _accountIdentityService = accountIdentityService;
        _httpClient = httpClient;
        _settings = settings;
        _showCaptcha = showCaptcha;
    }

    public async Task<CheckinTypeResult> ExecuteCheckinAsync(AccountCredentials account)
    {
        var result = new CheckinTypeResult { TypeName = "CheckinCommunity_Title".GetLocalized(), Executed = true };

        try
        {
            var identity = await _accountIdentityService.BuildAsync(account.ConfigPath);
            string? stoken = identity.Stoken;
            string mid = identity.Mid;
            if (string.IsNullOrWhiteSpace(stoken) || string.IsNullOrWhiteSpace(mid))
            {
                result.FailCount = 1;
                result.Message = "CheckinCommunity_LoginRequired".GetLocalized();
                return result;
            }

            string stokenCookie = $"stuid={account.Stuid};stoken={stoken};mid={mid}";
            var response = await SignAsync(identity, stokenCookie);
            if (response.Code is 1028 or 1034)
                response = await VerifyAndRetryAsync(identity, stokenCookie, response.Aigis);

            var (code, message, _) = response;
            if (code is int retcode && MiyousheCheckinGame.IsLoginExpired(retcode) &&
                await IsAlreadySignedAsync(identity.Cookies))
                code = AlreadySignedCode;

            result.Success = code == 0;
            result.Message = code switch
            {
                0 or AlreadySignedCode => "",
                null => "Status_RequestFailed".GetLocalized(),
                1028 or 1034 => message ?? "CheckinCommunity_VerificationFailed".GetLocalized(),
                int error when MiyousheCheckinGame.IsLoginExpired(error) =>
                    FormatSignFailure(error, "CheckinCommunity_LoginRequired".GetLocalized()),
                int error => FormatSignFailure(error, message)
            };

            if (code == AlreadySignedCode) result.SkippedCount = 1;
        }
        catch (Exception ex)
        {
            result.Message = ex.Message;
            DebugLog.Info(nameof(CommunityCheckinService), result.Message);
        }

        if (result.Success) result.SuccessCount = 1;
        else if (result.SkippedCount == 0) result.FailCount = 1;
        if (result.Message.Length > 0) result.Details.Add(result.Message);
        return result;
    }

    private static string FormatSignFailure(int code, string? message) =>
        $"[{code}] {message}".TrimEnd();

    private async Task<bool> IsAlreadySignedAsync(IReadOnlyDictionary<string, string> cookies)
    {
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
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.BbsSignStatusUrl);
            request.Headers.Referrer = new Uri(ApiEndpoints.MiyousheGenshinTavernUrl);
            request.Headers.Add(HeaderNames.Origin, ApiEndpoints.MiyousheWebBase);
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

    private async Task<SignResponse> VerifyAndRetryAsync(AccountContext identity, string cookie, string? rawAigis)
    {
        var disabled = await _settings.ReadSettingAsync("IsCaptchaPopupDisabled");
        if (bool.TryParse(disabled?.ToString(), out bool isDisabled) && isDisabled)
            return new(1034, "CheckinCommunity_VerificationDisabled".GetLocalized());

        try
        {
            string header, proof;
            if (!string.IsNullOrEmpty(rawAigis))
            {
                var aigis = MiyousheAigisChallenge.Parse(rawAigis);
                if (aigis == null) return new(1034, "CheckinCommunity_VerificationUnavailable".GetLocalized());
                var answer = await _showCaptcha(aigis.Parameters, aigis.SessionId);
                if (answer == null) return new(1034, "CheckinCommunity_VerificationCancelled".GetLocalized());
                header = HeaderNames.RpcAigis;
                proof = aigis.CreateProof(answer.Value);
            }
            else
            {
                var parameters = await RequestVerificationAsync(identity, cookie, ApiEndpoints.BbsCreateVerificationUrl);
                if (parameters.Text("gt").Length == 0)
                    return new(1034, "CheckinCommunity_VerificationUnavailable".GetLocalized());
                var answer = await _showCaptcha(parameters, null);
                if (answer == null) return new(1034, "CheckinCommunity_VerificationCancelled".GetLocalized());
                var verified = await RequestVerificationAsync(identity, cookie,
                    ApiEndpoints.BbsVerifyVerificationUrl, answer.Value.GetRawText());
                header = HeaderNames.RpcChallenge;
                proof = verified.Text("challenge");
                if (proof.Length == 0) return new(1034, "CheckinCommunity_VerificationFailed".GetLocalized());
            }

            return await SignAsync(identity, cookie, header, proof);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
                                   or InvalidOperationException)
        {
            DebugLog.Info(nameof(CommunityCheckinService), $"Verification interrupted ({ex.GetType().Name})");
            return new(1034, "CheckinCommunity_VerificationFailed".GetLocalized());
        }
    }

    private async Task<JsonElement> RequestVerificationAsync(AccountContext identity, string cookie, string url,
        string? body = null)
    {
        using var request = CreateAppRequest(identity, cookie, url, body);
        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var retcode = json.RootElement.Get("retcode");
        return retcode.ValueKind == JsonValueKind.Number && retcode.TryGetInt32(out int code) && code == 0
            ? json.RootElement.Get("data").Clone()
            : default;
    }

    private static HttpRequestMessage CreateAppRequest(AccountContext identity, string cookie, string url, string? body)
    {
        var uri = new Uri(url);
        var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, uri);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Referrer = new Uri(ApiEndpoints.AppMihoyoBase);
        request.Headers.UserAgent.ParseAdd(identity.UserAgent.OkHttp);
        request.Headers.Add(HeaderNames.Cookie, cookie);
        request.Headers.Add(HeaderNames.DS, SignatureHelper.GetDsX6(uri.Query.TrimStart('?'), body ?? ""));
        request.Headers.Add(HeaderNames.RpcClientType, "2");
        request.Headers.Add(HeaderNames.RpcAppVersion, HeaderVersions.BbsCn);
        request.Headers.Add(HeaderNames.RpcChannel, Channels.MiyousheLuodi);
        MiHoYoHeaderFactory.ApplyDeviceIdentityHeaders(request, identity.Device, identity.Device.BbsDeviceId);
        request.Headers.Add(HeaderNames.RpcH265Supported, "1");
        request.Headers.Add(HeaderNames.RpcVerifyKey, GenshinApiEndpoints.PassportAppId);
        return request;
    }

    private async Task<SignResponse> SignAsync(AccountContext identity, string cookie,
        string? verificationHeader = null, string? proof = null)
    {
        var body = JsonSerializer.Serialize(new { gids = GenshinApiEndpoints.GenshinCommunityId });
        using var request = CreateAppRequest(identity, cookie, ApiEndpoints.BbsSignUrl, body);
        request.Headers.Add(HeaderNames.RpcCsmSource, "discussion");
        if (verificationHeader != null) request.Headers.Add(verificationHeader, proof);

        using var response = await _httpClient.SendAsync(request);
        if (response.Headers.TryGetValues(HeaderNames.RpcAigis, out var aigis))
            return new(1034, null, aigis.FirstOrDefault());

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("retcode", out var retcode) ||
            retcode.ValueKind != JsonValueKind.Number ||
            !retcode.TryGetInt32(out var code))
            return new(null, null);

        var message = root.TryGetProperty("message", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
        return new(code, code is 1028 or 1034 ? null : message);
    }

    private sealed record SignResponse(int? Code, string? Message, string? Aigis = null);
}
