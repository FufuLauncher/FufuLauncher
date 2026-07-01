/* Copyright (c) FufuLauncher Dev Team. All rights reserved. Licensed under the MIT License. */
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Services.MiHoYo;

public sealed class DailyNoteService
{
    private readonly IDeviceFingerprintService _fingerprintService;

    private const string CNVersion = "2.109.0";
    private const string CNX4 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string CNX6 = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    private const string ToolVersion = "v6.6.1-gr-cn";
    private const string Page = "v6.6.1-gr-cn_#/ys";
    private const string Referer = "https://webstatic.mihoyo.com";
    private const string Origin = "https://webstatic.mihoyo.com";
    private const string DailyNoteUrl = "https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/dailyNote";
    private const string WidgetUrl = "https://api-takumi-record.mihoyo.com/game_record/app/genshin/aapi/widget/v2?game_id=2";


    private string _currentAccountId = "";
    private string _currentDeviceId = "";
    private string _currentDeviceName = "";
    private string _currentSysVersion = "";
    private string _currentUserAgent = "";

    private static readonly DeviceProfileService _deviceProfileService = new();
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };


    private static DailyNoteService? _currentInstance;

    public DailyNoteService()
    {
        _fingerprintService = App.GetService<IDeviceFingerprintService>()
            ?? throw new InvalidOperationException("无法获取设备指纹服务");
        _currentInstance = this;
    }

    public async Task<DailyNoteCardData> GetDailyNoteAsync(string roleId, string server)
    {
        await _semaphore.WaitAsync();
        try
        {
            AccountManager accountManager = App.GetService<AccountManager>();
            string activeId = accountManager.ActiveAccountId ?? throw new InvalidOperationException("无活跃账号");
            Dictionary<string, string> cookies = await accountManager.LoadCookiesAsync(activeId)
                ?? throw new InvalidOperationException("无法加载Cookie");

        
            if (_currentAccountId != activeId)
            {
                _currentAccountId = activeId;
                _currentDeviceId = DeviceProfileService.GetDeviceIdForAccount(activeId);
                InitDeviceProfile(activeId);
            }

            string deviceFp = await _fingerprintService.GetOrRegisterFingerprintAsync(activeId, cookies);

            string apiUrl = $"{DailyNoteUrl}?server={Uri.EscapeDataString(server)}&role_id={Uri.EscapeDataString(roleId)}";
            string json = await RequestDailyNoteAsync(apiUrl, cookies, null, deviceFp);
            int retcode = ParseRetcode(json);

            if (retcode == 1034)
            {
         
                await _fingerprintService.ResetFingerprintAsync(activeId);
                deviceFp = await _fingerprintService.GetOrRegisterFingerprintAsync(activeId, cookies);

                GeetestService geetestService = new();
                string xrpcChallenge = await geetestService.TryVerifyForDailyNoteAsync(cookies);
                if (!string.IsNullOrEmpty(xrpcChallenge))
                {
                    json = await RequestDailyNoteAsync(apiUrl, cookies, xrpcChallenge, deviceFp);
                    retcode = ParseRetcode(json);
                }
            }

            if (retcode == 5003 || retcode == 1034)
            {
                json = await RequestWidgetAsync(cookies, deviceFp);
                retcode = ParseRetcode(json);
            }

            if (retcode != 0)
                throw new InvalidOperationException($"获取便签失败: {ExtractMessage(json)} (retcode={retcode})");

            return DailyNoteParser.Parse(json);
        }
        finally { _semaphore.Release(); }
    }

    private async Task<string> RequestDailyNoteAsync(string apiUrl, Dictionary<string, string> cookies, string? xrpcChallenge, string deviceFp)
    {
        string cookieStr = BuildCookieString(cookies, CookieMode.Cookie);
        string query = new Uri(apiUrl).Query.TrimStart('?');
        string sortedQuery = string.Join("&", query.Split('&').OrderBy(s => s, StringComparer.Ordinal));
        string ds = CalculateDS2(CNX4, sortedQuery, "");

        using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", CNVersion);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", GenGameRecordDeviceId());
        req.Headers.Add("x-rpc-device_name", _currentDeviceName);
        req.Headers.Add("x-rpc-device_fp", deviceFp);
        req.Headers.Add("x-rpc-sys_version", _currentSysVersion);
        req.Headers.Add("x-rpc-tool_verison", ToolVersion);
        req.Headers.Add("x-rpc-page", Page);
        req.Headers.Add("X-Requested-With", "com.mihoyo.hyperion");
        req.Headers.Add("Origin", Origin);
        if (!string.IsNullOrEmpty(xrpcChallenge)) req.Headers.Add("x-rpc-challenge", xrpcChallenge);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.Add("Accept", "application/json, text/plain, */*");
        req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
        req.Headers.UserAgent.ParseAdd(_currentUserAgent);

        var resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> RequestWidgetAsync(Dictionary<string, string> cookies, string deviceFp)
    {
        string cookieStr = BuildCookieString(cookies, CookieMode.SToken);
        string sortedQuery = string.Join("&", WidgetUrl.Split('?', 2)[1].Split('&').OrderBy(s => s, StringComparer.Ordinal));
        string ds = CalculateDS2(CNX6, sortedQuery, "");

        using var req = new HttpRequestMessage(HttpMethod.Get, WidgetUrl);
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", CNVersion);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", GenGameRecordDeviceId());
        req.Headers.Add("x-rpc-device_name", _currentDeviceName);
        req.Headers.Add("x-rpc-device_fp", deviceFp);
        req.Headers.Add("x-rpc-sys_version", _currentSysVersion);
        req.Headers.Add("x-rpc-page", Page);
        req.Headers.Add("X-Requested-With", "com.mihoyo.hyperion");
        req.Headers.Add("Origin", Origin);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.Add("Accept", "application/json, text/plain, */*");
        req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
        req.Headers.UserAgent.ParseAdd(_currentUserAgent);

        var resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    internal static string CalculateDS2(string salt, string query, string body)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int rand = Random.Shared.Next(100000, 200000);
        string r = (rand == 100000 ? 642367 : rand).ToString();
        string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"salt={salt}&t={t}&r={r}&b={body}&q={query}"))).ToLowerInvariant();
        return $"{t},{r},{hash}";
    }

    internal static string BuildCookieString(Dictionary<string, string> cookies, CookieMode mode)
    {
        var sb = new StringBuilder();
        if (mode == CookieMode.SToken)
        {
            if (cookies.TryGetValue("stoken", out var stoken) && !string.IsNullOrEmpty(stoken)) sb.Append($"stoken={stoken}");
            if (cookies.TryGetValue("mid", out var mid) && !string.IsNullOrEmpty(mid)) sb.Append($";mid={mid}");
            string stuid = cookies.GetValueOrDefault("stuid") ?? cookies.GetValueOrDefault("account_id") ?? cookies.GetValueOrDefault("ltuid_v2") ?? "";
            if (!string.IsNullOrEmpty(stuid)) sb.Append($";stuid={stuid}");
        }
        else
        {
            foreach (var kv in cookies)
                if (!string.IsNullOrEmpty(kv.Value)) { if (sb.Length > 0) sb.Append(';'); sb.Append($"{kv.Key}={kv.Value}"); }
        }
        return sb.ToString();
    }


    internal static string GetDeviceId() => _currentInstance?._currentDeviceId ?? "";
    internal static string GetGameRecordDeviceId() => GenGameRecordDeviceId();
    internal static string GetCurrentUserAgent() => _currentInstance?._currentUserAgent ?? "";
    internal static string GetCurrentDeviceName() => _currentInstance?._currentDeviceName ?? "";

    private static Guid NameUuidFromBytes(byte[] name)
    {
        byte[] hash = MD5.HashData(name);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(new byte[] { hash[3], hash[2], hash[1], hash[0], hash[5], hash[4], hash[7], hash[6], hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15] });
    }

    private static string GenGameRecordDeviceId() =>
        NameUuidFromBytes(Encoding.UTF8.GetBytes(_currentInstance?._currentDeviceId ?? "")).ToString();

    private void InitDeviceProfile(string accountId)
    {
        var profile = _deviceProfileService.SelectProfile(accountId);
        _currentDeviceName = profile.DeviceName;
        _currentSysVersion = profile.SysVersion;
        _currentUserAgent = profile.UserAgent;
    }

    private static int ParseRetcode(string json)
    {
        using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty("retcode", out var rc) ? rc.GetInt32() : -1;
    }
    private static string ExtractMessage(string json)
    {
        using var doc = JsonDocument.Parse(json); return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "未知错误" : "未知错误";
    }

    internal enum CookieMode
    {
        Cookie, SToken
    }
}