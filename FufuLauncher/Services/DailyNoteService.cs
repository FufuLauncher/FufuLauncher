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
    private const string CNVersion = "2.109.0";
    private const string CNX4 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string CNX6 = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    private const string ToolVersion = "v6.6.1-gr-cn";
    private const string Page = "v6.6.1-gr-cn_#/ys";
    private const string SysVersion = "12";
    private const string DeviceName = "Xiaomi%2024031PN0DC";
    private const string MobileUserAgent = $"Mozilla/5.0 (Linux; Android 12; 24031PN0DC Build/V417IR; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/110.0.5481.154 Safari/537.36 miHoYoBBS/{CNVersion}";
    private const string Referer = "https://webstatic.mihoyo.com";
    private const string Origin = "https://webstatic.mihoyo.com";

    private const string DailyNoteUrl = "https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/dailyNote";
    private const string WidgetUrl = "https://api-takumi-record.mihoyo.com/game_record/app/genshin/aapi/widget/v2?game_id=2";
    private const string GetFpUrl = "https://public-data-api.mihoyo.com/device-fp/api/getFp";

    private static readonly string DeviceId = GetPersistentDeviceId();
    private static string _registeredDeviceFp;
    private static bool _fpRegistered;

    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

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
                await RegisterDeviceFpAsync(cookies, accountManager, activeId);

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

    /// <summary>
    /// 向 getFp 注册设备指纹，参数与 BBSWindow 保持一致。
    /// 注册成功后写入 cookies 并持久化，与 BBSWindow 共享绑定关系。
    /// </summary>
    private static async Task RegisterDeviceFpAsync(Dictionary<string, string> cookies, AccountManager accountManager, string activeId)
    {
        string localFp = GenerateHexString(13);
        string seedId = Guid.NewGuid().ToString();
        string seedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        // extFields 与 BBSWindow.GetExtFieldValue() 的 bbs_cn (platform=2) 对齐
        Dictionary<string, object> extFields = new()
        {
            { "proxyStatus", 1 },
            { "isRoot", 1 },
            { "romCapacity", "512" },
            { "deviceName", "24031PN0DC" },
            { "productName", "aurora" },
            { "romRemain", "478" },
            { "hostname", "6b29a8384f29" },
            { "screenSize", "1440x2560" },
            { "isTablet", 1 },
            { "aaid", "error_1008008" },
            { "model", "24031PN0DC" },
            { "brand", "Xiaomi" },
            { "hardware", "Xiaomi" },
            { "deviceType", "aurora" },
            { "devId", "REL" },
            { "serialNumber", "unknown" },
            { "sdCapacity", 127991 },
            { "buildTime", "1779448087000" },
            { "buildUser", "abc" },
            { "simState", 5 },
            { "ramRemain", "126327" },
            { "appUpdateTimeDiff", 1782396402635L },
            { "deviceInfo", "Xiaomi/aurora/aurora:12/V417IR/1747:user/release-keys" },
            { "vaid", "error_1008008" },
            { "buildType", "user" },
            { "sdkVersion", "32" },
            { "ui_mode", "UI_MODE_TYPE_NORMAL" },
            { "isMockLocation", 0 },
            { "cpuType", "arm64-v8a" },
            { "isAirMode", 0 },
            { "ringMode", 2 },
            { "chargeStatus", 1 },
            { "manufacturer", "Xiaomi" },
            { "emulatorStatus", 0 },
            { "appMemory", "512" },
            { "osVersion", "12" },
            { "vendor", "unknown" },
            { "accelerometer", "0.10001241x9.800007x0.1999938" },
            { "sdRemain", 119757 },
            { "buildTags", "release-keys" },
            { "packageName", "com.mihoyo.hyperion" },
            { "networkType", "WiFi" },
            { "oaid", "error_1008008" },
            { "debugStatus", 0 },
            { "ramCapacity", "127991" },
            { "magnetometer", "15.625x-28.25x-32.625" },
            { "display", "V417IR release-keys" },
            { "appInstallTimeDiff", 1782396402635L },
            { "packageVersion", "2.42.0" },
            { "gyroscope", "0.0x0.0x0.0" },
            { "batteryStatus", 79 },
            { "hasKeyboard", 1 },
            { "board", "24031PN0DC" },
        };

        DeviceFpRequest fpData = new()
        {
            DeviceId = DeviceId,              // 持久化 hex，与 x-rpc-device_id 原始标识一致
            SeedId = seedId,
            Platform = "2",
            SeedTime = seedTime,
            ExtFields = JsonSerializer.Serialize(extFields),
            AppName = "bbs_cn",
            BbsDeviceId = GenGameRecordDeviceId(), // UUID v3，与 api-takumi 请求头一致
            DeviceFp = localFp
        };

        string bodyJson = JsonSerializer.Serialize(fpData);

        using HttpRequestMessage req = new(HttpMethod.Post, GetFpUrl);
        req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        req.Headers.Add("User-Agent", MobileUserAgent);

        try
        {
            HttpResponseMessage resp = await _httpClient.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            // 优先从根级读取（SDK 方式），其次 data 嵌套
            string serverFp = null;
            if (doc.RootElement.TryGetProperty("device_fp", out JsonElement rootFp))
                serverFp = rootFp.GetString();
            else if (doc.RootElement.TryGetProperty("data", out JsonElement data)
                     && data.TryGetProperty("device_fp", out JsonElement nestedFp))
                serverFp = nestedFp.GetString();

            if (!string.IsNullOrEmpty(serverFp))
            {
                _registeredDeviceFp = serverFp;

                // 写入 cookies 并持久化，与 BBSWindow 共享绑定关系
                cookies["DEVICEFP"] = serverFp;
                cookies["DEVICEFP_SEED_ID"] = seedId;
                cookies["DEVICEFP_SEED_TIME"] = seedTime;
                await accountManager.UpdateCookiesAsync(activeId, cookies);
            }
            else
            {
                _registeredDeviceFp = localFp;
            }
        }
        catch
        {
            _registeredDeviceFp = localFp;
        }
        finally
        {
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
        req.Headers.Add("x-rpc-device_id", GenGameRecordDeviceId());
        req.Headers.Add("x-rpc-device_name", DeviceName);
        req.Headers.Add("x-rpc-device_fp", GetDeviceFp(cookies));
        req.Headers.Add("x-rpc-sys_version", SysVersion);
        req.Headers.Add("x-rpc-tool_verison", ToolVersion);
        req.Headers.Add("x-rpc-page", Page);
        req.Headers.Add("X-Requested-With", "com.mihoyo.hyperion");
        req.Headers.Add("Origin", Origin);
        if (!string.IsNullOrEmpty(xrpcChallenge))
            req.Headers.Add("x-rpc-challenge", xrpcChallenge);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.Add("Accept", "application/json, text/plain, */*");
        req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
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
        req.Headers.Add("x-rpc-device_id", GenGameRecordDeviceId());
        req.Headers.Add("x-rpc-device_name", DeviceName);
        req.Headers.Add("x-rpc-device_fp", GetDeviceFp(cookies));
        req.Headers.Add("x-rpc-sys_version", SysVersion);
        req.Headers.Add("x-rpc-page", Page);
        req.Headers.Add("X-Requested-With", "com.mihoyo.hyperion");
        req.Headers.Add("Origin", Origin);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.Add("Accept", "application/json, text/plain, */*");
        req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
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

    /// <summary>返回持久化 hex device_id（getFp 注册用）</summary>
    internal static string GetDeviceId()
    {
        return DeviceId;
    }

    /// <summary>返回 Game Record API 请求使用的 UUID v3 device_id</summary>
    internal static string GetGameRecordDeviceId()
    {
        return GenGameRecordDeviceId();
    }

    /// <summary>
    /// 复制 Java UUID.nameUUIDFromBytes() 行为，与 BBSWindow.NameUuidFromBytes 一致。
    /// </summary>
    private static Guid NameUuidFromBytes(byte[] name)
    {
        byte[] hash = MD5.HashData(name);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // UUID v3
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant

        return new Guid(new byte[] {
            hash[3], hash[2], hash[1], hash[0],
            hash[5], hash[4],
            hash[7], hash[6],
            hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]
        });
    }

    /// <summary>Game Record API (client_type=5) 用 UUID v3 派生 device_id</summary>
    private static string GenGameRecordDeviceId()
    {
        return NameUuidFromBytes(Encoding.UTF8.GetBytes(DeviceId)).ToString();
    }

    /// <summary>
    /// 持久化 device_id，由机器名+用户名确定性派生（与 BBSWindow 逻辑一致）。
    /// 文件删除后也能还原为相同值。
    /// </summary>
    private static string GetPersistentDeviceId()
    {
        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher", "Data");
        var path = System.IO.Path.Combine(dir, "device_id.txt");
        try
        {
            if (System.IO.File.Exists(path))
            {
                var existing = System.IO.File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(existing)) return existing;
            }
        }
        catch { }

        // 文件不存在或为空 → 由机器特征确定性生成，删了也能还原
        string raw = Environment.MachineName + Environment.UserName + "FufuLauncher";
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        string id = Convert.ToHexString(hash).ToLower()[..16];

        try
        {
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(path, id);
        }
        catch { }

        return id;
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
