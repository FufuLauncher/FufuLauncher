using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using FufuLauncher.ViewModels;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services;

namespace FufuLauncher.Views;

public sealed partial class CommunityPage : Page
{
    private const string CommunityNoticeKey = "HasShownCommunityNotice";

    public CommunityViewModel ViewModel { get; }

    public CommunityPage()
    {
        ViewModel = App.GetService<CommunityViewModel>();
        InitializeComponent();
    }

    private async void CommunityWebView_Loaded(object sender, RoutedEventArgs e)
    {
        // 检查并显示初次进入页面的提示弹窗
        await CheckAndShowFirstTimeNoticeAsync();

        await CommunityWebView.EnsureCoreWebView2Async();
        
        CommunityWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        
        CommunityWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
        
        CommunityWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        
        string initScript = @"
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
        await CommunityWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(initScript);
    }

    private async Task CheckAndShowFirstTimeNoticeAsync()
    {
        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var hasShownObj = await localSettingsService.ReadSettingAsync(CommunityNoticeKey);
            
            bool hasShown = hasShownObj is bool b && b;

            if (!hasShown)
            {
                var dialog = new ContentDialog
                {
                    Title = "欢迎访问VanillaBBS，请仔细阅读以下说明",
                    Content = "该论坛由CodeCubist和Vanilla联合策划，FufuLauncher作为平台发布。\n\n它不同于QQ频道和米游社，不是为了替代它们而生，它的目的是为了更方便玩家的分享和更快的解答疑问，你可以在这里发布你的游戏内容，也可以询问一切关于软件的问题，或者是分享你的生活，只要遵守社区规定，我们都欢迎进行发帖交流。\n\n对于软件更新我们也会在这里发布通知，同时你也可以直接与我们的开发者交流。\n\n目前网站仅在试运行，如实际表现效果较好，我们会进行正式的人工重构上线。",
                    CloseButtonText = "我知道了",
                    XamlRoot = Content.XamlRoot
                };

                await dialog.ShowAsync();
                
                await localSettingsService.SaveSettingAsync(CommunityNoticeKey, true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"显示初次进入提示失败: {ex.Message}");
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
                
                string dialogContent = $"当前网页请求访问您的以下数据：\n\n" +
                                       $"MID: {(string.IsNullOrEmpty(userData.Mid) ? "未知" : userData.Mid)}\n" +
                                       $"UID: {(string.IsNullOrEmpty(userData.Uid) ? "未知" : userData.Uid)}\n" +
                                       $"账户昵称: {(string.IsNullOrEmpty(userData.Nickname) ? "未知" : userData.Nickname)}\n\n" +
                                       $"是否同意提供？";
                
                var dialog = new ContentDialog
                {
                    Title = "授权请求",
                    Content = dialogContent,
                    PrimaryButtonText = "同意",
                    CloseButtonText = "拒绝",
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
            System.Diagnostics.Debug.WriteLine($"WebMessage 处理异常: {ex.Message}");
        }
    }

    private async Task<(string Mid, string Uid, string Nickname)> RetrieveUserDataAsync()
    {
        string uid = string.Empty;
        string nickname = string.Empty;
        string mid = string.Empty;

        try
        {
            var userConfigService = App.GetService<IUserConfigService>();
            var displayConfig = await userConfigService.LoadDisplayConfigAsync();
            if (displayConfig != null)
            {
                uid = displayConfig.GameUid ?? string.Empty;
                nickname = displayConfig.Nickname ?? string.Empty;
            }
            
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var activeFileObj = await localSettingsService.ReadSettingAsync("ActiveConfigFile");
            string activeFile = activeFileObj?.ToString() ?? "config.json";
            string configPath = Path.Combine(Helpers.AppPaths.DataDir, activeFile);

            if (File.Exists(configPath))
            {
                string json = await File.ReadAllTextAsync(configPath);
                
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("Account", out JsonElement accountElement))
                    {
                        if (accountElement.TryGetProperty("Mid", out JsonElement midElement))
                        {
                            mid = midElement.GetString() ?? string.Empty;
                        }
                        if (string.IsNullOrEmpty(mid) && accountElement.TryGetProperty("Cookie", out JsonElement cookieElement))
                        {
                            string cookie = cookieElement.GetString() ?? string.Empty;
                            var match = System.Text.RegularExpressions.Regex.Match(cookie, @"mid=([^;]+)");
                            if (match.Success)
                            {
                                mid = match.Groups[1].Value;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"提取用户数据失败: {ex.Message}");
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

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        CommunityWebView.Reload();
    }
}