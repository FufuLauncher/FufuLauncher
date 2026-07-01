/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Security.Cryptography;
using System.Text;

namespace FufuLauncher.Services.MiHoYo;

// 米游社设备档案服务：统一管理设备变体选择、ext_fields 构建、device_id 生成。
public sealed class DeviceProfileService
{
    internal sealed record DeviceVariant(
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

    //按账号确定设备
    internal DeviceProfile SelectProfile(string accountId)
    {
        int hash = GetStableHashCode(accountId);
        int idx = (hash & int.MaxValue) % DeviceVariants.Length;
        var v = DeviceVariants[idx];

        return new DeviceProfile
        {
            DeviceId = GetDeviceIdForAccount(accountId),
            DeviceName = $"Xiaomi%20{v.DeviceModel}",
            SysVersion = v.OsVersion,
            UserAgent = $"Mozilla/5.0 (Linux; Android {v.OsVersion}; {v.DeviceModel} Build/{v.BuildId}; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/110.0.5481.154 Safari/537.36 miHoYoBBS/2.109.0",
        };
    }
    //构建完整的 ext_fields 字典
    internal Dictionary<string, object> BuildExtFields(DeviceVariant v)
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

    //MD5 哈希
    public static int GetStableHashCode(string str)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(str));
        return BitConverter.ToInt32(hash, 0);
    }

    //MD5 稳定 device_id
    public static string GetDeviceIdForAccount(string accountId)
    {
        string raw = Environment.MachineName + accountId + "FufuLauncher";
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLower()[..16];
    }
}

public sealed record DeviceProfile
{
    public required string DeviceId { get; init; }
    public required string DeviceName { get; init; }
    public required string SysVersion { get; init; }
    public required string UserAgent { get; init; }
}
