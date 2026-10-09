/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using MihoyoBBS;

namespace FufuLauncher.Services;

/// <summary>国服每日奖励签到：不同游戏有界并发，同游戏角色顺序执行，账号内共享一次凭证刷新。</summary>
public sealed class MiyousheGameCheckinService : IDisposable
{
    private const int MaxConcurrentGames = 3;
    private const int MaxRateLimitRetries = 2;
    private readonly HttpClient _httpClient;
    private readonly Func<Task> _delay;
    private readonly Func<TimeSpan, Task> _requestDelay;
    private readonly TimeProvider _clock;

    public MiyousheGameCheckinService() : this(new HttpClient(new HttpClientHandler
    {
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    }) { Timeout = TimeSpan.FromSeconds(30) }, () => Task.Delay(Random.Shared.Next(2000, 5000)))
    {
    }

    internal MiyousheGameCheckinService(HttpClient httpClient, Func<Task> delay,
        Func<TimeSpan, Task>? requestDelay = null, TimeProvider? clock = null)
    {
        _httpClient = httpClient;
        _delay = delay;
        _requestDelay = requestDelay ?? (duration => Task.Delay(duration));
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<List<MiyousheRoleCheckinResult>> ExecuteAsync(
        AccountCredentials account, ISet<string> disabledUids,
        Func<Task<string?>> refreshCookie, IProgress<string>? progress = null,
        ISet<string>? enabledGames = null)
    {
        using var pacer = new MiyousheRequestPacer(_clock, _requestDelay);
        var session = new CheckinSession(account.Cookie, refreshCookie);
        var results = new List<MiyousheRoleCheckinResult>();
        var roles = await RequestAsync<AccountInfoData>(session, pacer, ApiEndpoints.MihoyoBbsAccountInfoUrl);
        if (roles.RetCode != 0 || roles.Data?.List == null)
        {
            results.Add(new("", "Checkin_GameCheckin".GetLocalized(), "", false,
                roles.Message));
            return results;
        }

        var boundRoles = roles.Data.List
            .Where(r => !string.IsNullOrWhiteSpace(r.GameUid) && !string.IsNullOrWhiteSpace(r.Region))
            .DistinctBy(r => (r.GameBiz, r.Region, r.GameUid)).ToList();
        if (boundRoles.Count == 0)
        {
            results.Add(new("", "Checkin_GameCheckin".GetLocalized(), "", false,
                "Checkin_NoBoundAccount".GetLocalized()));
            return results;
        }

        if (enabledGames != null)
            boundRoles = boundRoles.Where(r => enabledGames.Contains(r.GameBiz)).ToList();
        if (boundRoles.Count == 0)
            return [new("", "Checkin_GameCheckin".GetLocalized(), "", null, "Status_Skipped".GetLocalized())];

        var roleResults = new MiyousheRoleCheckinResult[boundRoles.Count];
        using var concurrency = new SemaphoreSlim(MaxConcurrentGames);
        var gameTasks = boundRoles.Select((role, index) => (Role: role, Index: index))
            .GroupBy(item => item.Role.GameBiz)
            .Select(async group =>
        {
            var game = MiyousheCheckinGame.SupportedGames.FirstOrDefault(g => g.GameBiz == group.Key);
            bool attemptedRole = false;
            foreach (var (role, index) in group)
            {
                if (game == null)
                {
                    roleResults[index] = new(role.GameBiz, role.GameBiz, role.GameUid, null,
                        "Checkin_UnsupportedGame".GetLocalized());
                    continue;
                }
                if (disabledUids.Contains(role.GameUid))
                {
                    roleResults[index] = new(game.GameBiz, game.Name, role.GameUid, null,
                        "Status_Skipped".GetLocalized());
                    continue;
                }

                // 只在同游戏的多个角色间间隔，等待期间不占用并发名额。
                if (attemptedRole) await _delay();
                attemptedRole = true;
                await concurrency.WaitAsync();
                try
                {
                    progress?.Report($"[{account.Nickname}] {game.Name} ({role.GameUid}) — {"Checkin_GameCheckinProgress".GetLocalized()}");
                    roleResults[index] = await SignRoleAsync(session, pacer, game, role);
                }
                catch (Exception ex)
                {
                    roleResults[index] = new(game.GameBiz, game.Name, role.GameUid, false,
                        string.Format("Checkin_NetworkRequestException".GetLocalized(), ex.Message));
                }
                finally
                {
                    concurrency.Release();
                }
            }
        });
        await Task.WhenAll(gameTasks);
        // 完成顺序可能不同，展示顺序仍与绑定角色列表一致。
        return roleResults.ToList();
    }

    private async Task<MiyousheRoleCheckinResult> SignRoleAsync(
        CheckinSession session, MiyousheRequestPacer pacer, MiyousheCheckinGame game, AccountItem role)
    {
        string infoUrl = $"{game.ApiBase}/info?lang=zh-cn&act_id={game.ActId}" +
                         $"&region={Uri.EscapeDataString(role.Region)}&uid={Uri.EscapeDataString(role.GameUid)}";
        var status = await RequestAsync<IsSignData>(session, pacer, infoUrl, game);
        if (status.RetCode != 0 || status.Data == null)
            return new(game.GameBiz, game.Name, role.GameUid, false, status.Message);
        if (status.Data.FirstBind)
            return new(game.GameBiz, game.Name, role.GameUid, false,
                "Checkin_FirstBindWarning".GetLocalized());

        bool alreadySigned = status.Data.IsSign;
        if (!alreadySigned)
        {
            string body = JsonSerializer.Serialize(new { act_id = game.ActId, region = role.Region, uid = role.GameUid });
            var sign = await RequestAsync<SignResponseData>(session, pacer, $"{game.ApiBase}/sign", game, body);
            if (sign.RetCode != 0)
                return new(game.GameBiz, game.Name, role.GameUid, false, sign.Message);
            if (sign.RetCode == 0 && sign.Data is { Success: not 0 })
                return new(game.GameBiz, game.Name, role.GameUid, false,
                    "Checkin_GameCaptcha".GetLocalized());

            // 正常提交后确认签到状态。
            status = await RequestAsync<IsSignData>(session, pacer, infoUrl, game);
            if (status.RetCode != 0 || status.Data == null)
                return new(game.GameBiz, game.Name, role.GameUid, false, status.Message);
            if (!status.Data.IsSign)
                return new(game.GameBiz, game.Name, role.GameUid, false,
                    "Checkin_SignNotConfirmed".GetLocalized());
        }

        int days = status.Data.TotalSignDay;
        string rewardItem = "";
        var rewards = await RequestAsync<CheckinRewardsData>(session, pacer,
            $"{game.ApiBase}/home?lang=zh-cn&act_id={game.ActId}", game);
        if (rewards.RetCode == 0 && rewards.Data?.Awards is { } awards && days > 0 && days <= awards.Count)
            rewardItem = Tools.GetItem(awards[days - 1]);

        string message = alreadySigned ? "Checkin_AlreadySignedToday".GetLocalized() : "Checkin_SignSuccess".GetLocalized();
        message += $" | {string.Format("Checkin_SignedDays".GetLocalized(), days)}";
        if (rewardItem.Length > 0)
            message += $" | {string.Format("Checkin_RewardIs".GetLocalized(), rewardItem)}";
        return new(game.GameBiz, game.Name, role.GameUid, true, message, days, rewardItem);
    }

    private async Task<CheckinResponse<T>> RequestAsync<T>(CheckinSession session, MiyousheRequestPacer pacer,
        string url, MiyousheCheckinGame? game = null, string? body = null) where T : class
    {
        bool authRetried = false;
        int rateLimitRetries = 0;
        while (true)
        {
            string cookie = await session.GetCookieAsync();
            if (!await pacer.WaitAsync())
                return new(-500004, "Checkin_RequestTooFrequent".GetLocalized(), null, true);
            var result = await SendAsync<T>(session, cookie, url, game, body);
            if (MiyousheCheckinGame.IsLoginExpired(result.RetCode))
            {
                if (authRetried || !await session.RefreshAsync(cookie))
                    return result with { Message = "Checkin_LoginExpired".GetLocalized() };
                authRetried = true;
                continue;
            }
            if (!result.RateLimited) return result;
            bool canWait = pacer.Defer(result.RetryAfter, rateLimitRetries);
            if (!canWait || rateLimitRetries >= MaxRateLimitRetries) return result;
            rateLimitRetries++;
        }
    }

    private async Task<CheckinResponse<T>> SendAsync<T>(CheckinSession session, string cookie, string url,
        MiyousheCheckinGame? game, string? body) where T : class
    {
        try
        {
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("DS", Tools.GetDs(true));
            request.Headers.TryAddWithoutValidation("x-rpc-device_id", session.DeviceId);
            request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
            request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.93.1");
            request.Headers.TryAddWithoutValidation("x-rpc-channel", "miyousheluodi");
            request.Headers.TryAddWithoutValidation("User-Agent", Tools.GetUserAgent(""));
            request.Headers.TryAddWithoutValidation("X-Requested-With", "com.mihoyo.hyperion");
            request.Headers.Referrer = new Uri(game?.Referer ?? "https://act.mihoyo.com/");
            request.Headers.TryAddWithoutValidation("Origin", game?.Referer.StartsWith("https://webstatic.") == true
                ? "https://webstatic.mihoyo.com" : "https://act.mihoyo.com");
            if (game?.SignGame != null) request.Headers.TryAddWithoutValidation("x-rpc-signgame", game.SignGame);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                bool limited = response.StatusCode == HttpStatusCode.TooManyRequests;
                var retryAfter = response.Headers.RetryAfter;
                TimeSpan? wait = retryAfter?.Delta ?? (retryAfter?.Date - _clock.GetUtcNow());
                return new(-1, limited ? "Checkin_RequestTooFrequent".GetLocalized()
                    : $"{"Checkin_SignRequestFailed".GetLocalized()} (HTTP {(int)response.StatusCode})", null, limited, wait);
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!json.RootElement.TryGetProperty("retcode", out var code) || !code.TryGetInt32(out int retCode))
                return new(-1, "Checkin_ParseResultFailed".GetLocalized(), null);
            string message = json.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String
                ? msg.GetString() ?? "" : "Checkin_ParseResultFailed".GetLocalized();
            T? data = null;
            // 错误响应的 data 可能是空字符串，不能让反序列化掩盖登录失效码。
            if (retCode == 0 && json.RootElement.TryGetProperty("data", out var value) && value.ValueKind == JsonValueKind.Object)
                data = value.Deserialize<T>();
            return new(retCode, data == null && retCode == 0 ? "Checkin_ParseResultFailed".GetLocalized() : message,
                data, retCode == -500004);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(-1, string.Format("Checkin_NetworkRequestException".GetLocalized(), ex.Message), null);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class CheckinSession(string cookie, Func<Task<string?>> refreshCookie)
    {
        private readonly object _refreshLock = new();
        private string _cookie = cookie;
        private Task<bool>? _refreshTask;
        public string DeviceId { get; } = Tools.GetDeviceId(cookie);

        public async Task<string> GetCookieAsync()
        {
            Task<bool>? pendingRefresh;
            lock (_refreshLock) pendingRefresh = _refreshTask;
            // 刷新开始后的新请求，先等凭证更新与保存完成。
            if (pendingRefresh != null) await pendingRefresh;
            lock (_refreshLock) return _cookie;
        }

        public Task<bool> RefreshAsync(string rejectedCookie)
        {
            TaskCompletionSource<bool>? completion = null;
            Task<bool> pendingRefresh;
            lock (_refreshLock)
            {
                // 已在途中发出的旧 Cookie 请求可能晚于刷新结果返回。
                if (_cookie != rejectedCookie) return Task.FromResult(true);
                if (_refreshTask == null)
                {
                    completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _refreshTask = completion.Task;
                }
                pendingRefresh = _refreshTask;
            }
            if (completion != null) _ = CompleteRefreshAsync(completion);
            return pendingRefresh;
        }

        private async Task CompleteRefreshAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                string? refreshedCookie = await refreshCookie();
                bool success = !string.IsNullOrWhiteSpace(refreshedCookie);
                if (success)
                {
                    lock (_refreshLock) _cookie = refreshedCookie!;
                }
                completion.SetResult(success);
            }
            catch (Exception ex)
            {
                // 所有等待者观察同一失败结果，不能各自重新刷新。
                completion.SetException(ex);
            }
        }
    }

    private sealed record CheckinResponse<T>(int RetCode, string Message, T? Data,
        bool RateLimited = false, TimeSpan? RetryAfter = null);
}
