/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net.Http;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Helpers;
using FufuLauncher.Models.CycleChallenge;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.MiHoYo;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Services.MiHoYo.Transport;

namespace FufuLauncher.Services.CycleChallenge;


public sealed record RawChallengeResponse(
    bool Success,
    ChallengeErrorKind Kind,
    int Retcode,
    string Message,
    string? DataJson,
    string RawJson);







public sealed class GameRecordApiClient
{
    private const string CnGameRecordBase = ApiEndpoints.CnGameRecordApiBase;
    private const string OsGameRecordBase = ApiEndpoints.OverseaGameRecordApiBase;
    private const string OsEventBase = ApiEndpoints.OverseaEventGameRecordApiBase;

    private const string ToolVersionCn = "v6.6.1-gr-cn";
    private const string PageCn = "v6.6.1-gr-cn_#/ys";

    private static readonly HttpClient HttpClient = new(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private readonly AccountIdentityService _identityService;
    private readonly GeetestService _geetestService;
    private readonly AccountManager _accountManager;
    private readonly string _osDeviceId = Guid.NewGuid().ToString("D");

    public GameRecordApiClient(
        AccountIdentityService identityService,
        GeetestService geetestService,
        AccountManager accountManager)
    {
        _identityService = identityService;
        _geetestService = geetestService;
        _accountManager = accountManager;
    }

    public Task<RawChallengeResponse> GetSpiralAbyssAsync(SelectedGameRole role, int scheduleType, CancellationToken token) =>
        GetAsync(role, ApiEndpoints.GameRecordSpiralAbyssEndpoint, $"{RoleQuery(role)}&schedule_type={scheduleType}", ChallengePath(ApiEndpoints.GameRecordSpiralAbyssEndpoint), token);

    public Task<RawChallengeResponse> GetRoleCombatAsync(SelectedGameRole role, CancellationToken token) =>
        GetAsync(role, ApiEndpoints.GameRecordRoleCombatEndpoint, $"{RoleQuery(role)}&need_detail=true&active=1", ChallengePath(ApiEndpoints.GameRecordRoleCombatEndpoint), token);

    public Task<RawChallengeResponse> GetHardChallengeAsync(SelectedGameRole role, CancellationToken token) =>
        GetAsync(role, ApiEndpoints.GameRecordHardChallengeEndpoint, $"{RoleQuery(role)}&need_detail=true", ChallengePath(ApiEndpoints.GameRecordHardChallengeEndpoint), token);

    private static string RoleQuery(SelectedGameRole role) =>
        $"role_id={Uri.EscapeDataString(role.Role.game_uid)}&server={Uri.EscapeDataString(role.Role.region)}";

    private static string ChallengePath(string apiName) => ApiEndpoints.GetGameRecordChallengePath(apiName);

    private static bool IsOversea(SelectedGameRole role) =>
        string.Equals(role.ServerType, "os", StringComparison.OrdinalIgnoreCase);

    private async Task<RawChallengeResponse> GetAsync(
        SelectedGameRole role, string apiName, string rawQuery, string challengePath, CancellationToken token)
    {
        AccountContext context;
        try
        {
            context = await _identityService.BuildAsync(role.AccountId, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failure(ChallengeErrorKind.NetworkError, -1, ex.Message);
        }

        if (context.Cookies.Count == 0)
        {
            return Failure(ChallengeErrorKind.LoginExpired, -1, "登录信息无效，请重新登录社区账号。");
        }

        var isOversea = IsOversea(role);
        var response = await SendWithFallbackAsync(context, isOversea, apiName, rawQuery, challenge: null, token)
            .ConfigureAwait(false);

        
        if (response.Kind == ChallengeErrorKind.RiskControlRequired && !isOversea)
        {
            try
            {
                var challenge = await _geetestService.TryVerifyForDailyNoteAsync(context, challengePath)
                    .ConfigureAwait(false);
                if (!string.IsNullOrEmpty(challenge))
                {
                    response = await SendWithFallbackAsync(context, isOversea, apiName, rawQuery, challenge, token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                
            }
        }

        
        if (response.Kind == ChallengeErrorKind.LoginExpired && !isOversea)
        {
            if (await TryRefreshCookiesAsync(role.AccountId, token).ConfigureAwait(false))
            {
                try
                {
                    context = await _identityService.BuildAsync(role.AccountId, token).ConfigureAwait(false);
                    if (context.Cookies.Count > 0)
                    {
                        response = await SendWithFallbackAsync(context, isOversea, apiName, rawQuery, challenge: null, token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    
                }
            }
        }

        return response;
    }

    
    private async Task<RawChallengeResponse> SendWithFallbackAsync(
        AccountContext context, bool isOversea, string apiName, string rawQuery, string? challenge, CancellationToken token)
    {
        var bases = GetCandidateBases(isOversea, apiName);
        RawChallengeResponse? last = null;

        foreach (var baseUrl in bases)
        {
            var url = $"{baseUrl}/{apiName}?{rawQuery}";
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (isOversea)
                {
                    ApplyOverseaHeaders(request, context, rawQuery, challenge);
                }
                else
                {
                    ApplyChineseHeaders(request, context, rawQuery, challenge);
                }

                using var httpResponse = await HttpClient.SendAsync(request, token).ConfigureAwait(false);
                var json = await httpResponse.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                last = Parse(json, (int)httpResponse.StatusCode);
                LogDebug($"{apiName} host={new Uri(url).Host} status={(int)httpResponse.StatusCode} " +
                         $"retcode={last.Retcode} kind={last.Kind} dataLen={last.DataJson?.Length ?? 0} msg={last.Message}");
                if (last.Success || last.Kind is ChallengeErrorKind.LoginExpired
                    or ChallengeErrorKind.RiskControlRequired or ChallengeErrorKind.RateLimited
                    or ChallengeErrorKind.NotParticipated or ChallengeErrorKind.NotUnlocked)
                {
                    return last;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = Failure(ChallengeErrorKind.NetworkError, -1, ex.Message);
            }
        }

        return last ?? Failure(ChallengeErrorKind.NetworkError, -1, "请求失败。");
    }

    private static string[] GetCandidateBases(bool isOversea, string apiName)
    {
        if (!isOversea)
        {
            return [CnGameRecordBase];
        }

        return apiName == "spiralAbyss"
            ? [OsGameRecordBase]
            : [OsEventBase, OsGameRecordBase];
    }

    private static string SortQuery(string rawQuery) =>
        string.Join("&", rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(part => part, StringComparer.Ordinal));

    private void ApplyChineseHeaders(HttpRequestMessage request, AccountContext context, string rawQuery, string? challenge)
    {
        string cookie = BbsRequestBuilder.BuildCookieString(context.Cookies, BbsRequestBuilder.CookieMode.Cookie);
        MiHoYoHeaderFactory.ApplyGameRecordHeaders(request, new GameRecordHeaderOptions(
            AppVersion: HeaderVersions.MobileCnLogin,
            UserAgent: context.UserAgent.Mobile,
            DeviceId: context.Device.BbsDeviceId,
            DeviceFp: context.Device.DeviceFp,
            DeviceName: Uri.EscapeDataString(context.Device.DeviceName),
            SysVersion: context.Device.SysVersion,
            Cookie: cookie,
            DsSalt: HeaderSalts.CnX4,
            SortedQuery: SortQuery(rawQuery),
            Challenge: challenge,
            ToolVersion: ToolVersionCn,
            Page: PageCn));
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
    }

    private void ApplyOverseaHeaders(HttpRequestMessage request, AccountContext context, string rawQuery, string? challenge)
    {
        string cookie = BbsRequestBuilder.BuildCookieString(context.Cookies, BbsRequestBuilder.CookieMode.Cookie);
        request.Headers.TryAddWithoutValidation(HeaderNames.UserAgent, UserAgents.WindowsBbsOversea254);
        request.Headers.TryAddWithoutValidation(HeaderNames.Accept, "application/json");
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcAppVersion, HeaderVersions.BbsOs254);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcClientType, ClientTypes.Other);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcLanguage, "zh-cn");
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcDeviceId, _osDeviceId);
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.Cookie, cookie);
        }

        if (!string.IsNullOrEmpty(challenge))
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.RpcChallenge, challenge);
        }

        request.Headers.TryAddWithoutValidation(
            HeaderNames.DS, MiHoYoHeaderFactory.CalculateDs2(HeaderSalts.OsGameRecord, SortQuery(rawQuery)));
    }

    private async Task<bool> TryRefreshCookiesAsync(string accountId, CancellationToken token)
    {
        try
        {
            var cookies = await _accountManager.LoadCookiesAsync(accountId).ConfigureAwait(false);
            if (cookies is null || cookies.Count == 0)
            {
                return false;
            }

            using var refreshService = new TokenRefreshService();
            var refreshed = await refreshService.RefreshCookieAsync(cookies, forceRefresh: true).ConfigureAwait(false);
            if (refreshed is null || refreshed.Count == 0)
            {
                return false;
            }

            await _accountManager.UpdateCookiesAsync(accountId, refreshed).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (int Retcode, string Message, string? DataJson) ReadEnvelope(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        int retcode = root.TryGetProperty("retcode", out var code) ? code.GetInt32() : -1;
        string message = root.TryGetProperty("message", out var msg) ? msg.GetString() ?? string.Empty : string.Empty;
        string? dataJson = null;
        if (root.TryGetProperty("data", out var data) && data.ValueKind is not JsonValueKind.Null)
        {
            dataJson = data.GetRawText();
        }

        return (retcode, message, dataJson);
    }

    private static RawChallengeResponse Parse(string json, int statusCode)
    {
        try
        {
            var (retcode, message, dataJson) = ReadEnvelope(json);
            var kind = retcode switch
            {
                0 => ChallengeErrorKind.Success,
                -100 or 10001 => ChallengeErrorKind.LoginExpired,
                1034 => ChallengeErrorKind.RiskControlRequired,
                -110 => ChallengeErrorKind.RateLimited,
                -1 => ChallengeErrorKind.NotParticipated,
                _ => ChallengeErrorKind.ServerError,
            };

            return new RawChallengeResponse(
                kind == ChallengeErrorKind.Success, kind, retcode, message, dataJson, json);
        }
        catch (JsonException)
        {
            
            string snippet = json.Length > 60 ? json[..60] : json;
            snippet = snippet.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return Failure(ChallengeErrorKind.ServerError, -1, $"HTTP {statusCode} 非 JSON 响应：{snippet}");
        }
    }

    private static RawChallengeResponse Failure(ChallengeErrorKind kind, int retcode, string message) =>
        new(false, kind, retcode, message, null, string.Empty);

    
    private static void LogDebug(string line)
    {
#if DEBUG
        try
        {
            string path = Path.Combine(AppPaths.DataDir, "cyclechallenge-api.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
        }
        catch
        {
            
        }
#endif
    }
}
