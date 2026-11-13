/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.UI;

namespace FufuLauncher.Views;

public sealed partial class CloudCaptchaWindow : Window
{
    private readonly Uri _captchaUri;
    private readonly SemaphoreSlim _pollSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<Task<bool>> _tryConsumePollAsync;
    private bool _finished;

    private CloudCaptchaWindow(Uri captchaUri, Func<Task<bool>> tryConsumePollAsync)
    {
        _captchaUri = captchaUri;
        _tryConsumePollAsync = tryConsumePollAsync;

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindow();
        ApplyTheme();

        Closed += (_, _) =>
        {
            _finished = true;
            _cts.Cancel();
        };

        _ = InitializeAsync();
    }

    public static async Task<CloudApiResult<T>?> ShowChallengeAsync<T>(
        ICloudSyncService cloudSync,
        CloudChallengeInfo challenge)
    {
        var captchaUri = cloudSync.ResolveServiceUri(challenge.CaptchaUrl);
        var tcs = new TaskCompletionSource<CloudApiResult<T>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CloudCaptchaWindow? window = null;

        async Task<bool> PollOnceAsync()
        {
            var result = await cloudSync.PollChallengeAsync<T>(challenge.PollUrl, challenge.ChallengeSecret);
            if (result.IsPending)
            {
                return false;
            }

            tcs.TrySetResult(result);
            window?.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    window.Close();
                }
                catch
                {
                }
            });
            return true;
        }

        window = new CloudCaptchaWindow(captchaUri, PollOnceAsync);
        window.Closed += (_, _) => tcs.TrySetResult(null);
        window.Activate();

        return await tcs.Task;
    }

    private void ConfigureWindow()
    {
        try
        {
            var appWindow = AppWindow;
            if (appWindow == null) return;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new SizeInt32(520, 620);
            appWindow.Resize(size);
            WindowManagerHelper.CenterWindowOnScreen(appWindow, size.Width, size.Height);
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudCaptchaWindow", $"[CloudCaptcha] ConfigureWindow failed: {ex.Message}");
        }
    }

    private void ApplyTheme()
    {
        try
        {
            var theme = App.GetService<IThemeSelectorService>().Theme;
            RootGrid.RequestedTheme = theme;
            var isDark = theme == ElementTheme.Dark ||
                         (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            var titleBar = AppWindow?.TitleBar;
            if (titleBar == null) return;

            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonInactiveForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
        }
        catch
        {
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            await CaptchaWebView.EnsureCoreWebView2Async();
            CaptchaWebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            CaptchaWebView.CoreWebView2.NavigationCompleted += (_, _) => LoadingRing.IsActive = false;
            CaptchaWebView.Source = _captchaUri;
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudCaptchaWindow", $"[CloudCaptcha] WebView2 init failed: {ex.Message}");
            LoadingRing.IsActive = false;
            StatusTextBlock.Text = "CloudSync_CaptchaLoadError".GetLocalized();
        }

        _ = RunPollLoopAsync(_cts.Token);
    }

    private async void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.Uri.Contains(ApiEndpoints.CloudSyncAuthResultPath, StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
            StatusTextBlock.Text = "CloudSync_CaptchaSubmitting".GetLocalized();
            await TrySinglePollAsync();
        }
    }

    private async Task RunPollLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_finished)
            {
                await Task.Delay(1200, ct);
                if (await TrySinglePollAsync())
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<bool> TrySinglePollAsync()
    {
        if (_finished) return true;
        await _pollSemaphore.WaitAsync();
        try
        {
            if (_finished) return true;
            var done = await _tryConsumePollAsync();
            if (done)
            {
                _finished = true;
            }

            return done;
        }
        finally
        {
            _pollSemaphore.Release();
        }
    }

    private void OnOpenInBrowserClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _captchaUri.ToString(),
                UseShellExecute = true
            });
            StatusTextBlock.Text = "CloudSync_CaptchaBrowserOpened".GetLocalized();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudCaptchaWindow", $"[CloudCaptcha] Open browser failed: {ex.Message}");
            StatusTextBlock.Text = "CloudSync_CaptchaBrowserOpenFailed".GetLocalized();
        }
    }
}