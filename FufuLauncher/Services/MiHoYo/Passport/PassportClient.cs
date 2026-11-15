/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Models.MiHoYo.Passport;
using FufuLauncher.Services.MiHoYo.Networking;

namespace FufuLauncher.Services.MiHoYo.Passport;

public sealed class PassportClient
{
    private const string AigisHeader = "X-Rpc-Aigis";

    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<DeviceIdentity>> _getDevice;

    public PassportClient(AccountIdentityService identity) : this(SharedHttpClient, identity.GetDeviceAsync)
    {
    }

    internal PassportClient(HttpClient httpClient, Func<CancellationToken, Task<DeviceIdentity>> getDevice)
    {
        _httpClient = httpClient;
        _getDevice = getDevice;
    }

    internal Task<DeviceIdentity> GetDeviceAsync(CancellationToken token = default) => _getDevice(token);

    public async Task<(string? Aigis, PassportResponse<MobileCaptcha> Response)> CreateLoginCaptchaAsync(
        string mobile, string? aigis, CancellationToken token = default)
    {
        var data = new Dictionary<string, string>
        {
            ["area_code"] = PassportRsaCrypto.EncryptCn("+86"),
            ["mobile"] = PassportRsaCrypto.EncryptCn(mobile),
        };

        using var request = CreateCnJsonRequest(HttpMethod.Post, ApiEndpoints.AccountCreateLoginCaptchaUrl, data,
            aigis, await GetDeviceAsync(token).ConfigureAwait(false));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        PassportResponse<MobileCaptcha> body =
            await PassportHttpUtil.DeserializeAsync<MobileCaptcha>(response, token).ConfigureAwait(false);
        return (PassportHttpUtil.GetSingleHeader(response, AigisHeader), body);
    }

    public async Task<PassportResponse<LoginResult>> LoginByMobileCaptchaAsync(
        string actionType, string mobile, string captcha, string? aigis, CancellationToken token = default)
    {
        var data = new Dictionary<string, string>
        {
            ["area_code"] = PassportRsaCrypto.EncryptCn("+86"),
            ["action_type"] = actionType,
            ["captcha"] = captcha,
            ["mobile"] = PassportRsaCrypto.EncryptCn(mobile),
        };

        using var request = CreateCnJsonRequest(HttpMethod.Post, ApiEndpoints.AccountLoginByMobileCaptchaUrl, data,
            aigis, await GetDeviceAsync(token).ConfigureAwait(false));
        using HttpResponseMessage response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        return await PassportHttpUtil.DeserializeAsync<LoginResult>(response, token).ConfigureAwait(false);
    }

    internal async Task<Dictionary<string, string>?> ExchangeWebCookiesAsync(string stoken, string mid, string aid,
        DeviceIdentity device, CancellationToken token = default)
    {
        string cookie = $"mid={mid}; stoken={stoken}; stuid={aid}";
        var account = await ExchangeAsync<UidCookieToken>(ApiEndpoints.GetCookieAccountInfoBySTokenUrl, cookie,
            device, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(account?.CookieToken) ||
            (!string.IsNullOrEmpty(account.Uid) && account.Uid != aid))
            return null;

        var login = await ExchangeAsync<LTokenWrapper>(ApiEndpoints.GetLTokenBySTokenUrl, cookie,
            device, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(login?.LToken)) return null;

        var cookies = new Dictionary<string, string>();
        AddToken(login.LToken, "ltoken", "ltuid", "ltmid_v2");
        AddToken(account.CookieToken, "cookie_token", "account_id", "account_mid_v2");
        return cookies;

        void AddToken(string value, string tokenName, string uidName, string midName)
        {
            string suffix = value.StartsWith("v2_", StringComparison.Ordinal) ? "_v2" : "";
            cookies[tokenName + suffix] = value;
            cookies[uidName + suffix] = aid;
            if (suffix.Length > 0) cookies[midName] = mid;
        }
    }

    private async Task<T?> ExchangeAsync<T>(string url, string cookie, DeviceIdentity device, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyCnHeaders(request, body: string.Empty, aigis: null, cookie, device);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Headers.Contains(AigisHeader)) return default;
        var body = await PassportHttpUtil.DeserializeAsync<T>(response, token).ConfigureAwait(false);
        return body.IsSuccess ? body.Data : default;
    }

    private static HttpRequestMessage CreateCnJsonRequest(HttpMethod method, string url, Dictionary<string, string> data,
        string? aigis, DeviceIdentity device)
    {
        string body = JsonSerializer.Serialize(data);
        var request = new HttpRequestMessage(method, url);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        ApplyCnHeaders(request, body, aigis, cookie: null, device);
        return request;
    }

    private static void ApplyCnHeaders(HttpRequestMessage request, string body, string? aigis, string? cookie,
        DeviceIdentity device)
    {
        request.Headers.TryAddWithoutValidation(HeaderNames.UserAgent, UserAgents.WindowsBbs295);
        request.Headers.TryAddWithoutValidation(HeaderNames.Accept, "application/json");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-cn");
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Cookie, cookie);
        }

        request.Headers.TryAddWithoutValidation(HeaderNames.RpcAigis, aigis ?? string.Empty);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcAppId, AppIds.Passport);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcAppVersion, HeaderVersions.BbsCn295);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcClientType, ClientTypes.AndroidApp);
        MiHoYoHeaderFactory.ApplyDeviceIdentityHeaders(request, device, device.DeviceId);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcGameBiz, GameBizValues.BbsCn);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcSdkVersion, HeaderVersions.PassportSdkVersion);
        request.Headers.TryAddWithoutValidation(HeaderNames.DS,
            MiHoYoHeaderFactory.CalculateDsGen2(HeaderSalts.PassportProd, body));
    }
}
