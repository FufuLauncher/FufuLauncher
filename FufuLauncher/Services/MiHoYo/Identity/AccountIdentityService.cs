/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.Device;
using FufuLauncher.Services.MiHoYo.Fingerprint;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.MiHoYo;

public sealed class AccountIdentityService
{
    private readonly AccountManager _accountManager;
    private readonly DeviceFpService _deviceFpService;
    private readonly MobileDeviceService _deviceService;

    public AccountIdentityService(
        AccountManager accountManager,
        DeviceFpService deviceFpService,
        MobileDeviceService deviceService)
    {
        _accountManager = accountManager;
        _deviceFpService = deviceFpService;
        _deviceService = deviceService;
    }

    public async Task<AccountContext> BuildAsync(string accountId, CancellationToken token = default)
    {
        var cookies = await _accountManager.LoadCookiesAsync(accountId);
        if (cookies == null)
        {
            cookies = new Dictionary<string, string>();
            DebugLog.Info("AccountIdentityService",
                $"[AccountIdentity] No cookies found for account {accountId}, returning empty ctx");
        }

        var device = await _deviceFpService.GetOrRegisterAsync(token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(device.DeviceFp))
        {
            throw new InvalidOperationException("Account_DeviceFpUnavailable".GetLocalized());
        }

        var profile = _deviceService.Device;
        var serverType = ServerTypeExtensions.ParseServerType(ExtractServerType(accountId));
        var accountIdentity = new AccountIdentity(
            Stuid: ExtractStuid(cookies, serverType),
            Mid: cookies.GetValueOrDefault("mid") ?? "");


        var deviceIdentity = new DeviceIdentity(
            DeviceId: device.DeviceId,
            BbsDeviceId: device.BbsDeviceId,
            DeviceFp: device.DeviceFp,
            DeviceName: profile.ResolvedDisplayName,
            SysVersion: profile.OsVersion,
            Model: profile.Model,
            FpLastUpdate: DateTimeOffset.UtcNow);

        var ua = new UserAgent(
            Mobile: string.Format(
                UserAgents.AndroidBbsTemplate,
                profile.OsVersion,
                profile.Model,
                profile.BuildId,
                HeaderVersions.MobileCnLogin),
            OkHttp: UserAgents.OkHttp);

        return new AccountContext(
            AccountId: accountId,
            ServerType: serverType,
            Cookies: cookies,
            Identity: accountIdentity,
            Device: deviceIdentity,
            UserAgent: ua);
    }

    private static string ExtractServerType(string accountId)
    {
        var idx = accountId.IndexOf('_');
        return idx > 0 ? accountId[..idx] : "cn";
    }

    private static string ExtractStuid(Dictionary<string, string> cookies, ServerType serverType)
    {
        if (serverType == ServerType.Cn)
        {
            if (cookies.TryGetValue("ltuid", out var ltuid) && !string.IsNullOrEmpty(ltuid))
                return ltuid;
            if (cookies.TryGetValue("stuid", out var stuid) && !string.IsNullOrEmpty(stuid))
                return stuid;
        }
        else
        {
            if (cookies.TryGetValue("ltuid_v2", out var ltuidV2) && !string.IsNullOrEmpty(ltuidV2))
                return ltuidV2;
            if (cookies.TryGetValue("stuid", out var stuid) && !string.IsNullOrEmpty(stuid))
                return stuid;
        }

        return "";
    }
}