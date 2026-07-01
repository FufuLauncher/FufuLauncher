using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services.MiHoYo;

namespace FufuLauncher.Services.MiHoYo;

internal sealed class DeviceFingerprintService : IDeviceFingerprintService
{
    #region 
    private const string GetFpUrl = "https://public-data-api.mihoyo.com/device-fp/api/getFp";
    private const string GetExtListUrl = "https://public-data-api.mihoyo.com/device-fp/api/getExtList";

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };


    private string _registeredDeviceFp = "";
    private bool _fpRegistered;
    private string _currentAccountId = "";

    private readonly ILocalSettingsService _settings;
    #endregion

    #region 
    public DeviceFingerprintService(ILocalSettingsService settings)
    {
        _settings = settings;
    }

    public string? GetCurrentFingerprint(string accountId) =>
    _currentAccountId == accountId && !string.IsNullOrEmpty(_registeredDeviceFp)
        ? _registeredDeviceFp
        : null;

    public async Task<string> GetOrRegisterFingerprintAsync(string accountId, Dictionary<string, string> cookies)
    {
       
        if (_currentAccountId != accountId)
        {
            _fpRegistered = false;
            _registeredDeviceFp = "";
            _currentAccountId = accountId;
        }

        if (_fpRegistered && !string.IsNullOrEmpty(_registeredDeviceFp))
            return _registeredDeviceFp;

        if (await TryRestoreFpStateAsync(accountId))
        {
            _fpRegistered = true;
            return _registeredDeviceFp;
        }

        await RegisterDeviceFpAsync(accountId, cookies);
        return _registeredDeviceFp;
    }

    public async Task ResetFingerprintAsync(string accountId)
    {
        if (_currentAccountId == accountId)
        {
            _fpRegistered = false;
            _registeredDeviceFp = "";
        }
    }
    #endregion

    #region 设备档案
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
        new("24031PN0DC", "aurora",   "Xiaomi", "24031PN0DC", "Xiaomi", "aurora", "Xiaomi",
            "Xiaomi/aurora/aurora:12/V417IR/1747:user/release-keys", "12", "32", "V417IR", "V417IR release-keys", 1779448087000L, "6b29a8384f29"),
        new("2211133C",  "fuxi",     "Xiaomi", "2211133C",  "qcom",   "fuxi",  "Xiaomi",
            "Xiaomi/fuxi/fuxi:14/UKQ1.230804.001/18.3.21:user/release-keys", "14", "34", "UKQ1.230804.001", "UKQ1.230804.001 release-keys", 1700000000000L, "dg02-pool03-kvm87"),
        new("23127PN0CC","shennong", "Xiaomi", "23127PN0CC","qcom",   "shennong","Xiaomi",
            "Xiaomi/shennong/shennong:15/AP3A.240805.005/18.6.10:user/release-keys", "15", "35", "AP3A.240805.005", "AP3A.240805.005 release-keys", 1720000000000L, "6b29a8384f29"),
        new("V2366GA",  "PD2366",   "vivo",   "V2366GA",  "vivo",   "PD2366","vivo",
            "vivo/PD2366/PD2366:12/V417IR/1747:user/release-keys", "12", "32", "V417IR", "V417IR release-keys", 1779448087000L, "6b29a8384f29")
    };

    private static DeviceVariant SelectVariant(string accountId)
    {
        int hash = GetStableHashCode(accountId);
        int idx = (hash & int.MaxValue) % DeviceVariants.Length;
        return DeviceVariants[idx];
    }

    private static int GetStableHashCode(string str)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(str));
        return BitConverter.ToInt32(hash, 0);
    }
    #endregion

    #region ext_fields 构建
    private static Dictionary<string, object> BuildExtFields(DeviceVariant v)
    {
        long sessionSeed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600;
        var rng = new Random((int)(sessionSeed & 0x7FFFFFFF));

        int battery = rng.Next(70, 100);
        int ramRemain = rng.Next(120000, 130000);
        int sdRemain = rng.Next(110000, 130000);
        string accelerometer = $"{0.1 + rng.NextDouble() * 0.05:F8}x{9.78 + rng.NextDouble() * 0.04:F8}x{0.15 + rng.NextDouble() * 0.1:F8}";
        string magnetometer = $"{15 + rng.NextDouble() * 2:F3}x{-28 + rng.NextDouble() * -1:F3}x{-32 + rng.NextDouble() * -1:F3}";
        string gyroscope = "0.0x0.0x0.0";
        long timeDiff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1782425023662L;

        return new Dictionary<string, object>
        {
            { "proxyStatus", 1 }, { "isRoot", 0 }, { "romCapacity", "512" },
            { "deviceName", v.DeviceModel }, { "productName", v.ProductName },
            { "romRemain", rng.Next(400, 600).ToString() }, { "hostname", v.Hostname },
            { "screenSize", "1080x1920" }, { "isTablet", 1 }, { "aaid", "error_1008008" },
            { "model", v.DeviceModel }, { "brand", v.Brand }, { "hardware", v.Hardware },
            { "deviceType", v.DeviceType }, { "devId", "REL" },
            { "sdCapacity", rng.Next(127000, 129000) }, { "buildTime", v.BuildTime.ToString() },
            { "buildUser", "abc" }, { "simState", 5 }, { "ramRemain", ramRemain.ToString() },
            { "appUpdateTimeDiff", timeDiff }, { "deviceInfo", v.DeviceInfo },
            { "vaid", "error_1008008" }, { "buildType", "user" }, { "sdkVersion", v.SdkVersion },
            { "ui_mode", "UI_MODE_TYPE_NORMAL" }, { "isMockLocation", 0 }, { "cpuType", "arm64-v8a" },
            { "isAirMode", 0 }, { "ringMode", 2 }, { "chargeStatus", 1 },
            { "manufacturer", v.Manufacturer }, { "emulatorStatus", 0 }, { "appMemory", "512" },
            { "osVersion", v.OsVersion }, { "vendor", "unknown" }, { "accelerometer", accelerometer },
            { "sdRemain", sdRemain }, { "buildTags", "release-keys" },
            { "packageName", "com.mihoyo.hyperion" }, { "networkType", "WiFi" },
            { "oaid", "error_1008008" }, { "debugStatus", 0 },
            { "ramCapacity", (ramRemain + rng.Next(500, 1500)).ToString() },
            { "magnetometer", magnetometer }, { "display", v.BuildDisplay },
            { "appInstallTimeDiff", timeDiff }, { "packageVersion", "2.42.0" },
            { "gyroscope", gyroscope }, { "batteryStatus", battery }, { "hasKeyboard", 1 },
            { "board", v.Board }
        };
    }
    #endregion

    #region ext_list 请求
    private static async Task<HashSet<string>> FetchExtListAsync()
    {
        try
        {
            string url = $"{GetExtListUrl}?platform=2&app_name=bbs_cn";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "okhttp/4.9.3");
            using var resp = await _httpClient.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("ext_list", out var extList))
            {
                var list = new HashSet<string>();
                foreach (var item in extList.EnumerateArray())
                    if (item.GetString() is string name) list.Add(name);
                return list;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceFingerprint] FetchExtListAsync 异常: {ex.Message}");
        }
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
    #endregion

    #region 种子管理
    private async Task<(string seedId, string seedTime)> GetOrCreateSeedAsync(string accountId)
    {
        string seedJson = (await _settings.ReadSettingAsync($"DeviceFpSeed_{accountId}")) as string ?? "";
        if (!string.IsNullOrEmpty(seedJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(seedJson);
                string id = doc.RootElement.GetProperty("seedId").GetString() ?? "";
                string time = doc.RootElement.GetProperty("seedTime").GetString() ?? "";
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(time))
                    return (id, time);
            }
            catch { }
        }

        string newId = Guid.NewGuid().ToString();
        string newTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var newSeed = JsonSerializer.Serialize(new { seedId = newId, seedTime = newTime });
        await _settings.SaveSettingAsync($"DeviceFpSeed_{accountId}", newSeed);
        Debug.WriteLine($"[DeviceFingerprint] 生成新种子 seedId={newId}");
        return (newId, newTime);
    }
    #endregion

    #region 设备标识生成
    private async Task<string> GetOrCreateBbsDeviceIdAsync(string accountId)
    {
        string cached = (await _settings.ReadSettingAsync($"DeviceFpBbsDeviceId_{accountId}")) as string ?? "";
        if (!string.IsNullOrEmpty(cached))
            return cached;

        string newId = Guid.NewGuid().ToString();
        await _settings.SaveSettingAsync($"DeviceFpBbsDeviceId_{accountId}", newId);
        Debug.WriteLine($"[DeviceFingerprint] 生成新 bbs_device_id={newId}");
        return newId;
    }

    private static string GetDeviceIdForAccount(string accountId)
    {
        string raw = Environment.MachineName + accountId + "FufuLauncher";
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLower()[..16];
    }

    private async Task<string> GetOrCacheDeviceIdAsync(string accountId)
    {
        string cached = (await _settings.ReadSettingAsync($"DeviceFpDeviceId_{accountId}")) as string ?? "";
        if (!string.IsNullOrEmpty(cached))
        {
            Debug.WriteLine($"[DeviceFingerprint] 从持久化加载 device_id={cached}");
            return cached;
        }

        string newId = GetDeviceIdForAccount(accountId);
        await _settings.SaveSettingAsync($"DeviceFpDeviceId_{accountId}", newId);
        Debug.WriteLine($"[DeviceFingerprint] device_id={newId} 已持久化");
        return newId;
    }
    #endregion

    #region 主注册流程
    private async Task RegisterDeviceFpAsync(string accountId, Dictionary<string, string> cookies)
    {
        string defaultFp = GenerateDefaultDeviceId();
        string errorFp = GenerateErrorDeviceId();
        var (seedId, seedTime) = await GetOrCreateSeedAsync(accountId);
        string bbsDeviceId = await GetOrCreateBbsDeviceIdAsync(accountId);

        var variant = SelectVariant(accountId);

        var extList = await FetchExtListAsync();
        var allFields = BuildExtFields(variant);
        var filtered = allFields.Where(kv => extList.Contains(kv.Key))
                                .ToDictionary(kv => kv.Key, kv => kv.Value);

        var fpData = new DeviceFpRequest
        {
            DeviceId = await GetOrCacheDeviceIdAsync(accountId),
            SeedId = seedId,
            Platform = "2",
            SeedTime = seedTime,
            ExtFields = JsonSerializer.Serialize(filtered, _jsonOptions),
            AppName = "bbs_cn",
            BbsDeviceId = bbsDeviceId,
            DeviceFp = defaultFp
        };

        string bodyJson = JsonSerializer.Serialize(fpData, _jsonOptions);
        Debug.WriteLine($"[DeviceFingerprint] >>> 请求体: {bodyJson.Substring(0, Math.Min(bodyJson.Length, 800))}");

        string serverFp = null;
        if (string.IsNullOrEmpty(serverFp))
        {
            Debug.WriteLine("[DeviceFingerprint] 使用 HttpClient 请求 device_fp");
            serverFp = await TryGetFpViaHttpAsync(bodyJson);
        }

        if (!string.IsNullOrEmpty(serverFp))
        {
            _registeredDeviceFp = serverFp;
            cookies["DEVICEFP"] = serverFp;
            cookies["DEVICEFP_SEED_ID"] = seedId;
            cookies["DEVICEFP_SEED_TIME"] = seedTime;

            try
            {
                var accountManager = App.GetService<AccountManager>();
                await accountManager.UpdateCookiesAsync(accountId, cookies);
                Debug.WriteLine($"[DeviceFingerprint] Cookie 已持久化, DEVICEFP={serverFp}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DeviceFingerprint] Cookie 持久化失败: {ex.Message}");
            }
            await PersistFpStateAsync(accountId, seedTime);
        }
        else
        {
            _registeredDeviceFp = errorFp;
            Debug.WriteLine($"[DeviceFingerprint] 请求失败，使用 errorFp={errorFp}");
        }
        _fpRegistered = true;
    }
    #endregion

    #region 网络请求（TLS 代理工具 / HttpClient 回退）
    //private static async Task<string?> TryGetFpViaProxyAsync(string bodyJson)
    //{
    //    string? exePath = FindExePath("getfp.exe");
    //    if (exePath == null) return null;
    //    Debug.WriteLine($"[DeviceFingerprint] 使用 getfp.exe: {exePath}");
    //    try
    //    {
    //        var psi = new ProcessStartInfo
    //        {
    //            FileName = exePath,
    //            RedirectStandardInput = true,
    //            RedirectStandardOutput = true,
    //            UseShellExecute = false,
    //            CreateNoWindow = true
    //        };
    //        using var proc = new Process { StartInfo = psi };
    //        proc.Start();
    //        byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
    //        await proc.StandardInput.BaseStream.WriteAsync(bodyBytes);
    //        await proc.StandardInput.BaseStream.FlushAsync();
    //        proc.StandardInput.Close();
    //        string response = await proc.StandardOutput.ReadToEndAsync();
    //        if (!proc.WaitForExit(10000)) { proc.Kill(); return null; }
    //        return ParseFpResponse(response);
    //    }
    //    catch (Exception ex)
    //    {
    //        Debug.WriteLine($"[DeviceFingerprint] getfp.exe 异常: {ex.Message}");
    //        return null;
    //    }
    //}

    private static async Task<string?> TryGetFpViaHttpAsync(string bodyJson)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, GetFpUrl);
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            req.Headers.Add("User-Agent", "okhttp/4.9.3");
            Debug.WriteLine($"[DeviceFingerprint] >>> HttpClient POST {GetFpUrl}");
            using var resp = await _httpClient.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();
            Debug.WriteLine($"[DeviceFingerprint] <<< HttpClient 状态码: {(int)resp.StatusCode}, 响应: {json.Substring(0, Math.Min(json.Length, 500))}");
            return ParseFpResponse(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceFingerprint] <<< HttpClient 异常: {ex.Message}");
            return null;
        }
    }
    #endregion

    #region 响应解析
    private static string? ParseFpResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("retcode", out var rc))
                Debug.WriteLine($"[DeviceFingerprint] ParseFpResponse: retcode={rc.GetInt32()}");
            if (doc.RootElement.TryGetProperty("device_fp", out var rootFp))
            {
                string fp = rootFp.GetString() ?? "";
                Debug.WriteLine($"[DeviceFingerprint] ParseFpResponse: 根级 device_fp={fp}");
                return string.IsNullOrEmpty(fp) ? null : fp;
            }
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("device_fp", out var nestedFp))
            {
                string fp = nestedFp.GetString() ?? "";
                Debug.WriteLine($"[DeviceFingerprint] ParseFpResponse: data.device_fp={fp}");
                return string.IsNullOrEmpty(fp) ? null : fp;
            }
            Debug.WriteLine($"[DeviceFingerprint] ParseFpResponse: 未找到 device_fp");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceFingerprint] ParseFpResponse 解析异常: {ex.Message}");
            return null;
        }
    }
    #endregion

    #region 持久化
    private async Task<bool> TryRestoreFpStateAsync(string accountId)
    {
        var seedTime = await _settings.ReadSettingAsync($"DeviceFpSeedTime_{accountId}");
        if (seedTime is string st && !string.IsNullOrEmpty(st))
        {
          
            var savedFp = await _settings.ReadSettingAsync($"DeviceFpFingerprint_{accountId}");
            if (savedFp is string sfp && !string.IsNullOrEmpty(sfp))
            {
                _registeredDeviceFp = sfp;
                _currentAccountId = accountId;
                _fpRegistered = true;
                Debug.WriteLine($"[DeviceFingerprint] 从持久化恢复指纹={sfp}");
                return true;
            }
      
            try
            {
                var accountManager = App.GetService<AccountManager>();
                var cookies = await accountManager.LoadCookiesAsync(accountId);
                if (cookies != null && cookies.TryGetValue("DEVICEFP", out var fp) && !string.IsNullOrEmpty(fp))
                {
                    _registeredDeviceFp = fp;
                    _currentAccountId = accountId;
                    _fpRegistered = true;
                    Debug.WriteLine($"[DeviceFingerprint] 从 Cookie 恢复指纹 device_fp={fp}");
                    return true;
                }
            }
            catch { }
     
            await _settings.SaveSettingAsync($"DeviceFpSeedTime_{accountId}", "");
            Debug.WriteLine("[DeviceFingerprint] 有 seedTime 但无指纹，已清除 seedTime，需要重新注册");
        }
        return false;
    }

    private async Task PersistFpStateAsync(string accountId, string seedTime)
    {
        await _settings.SaveSettingAsync($"DeviceFpSeedTime_{accountId}", seedTime);
        if (!string.IsNullOrEmpty(_registeredDeviceFp))
        {
            await _settings.SaveSettingAsync($"DeviceFpFingerprint_{accountId}", _registeredDeviceFp);
        }
    }
    #endregion

    #region 工具方法
    private static string GenerateDefaultDeviceId()
    {
        var rng = Random.Shared;
        return new string(new[] { (char)('1' + rng.Next(9)) }.Concat(Enumerable.Range(0, 9).Select(_ => (char)('0' + rng.Next(10)))).ToArray());
    }

    private static string GenerateErrorDeviceId()
    {
        var rng = Random.Shared;
        return new string(new[] { (char)('1' + rng.Next(9)) }.Concat(Enumerable.Range(0, 10).Select(_ => (char)('0' + rng.Next(10)))).ToArray());
    }

    //private static string? FindExePath(string exeName)
    //{
    //    string exePath = Path.Combine(AppContext.BaseDirectory, exeName);
    //    if (File.Exists(exePath)) return exePath;
    //    string dir = AppContext.BaseDirectory;
    //    for (int i = 0; i < 5; i++)
    //    {
    //        string candidate = Path.Combine(dir, "tools", "getfp", exeName);
    //        if (File.Exists(candidate)) return candidate;
    //        string parent = Path.GetDirectoryName(dir);
    //        if (string.IsNullOrEmpty(parent)) break;
    //        dir = parent;
    //    }
    //    return null;
    //}
    #endregion

    #region 内部数据模型
    private sealed record DeviceFpRequest
    {
        [JsonPropertyName("device_id")] public string DeviceId { get; set; } = "";
        [JsonPropertyName("seed_id")] public string SeedId { get; set; } = "";
        [JsonPropertyName("platform")] public string Platform { get; set; } = "";
        [JsonPropertyName("seed_time")] public string SeedTime { get; set; } = "";
        [JsonPropertyName("ext_fields")] public string ExtFields { get; set; } = "";
        [JsonPropertyName("app_name")] public string AppName { get; set; } = "";
        [JsonPropertyName("bbs_device_id")] public string BbsDeviceId { get; set; } = "";
        [JsonPropertyName("device_fp")] public string DeviceFp { get; set; } = "";
    }
    #endregion
}