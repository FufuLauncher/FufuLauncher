/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace FufuLauncher.Views;

public sealed partial class ServiceOverviewWebWindow : Window
{
    public const string StatusUrl = "https://status.fu1.fun/";

    private static ServiceOverviewWebWindow? _current;

    public static void ShowOrActivate()
    {
        if (_current is not null)
        {
            _current.Activate();
            return;
        }

        var window = new ServiceOverviewWebWindow();
        _current = window;
        window.Closed += (_, _) => _current = null;
        window.Activate();
    }

    public ServiceOverviewWebWindow()
    {
        InitializeComponent();

        Title = "服务状态总览";

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        SetWindowSizeAndCenter();

        StatusWebView.Source = new Uri(StatusUrl);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (StatusWebView.CoreWebView2 is { } core)
        {
            core.Reload();
        }
        else
        {
            StatusWebView.Source = new Uri(StatusUrl);
        }
    }

    private void OpenInEdgeButton_Click(object sender, RoutedEventArgs e)
    {
        var url = StatusWebView.Source?.AbsoluteUri ?? StatusUrl;
        AnnouncementLauncher.Open(url);
    }

    private void SetWindowSizeAndCenter()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);

            int newWidth = Math.Max((int)(displayArea.WorkArea.Width * 0.6), 800);
            int newHeight = Math.Max((int)(displayArea.WorkArea.Height * 0.75), 600);

            int x = (displayArea.WorkArea.Width - newWidth) / 2;
            int y = (displayArea.WorkArea.Height - newHeight) / 2;

            appWindow.MoveAndResize(new RectInt32(x, y, newWidth, newHeight));
        }
    }

    private void StatusWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void StatusWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }
}
