/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class BBSWindow
{
    #region 设备指纹与账号 Cookie

    private Dictionary<string, string> cookieDic = new();

    private string _activeDeviceFp = string.Empty;

    private async Task EnsureDeviceFpAsync()
    {
        try
        {
            var identity = await _deviceFpService.GetOrRegisterAsync();
            _deviceId = identity.BbsDeviceId;
            _activeDeviceFp = identity.DeviceFp;
            DebugLog.Info("BBSWindow.Cookies", $"[BBSWindow] Device identity acquired: device_fp={_activeDeviceFp}");

            if (!string.IsNullOrEmpty(_activeDeviceFp))
            {
                cookieDic["DEVICEFP"] = _activeDeviceFp;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("BBSWindow.Cookies", $"[BBSWindow] EnsureDeviceFpAsync failed: {ex.Message}");
        }
    }

    private async Task LoadPageAsync(string url)
    {
        DebugLog.Info("BBSWindow.Cookies", $"[BBSWindow] LoadPageAsync called with URL: {url}");
        await LoadActiveAccountCookiesAsync();

        var manager = BBSWebView.CoreWebView2.CookieManager;
        if (BBSWebView.Source == null || BBSWebView.Source.ToString() == "about:blank")
        {
            var cookies = await manager.GetCookiesAsync(ApiEndpoints.WebstaticRefererUrl);
            foreach (var c in cookies) manager.DeleteCookie(c);
        }

        foreach (var kv in cookieDic)
        {
            var cookie = manager.CreateCookie(kv.Key, kv.Value, ApiEndpoints.MihoyoCookieDomain, "/");
            manager.AddOrUpdateCookie(cookie);
        }

        DebugLog.Info("BBSWindow.Cookies", $"[BBSWindow] Added {cookieDic.Count} cookies to WebView2");
        BBSWebView.CoreWebView2.Navigate(url);
    }

    private async Task LoadActiveAccountCookiesAsync()
    {
        cookieDic.Clear();

        var accountManager = App.GetService<AccountManager>();
        var activeId = accountManager.ActiveAccountId;
        if (activeId == null) return;

        var cookies = await accountManager.LoadCookiesAsync(activeId);
        if (cookies == null || cookies.Count == 0) return;

        foreach (var kv in cookies)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            {
                cookieDic[kv.Key] = kv.Value;
            }
        }
    }

    private void ParseCookie(string cookieStr)
    {
        cookieDic.Clear();
        if (string.IsNullOrWhiteSpace(cookieStr)) return;
        foreach (var item in cookieStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = item.Split('=', 2);
            if (kv.Length == 2) cookieDic[kv[0].Trim()] = kv[1].Trim();
        }
    }

    #endregion
}