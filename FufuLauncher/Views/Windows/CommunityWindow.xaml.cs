/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services;

namespace FufuLauncher.Views;

public sealed partial class CommunityWindow : Window
{
    private const string CommunityNoticeKey = "HasShownCommunityNotice";

    private const string UserDataBridgeScript = @"
            window.fufuApp = {
                requestUserData: function() {
                    return new Promise((resolve, reject) => {
                        const callbackId = 'cb_' + Date.now() + '_' + Math.random().toString(36).substring(2, 11);
                        
                        const listener = (event) => {
                            if (event.data && event.data.callbackId === callbackId) {
                                window.chrome.webview.removeEventListener('message', listener);
                                if (event.data.success) {
                                    resolve(event.data.data);
                                } else {
                                    reject(new Error(event.data.error));
                                }
                            }
                        };
                        window.chrome.webview.addEventListener('message', listener);
                        
                        const requestPayload = {
                            type: 'requestUserData',
                            callbackId: callbackId
                        };
                        window.chrome.webview.postMessage(JSON.stringify(requestPayload));
                    });
                }
            };
        ";

    public CommunityWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        SetTitleBar(AppTitleBar);

        WindowManagerHelper.ResizeWithDpi(AppWindow, this, 1200, 800);
        WindowManagerHelper.CenterWindowOnScreen(AppWindow, 1200, 800);
    }

    private async void CommunityWebView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await CheckAndShowFirstTimeNoticeAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CommunityWindow", $"First-visit prompt exception: {ex.Message}");
        }

        await CommunityWebView.EnsureCoreWebView2Async();

        if (CommunityWebView.CoreWebView2 == null)
        {
            DebugLog.Info("CommunityWindow", "CoreWebView2 initialization failed");
            return;
        }

        CommunityWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;

        try
        {
            CommunityWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
        }
        catch (InvalidCastException ex)
        {
            DebugLog.Info("CommunityWindow",
                $"ContextMenuRequested event unsupported (WebView2 version too old): {ex.Message}");
        }

        CommunityWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

        await CommunityWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(UserDataBridgeScript);
        UpdateNavigationState();
    }

    private async Task CheckAndShowFirstTimeNoticeAsync()
    {
        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var hasShownObj = await localSettingsService.ReadSettingAsync(CommunityNoticeKey);

            bool hasShown = hasShownObj != null && Convert.ToBoolean(hasShownObj);

            if (!hasShown)
            {
                if (Content?.XamlRoot == null)
                {
                    DebugLog.Info("CommunityWindow", "XamlRoot not ready, skipping first-visit prompt");
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "CommunityPage_WelcomeTitle".GetLocalized(),
                    Content = "CommunityPage_WelcomeDesc".GetLocalized(),
                    CloseButtonText = "GotItBtn".GetLocalized(),
                    XamlRoot = Content.XamlRoot
                };

                await dialog.ShowAsync();

                await localSettingsService.SaveSettingAsync(CommunityNoticeKey, true);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CommunityWindow", $"Failed to show first-visit prompt: {ex.Message}");
        }
    }

    private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string message = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message)) return;

            var request = JsonSerializer.Deserialize<JsonElement>(message);
            if (request.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "requestUserData")
            {
                string callbackId = request.GetProperty("callbackId").GetString() ?? string.Empty;

                var userData = await RetrieveUserDataAsync();

                string unknownText = "CommunityPage_Unknown".GetLocalized();
                string dialogContent = "CommunityPage_DataRequest".GetLocalized() + "\n\n" +
                                       $"MID: {(string.IsNullOrEmpty(userData.Mid) ? unknownText : userData.Mid)}\n" +
                                       $"UID: {(string.IsNullOrEmpty(userData.Uid) ? unknownText : userData.Uid)}\n" +
                                       string.Format("CommunityPage_AccountNickname_Format".GetLocalized(), string.IsNullOrEmpty(userData.Nickname) ? unknownText : userData.Nickname) + "\n\n" +
                                       "CommunityPage_ConsentPrompt".GetLocalized();

                if (Content?.XamlRoot == null) return;

                var dialog = new ContentDialog
                {
                    Title = "CommunityPage_AuthTitle".GetLocalized(),
                    Content = dialogContent,
                    PrimaryButtonText = "CommunityPage_Agree".GetLocalized(),
                    CloseButtonText = "CommunityPage_Deny".GetLocalized(),
                    XamlRoot = Content.XamlRoot
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    var response = new
                    {
                        callbackId,
                        success = true,
                        data = new
                        {
                            mid = userData.Mid,
                            uid = userData.Uid,
                            nickname = userData.Nickname
                        }
                    };
                    CommunityWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                }
                else
                {
                    var response = new
                    {
                        callbackId,
                        success = false,
                        error = "User denied the authorization request."
                    };
                    CommunityWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CommunityWindow", $"WebMessage handling exception: {ex.Message}");
        }
    }

    private async Task<(string Mid, string Uid, string Nickname)> RetrieveUserDataAsync()
    {
        string mid = string.Empty;
        string uid = string.Empty;
        string nickname = string.Empty;

        try
        {
            var accountManager = App.GetService<AccountManager>();
            var activeAccount = accountManager.GetActiveAccountEntry();

            if (activeAccount != null)
            {
                uid = activeAccount.GameUid ?? string.Empty;
                nickname = activeAccount.Nickname ?? string.Empty;

                var cookies = await accountManager.LoadCookiesAsync(activeAccount.Id);
                if (cookies != null && cookies.TryGetValue("mid", out var midValue))
                    mid = midValue ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CommunityWindow", $"Failed to extract user data: {ex.Message}");
        }

        return (mid, uid, nickname);
    }

    private void CommunityWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        LoadingBar.Visibility = Visibility.Visible;
    }

    private void CommunityWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingBar.Visibility = Visibility.Collapsed;
        UpdateNavigationState();
    }

    private void UpdateNavigationState()
    {
        var coreWebView = CommunityWebView.CoreWebView2;
        BackButton.IsEnabled = coreWebView != null && coreWebView.CanGoBack;
        ForwardButton.IsEnabled = coreWebView != null && coreWebView.CanGoForward;
    }

    private void CoreWebView2_ContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        var menuList = args.MenuItems;
        var itemsToKeep = new List<CoreWebView2ContextMenuItem>();

        foreach (var item in menuList)
        {
            if (item.Name == "copy" || item.Name == "paste" || item.Name == "cut" || item.Name == "selectAll")
            {
                itemsToKeep.Add(item);
            }
        }

        menuList.Clear();
        foreach (var item in itemsToKeep)
        {
            menuList.Add(item);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityWebView.CanGoBack)
        {
            CommunityWebView.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityWebView.CanGoForward)
        {
            CommunityWebView.GoForward();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommunityWebView.CoreWebView2 == null)
        {
            await CommunityWebView.EnsureCoreWebView2Async();
        }

        CommunityWebView.Reload();
    }
}