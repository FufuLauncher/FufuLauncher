/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Services.MiHoYo.Transport;

namespace FufuLauncher.Services.MiHoYo.Record;


public sealed class GameRecordWarmUpService
{
    private const string RecordApiBase = ApiEndpoints.DailyNoteRecordApiBase;
    private const string IndexPath = ApiEndpoints.DailyNoteIndexPath;
    private const string Page = "v6.6.1-gr-cn_#/ys";

    /// <summary>How long a successful warm-up stays valid before another one is sent.</summary>
    private static readonly TimeSpan FreshWindow = TimeSpan.FromMinutes(5);

    private static readonly HttpClient SharedHttpClient = new(new HttpClientHandler { UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(15) };

    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, WarmUpState> _states = new(StringComparer.Ordinal);

    public GameRecordWarmUpService() : this(SharedHttpClient)
    {
    }

    internal GameRecordWarmUpService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }


    public async Task TryWarmUpAsync(
        AccountIdentityService identityService,
        string accountId,
        string roleId,
        string server,
        CancellationToken token = default)
    {
        string key = Key(accountId, roleId, server);
        if (IsFresh(key))
        {
            LogSkipped(key);
            return;
        }

        try
        {
            var context = await identityService.BuildAsync(accountId, token).ConfigureAwait(false);
            await WarmUpAsync(key, context, roleId, server, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameRecordWarmUp", $"[WarmUp] index warmup failed (ignored): {ex.Message}");
        }
    }


    public async Task TryWarmUpAsync(
        AccountContext context,
        string roleId,
        string server,
        CancellationToken token = default)
    {
        string key = Key(context.AccountId, roleId, server);
        if (IsFresh(key))
        {
            LogSkipped(key);
            return;
        }

        try
        {
            await WarmUpAsync(key, context, roleId, server, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameRecordWarmUp", $"[WarmUp] index warmup failed (ignored): {ex.Message}");
        }
    }

    public Task RefreshAsync(AccountContext context, string roleId, string server, CancellationToken token = default)
    {
        _states.TryRemove(Key(context.AccountId, roleId, server), out _);
        return TryWarmUpAsync(context, roleId, server, token);
    }

    private async Task WarmUpAsync(
        string key, AccountContext context, string roleId, string server, CancellationToken token)
    {
        while (true)
        {
            if (_states.TryGetValue(key, out var existing))
            {
                if (existing.CompletedAt is { } completedAt)
                {
                    if (DateTimeOffset.UtcNow - completedAt < FreshWindow)
                    {
                        LogSkipped(key);
                        return;
                    }

                    _states.TryRemove(new KeyValuePair<string, WarmUpState>(key, existing));
                    continue;
                }

                // Another caller is warming up right now; share its request.
                await existing.Completion.Task.WaitAsync(token).ConfigureAwait(false);
                return;
            }

            var state = new WarmUpState();
            if (!_states.TryAdd(key, state))
            {
                continue;
            }

            try
            {
                await SendAsync(context, roleId, server, token).ConfigureAwait(false);
                state.MarkCompleted();
            }
            catch
            {
                _states.TryRemove(new KeyValuePair<string, WarmUpState>(key, state));
                throw;
            }
            finally
            {
                state.Completion.TrySetResult(true);
            }

            return;
        }
    }

    private bool IsFresh(string key) =>
        _states.TryGetValue(key, out var state) &&
        state.CompletedAt is { } completedAt &&
        DateTimeOffset.UtcNow - completedAt < FreshWindow;

    private static void LogSkipped(string key) =>
        DebugLog.Info("GameRecordWarmUp",
            $"[WarmUp] skipped (fresh within {FreshWindow.TotalMinutes:0}m): {key}");

    private async Task SendAsync(AccountContext context, string roleId, string server, CancellationToken token)
    {
        string playerQuery = $"role_id={Uri.EscapeDataString(roleId)}&server={Uri.EscapeDataString(server)}";
        string query = $"avatar_list_type=1&{playerQuery}";
        string url = $"{RecordApiBase}{IndexPath}?{query}";
        string cookieStr = BbsRequestBuilder.BuildCookieString(context.Cookies, BbsRequestBuilder.CookieMode.CnWeb);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        MiHoYoHeaderFactory.ApplyGameRecordHeaders(req, new GameRecordHeaderOptions(
            AppVersion: HeaderVersions.MobileCnLogin,
            UserAgent: context.UserAgent.Mobile,
            DeviceId: context.Device.BbsDeviceId,
            DeviceFp: context.Device.DeviceFp,
            DeviceName: Uri.EscapeDataString(context.Device.DeviceName),
            SysVersion: context.Device.SysVersion,
            Cookie: cookieStr,
            DsSalt: HeaderSalts.CnX4,
            SortedQuery: SortedQuery(url),
            ToolVersion: HeaderVersions.ToolVersionCn,
            Page: Page));
        req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");

        using var resp = await _httpClient.SendAsync(req, token).ConfigureAwait(false);
        string json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        int retcode = ReadRetcode(json);
        DebugLog.Info("GameRecordWarmUp",
            $"[WarmUp] index status={(int)resp.StatusCode}, retcode={retcode}");

        if (!resp.IsSuccessStatusCode || retcode != 0)
        {
            throw new InvalidOperationException(
                $"Warm-up failed: status={(int)resp.StatusCode}, retcode={retcode}.");
        }
    }

    private static string Key(string accountId, string roleId, string server) =>
        $"{accountId}|{roleId}|{server}";

    private static int ReadRetcode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("retcode", out var rc) ? rc.GetInt32() : -1;
        }
        catch (JsonException)
        {
            return -1;
        }
    }

    private static string SortedQuery(string url) =>
        string.Join("&", new Uri(url).Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(s => s, StringComparer.Ordinal));


    private sealed class WarmUpState
    {
        private long _completedAtMs;

        public TaskCompletionSource<bool> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DateTimeOffset? CompletedAt
        {
            get
            {
                long ms = Interlocked.Read(ref _completedAtMs);
                return ms == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms);
            }
        }

        public void MarkCompleted() =>
            Interlocked.Exchange(ref _completedAtMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
