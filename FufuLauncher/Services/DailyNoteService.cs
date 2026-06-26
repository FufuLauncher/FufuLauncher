/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
// Copyright (c) FufuLauncher Dev Team. All rights reserved.
// By kyxsan.
// Licensed under the MIT License.

using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.Services;

public sealed class DailyNoteService
{
    private const string CNVersion = "2.95.1";
    private const string CNX4 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string CNX6 = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    private const string ToolVersion = "v5.0.1-ys";
    private const string MobileUserAgent = $"Mozilla/5.0 (Linux; Android 15) Mobile miHoYoBBS/{CNVersion}";
    private const string Referer = "https://webstatic.mihoyo.com";

    private const string DailyNoteUrl = "https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/dailyNote";
    private const string WidgetUrl = "https://api-takumi-record.mihoyo.com/game_record/app/genshin/aapi/widget/v2?game_id=2";
    private const string GetFpUrl = "https://public-data-api.mihoyo.com/device-fp/api/getFp";

    private static readonly string DeviceId = Guid.NewGuid().ToString();
    private static string _registeredDeviceFp;
    private static bool _fpRegistered;

    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly HttpClient _httpClient = new();

    public async Task<DailyNoteCardData> GetDailyNoteAsync(string roleId, string server)
    {
        await _semaphore.WaitAsync();
        try
        {
            AccountManager accountManager = App.GetService<AccountManager>();
            string activeId = accountManager.ActiveAccountId;
            if (activeId == null)
                throw new InvalidOperationException("无活跃账号");

            Dictionary<string, string> cookies = await accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0)
                throw new InvalidOperationException("无法加载Cookie");

            if (!_fpRegistered)
                await RegisterDeviceFpAsync();

            string apiUrl = $"{DailyNoteUrl}?server={server}&role_id={roleId}";
            string json = await RequestDailyNoteAsync(apiUrl, cookies, null);

            JsonDocument doc = JsonDocument.Parse(json);
            int retcode = doc.RootElement.TryGetProperty("retcode", out JsonElement rc) ? rc.GetInt32() : -1;

            if (retcode == 1034)
            {
                GeetestService geetestService = new();
                string xrpcChallenge = await geetestService.TryVerifyForDailyNoteAsync(cookies);

                if (!string.IsNullOrEmpty(xrpcChallenge))
                {
                    json = await RequestDailyNoteAsync(apiUrl, cookies, xrpcChallenge);
                    doc = JsonDocument.Parse(json);
                    retcode = doc.RootElement.TryGetProperty("retcode", out rc) ? rc.GetInt32() : -1;
                }
            }

            if (retcode == 5003 || retcode == 1034)
            {
                json = await RequestWidgetAsync(cookies);
                doc = JsonDocument.Parse(json);
                retcode = doc.RootElement.TryGetProperty("retcode", out rc) ? rc.GetInt32() : -1;
            }

            if (retcode != 0)
            {
                string msg = doc.RootElement.TryGetProperty("message", out JsonElement m) ? m.GetString() : "未知错误";
                throw new InvalidOperationException($"获取便签失败: {msg} (retcode={retcode})");
            }

            return DailyNoteParser.Parse(json);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static async Task RegisterDeviceFpAsync()
    {
        string localFp = GenerateHexString(13);
        string device = GenerateAlphaNumString(12);
        string product = GenerateAlphaNumString(6);

        Dictionary<string, object> extFields = new()
        {
            { "proxyStatus", 0 },
            { "isRoot", 0 },
            { "romCapacity", "512" },
            { "deviceName", device },
            { "productName", product },
            { "romRemain", "512" },
            { "hostname", "dg02-pool03-kvm87" },
            { "screenSize", "1440x2905" },
            { "isTablet", 0 },
            { "aaid", "" },
            { "model", device },
            { "brand", "XiaoMi" },
            { "hardware", "qcom" },
            { "deviceType", "OP5913L1" },
            { "devId", "REL" },
            { "serialNumber", "unknown" },
            { "sdCapacity", 512215 },
            { "buildTime", "1693626947000" },
            { "buildUser", "android-build" },
            { "simState", 5 },
            { "ramRemain", "239814" },
            { "appUpdateTimeDiff", 1702604034482 },
            { "deviceInfo", $"XiaoMi/{product}/OP5913L1:14/SKQ1.221119.001/T.118e6c7-5aa23-73911:user/release-keys" },
            { "vaid", "" },
            { "buildType", "user" },
            { "sdkVersion", "34" },
            { "ui_mode", "UI_MODE_TYPE_NORMAL" },
            { "isMockLocation", 0 },
            { "cpuType", "arm64-v8a" },
            { "isAirMode", 0 },
            { "ringMode", 2 },
            { "chargeStatus", 1 },
            { "manufacturer", "XiaoMi" },
            { "emulatorStatus", 0 },
            { "appMemory", "512" },
            { "osVersion", "14" },
            { "vendor", "unknown" },
            { "accelerometer", "1.4883357x7.1712894x6.2847486" },
            { "sdRemain", 239600 },
            { "buildTags", "release-keys" },
            { "packageName", "com.mihoyo.hyperion" },
            { "networkType", "WiFi" },
            { "oaid", "" },
            { "debugStatus", 1 },
            { "ramCapacity", "469679" },
            { "magnetometer", "20.081251x-27.487501x2.1937501" },
            { "display", $"{product}_14.1.0.181(CN01)" },
            { "appInstallTimeDiff", 1688455751496 },
            { "packageVersion", "2.20.1" },
            { "gyroscope", "0.030226856x0.014647375x0.010652636" },
            { "batteryStatus", 100 },
            { "hasKeyboard", 0 },
            { "board", "taro" },
        };

        DeviceFpRequest fpData = new()
        {
            DeviceId = GenerateHexString(16),
            SeedId = Guid.NewGuid().ToString(),
            Platform = "2",
            SeedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
            ExtFields = JsonSerializer.Serialize(extFields),
            AppName = "bbs_cn",
            BbsDeviceId = DeviceId,
            DeviceFp = localFp
        };

        string bodyJson = JsonSerializer.Serialize(fpData);

        using HttpRequestMessage req = new(HttpMethod.Post, GetFpUrl);
        req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        try
        {
            HttpResponseMessage resp = await _httpClient.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out JsonElement data)
                && data.TryGetProperty("device_fp", out JsonElement fp))
            {
                _registeredDeviceFp = fp.GetString();
                _fpRegistered = true;
            }
            else
            {
                _registeredDeviceFp = localFp;
                _fpRegistered = true;
            }
        }
        catch
        {
            _registeredDeviceFp = localFp;
            _fpRegistered = true;
        }
    }

    private static async Task<string> RequestDailyNoteAsync(string apiUrl, Dictionary<string, string> cookies, string xrpcChallenge)
    {
        string cookieStr = BuildCookieString(cookies, CookieMode.Cookie);
        string query = new Uri(apiUrl).Query.TrimStart('?');
        string sortedQuery = string.Join("&", query.Split('&').OrderBy(s => s, StringComparer.Ordinal));
        string ds = CalculateDS2(CNX4, sortedQuery, "");

        using HttpRequestMessage req = new(HttpMethod.Get, apiUrl);
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", CNVersion);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", DeviceId);
        req.Headers.Add("x-rpc-device_fp", GetDeviceFp(cookies));
        req.Headers.Add("x-rpc-tool_verison", ToolVersion);
        if (!string.IsNullOrEmpty(xrpcChallenge))
            req.Headers.Add("x-rpc-challenge", xrpcChallenge);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.UserAgent.ParseAdd(MobileUserAgent);

        HttpResponseMessage resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private static async Task<string> RequestWidgetAsync(Dictionary<string, string> cookies)
    {
        string cookieStr = BuildCookieString(cookies, CookieMode.SToken);
        string sortedQuery = string.Join("&", WidgetUrl.Split('?', 2)[1].Split('&').OrderBy(s => s, StringComparer.Ordinal));
        string ds = CalculateDS2(CNX6, sortedQuery, "");

        using HttpRequestMessage req = new(HttpMethod.Get, WidgetUrl);
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", CNVersion);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", DeviceId);
        req.Headers.Add("x-rpc-device_fp", GetDeviceFp(cookies));
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.UserAgent.ParseAdd(MobileUserAgent);

        HttpResponseMessage resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    internal static string CalculateDS2(string salt, string query, string body)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int rand = Random.Shared.Next(100000, 200000);
        string r = (rand == 100000 ? 642367 : rand).ToString();
        string input = $"salt={salt}&t={t}&r={r}&b={body}&q={query}";
        string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        return $"{t},{r},{hash}";
    }

    internal static string BuildCookieString(Dictionary<string, string> cookies, CookieMode mode)
    {
        StringBuilder sb = new();

        if (mode == CookieMode.SToken)
        {
            if (cookies.TryGetValue("stoken", out string stoken) && !string.IsNullOrEmpty(stoken))
                sb.Append($"stoken={stoken}");
            if (cookies.TryGetValue("mid", out string mid) && !string.IsNullOrEmpty(mid))
                sb.Append($";mid={mid}");
            string stuid = cookies.GetValueOrDefault("stuid")
                ?? cookies.GetValueOrDefault("account_id")
                ?? cookies.GetValueOrDefault("ltuid_v2")
                ?? "";
            if (!string.IsNullOrEmpty(stuid))
                sb.Append($";stuid={stuid}");
        }
        else
        {
            foreach (KeyValuePair<string, string> kv in cookies)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    if (sb.Length > 0) sb.Append(';');
                    sb.Append($"{kv.Key}={kv.Value}");
                }
            }
        }

        return sb.ToString();
    }

    internal static string GetDeviceFp(Dictionary<string, string> cookies)
    {
        if (!string.IsNullOrEmpty(_registeredDeviceFp))
            return _registeredDeviceFp;
        if (cookies.TryGetValue("DEVICEFP", out string fp) && !string.IsNullOrEmpty(fp))
            return fp;
        return GenerateHexString(13);
    }

    internal static string GetDeviceId()
    {
        return DeviceId;
    }

    private static string GenerateHexString(int length)
    {
        Span<byte> bytes = stackalloc byte[(length + 1) / 2];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, length);
    }

    private static string GenerateAlphaNumString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        char[] result = new char[length];
        for (int i = 0; i < length; i++)
            result[i] = chars[Random.Shared.Next(chars.Length)];
        return new string(result);
    }

    internal enum CookieMode
    {
        Cookie,
        SToken
    }

    private sealed class DeviceFpRequest
    {
        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; }

        [JsonPropertyName("seed_id")]
        public string SeedId { get; set; }

        [JsonPropertyName("platform")]
        public string Platform { get; set; }

        [JsonPropertyName("seed_time")]
        public string SeedTime { get; set; }

        [JsonPropertyName("ext_fields")]
        public string ExtFields { get; set; }

        [JsonPropertyName("app_name")]
        public string AppName { get; set; }

        [JsonPropertyName("bbs_device_id")]
        public string BbsDeviceId { get; set; }

        [JsonPropertyName("device_fp")]
        public string DeviceFp { get; set; }
    }
}

