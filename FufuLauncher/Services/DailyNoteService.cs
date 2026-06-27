/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
// Copyright (c) FufuLauncher Dev Team. All rights reserved.
// By kyxsan.
// Licensed under the MIT License.

using System.Diagnostics;
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
        ),

        new(
            DeviceModel:   "V2366GA",
            ProductName:   "PD2366",
            Brand:         "vivo",
            Board:         "V2366GA",
            Hardware:      "vivo",
            DeviceType:    "PD2366",
            Manufacturer:  "vivo",
            DeviceInfo:    "vivo/PD2366/PD2366:12/V417IR/1747:user/release-keys",
            OsVersion:     "12",
            SdkVersion:    "32",
            BuildId:       "V417IR",
            BuildDisplay:  "V417IR release-keys",
            BuildTime:     1779448087000L,
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
        // 首次 getFp 时发送的初始 device_fp，与 SDK 的 DeviceFingerprintSharedPreferences.newDefaultDeviceId() 一致：
        // Random(10) = 首位 1~9 + 9 位 0~9，共 10 位纯数字
        string localFp = new string(new[] { (char)('1' + Random.Shared.Next(9)) }
            .Concat(Enumerable.Range(0, 9).Select(_ => (char)('0' + Random.Shared.Next(10))))
            .ToArray());
        // seedId 和 seedTime 在 SDK 中以 JSON {"seedId":"uuid","seedTime":"ms"} 存入 SharedPreferences，
        // 解析后作为独立参数传入 fetchFingerprint，这里直接生成
        string seedId = Guid.NewGuid().ToString();
        string seedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();

        // 先向服务端请求当前需要的 ext_fields 列表（SDK 在 generateFingerprint 第一步调用 loadPropertiesList）
        var extList = await FetchExtListAsync();
        Debug.WriteLine($"[DailyNoteService] RegisterDeviceFp: 服务端 ext_list 共 {extList.Count} 个字段");

        // extFields 由当前账号的设备档案派生，与 BBSWindow.GetExtFieldValue() 的 bbs_cn (platform=2) 对齐
        var variant = GetCurrentVariant();
        var allExtFields = BuildExtFields(variant);
        // 只保留服务端要求的字段，不多发不少发
        var extFields = allExtFields.Where(kv => extList.Contains(kv.Key))
                                    .ToDictionary(kv => kv.Key, kv => kv.Value);

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
        req.Headers.Add("User-Agent", "okhttp/4.9.3");

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
                Debug.WriteLine($"[DailyNoteService] RegisterDeviceFp: 服务端返回指纹 device_fp={serverFp}");

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
                Debug.WriteLine($"[DailyNoteService] RegisterDeviceFp: 服务端未返回指纹，使用本地生成 localFp={localFp}");
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
        string fp = GetDeviceFp(cookies);
        Debug.WriteLine($"[DailyNoteService] RequestDailyNote: device_fp={fp}");

        using HttpRequestMessage req = new(HttpMethod.Get, apiUrl);
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", CNVersion);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", GenGameRecordDeviceId());
        req.Headers.Add("x-rpc-device_name", _currentDeviceName);
        req.Headers.Add("x-rpc-device_fp", fp);
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
        string fp = GetDeviceFp(cookies);
        Debug.WriteLine($"[DailyNoteService] RequestWidget: device_fp={fp}");

        using HttpRequestMessage req = new(HttpMethod.Get, WidgetUrl);
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", CNVersion);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", GenGameRecordDeviceId());
        req.Headers.Add("x-rpc-device_name", _currentDeviceName);
        req.Headers.Add("x-rpc-device_fp", fp);
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
        {
            Debug.WriteLine($"[DailyNoteService] GetDeviceFp: 使用已注册的服务端指纹 _registeredDeviceFp={_registeredDeviceFp}");
            return _registeredDeviceFp;
        }
        if (cookies.TryGetValue("DEVICEFP", out string fp) && !string.IsNullOrEmpty(fp))
        {
            Debug.WriteLine($"[DailyNoteService] GetDeviceFp: 从Cookie读取指纹 DEVICEFP={fp}");
            return fp;
        }
        string fallback = GenerateHexString(13);
        Debug.WriteLine($"[DailyNoteService] GetDeviceFp: 无可用指纹，生成本地回退指纹={fallback}");
        return fallback;
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

        int battery = rng.Next(70, 100);
        int ramRemain = rng.Next(120000, 130000);
        int sdRemain = rng.Next(110000, 130000);

        // 物理传感器：手机平放静止状态
        string accelerometer = $"{0.1 + rng.NextDouble() * 0.05:F8}x{9.78 + rng.NextDouble() * 0.04:F8}x{0.15 + rng.NextDouble() * 0.1:F8}";
        string magnetometer = $"{15 + rng.NextDouble() * 2:F3}x{-28 + rng.NextDouble() * -1:F3}x{-32 + rng.NextDouble() * -1:F3}";
        string gyroscope = "0.0x0.0x0.0";

        // timeDiff：同一账号每次 getFp 使用同一个差值
        long timeDiff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1782425023662L;

        return new Dictionary<string, object>
        {
            { "proxyStatus", 1 },
            { "isRoot", 0 },
            { "romCapacity", "512" },
            { "deviceName", v.DeviceModel },
            { "productName", v.ProductName },
            { "romRemain", rng.Next(400, 600).ToString() },
            { "hostname", v.Hostname },
            { "screenSize", "1080x1920" },
            { "isTablet", 1 },
            { "aaid", "error_1008008" },
            { "model", v.DeviceModel },
            { "brand", v.Brand },
            { "hardware", v.Hardware },
            { "deviceType", v.DeviceType },
            { "devId", "REL" },
            { "sdCapacity", rng.Next(127000, 129000) },
            { "buildTime", v.BuildTime.ToString() },
            { "buildUser", "abc" },
            { "simState", 5 },
            { "ramRemain", ramRemain.ToString() },
            { "appUpdateTimeDiff", timeDiff },
            { "deviceInfo", v.DeviceInfo },
            { "vaid", "error_1008008" },
            { "buildType", "user" },
            { "sdkVersion", v.SdkVersion },
            { "ui_mode", "UI_MODE_TYPE_NORMAL" },
            { "isMockLocation", 0 },
            { "cpuType", "arm64-v8a" },
            { "isAirMode", 0 },
            { "ringMode", 2 },
            { "chargeStatus", 1 },
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
            { "ramCapacity", (ramRemain + rng.Next(500, 1500)).ToString() },
            { "magnetometer", magnetometer },
            { "display", v.BuildDisplay },
            { "appInstallTimeDiff", timeDiff },
            { "packageVersion", "2.42.0" },
            { "gyroscope", gyroscope },
            { "batteryStatus", battery },
            { "hasKeyboard", 1 },
            { "board", v.Board },
        };
    }

  
    /// 调 /device-fp/api/getExtList 获取服务端要求的 ext_fields 字段列表。
    /// 对齐 SDK: AbstractDeviceUniqueIdentifier.generateFingerprint() → loadPropertiesList()
    
    private static async Task<HashSet<string>> FetchExtListAsync()
    {
        try
        {
            string url = $"{GetFpUrl.Replace("/api/getFp", "/api/getExtList")}?platform=2&app_name=bbs_cn";
            using HttpRequestMessage req = new(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "okhttp/4.9.3");

            HttpResponseMessage resp = await _httpClient.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();

            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out JsonElement data)
                && data.TryGetProperty("ext_list", out JsonElement extList))
            {
                var list = new HashSet<string>();
                foreach (var item in extList.EnumerateArray())
                {
                    string name = item.GetString();
                    if (name != null) list.Add(name);
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DailyNoteService] FetchExtListAsync 异常: {ex.Message}");
        }

        // 请求失败时使用硬编码的已知完整列表，确保 getFp 仍能正常进行
        return new HashSet<string>
        {
            "oaid","vaid","aaid","board","brand","hardware","cpuType","deviceType","display",
            "hostname","manufacturer","productName","model","deviceInfo","sdkVersion","osVersion",
            "devId","buildTags","buildType","buildUser","buildTime","screenSize","vendor",
            "romCapacity","romRemain","ramCapacity","ramRemain","appMemory","accelerometer",
            "gyroscope","magnetometer","isRoot","debugStatus","proxyStatus","emulatorStatus",
            "isTablet","simState","ui_mode","sdCapacity","sdRemain","hasKeyboard","isMockLocation",
            "ringMode","isAirMode","batteryStatus","chargeStatus","deviceName",
            "appInstallTimeDiff","appUpdateTimeDiff","packageName","packageVersion","networkType"
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

