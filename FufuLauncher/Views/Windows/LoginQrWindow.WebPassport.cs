/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Constants;
using Microsoft.Web.WebView2.Core;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow
{
    #region 通行证

    private async Task StartWebPassportLoginAsync()
    {
        _currentSession?.Cancel();

        UpdateStatus("正在加载通行证登录页面...", true);
        try
        {
            await PassportWebView.EnsureCoreWebView2Async();
            PassportWebView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
            PassportWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;

            PassportWebView.CoreWebView2.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
            PassportWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;

            PassportWebView.CoreWebView2.Stop();
            PassportWebView.CoreWebView2.CookieManager.DeleteAllCookies();
            try
            {
                await PassportWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
            }
            catch
            {
            }

            PassportWebView.CoreWebView2.Navigate("about:blank");

            PassportWebView.CoreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived;
            PassportWebView.CoreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;

            PassportWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            PassportWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string url = ApiEndpoints.GetWebPassportLoginUrl(timestamp);
            await Task.Delay(100);
            PassportWebView.CoreWebView2.Navigate(url);

            UpdateStatus("请在网页中完成登录验证", false, true);
        }
        catch (Exception ex)
        {
            UpdateStatus($"加载通行证网页失败: {ex.Message}", false);
        }
    }

    private void CoreWebView2_ContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var allowedItems = new HashSet<string>
        {
            "selectAll",
            "copy",
            "cut",
            "paste"
        };

        for (int i = e.MenuItems.Count - 1; i >= 0; i--)
        {
            if (!allowedItems.Contains(e.MenuItems[i].Name))
            {
                e.MenuItems.RemoveAt(i);
            }
        }
    }

    private async void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        string script = @"
        document.body.style.overflow = 'hidden';
        document.documentElement.style.overflow = 'hidden';
        document.body.style.width = '100vw';
        document.body.style.height = '100vh';
        document.body.style.margin = '0';
        document.body.style.padding = '0';
    ";
        await PassportWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private async void CoreWebView2_WebResourceResponseReceived(object sender,
        CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            string uri = e.Request.Uri;

            if (uri.Contains(ApiEndpoints.PassportWebLoginByPasswordPath) ||
                uri.Contains(ApiEndpoints.PassportWebLoginByMobileCaptchaPath) ||
                uri.Contains(ApiEndpoints.PassportWebQueryQrLoginStatusPath))
            {
                if (e.Response.StatusCode == 200)
                {
                    var cookies =
                        await PassportWebView.CoreWebView2.CookieManager.GetCookiesAsync(ApiEndpoints.MihoyoWebBase);
                    var cookieDict = new Dictionary<string, string>();

                    foreach (var cookie in cookies)
                    {
                        cookieDict[cookie.Name] = cookie.Value;
                    }

                    bool hasLoginToken = cookieDict.ContainsKey("cookie_token") ||
                                         cookieDict.ContainsKey("cookie_token_v2");
                    bool hasAccountId = cookieDict.ContainsKey("ltuid") || cookieDict.ContainsKey("stuid");

                    if (hasLoginToken && hasAccountId)
                    {
                        try
                        {
                            PassportWebView.CoreWebView2.WebResourceResponseReceived -=
                                CoreWebView2_WebResourceResponseReceived;
                        }
                        catch (ObjectDisposedException)
                        {
                        }

                        bool enqueued = DispatcherQueue.TryEnqueue(() =>
                        {
                            try
                            {
                                UpdateStatus("凭证提取成功", true);
                                OnLoginSuccess(cookieDict, "cn");
                            }
                            catch (Exception ex)
                            {
                                UpdateStatus($"处理失败: {ex.Message}", false);
                            }
                        });

                        if (!enqueued)
                        {
                            DebugLog.Info("LoginQrWindow.WebPassport",
                                "Cannot dispatch save operation to UI thread, window may be closed.");
                        }
                    }
                    else if (hasLoginToken && !hasAccountId)
                    {
                        DebugLog.Info("LoginQrWindow.WebPassport",
                            "WebView2 cookie missing account ID (ltuid/stuid), waiting for subsequent events...");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("LoginQrWindow.WebPassport", $"WebResourceResponseReceived handling exception: {ex.Message}");
        }
    }

    #endregion
}