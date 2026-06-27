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
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Services;

public sealed class DailyNoteService
{
    private const string CNVersion = "2.109.0";
    private const string CNX4 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string CNX6 = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    private const string ToolVersion = "v6.6.1-gr-cn";
    private const string Page = "v6.6.1-gr-cn_#/ys";
    private const string Referer = "https://webstatic.mihoyo.com";
    private const string Origin = "https://webstatic.mihoyo.com";

    private const string DailyNoteUrl = "https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/dailyNote";
    private const string WidgetUrl = "https://api-takumi-record.mihoyo.com/game_record/app/genshin/aapi/widget/v2?game_id=2";
    private const string GetFpUrl = "https://public-data-api.mihoyo.com/device-fp/api/getFp";

    // 按账号隔离：每个账号独立的 device_id + device_fp + 设备档案，防止风险账号互相影响
    private static string _currentAccountId = "";
    private static string _currentDeviceId = "";
    private static string _currentDeviceName = "";
    private static string _currentSysVersion = "";
    private static string _currentUserAgent = "";
    private static DeviceVariant _currentVariant = null!;  
    private static string _registeredDeviceFp = "";
    private static bool _fpRegistered;

    // 持久化指纹注册状态，避免进程重启后重新 getFp
    private static ILocalSettingsService? _settings;
    private const int DeviceFpMaxAgeDays = 7;

    //每个账号派生一套一致的设备特征
    private sealed record DeviceVariant(
        string DeviceModel,    
        string ProductName,    
        string Brand,          
        string Board,          
        string Hardware,       
        string DeviceType,     
        string Manufacturer,   
        string DeviceInfo,     
        string OsVersion,      
        string SdkVersion,     
        string BuildId,        
        string BuildDisplay,   
        long BuildTime,        
        string Hostname        
    );

    private static readonly DeviceVariant[] DeviceVariants =
    {
       
        new(
            DeviceModel:   "24031PN0DC",
            ProductName:   "aurora",
            Brand:         "Xiaomi",
            Board:         "24031PN0DC",
            Hardware:      "Xiaomi",
            DeviceType:    "aurora",
            Manufacturer:  "Xiaomi",
            DeviceInfo:    "Xiaomi/aurora/aurora:12/V417IR/1747:user/release-keys",
            OsVersion:     "12",
            SdkVersion:    "32",
            BuildId:       "V417IR",
            BuildDisplay:  "V417IR release-keys",
            BuildTime:     1779448087000L,
            Hostname:      "6b29a8384f29"
        ),
  
        new(
            DeviceModel:   "2211133C",
            ProductName:   "fuxi",
            Brand:         "Xiaomi",
            Board:         "2211133C",
            Hardware:      "qcom",
            DeviceType:    "fuxi",
            Manufacturer:  "Xiaomi",
            DeviceInfo:    "Xiaomi/fuxi/fuxi:14/UKQ1.230804.001/18.3.21:user/release-keys",
            OsVersion:     "14",
            SdkVersion:    "34",
            BuildId:       "UKQ1.230804.001",
            BuildDisplay:  "UKQ1.230804.001 release-keys",
            BuildTime:     1700000000000L,
            Hostname:      "dg02-pool03-kvm87"
        ),
    
        new(
            DeviceModel:   "23127PN0CC",
            ProductName:   "shennong",
            Brand:         "Xiaomi",
            Board:         "23127PN0CC",
            Hardware:      "qcom",
            DeviceType:    "shennong",
            Manufacturer:  "Xiaomi",
            DeviceInfo:    "Xiaomi/shennong/shennong:15/AP3A.240805.005/18.6.10:user/release-keys",
            OsVersion:     "15",
            SdkVersion:    "35",
            BuildId:       "AP3A.240805.005",
            BuildDisplay:  "AP3A.240805.005 release-keys",
            BuildTime:     1720000000000L,
            Hostname:      "6b29a8384f29"
        )
    };

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

            // 切换账号时重置状态，并尝试从持久化恢复指纹注册
            if (_currentAccountId != activeId)
            {
                _currentAccountId = activeId;
                _currentDeviceId = GetDeviceIdForAccount(activeId);
                InitDeviceProfile(activeId);
                _registeredDeviceFp = "";
                _fpRegistered = false;

                if (!await TryRestoreFpStateAsync(activeId))
                {
                    // 持久化状态不存在或已过期，走正常注册流程
                }
            }

            if (!_fpRegistered)
                await RegisterDeviceFpAsync(cookies, accountManager, activeId);

            string apiUrl = $"{DailyNoteUrl}?server={server}&role_id={roleId}";
            string json = await RequestDailyNoteAsync(apiUrl, cookies, null);

            int retcode = ParseRetcode(json);

            if (retcode == 1034)
            {
                // 1034 = 风控拦截，持久化指纹已失效，标记为未注册
                await InvalidateFpStateAsync(activeId);
                GeetestService geetestService = new();
                string xrpcChallenge = await geetestService.TryVerifyForDailyNoteAsync(cookies);

                if (!string.IsNullOrEmpty(xrpcChallenge))
                {
                    json = await RequestDailyNoteAsync(apiUrl, cookies, xrpcChallenge);
                    retcode = ParseRetcode(json);
                }
            }

            if (retcode == 5003 || retcode == 1034)
            {
                json = await RequestWidgetAsync(cookies);
                retcode = ParseRetcode(json);
            }

            if (retcode != 0)
            {
                string msg = ExtractMessage(json);
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

        // extFields 由当前账号的设备档案派生，与 BBSWindow.GetExtFieldValue() 的 bbs_cn (platform=2) 对齐
        var variant = GetCurrentVariant();
        var extFields = BuildExtFields(variant);

        DeviceFpRequest fpData = new()
        {
            DeviceId = _currentDeviceId,       // 当前账号的持久化 hex
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
        req.Headers.Add("User-Agent", _currentUserAgent);

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

                // 持久化指纹注册状态，进程重启后免重新 getFp
                await PersistFpStateAsync(activeId, seedTime);
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
        req.Headers.Add("x-rpc-device_name", _currentDeviceName);
        req.Headers.Add("x-rpc-device_fp", GetDeviceFp(cookies));
        req.Headers.Add("x-rpc-sys_version", _currentSysVersion);
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
        req.Headers.UserAgent.ParseAdd(_currentUserAgent);

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
        req.Headers.Add("x-rpc-device_name", _currentDeviceName);
        req.Headers.Add("x-rpc-device_fp", GetDeviceFp(cookies));
        req.Headers.Add("x-rpc-sys_version", _currentSysVersion);
        req.Headers.Add("x-rpc-page", Page);
        req.Headers.Add("X-Requested-With", "com.mihoyo.hyperion");
        req.Headers.Add("Origin", Origin);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.Add("Accept", "application/json, text/plain, */*");
        req.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
        req.Headers.UserAgent.ParseAdd(_currentUserAgent);

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

    /// <summary>返回当前账号的持久化 hex device_id</summary>
    internal static string GetDeviceId() => _currentDeviceId;

    /// <summary>返回当前账号 Game Record API 用的 UUID v3 device_id</summary>
    internal static string GetGameRecordDeviceId() => GenGameRecordDeviceId();

    /// <summary>返回当前账号的 User-Agent（供 GeetestService 等外部调用）</summary>
    internal static string GetCurrentUserAgent() => _currentUserAgent;

    /// <summary>返回当前账号的 DeviceName（x-rpc-device_name）</summary>
    internal static string GetCurrentDeviceName() => _currentDeviceName;

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
        return NameUuidFromBytes(Encoding.UTF8.GetBytes(_currentDeviceId)).ToString();
    }

    /// <summary>
    /// 按账号确定性派生 16 位 hex device_id。
    /// 账号+机器 → MD5 → hex，不依赖文件，删了也能还原。
    /// </summary>
    private static string GetDeviceIdForAccount(string accountId)
    {
        string raw = Environment.MachineName + accountId + "FufuLauncher";
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLower()[..16];
    }

    private static string GenerateHexString(int length)
    {
        Span<byte> bytes = stackalloc byte[(length + 1) / 2];
        Random.Shared.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant().Substring(0, length);
    }

    // ── 指纹注册状态持久化（跨进程免重新 getFp） ──

    private static ILocalSettingsService GetSettings()
    {
        _settings ??= App.GetService<ILocalSettingsService>();
        return _settings;
    }

    /// <summary>尝试从 SQLite 持久化恢复指纹注册状态，成功则免去 getFp 注册</summary>
    private static async Task<bool> TryRestoreFpStateAsync(string accountId)
    {
        try
        {
            var settings = GetSettings();
            var seedTimeObj = await settings.ReadSettingAsync($"DeviceFpSeedTime_{accountId}");
            if (seedTimeObj is not string seedTimeStr || !long.TryParse(seedTimeStr, out var seedTimeMs))
                return false;

            // 检查是否过期
            var age = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - seedTimeMs;
            if (age > TimeSpan.FromDays(DeviceFpMaxAgeDays).TotalMilliseconds)
                return false;

            // 从 cookies 中恢复 DEVICEFP
            var accountManager = App.GetService<AccountManager>();
            var cookies = await accountManager.LoadCookiesAsync(accountId);
            if (cookies == null || !cookies.TryGetValue("DEVICEFP", out var fp) || string.IsNullOrEmpty(fp))
                return false;

            _registeredDeviceFp = fp;
            _fpRegistered = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>getFp 注册成功后持久化时间戳，跨进程可用</summary>
    private static async Task PersistFpStateAsync(string accountId, string seedTime)
    {
        try
        {
            var settings = GetSettings();
            await settings.SaveSettingAsync($"DeviceFpSeedTime_{accountId}", seedTime);
        }
        catch
        {
            // 持久化失败不影响主流程
        }
    }

    /// <summary>API 返回 1034 时清除持久化注册状态，下次强制重新注册</summary>
    private static async Task InvalidateFpStateAsync(string accountId)
    {
        try
        {
            var settings = GetSettings();
            await settings.SaveSettingAsync($"DeviceFpSeedTime_{accountId}", "");
            _fpRegistered = false;
        }
        catch
        {
            _fpRegistered = false;
        }
    }

    /// <summary>解析 JSON 中的 retcode（using 确保 JsonDocument 及时释放）</summary>
    private static int ParseRetcode(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("retcode", out JsonElement rc) ? rc.GetInt32() : -1;
    }

    /// <summary>提取 JSON 中的 message 字段（using 确保 JsonDocument 及时释放）</summary>
    private static string ExtractMessage(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("message", out JsonElement m) ? m.GetString() : "未知错误";
    }

    /// <summary>
    /// 基于当前账号初始化设备档案（变体选择 + 动态字段）。
    /// 切换账号时由 GetDailyNoteAsync 调用。
    /// </summary>
    private static void InitDeviceProfile(string accountId)
    {
        _currentVariant = SelectVariant(accountId);

        _currentDeviceName = $"Xiaomi%20{_currentVariant.DeviceModel}";
        _currentSysVersion = _currentVariant.OsVersion;
        _currentUserAgent =
            $"Mozilla/5.0 (Linux; Android {_currentVariant.OsVersion}; {_currentVariant.DeviceModel} Build/{_currentVariant.BuildId}; wv) " +
            $"AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/110.0.5481.154 Safari/537.36 miHoYoBBS/{CNVersion}";
    }

    /// <summary>按账号确定性选择设备变体，同一账号永远得到同一变体。</summary>
    private static DeviceVariant SelectVariant(string accountId)
    {
        // Math.Abs(int.MinValue) 会抛 OverflowException，用位运算替代
        int hash = GetStableHashCode(accountId);
        int idx = (hash & int.MaxValue) % DeviceVariants.Length;
        return DeviceVariants[idx];
    }

    /// <summary>返回当前账号的变体（缓存引用，避免重复哈希）</summary>
    private static DeviceVariant GetCurrentVariant()
    {
        return _currentVariant ?? SelectVariant(_currentAccountId);
    }

    /// <summary>
    /// 为指定变体构建完整的 extFields 字典。
    /// 设备标识部分由变体确定性决定；传感器/状态部分按会话种子抖动，使每次 getFp 看起来略有不同。
    /// </summary>
    private static Dictionary<string, object> BuildExtFields(DeviceVariant v)
    {
        long sessionSeed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600;
        var rng = new Random((int)(sessionSeed & 0x7FFFFFFF));

        int battery = rng.Next(20, 100);
        int ramRemain = rng.Next(80000, 240000);
        int romRemain = rng.Next(100000, 500000);
        int sdRemain = rng.Next(100000, 400000);

        string accelerometer = $"{rng.NextDouble() * 10:F8}x{rng.NextDouble() * 10:F8}x{rng.NextDouble() * 10:F8}";
        string magnetometer = $"{rng.Next(-30, 30) + rng.NextDouble():F6}x{rng.Next(-30, 30) + rng.NextDouble():F6}x{rng.Next(-30, 30) + rng.NextDouble():F6}";
        string gyroscope = $"{rng.NextDouble() * 0.05:F9}x{rng.NextDouble() * 0.05:F9}x{rng.NextDouble() * 0.05:F9}";

        return new Dictionary<string, object>
        {
            { "proxyStatus", rng.Next(2) },
            { "isRoot", 0 },
            { "romCapacity", "512" },
            { "deviceName", v.DeviceModel },
            { "productName", v.ProductName },
            { "romRemain", romRemain },
            { "hostname", v.Hostname },
            { "screenSize", "1440x2560" },
            { "isTablet", 1 },
            { "aaid", "error_1008008" },
            { "model", v.DeviceModel },
            { "brand", v.Brand },
            { "hardware", v.Hardware },
            { "deviceType", v.DeviceType },
            { "devId", "REL" },
            { "serialNumber", "unknown" },
            { "sdCapacity", 512215 },
            { "buildTime", v.BuildTime.ToString() },
            { "buildUser", "abc" },
            { "simState", 5 },
            { "ramRemain", ramRemain },
            { "appUpdateTimeDiff", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - rng.Next(100000, 86400000) },
            { "deviceInfo", v.DeviceInfo },
            { "vaid", "error_1008008" },
            { "buildType", "user" },
            { "sdkVersion", v.SdkVersion },
            { "ui_mode", "UI_MODE_TYPE_NORMAL" },
            { "isMockLocation", 0 },
            { "cpuType", "arm64-v8a" },
            { "isAirMode", 0 },
            { "ringMode", 2 },
            { "chargeStatus", battery > 50 ? 1 : 0 },
            { "manufacturer", v.Manufacturer },
            { "emulatorStatus", 0 },
            { "appMemory", "512" },
            { "osVersion", v.OsVersion },
            { "vendor", "unknown" },
            { "accelerometer", accelerometer },
            { "sdRemain", sdRemain },
            { "buildTags", "release-keys" },
            { "packageName", "com.mihoyo.hyperion" },
            { "networkType", "WiFi" },
            { "oaid", "error_1008008" },
            { "debugStatus", 0 },
            { "ramCapacity", ramRemain + rng.Next(10000, 50000) },
            { "magnetometer", magnetometer },
            { "display", v.BuildDisplay },
            { "appInstallTimeDiff", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - rng.NextInt64(86400L, 86400L * 30) * 1000L },
            { "packageVersion", "2.42.0" },
            { "gyroscope", gyroscope },
            { "batteryStatus", battery },
            { "hasKeyboard", rng.Next(2) },
            { "board", v.Board },
        };
    }

    /// <summary>稳定的字符串哈希（MD5-based，不受运行时随机化影响）</summary>
    private static int GetStableHashCode(string str)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(str));
        return BitConverter.ToInt32(hash, 0);
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

