using System.Diagnostics;
using System.Text.Json;
using Windows.Graphics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views;

public sealed partial class CloudCredentialWindow : Window
{
    private readonly string _uid;
    private bool _captured;

    public CloudCredentialWindow(string uid)
    {
        _uid = uid;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1280, 720));

        TitleText.Text = $"添加云游戏凭证 - {_uid}";
        InitializeWebViewAsync();
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            await CloudWebView.EnsureCoreWebView2Async();
            CloudWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            CloudWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;

            CloudWebView.CoreWebView2.AddWebResourceRequestedFilter(
                "*://api-cloudgame.mihoyo.com/hk4e_cg_cn/wallet/wallet/get*",
                CoreWebView2WebResourceContext.All);
            CloudWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            CloudWebView.CoreWebView2.Navigate("https://ys.mihoyo.com/cloud/#/");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CloudCredentialWindow 初始化失败: {ex.Message}");
        }
    }

    private async void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (_captured) return;

        try
        {
            if (!args.Request.Headers.Contains("x-rpc-combo_token")) return;

            var comboToken = args.Request.Headers.GetHeader("x-rpc-combo_token");
            if (!string.IsNullOrEmpty(comboToken))
            {
                _captured = true;
                SettingsViewModel.SaveCloudCredential(_uid, comboToken);

                DispatcherQueue.TryEnqueue(() =>
                {
                    TitleText.Text = $"添加云游戏凭证 - {_uid} (已获取)";
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("云游戏凭证", "凭证获取成功", NotificationType.Success, 2000));
                    Task.Delay(1500).ContinueWith(_ => DispatcherQueue.TryEnqueue(() => Close()));
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CloudCredentialWindow 抓取凭证失败: {ex.Message}");
        }
    }

    private void CoreWebView2_ContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        e.Handled = true;
    }
}
