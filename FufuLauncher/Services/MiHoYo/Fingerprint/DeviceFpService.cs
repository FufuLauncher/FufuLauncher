/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Models.MiHoYo.Fingerprint;
using FufuLauncher.Services.MiHoYo.Networking;

namespace FufuLauncher.Services.MiHoYo.Fingerprint;

public sealed class DeviceFpService
{
    private const string GetFpUrl = "https://public-data-api.mihoyo.com/device-fp/api/getFp";
    private const string AppName = "bbs_cn";
    private const string Platform = "2";

    // 进程级 UUID
    private static readonly string ProcessUuid = Guid.NewGuid().ToString();

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AccountManager _accountManager;
    
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public DeviceFpService(AccountManager accountManager)
    {
        _accountManager = accountManager;
    }
    
    public string? GetCurrentDeviceId(string accountId) =>
        _accountManager.LoadFingerprint(accountId)?.BbsDeviceId;
    
    public async Task<string?> GetFingerprintAsync(string accountId)
    {
        var req = await GetFingerprintRequestAsync(accountId);
        return req?.DeviceFp;
    }
    
    public async Task<DeviceFpRequest?> GetFingerprintRequestAsync(string accountId)
    {
        var sem = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            var saved = await _accountManager.LoadFingerprintAsync(accountId);
            if (saved is not null && !string.IsNullOrEmpty(saved.DeviceFp) && !string.IsNullOrEmpty(saved.DeviceId))
            {
                
                saved = saved with { SeedId = ProcessUuid };

                if (string.IsNullOrEmpty(saved.BbsDeviceId))
                {
                    saved = saved with { BbsDeviceId = NameUuidFromBytes(Encoding.UTF8.GetBytes(saved.DeviceId)).ToString() };
                    await _accountManager.SaveFingerprintAsync(accountId, saved);
                    Debug.WriteLine($"[DeviceFp] 补全 bbs_device_id: {saved.BbsDeviceId}");
                }
                else
                {
                    Debug.WriteLine($"[DeviceFp] 命中已保存指纹: {saved.DeviceFp}");
                }
                return saved;
            }

            var request = BuildNewRequest();
            string? fp = await RegisterAsync(request);
            if (string.IsNullOrEmpty(fp))
            {
                Debug.WriteLine("[DeviceFp] 注册失败，未获得指纹");
                return null;
            }

            var persisted = request with { DeviceFp = fp };
            await _accountManager.SaveFingerprintAsync(accountId, persisted);
            Debug.WriteLine($"[DeviceFp] 注册成功并已持久化: {fp}");
            return persisted;
        }
        finally
        {
            sem.Release();
        }
    }
    
    private static DeviceFpRequest BuildNewRequest()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long firstInstall = now - Random.Shared.NextInt64(30, 120) * 86_400_000L;
        long lastUpdate = firstInstall + Random.Shared.NextInt64(0, 30) * 86_400_000L;
        if (lastUpdate > now)
            lastUpdate = now;
        
        var deviceId = GenerateRandomHex(16);

        var extFields = new ExtFields
        {
            AppInstallTimeDiff = firstInstall,
            AppUpdateTimeDiff = lastUpdate,
        };

        return new DeviceFpRequest
        {
            DeviceId = deviceId,
            SeedId = ProcessUuid,
            SeedTime = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
            Platform = Platform,
            DeviceFp = GenerateDefaultDeviceId(),
            AppName = AppName,
            ExtFields = JsonSerializer.Serialize(extFields, _jsonOptions),
            BbsDeviceId = NameUuidFromBytes(Encoding.UTF8.GetBytes(deviceId)).ToString(),
        };
    }
    
    private static async Task<string?> RegisterAsync(DeviceFpRequest request)
    {
        try
        {
            var bodyJson = JsonSerializer.Serialize(request, _jsonOptions);
            using var req = new HttpRequestMessage(HttpMethod.Post, GetFpUrl);
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            MiHoYoHeaderFactory.ApplyDeviceFpHeaders(req);

            using var resp = await _httpClient.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            Debug.WriteLine($"[DeviceFp] getFp 状态码: {(int)resp.StatusCode}, 响应: {Truncate(json, 300)}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("retcode", out var rc) && rc.GetInt32() != 0)
            {
                Debug.WriteLine($"[DeviceFp] getFp retcode={rc.GetInt32()}");
                return null;
            }

            if (root.TryGetProperty("data", out var data)
                && data.TryGetProperty("device_fp", out var fpProp))
            {
                var fp = fpProp.GetString();
                return string.IsNullOrEmpty(fp) ? null : fp;
            }

            Debug.WriteLine("[DeviceFp] getFp 响应未包含 device_fp");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DeviceFp] getFp 异常: {ex.Message}");
            return null;
        }
    }


    private static string GenerateDefaultDeviceId() => GenerateRandomHex(13);
    private static string GenerateRandomHex(int length)
    {
        var bytes = RandomNumberGenerator.GetBytes((length + 1) / 2);
        return Convert.ToHexString(bytes).ToLowerInvariant()[..length];
    }
    
    private static Guid NameUuidFromBytes(byte[] name)
    {
        var hash = MD5.HashData(name);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        Array.Reverse(hash, 0, 4);
        Array.Reverse(hash, 4, 2);
        Array.Reverse(hash, 6, 2);
        return new Guid(hash);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
