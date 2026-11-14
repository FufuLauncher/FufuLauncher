/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net.Http;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.CycleChallenge;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.MiHoYo;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Services.MiHoYo.Passport;
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
    private readonly PassportClient _passportClient;
    private readonly OverseaPassportClient _overseaPassportClient;
    private readonly string _osDeviceId = Guid.NewGuid().ToString("D");

    public GameRecordApiClient(
        AccountIdentityService identityService,
        GeetestService geetestService,
        AccountManager accountManager,
        PassportClient passportClient,
        OverseaPassportClient overseaPassportClient)
    {
        _identityService = identityService;
        _geetestService = geetestService;
        _accountManager = accountManager;
        _passportClient = passportClient;
        _overseaPassportClient = overseaPassportClient;
    }

    public Task<RawChallengeResponse> GetSpiralAbyssAsync(SelectedGameRole role, int scheduleType,
        CancellationToken token) =>
        GetAsync(role, ApiEndpoints.GameRecordSpiralAbyssEndpoint, $"{RoleQuery(role)}&schedule_type={scheduleType}",
            ChallengePath(ApiEndpoints.GameRecordSpiralAbyssEndpoint), token);

    public Task<RawChallengeResponse> GetRoleCombatAsync(SelectedGameRole role, CancellationToken token) =>
        GetAsync(role, ApiEndpoints.GameRecordRoleCombatEndpoint, $"{RoleQuery(role)}&need_detail=true&active=1",
            ChallengePath(ApiEndpoints.GameRecordRoleCombatEndpoint), token);

    public Task<RawChallengeResponse> GetHardChallengeAsync(SelectedGameRole role, CancellationToken token) =>
        GetAsync(role, ApiEndpoints.GameRecordHardChallengeEndpoint, $"{RoleQuery(role)}&need_detail=true",
            ChallengePath(ApiEndpoints.GameRecordHardChallengeEndpoint), token);

    public async Task<(bool Ok, string ErrorMessage, List<HutaoSimpleAvatar> Avatars)> GetCharactersForUidAsync(
        string uid,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return (false, string.Empty, new List<HutaoSimpleAvatar>());
        }

        var cleanUid = uid.Trim();
        var activeId = _accountManager.ActiveAccountId;
        var accounts = _accountManager.GetAllAccounts()
            .OrderByDescending(a => string.Equals(a.Id, activeId, StringComparison.Ordinal))
            .ToList();

        var candidates = new List<(AccountEntry Account, GameRoleInfo Role)>();

        foreach (var acc in accounts)
        {
            var roles = GameRoleSelection.Filter(acc.GameRoles ?? [], acc.ServerType);
            var role = roles.FirstOrDefault(r => string.Equals(r.game_uid, cleanUid, StringComparison.Ordinal));
            if (role != null)
            {
                candidates.Add((acc, role));
            }
        }

        if (candidates.Count == 0)
        {
            return (false, string.Empty, new List<HutaoSimpleAvatar>());
        }

        GameRoleInfo? matchedRole = null;
        SelectedGameRole? selectedRole = null;
        RawChallengeResponse? listResp = null;
        var lastKind = ChallengeErrorKind.ServerError;
        var lastError = string.Empty;

        foreach (var candidate in candidates)
        {
            var cookies = await _accountManager.LoadCookiesAsync(candidate.Account.Id).ConfigureAwait(false);
            if (cookies == null || cookies.Count == 0)
            {
                lastKind = ChallengeErrorKind.LoginExpired;
                lastError = "HutaoSync_ErrMiHoYoLoginExpired".GetLocalized();
                continue;
            }

            var candidateSelectedRole = new SelectedGameRole(
                candidate.Account.Id,
                candidate.Account.ServerType,
                candidate.Role,
                cookies);
            var listBody = JsonSerializer.Serialize(new
            {
                role_id = candidate.Role.game_uid,
                server = candidate.Role.region
            });

            var resp = await PostAsync(
                candidateSelectedRole,
                ApiEndpoints.GameRecordCharacterListEndpoint,
                listBody,
                ChallengePath(ApiEndpoints.GameRecordCharacterListEndpoint),
                token).ConfigureAwait(false);

            if (resp.Success && !string.IsNullOrWhiteSpace(resp.DataJson))
            {
                matchedRole = candidate.Role;
                selectedRole = candidateSelectedRole;
                listResp = resp;
                break;
            }

            lastKind = resp.Kind;
            lastError = resp.Message;
            if (resp.Kind != ChallengeErrorKind.LoginExpired)
            {
                return (false, resp.Message, new List<HutaoSimpleAvatar>());
            }
        }

        if (listResp == null || matchedRole == null || selectedRole == null)
        {
            var failureMessage = lastKind == ChallengeErrorKind.LoginExpired
                ? "HutaoSync_ErrMiHoYoLoginExpired".GetLocalized()
                : lastError;
            return (false, failureMessage, new List<HutaoSimpleAvatar>());
        }

        var fallbackList = new List<HutaoSimpleAvatar>();
        var avatarIds = new List<int>();
        try
        {
            using var listDoc = JsonDocument.Parse(listResp.DataJson);
            if (listDoc.RootElement.TryGetProperty("list", out var listArr) && listArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in listArr.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var avatarId) || avatarId <= 0)
                    {
                        continue;
                    }

                    avatarIds.Add(avatarId);
                    var weaponId = 0;
                    if (item.TryGetProperty("weapon", out var wp) && wp.ValueKind == JsonValueKind.Object &&
                        wp.TryGetProperty("id", out var wpIdProp))
                    {
                        wpIdProp.TryGetInt32(out weaponId);
                    }

                    var constellation = 0;
                    if (item.TryGetProperty("actived_constellation_num", out var constProp))
                    {
                        constProp.TryGetInt32(out constellation);
                    }

                    fallbackList.Add(new HutaoSimpleAvatar
                    {
                        AvatarId = avatarId,
                        WeaponId = weaponId,
                        ActivedConstellationNumber = constellation,
                        ReliquarySetIds = new List<int>()
                    });
                }
            }
        }
        catch
        {
            return (false, string.Empty, new List<HutaoSimpleAvatar>());
        }

        if (avatarIds.Count == 0)
        {
            return (false, string.Empty, new List<HutaoSimpleAvatar>());
        }

        var detailed = new List<HutaoSimpleAvatar>();
        const int chunkSize = 40;
        for (var i = 0; i < avatarIds.Count; i += chunkSize)
        {
            var chunk = avatarIds.Skip(i).Take(chunkSize).ToList();
            var detailBody = JsonSerializer.Serialize(new
            {
                role_id = matchedRole.game_uid,
                server = matchedRole.region,
                character_ids = chunk
            });

            var detailResp = await PostAsync(
                selectedRole,
                ApiEndpoints.GameRecordCharacterDetailEndpoint,
                detailBody,
                ChallengePath(ApiEndpoints.GameRecordCharacterDetailEndpoint),
                token).ConfigureAwait(false);

            if (!detailResp.Success || string.IsNullOrWhiteSpace(detailResp.DataJson))
            {
                detailed.Clear();
                break;
            }

            try
            {
                using var detailDoc = JsonDocument.Parse(detailResp.DataJson);
                if (!detailDoc.RootElement.TryGetProperty("list", out var detailArr) ||
                    detailArr.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var entry in detailArr.EnumerateArray())
                {
                    if (!entry.TryGetProperty("base", out var baseObj) || baseObj.ValueKind != JsonValueKind.Object ||
                        !baseObj.TryGetProperty("id", out var avatarIdProp) || !avatarIdProp.TryGetInt32(out var avatarId) ||
                        avatarId <= 0)
                    {
                        continue;
                    }

                    var constellation = 0;
                    if (baseObj.TryGetProperty("actived_constellation_num", out var constProp))
                    {
                        constProp.TryGetInt32(out constellation);
                    }

                    var weaponId = 0;
                    if (entry.TryGetProperty("weapon", out var wpObj) && wpObj.ValueKind == JsonValueKind.Object &&
                        wpObj.TryGetProperty("id", out var wpIdProp))
                    {
                        wpIdProp.TryGetInt32(out weaponId);
                    }

                    var relicSetIds = new List<int>();
                    if (entry.TryGetProperty("relics", out var relicsArr) && relicsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var relic in relicsArr.EnumerateArray())
                        {
                            if (relic.TryGetProperty("set", out var setObj) && setObj.ValueKind == JsonValueKind.Object &&
                                setObj.TryGetProperty("id", out var setIdProp) && setIdProp.TryGetInt32(out var setId) &&
                                setId > 0)
                            {
                                relicSetIds.Add(setId);
                            }
                        }
                    }

                    detailed.Add(new HutaoSimpleAvatar
                    {
                        AvatarId = avatarId,
                        WeaponId = weaponId,
                        ActivedConstellationNumber = constellation,
                        ReliquarySetIds = relicSetIds
                    });
                }
            }
            catch
            {
                detailed.Clear();
                break;
            }
        }

        return detailed.Count > 0
            ? (true, string.Empty, detailed)
            : (true, string.Empty, fallbackList);
    }

    private static string RoleQuery(SelectedGameRole role) =>
        $"role_id={Uri.EscapeDataString(role.Role.game_uid)}&server={Uri.EscapeDataString(role.Role.region)}";

    private static string ChallengePath(string apiName) => ApiEndpoints.GetGameRecordChallengePath(apiName);

    private static bool IsOversea(SelectedGameRole role) =>
        string.Equals(role.ServerType, "os", StringComparison.OrdinalIgnoreCase);

    private Task<RawChallengeResponse> GetAsync(
        SelectedGameRole role, string apiName, string rawQuery, string challengePath, CancellationToken token) =>
        ExecuteAsync(role, HttpMethod.Get, apiName, rawQuery, jsonBody: null, challengePath, token);

    private Task<RawChallengeResponse> PostAsync(
        SelectedGameRole role, string apiName, string jsonBody, string challengePath, CancellationToken token) =>
        ExecuteAsync(role, HttpMethod.Post, apiName, rawQuery: string.Empty, jsonBody, challengePath, token);

    private async Task<RawChallengeResponse> ExecuteAsync(
        SelectedGameRole role, HttpMethod method, string apiName, string rawQuery, string? jsonBody,
        string challengePath, CancellationToken token)
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
        var response = await SendWithFallbackAsync(context, isOversea, method, apiName, rawQuery, jsonBody,
                challenge: null, token)
            .ConfigureAwait(false);

        if (response.Kind == ChallengeErrorKind.RiskControlRequired && !isOversea)
        {
            response = await TryApplyRiskChallengeAsync(
                context, isOversea, method, apiName, rawQuery, jsonBody, challengePath, cookieOverride: null, response, token)
                .ConfigureAwait(false);
        }

        if (response.Kind == ChallengeErrorKind.LoginExpired)
        {
            if (await TryRefreshCookiesAsync(role.AccountId, isOversea, token).ConfigureAwait(false))
            {
                try
                {
                    context = await _identityService.BuildAsync(role.AccountId, token).ConfigureAwait(false);
                    if (context.Cookies.Count > 0)
                    {
                        response = await SendWithFallbackAsync(context, isOversea, method, apiName, rawQuery, jsonBody,
                                challenge: null, token)
                            .ConfigureAwait(false);

                        if (response.Kind == ChallengeErrorKind.RiskControlRequired && !isOversea)
                        {
                            response = await TryApplyRiskChallengeAsync(
                                context, isOversea, method, apiName, rawQuery, jsonBody, challengePath,
                                cookieOverride: null, response, token)
                                .ConfigureAwait(false);
                        }
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

            if (response.Kind == ChallengeErrorKind.LoginExpired)
            {
                var altResponse = await SendWithAlternateCookiesAsync(
                    context, isOversea, method, apiName, rawQuery, jsonBody, challengePath, token)
                    .ConfigureAwait(false);
                if (altResponse is not null)
                {
                    response = altResponse;
                }
            }
        }

        return response;
    }

    private async Task<RawChallengeResponse> TryApplyRiskChallengeAsync(
        AccountContext context, bool isOversea, HttpMethod method, string apiName, string rawQuery, string? jsonBody,
        string challengePath, string? cookieOverride, RawChallengeResponse current, CancellationToken token)
    {
        try
        {
            var challenge = await _geetestService.TryVerifyForDailyNoteAsync(context, challengePath)
                .ConfigureAwait(false);
            if (!string.IsNullOrEmpty(challenge))
            {
                return await SendWithFallbackAsync(
                    context, isOversea, method, apiName, rawQuery, jsonBody, challenge, token, cookieOverride)
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

        return current;
    }

    private async Task<RawChallengeResponse?> SendWithAlternateCookiesAsync(
        AccountContext context, bool isOversea, HttpMethod method, string apiName, string rawQuery, string? jsonBody,
        string challengePath, CancellationToken token)
    {
        var primaryCookie = BbsRequestBuilder.BuildCookieString(context.Cookies, BbsRequestBuilder.CookieMode.Cookie);
        var alternateCookies = BuildAlternateRecordCookieStrings(context.Cookies, primaryCookie);
        RawChallengeResponse? bestNonSuccess = null;

        foreach (var cookieOverride in alternateCookies)
        {
            var resp = await SendWithFallbackAsync(
                context, isOversea, method, apiName, rawQuery, jsonBody, challenge: null, token, cookieOverride)
                .ConfigureAwait(false);

            if (resp.Kind == ChallengeErrorKind.RiskControlRequired && !isOversea)
            {
                resp = await TryApplyRiskChallengeAsync(
                    context, isOversea, method, apiName, rawQuery, jsonBody, challengePath, cookieOverride, resp, token)
                    .ConfigureAwait(false);
            }

            if (resp.Success)
            {
                return resp;
            }

            if (resp.Kind != ChallengeErrorKind.LoginExpired)
            {
                return resp;
            }

            bestNonSuccess ??= resp;
        }

        return bestNonSuccess;
    }

    private async Task<RawChallengeResponse> SendWithFallbackAsync(
        AccountContext context, bool isOversea, HttpMethod method, string apiName, string rawQuery, string? jsonBody,
        string? challenge, CancellationToken token, string? cookieOverride = null)
    {
        var bases = GetCandidateBases(isOversea, apiName);
        RawChallengeResponse? last = null;

        foreach (var baseUrl in bases)
        {
            var url = string.IsNullOrEmpty(rawQuery) ? $"{baseUrl}/{apiName}" : $"{baseUrl}/{apiName}?{rawQuery}";
            try
            {
                using var request = new HttpRequestMessage(method, url);
                if (jsonBody != null)
                {
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                }

                if (isOversea)
                {
                    ApplyOverseaHeaders(request, context, rawQuery, jsonBody, challenge, cookieOverride);
                }
                else
                {
                    ApplyChineseHeaders(request, context, rawQuery, jsonBody, challenge, cookieOverride);
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

        return apiName is "spiralAbyss" or ApiEndpoints.GameRecordCharacterListEndpoint or ApiEndpoints.GameRecordCharacterDetailEndpoint
            ? [OsGameRecordBase, OsEventBase]
            : [OsEventBase, OsGameRecordBase];
    }

    private static string SortQuery(string rawQuery) =>
        string.Join("&", rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(part => part, StringComparer.Ordinal));

    private void ApplyChineseHeaders(HttpRequestMessage request, AccountContext context, string rawQuery,
        string? jsonBody, string? challenge, string? cookieOverride = null)
    {
        string cookie = !string.IsNullOrEmpty(cookieOverride)
            ? cookieOverride
            : BbsRequestBuilder.BuildCookieString(context.Cookies, BbsRequestBuilder.CookieMode.Cookie);
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
            Body: jsonBody ?? string.Empty,
            Challenge: challenge,
            ToolVersion: ToolVersionCn,
            Page: PageCn));
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
    }

    private void ApplyOverseaHeaders(HttpRequestMessage request, AccountContext context, string rawQuery,
        string? jsonBody, string? challenge, string? cookieOverride = null)
    {
        string cookie = !string.IsNullOrEmpty(cookieOverride)
            ? cookieOverride
            : BbsRequestBuilder.BuildCookieString(context.Cookies, BbsRequestBuilder.CookieMode.Cookie);
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
            HeaderNames.DS, MiHoYoHeaderFactory.CalculateDs2(HeaderSalts.OsGameRecord, SortQuery(rawQuery), jsonBody ?? string.Empty));
    }

    private async Task<bool> TryRefreshCookiesAsync(string accountId, bool isOversea, CancellationToken token)
    {
        try
        {
            var cookies = await _accountManager.LoadCookiesAsync(accountId).ConfigureAwait(false);
            if (cookies is null || cookies.Count == 0)
            {
                return false;
            }

            if (cookies.TryGetValue("stoken", out var stoken) && !string.IsNullOrWhiteSpace(stoken))
            {
                var aid = FirstNonEmpty(cookies, "stuid", "ltuid_v2", "ltuid", "account_id_v2", "account_id");
                var mid = cookies.TryGetValue("mid", out var midVal) ? midVal : string.Empty;

                if (!string.IsNullOrWhiteSpace(aid))
                {
                    string newCookieToken = await TryFetchExchangedTokenAsync(() => isOversea
                        ? _overseaPassportClient.GetCookieAccountInfoBySTokenAsync(stoken, mid, aid, token)
                        : _passportClient.GetCookieAccountInfoBySTokenAsync(stoken, mid, aid, token))
                        .ConfigureAwait(false);

                    string newLToken = await TryFetchExchangedTokenAsync(() => isOversea
                        ? _overseaPassportClient.GetLTokenBySTokenAsync(stoken, mid, aid, token)
                        : _passportClient.GetLTokenBySTokenAsync(stoken, mid, aid, token))
                        .ConfigureAwait(false);

                    var updated = new Dictionary<string, string>(cookies, StringComparer.Ordinal);
                    bool changed = ApplyExchangedToken(updated, newCookieToken, aid, isOversea,
                        "cookie_token", "cookie_token_v2", "account_id", "account_id_v2");
                    changed |= ApplyExchangedToken(updated, newLToken, aid, isOversea,
                        "ltoken", "ltoken_v2", "ltuid", "ltuid_v2");

                    if (changed)
                    {
                        await _accountManager.UpdateCookiesAsync(accountId, updated).ConfigureAwait(false);
                        return true;
                    }
                }
            }

            if (isOversea)
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> TryFetchExchangedTokenAsync(Func<Task<string>> fetcher)
    {
        try
        {
            return await fetcher().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool ApplyExchangedToken(
        Dictionary<string, string> target,
        string tokenValue,
        string aid,
        bool isOversea,
        string v1Key,
        string v2Key,
        string v1UidKey,
        string v2UidKey)
    {
        if (string.IsNullOrWhiteSpace(tokenValue))
        {
            return false;
        }

        if (tokenValue.StartsWith("v2_", StringComparison.Ordinal))
        {
            target[v2Key] = tokenValue;
            target[v2UidKey] = aid;
        }
        else if (isOversea)
        {
            target[v2Key] = tokenValue;
            target[v2UidKey] = aid;
            target[v1Key] = tokenValue;
            target[v1UidKey] = aid;
        }
        else
        {
            target[v1Key] = tokenValue;
            target[v1UidKey] = aid;
            target.Remove(v2Key);
        }

        return true;
    }

    private static string? FirstNonEmpty(IReadOnlyDictionary<string, string> cookies, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (cookies.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
            {
                return val;
            }
        }

        return null;
    }

    private static List<string> BuildAlternateRecordCookieStrings(
        IReadOnlyDictionary<string, string> cookies,
        string primaryCookie)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(primaryCookie))
        {
            seen.Add(primaryCookie);
        }

        void AddCandidate(params (string Key, string? Value)[] pairs)
        {
            var parts = new List<string>(pairs.Length);
            foreach (var (key, value) in pairs)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                parts.Add($"{key}={value}");
            }

            var combined = string.Join("; ", parts);
            if (seen.Add(combined))
            {
                candidates.Add(combined);
            }
        }

        var accountId = FirstNonEmpty(cookies, "account_id", "account_id_v2", "stuid", "ltuid", "ltuid_v2");
        var accountIdV2 = FirstNonEmpty(cookies, "account_id_v2", "account_id", "ltuid_v2", "stuid", "ltuid");
        var ltuid = FirstNonEmpty(cookies, "ltuid", "ltuid_v2", "account_id", "account_id_v2", "stuid");
        var ltuidV2 = FirstNonEmpty(cookies, "ltuid_v2", "ltuid", "account_id_v2", "account_id", "stuid");

        cookies.TryGetValue("cookie_token", out var cookieToken);
        cookies.TryGetValue("cookie_token_v2", out var cookieTokenV2);
        cookies.TryGetValue("ltoken", out var ltoken);
        cookies.TryGetValue("ltoken_v2", out var ltokenV2);
        cookies.TryGetValue("mid", out var mid);

        AddCandidate(("account_id", accountId), ("cookie_token", cookieToken));
        AddCandidate(("account_id_v2", accountIdV2), ("cookie_token_v2", cookieTokenV2));
        AddCandidate(("ltuid", ltuid), ("ltoken", ltoken));
        AddCandidate(("ltuid_v2", ltuidV2), ("ltoken_v2", ltokenV2));
        AddCandidate(("account_id", accountId), ("cookie_token", cookieToken), ("ltuid", ltuid), ("ltoken", ltoken));
        AddCandidate(("account_id_v2", accountIdV2), ("cookie_token_v2", cookieTokenV2), ("ltuid_v2", ltuidV2), ("ltoken_v2", ltokenV2));
        AddCandidate(("account_id_v2", accountIdV2), ("cookie_token_v2", cookieTokenV2), ("ltuid_v2", ltuidV2), ("ltoken_v2", ltokenV2), ("mid", mid));

        return candidates;
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