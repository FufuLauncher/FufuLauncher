/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Models;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Services.MiHoYo.Transport;

namespace FufuLauncher.Services.MiHoYo.DailyNote;

public sealed class DailyNoteService
{
    private const string RecordApiBase = ApiEndpoints.DailyNoteRecordApiBase;

    private const string IndexPath = "/api/index";
    private const string DailyNotePath = "/api/dailyNote";
    private const string WidgetPath = "/aapi/widget/v2?game_id=2";

    private const string Page = "v6.6.1-gr-cn_#/ys";

    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(15) };

    private readonly HttpClient _httpClient;
    private readonly Func<TokenRefreshService> _createTokenRefreshService;
    private readonly AccountIdentityService _identityService;
    private readonly OverseaGameRecordClient _overseaGameRecordClient = new();

    public DailyNoteService() : this(SharedHttpClient, () => new TokenRefreshService())
    {
    }

    internal DailyNoteService(HttpClient httpClient, Func<TokenRefreshService> createTokenRefreshService)
    {
        _httpClient = httpClient;
        _createTokenRefreshService = createTokenRefreshService;
        _identityService = App.GetService<AccountIdentityService>()
                           ?? throw new InvalidOperationException("DailyNote_NoIdentityService".GetLocalized());
    }

    public async Task<DailyNoteCardData?> GetDailyNoteAsync(string roleId, string server, string? accountId = null)
    {
        await _semaphore.WaitAsync();
        try
        {
            AccountManager accountManager = App.GetService<AccountManager>();
            string activeId = accountId ?? accountManager.ActiveAccountId
                ?? throw new InvalidOperationException("DailyNote_NoActiveAccount".GetLocalized());

            var ctx = await _identityService.BuildAsync(activeId);
            if (ctx.Cookies.Count == 0)
                throw new InvalidOperationException("DailyNote_CannotLoadCookie".GetLocalized());

            if (ServerRegion.IsOversea(server))
            {
                return await _overseaGameRecordClient.GetDailyNoteAsync(roleId, server, ctx.Cookies);
            }

            string playerQuery = $"role_id={Uri.EscapeDataString(roleId)}&server={Uri.EscapeDataString(server)}";

            bool refreshAttempted = false;
            async Task<string> RequestWithRefreshAsync(Func<AccountContext, Task<string>> send)
            {
                string response = await send(ctx);
                if (refreshAttempted || !MiyousheCheckinGame.IsLoginExpired(ParseResponse(response).Retcode))
                    return response;

                refreshAttempted = true;
                var rebuilt = await RefreshCookiesAsync(accountManager, activeId);
                if (rebuilt == null) return response;
                ctx = rebuilt;
                return await send(ctx);
            }

            await WarmUpPlayerInfoAsync(playerQuery, ctx);

            string json = await RequestWithRefreshAsync(context => RequestAsync(
                $"{RecordApiBase}{DailyNotePath}?{playerQuery}", context, challenge: null));
            var (retcode, message) = ParseResponse(json);

            if (retcode == 1034)
            {
                string challenged = await RequestWithRefreshAsync(context =>
                    TrySolveChallengeAsync(playerQuery, context, json));
                var (challengeRetcode, challengeMessage) = ParseResponse(challenged);

                if (challengeRetcode == 0)
                {
                    json = challenged;
                    (retcode, message) = (challengeRetcode, challengeMessage);
                }
                else
                {
                    DebugLog.Info("DailyNoteService",
                        $"[DailyNoteService] Retried after verification retcode={challengeRetcode}, falling back to widget");
                }
            }

            if (retcode == 5003 || retcode == 1034)
            {
                json = await RequestWithRefreshAsync(RequestWidgetAsync);
                (retcode, message) = ParseResponse(json);

                if (retcode == 0 && !GameRoleResponseValidator.MatchesWidget(json, roleId, server))
                    throw new InvalidOperationException("GameRole_WidgetMismatch".GetLocalized());
            }

            if (retcode != 0)
                throw new InvalidOperationException(
                    string.Format("DailyNote_FetchFailed".GetLocalized(), message, retcode));

            return DailyNoteParser.Parse(json);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WarmUpPlayerInfoAsync(string playerQuery, AccountContext ctx)
    {
        try
        {
            string query = $"avatar_list_type=1&{playerQuery}";
            await RequestAsync($"{RecordApiBase}{IndexPath}?{query}", ctx, challenge: null);
        }
        catch (Exception ex)
        {
            DebugLog.Info("DailyNoteService", $"[DailyNoteService] index warmup failed (ignored): {ex.Message}");
        }
    }

    private async Task<AccountContext?> RefreshCookiesAsync(AccountManager accountManager, string activeId)
    {
        DebugLog.Info("DailyNoteService", "[DailyNoteService] Login expired, refreshing Cookie");

        var currentCookies = await accountManager.LoadCookiesAsync(activeId);
        if (currentCookies is null || currentCookies.Count == 0)
        {
            DebugLog.Info("DailyNoteService", "[DailyNoteService] Failed to load current Cookie");
            return null;
        }

        using var refreshService = _createTokenRefreshService();
        // A business request already rejected these credentials; do not gate the retry on another API.
        var refreshed = await refreshService.RefreshCookieAsync(currentCookies, forceRefresh: true);
        if (refreshed is null || refreshed.Count == 0)
        {
            DebugLog.Info("DailyNoteService", "[DailyNoteService] Cookie refresh failed");
            return null;
        }

        await accountManager.UpdateCookiesAsync(activeId, refreshed);
        return await _identityService.BuildAsync(activeId);
    }

    private async Task<string> TrySolveChallengeAsync(string playerQuery, AccountContext ctx, string fallback)
    {
        var localSettingsService = App.GetService<ILocalSettingsService>();
        var captchaDisabledJson = await localSettingsService.ReadSettingAsync("IsCaptchaPopupDisabled");
        if (captchaDisabledJson != null && Convert.ToBoolean(captchaDisabledJson))
        {
            DebugLog.Info("DailyNoteService",
                "[DailyNoteService] Risk-control captcha prompt disabled by user, skipping verification");
            return fallback;
        }

        string xrpcChallenge = await new GeetestService().TryVerifyForDailyNoteAsync(ctx);
        if (string.IsNullOrEmpty(xrpcChallenge))
        {
            return fallback;
        }

        return await RequestAsync(
            $"{RecordApiBase}{DailyNotePath}?{playerQuery}", ctx, xrpcChallenge);
    }

    private async Task<string> RequestWidgetAsync(AccountContext ctx)
    {
        string url = RecordApiBase + WidgetPath;
        string cookieStr = BbsRequestBuilder.BuildCookieString(ctx.Cookies, BbsRequestBuilder.CookieMode.SToken);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        MiHoYoHeaderFactory.ApplyGameRecordHeaders(req, new GameRecordHeaderOptions(
            AppVersion: HeaderVersions.MobileCnLogin,
            UserAgent: ctx.UserAgent.Mobile,
            DeviceId: ctx.Device.BbsDeviceId,
            DeviceFp: ctx.Device.DeviceFp,
            DeviceName: Uri.EscapeDataString(ctx.Device.DeviceName),
            SysVersion: ctx.Device.SysVersion,
            Cookie: cookieStr,
            DsSalt: HeaderSalts.CnX6,
            SortedQuery: SortedQuery(url),
            Page: Page));
        req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");

        using var resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> RequestAsync(string apiUrl, AccountContext ctx, string? challenge)
    {
        string cookieStr = BbsRequestBuilder.BuildCookieString(ctx.Cookies, BbsRequestBuilder.CookieMode.Cookie);

        using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        MiHoYoHeaderFactory.ApplyGameRecordHeaders(req, new GameRecordHeaderOptions(
            AppVersion: HeaderVersions.MobileCnLogin,
            UserAgent: ctx.UserAgent.Mobile,
            DeviceId: ctx.Device.BbsDeviceId,
            DeviceFp: ctx.Device.DeviceFp,
            DeviceName: Uri.EscapeDataString(ctx.Device.DeviceName),
            SysVersion: ctx.Device.SysVersion,
            Cookie: cookieStr,
            DsSalt: HeaderSalts.CnX4,
            SortedQuery: SortedQuery(apiUrl),
            Challenge: challenge,
            ToolVersion: HeaderVersions.ToolVersionCn,
            Page: Page));
        req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");

        using var resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private static string SortedQuery(string url) =>
        string.Join("&", new Uri(url).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(s => s, StringComparer.Ordinal));

    private static (int Retcode, string Message) ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            int retcode = root.TryGetProperty("retcode", out var rc) ? rc.GetInt32() : -1;
            string message = root.TryGetProperty("message", out var m)
                ? m.GetString() ?? "Status_UnknownError".GetLocalized()
                : "Status_UnknownError".GetLocalized();
            return (retcode, message);
        }
        catch (JsonException)
        {
            return (-1, "Status_UnknownError".GetLocalized());
        }
    }
}
