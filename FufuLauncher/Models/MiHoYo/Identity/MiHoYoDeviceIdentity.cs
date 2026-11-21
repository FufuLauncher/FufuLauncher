/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace FufuLauncher.Models.MiHoYo.Identity;

public sealed record MiHoYoDeviceIdentity
{
    public const int DeviceIdLength = 16;

    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version
    {
        get;
        init;
    } = CurrentVersion;

    [JsonPropertyName("device_id")]
    public string DeviceId
    {
        get;
        init;
    } = "";

    [JsonPropertyName("device_fp")]
    public string DeviceFp
    {
        get;
        init;
    } = "";

    [JsonPropertyName("seed_id")]
    public string SeedId
    {
        get;
        init;
    } = "";

    [JsonPropertyName("seed_time")]
    public string SeedTime
    {
        get;
        init;
    } = "";

    [JsonPropertyName("fingerprint_updated_at")]
    public DateTimeOffset? FingerprintUpdatedAt
    {
        get;
        init;
    }

    [JsonIgnore] public string BbsDeviceId => NameUuidFromBytes(Encoding.UTF8.GetBytes(DeviceId)).ToString();


    [JsonIgnore]
    public bool IsUsable =>
        IsValidDeviceId(DeviceId)
        && IsValidSeedId(SeedId)
        && IsValidSeedTime(SeedTime);

    public static bool IsValidDeviceId(string? deviceId) =>
        deviceId is { Length: DeviceIdLength } && deviceId.All(Uri.IsHexDigit);

    public static bool IsValidSeedId(string? seedId) =>
        Guid.TryParse(seedId, out _);

    public static bool IsValidSeedTime(string? seedTime) =>
        long.TryParse(seedTime, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
        && value > 0;

    public static MiHoYoDeviceIdentity CreateNew(string? deviceId = null) => new()
    {
        DeviceId = IsValidDeviceId(deviceId)
            ? deviceId!
            : Convert.ToHexString(RandomNumberGenerator.GetBytes(DeviceIdLength / 2)).ToLowerInvariant(),
        SeedId = Guid.NewGuid().ToString(),
        SeedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
    };

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
}