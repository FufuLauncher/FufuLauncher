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

        var deviceIdentity = await GetDeviceAsync(token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(deviceIdentity.DeviceFp))
        {
            throw new InvalidOperationException("Account_DeviceFpUnavailable".GetLocalized());
        }

        var profile = _deviceService.Device;
        var serverType = ServerTypeExtensions.ParseServerType(ExtractServerType(accountId));
        var accountIdentity = new AccountIdentity(
            Stuid: AccountCookieIdentity.GetUid(cookies) ?? "",
            Mid: cookies.GetValueOrDefault("mid") ?? cookies.GetValueOrDefault("account_mid_v2") ??
                 cookies.GetValueOrDefault("ltmid_v2") ?? "");


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

    internal async Task<DeviceIdentity> GetDeviceAsync(CancellationToken token = default)
    {
        var device = await _deviceFpService.GetOrRegisterAsync(token).ConfigureAwait(false);
        var profile = _deviceService.Device;
        return new DeviceIdentity(device.DeviceId, device.BbsDeviceId, device.DeviceFp,
            profile.ResolvedDisplayName, profile.OsVersion, profile.Model, DateTimeOffset.UtcNow);
    }

    private static string ExtractServerType(string accountId)
    {
        var idx = accountId.IndexOf('_');
        return idx > 0 ? accountId[..idx] : "cn";
    }

}
