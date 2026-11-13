/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Windows.Graphics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views;

public sealed partial class CloudCredentialWindow : Window
{
    private readonly string _uid;
    private bool _captured;
    private bool _isCapturing;
    private bool _isClosed;

    public CloudCredentialWindow(string uid)
    {
        _uid = uid;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1280, 720));

        TitleText.Text = $"添加云游戏凭证 - {_uid}";
        Closed += OnWindowClosed;
        InitializeWebViewAsync();
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync();
            if (_isClosed) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = CloudCredentialIdentity.CreateProfileName(_uid);
            options.IsInPrivateModeEnabled = true;
            await CloudWebView.EnsureCoreWebView2Async(environment, options);
            if (_isClosed) return;
            CloudWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            CloudWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;

            CloudWebView.CoreWebView2.AddWebResourceRequestedFilter(
                ApiEndpoints.CloudGameWalletWebViewFilter,
                CoreWebView2WebResourceContext.All);
            CloudWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;

            CloudWebView.CoreWebView2.Navigate(ApiEndpoints.CloudGamePageUrl);
        }
        catch (Exception ex)
        {
            DebugLog.Error("CloudCredentialWindow", "CloudCredentialWindow initialization failed", ex);
            ShowCredentialError("CloudCredential_InitFailed".GetLocalized());
        }
    }

    private async void CoreWebView2_WebResourceRequested(CoreWebView2 sender,
        CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (_captured || _isCapturing || _isClosed) return;

        try
        {
            if (!args.Request.Headers.Contains("x-rpc-combo_token")) return;

            var comboToken = args.Request.Headers.GetHeader("x-rpc-combo_token");
            if (!string.IsNullOrEmpty(comboToken))
            {
                _isCapturing = true;
                var cookies = await sender.CookieManager.GetCookiesAsync(ApiEndpoints.CloudGamePageUrl);
                if (_isClosed) return;
                var accountUid = CloudCredentialIdentity.GetAccountUid(cookies.Select(cookie =>
                    new KeyValuePair<string, string>(cookie.Name, cookie.Value)));
                if (accountUid == null)
                {
                    ShowCredentialError("CloudCredential_IdentityUnavailable".GetLocalized());
                    return;
                }
                if (!string.Equals(accountUid, _uid, StringComparison.Ordinal))
                {
                    ShowCredentialError(string.Format("CloudCredential_AccountMismatch".GetLocalized(), accountUid, _uid));
                    return;
                }

                if (!await SettingsViewModel.SaveCloudCredentialAsync(_uid, comboToken, accountUid))
                {
                    ShowCredentialError("CloudCredential_SaveFailed".GetLocalized());
                    return;
                }
                _captured = true;
                if (_isClosed) return;
                TitleText.Text = $"添加云游戏凭证 - {_uid} (已获取)";
                CredentialInfoBar.IsOpen = false;
                WeakReferenceMessenger.Default.Send(new NotificationMessage("云游戏凭证", "凭证获取成功",
                    NotificationType.Success, 2000));
                await Task.Delay(1500);
                if (!_isClosed) Close();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudCredentialWindow", $"CloudCredentialWindow failed to fetch credentials: {ex.Message}");
            ShowCredentialError("CloudCredential_SaveFailed".GetLocalized());
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private void ShowCredentialError(string message)
    {
        if (_isClosed) return;
        CredentialInfoBar.Message = message;
        CredentialInfoBar.IsOpen = true;
    }

    private void CoreWebView2_ContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        try
        {
            if (CloudWebView.CoreWebView2 != null)
            {
                CloudWebView.CoreWebView2.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
                CloudWebView.CoreWebView2.WebResourceRequested -= CoreWebView2_WebResourceRequested;
            }

            CloudWebView.Close();
        }
        catch
        {
        }
    }
}
