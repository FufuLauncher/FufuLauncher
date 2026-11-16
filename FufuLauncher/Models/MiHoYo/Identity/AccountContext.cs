/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Models.MiHoYo.Identity;

public sealed record AccountContext(
    string AccountId,
    ServerType ServerType,
    IReadOnlyDictionary<string, string> Cookies,
    AccountIdentity Identity,
    DeviceIdentity Device,
    UserAgent UserAgent)
{
    public string Stuid => Identity.Stuid;

    public string Mid => Identity.Mid;

    public string? Stoken => Cookies.GetValueOrDefault("stoken") ?? Cookies.GetValueOrDefault("stoken_v2");
}

public sealed record AccountIdentity(string Stuid, string Mid);

public sealed record DeviceIdentity(
    string DeviceId,
    string BbsDeviceId,
    string DeviceFp,
    string DeviceName,
    string SysVersion,
    string Model,
    DateTimeOffset FpLastUpdate);

public sealed record UserAgent(string Mobile, string OkHttp);

public enum ServerType
{
    Cn,
    Os
}

public static class ServerTypeExtensions
{
    public static string ToWire(this ServerType s) => s switch
    {
        ServerType.Cn => "cn",
        ServerType.Os => "os",
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null)
    };

    public static ServerType ParseServerType(string? raw) =>
        string.Equals(raw, "os", StringComparison.OrdinalIgnoreCase)
            ? ServerType.Os
            : ServerType.Cn;
}
