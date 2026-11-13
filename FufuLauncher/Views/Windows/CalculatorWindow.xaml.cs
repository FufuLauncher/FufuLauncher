/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Timer = System.Timers.Timer;

namespace FufuLauncher.Views;

public sealed partial class CalculatorWindow : Window
{
    private Timer? _loadingTimeoutTimer;
    private Timer? _minDisplayTimer;
    private bool _isNavigationEventsSubscribed;
    private bool _isPageLoaded;
    private bool _isMinDisplayElapsed;
    private bool _isClosed;

    public CalculatorWindow()
    {
        InitializeComponent();

        RootGrid.RequestedTheme = App.GetService<IThemeSelectorService>().Theme;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ResizeWindowBasedOnResolution(0.85);
        Closed += OnWindowClosed;

        _ = InitializeWebViewAsync();
    }

    private void ResizeWindowBasedOnResolution(double scaleFactor)
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea == null) return;

        var workArea = displayArea.WorkArea;
        int width = Math.Max((int)(workArea.Width * scaleFactor), 800);
        int height = Math.Max((int)(workArea.Height * scaleFactor), 600);
        int x = workArea.X + (workArea.Width - width) / 2;
        int y = workArea.Y + (workArea.Height - height) / 2;

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        StopLoadingTimeout();
        StopMinDisplayTimer();

        try
        {
            CalculatorWebView.Close();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CalculatorWindow", "Failed to close WebView2: " + ex.Message);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            DebugLog.Info("CalculatorWindow", "Initializing WebView2...");
            _isPageLoaded = false;
            SetLoadingState(true);
            StartLoadingTimeout();

            await CalculatorWebView.EnsureCoreWebView2Async();

            if (_isClosed) return;

            if (CalculatorWebView.CoreWebView2 == null)
            {
                DebugLog.Info("CalculatorWindow", "CoreWebView2 initialization failed");
                ClearLoading();
                return;
            }

            CalculatorWebView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            CalculatorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            CalculatorWebView.CoreWebView2.Settings.UserAgent =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

            if (!_isNavigationEventsSubscribed)
            {
                CalculatorWebView.CoreWebView2.NavigationStarting += OnWebViewNavigationStarting;
                CalculatorWebView.CoreWebView2.FrameNavigationStarting += OnWebViewFrameNavigationStarting;
                CalculatorWebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                CalculatorWebView.CoreWebView2.DOMContentLoaded += OnDOMContentLoaded;
                CalculatorWebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

                _isNavigationEventsSubscribed = true;
            }

            await LoadCalculatorAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CalculatorWindow", "WebView2 initialization failed: " + ex.Message);
            if (!_isClosed) ClearLoading();
        }
    }

    private async Task InjectCurrentCookiesAsync(CoreWebView2 coreWebView)
    {
        var cookieStr = await GetCurrentCookieAsync();
        if (_isClosed || string.IsNullOrWhiteSpace(cookieStr)) return;

        var manager = coreWebView.CookieManager;
        var cookies = await manager.GetCookiesAsync(ApiEndpoints.ActMihoyoBase);
        foreach (var c in cookies) manager.DeleteCookie(c);

        foreach (var item in cookieStr.Split(';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = item.Split('=', 2);
            if (kv.Length == 2)
            {
                var cookie = manager.CreateCookie(kv[0].Trim(), kv[1].Trim(), ApiEndpoints.MihoyoCookieDomain, "/");
                manager.AddOrUpdateCookie(cookie);
            }
        }

        DebugLog.Info("CalculatorWindow", $"Injected {cookieStr.Split(';').Length} cookies");
    }

    private async Task<string?> GetCurrentCookieAsync()
    {
        var accountManager = App.GetService<AccountManager>();
        var activeId = accountManager.ActiveAccountId;
        if (activeId == null) return null;

        var cookies = await accountManager.LoadCookiesAsync(activeId);
        if (cookies == null || cookies.Count == 0) return null;

        return string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private async Task LoadCalculatorAsync()
    {
        try
        {
            DebugLog.Info("CalculatorWindow", "Loading calculator page...");
            _isPageLoaded = false;
            SetLoadingState(true);

            var coreWebView = CalculatorWebView.CoreWebView2;
            if (coreWebView == null)
            {
                ClearLoading();
                return;
            }

            await InjectCurrentCookiesAsync(coreWebView);
            if (_isClosed) return;

            StartLoadingTimeout();
            StartMinDisplayTimer();

            coreWebView.Navigate(ApiEndpoints.CalculatorWebUrl);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CalculatorWindow", "Failed to load calculator page: " + ex.Message);
            if (!_isClosed) ClearLoading();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var coreWebView = CalculatorWebView.CoreWebView2;
            if (coreWebView == null)
            {
                _ = InitializeWebViewAsync();
                return;
            }

            _isPageLoaded = false;
            SetLoadingState(true);
            StartLoadingTimeout();

            await InjectCurrentCookiesAsync(coreWebView);
            if (_isClosed) return;

            StartMinDisplayTimer();
            CalculatorWebView.Reload();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CalculatorWindow", "Failed to refresh calculator page: " + ex.Message);
            if (!_isClosed) ClearLoading();
        }
    }

    private void SetLoadingState(bool isLoading)
    {
        LoadingBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearLoading()
    {
        StopLoadingTimeout();
        StopMinDisplayTimer();
        SetLoadingState(false);
    }

    private void StartLoadingTimeout()
    {
        StopLoadingTimeout();
        _loadingTimeoutTimer = new Timer(15000);
        _loadingTimeoutTimer.Elapsed += OnLoadingTimeout;
        _loadingTimeoutTimer.Start();
    }

    private void StopLoadingTimeout()
    {
        if (_loadingTimeoutTimer != null)
        {
            _loadingTimeoutTimer.Stop();
            _loadingTimeoutTimer.Dispose();
            _loadingTimeoutTimer = null;
        }
    }

    private void OnLoadingTimeout(object? sender, System.Timers.ElapsedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || _isPageLoaded) return;
            ClearLoading();
        });
    }

    private void StartMinDisplayTimer()
    {
        StopMinDisplayTimer();
        _isMinDisplayElapsed = false;
        _minDisplayTimer = new Timer(800);
        _minDisplayTimer.Elapsed += OnMinDisplayTimeComplete;
        _minDisplayTimer.Start();
    }

    private void StopMinDisplayTimer()
    {
        if (_minDisplayTimer != null)
        {
            _minDisplayTimer.Stop();
            _minDisplayTimer.Dispose();
            _minDisplayTimer = null;
        }
    }

    private void OnMinDisplayTimeComplete(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _isMinDisplayElapsed = true;
        StopMinDisplayTimer();

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isPageLoaded && !_isClosed) SetLoadingState(false);
        });
    }

    private void OnDOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
    {
        DebugLog.Info("CalculatorWindow", "DOM content loaded!");
        _isPageLoaded = true;
        StopLoadingTimeout();

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isMinDisplayElapsed && !_isClosed) SetLoadingState(false);
        });
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess) return;

        DebugLog.Info("CalculatorWindow", $"Navigation failed: {args.WebErrorStatus}");
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || _isPageLoaded) return;
            ClearLoading();
        });
    }

    private void OnWebViewNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        CheckAndBlockNavigation(args);
    }

    private void OnWebViewFrameNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        CheckAndBlockNavigation(args);
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
    }

    private void CheckAndBlockNavigation(CoreWebView2NavigationStartingEventArgs args)
    {
        try
        {
            var uri = new Uri(args.Uri);
            bool isAllowed = uri.Host.EndsWith(ApiEndpoints.MihoyoDomain, StringComparison.OrdinalIgnoreCase);

            if (!isAllowed)
            {
                DebugLog.Info("CalculatorWindow", "[Blocked] External link: " + uri.Host);
                args.Cancel = true;
            }
        }
        catch
        {
            args.Cancel = true;
        }
    }
}