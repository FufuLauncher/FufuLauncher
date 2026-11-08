/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    #region 子窗口

    private void OnOpenOfficialBackgroundWindowClick(object sender, RoutedEventArgs e)
    {
        OfficialBackgroundWindow.ShowOrActivate();
    }

    private void OnEasterEggClick(object sender, RoutedEventArgs e)
    {
        if (_easterEggWindow != null)
        {
            _easterEggWindow.Activate();
            return;
        }

        var window = new Window();
        var page = new EasterEggPage();
        window.Content = page;

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(page.AppTitleBarElement);

        window.Title = "Philia093";

        IntPtr hWnd = WindowNative.GetWindowHandle(window);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new Windows.Graphics.SizeInt32(1300, 850);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }
        }

        window.Closed += (s, args) =>
        {
            page.Cleanup();
            _easterEggWindow = null;
        };

        _easterEggWindow = window;
        window.Activate();
    }

    private void OnOpenAchievementUpdaterClick(object sender, RoutedEventArgs e)
    {
        var updaterWindow = new AchievementUpdaterWindow();
        updaterWindow.ExtendsContentIntoTitleBar = true;

        IntPtr hWnd = WindowNative.GetWindowHandle(updaterWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new Windows.Graphics.SizeInt32(1100, 750);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }
        }

        updaterWindow.Activate();
    }

    private void OnOpenDatabaseEditorClick(object sender, RoutedEventArgs e)
    {
        var editorWindow = new DatabaseEditorWindow();

        editorWindow.ExtendsContentIntoTitleBar = true;

        IntPtr hWnd = WindowNative.GetWindowHandle(editorWindow);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new Windows.Graphics.SizeInt32(800, 550);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }
        }

        editorWindow.Activate();
    }

    private void OnOpenSponsorWindowClick(object sender, RoutedEventArgs e)
    {
        var sponsorWindow = new SponsorWindow();

        IntPtr hWnd = WindowNative.GetWindowHandle(sponsorWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new Windows.Graphics.SizeInt32(640, 520);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }

            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
            }
        }

        sponsorWindow.Activate();
    }

    private void OnOpenAboutWindowClick(object sender, RoutedEventArgs e)
    {
        var window = new Window();
        var page = new AboutPage();
        window.Content = page;

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(page.AppTitleBar);

        window.Title = "关于 FufuLauncher";

        try
        {
            window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch
        {
        }

        IntPtr hWnd = WindowNative.GetWindowHandle(window);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            appWindow.SetIcon("Assets/WindowIcon.ico");

            var size = new Windows.Graphics.SizeInt32(1350, 850);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }

            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
            }
        }

        window.Activate();
    }

    private void OnOpenServiceStatusWindowClick(object sender, RoutedEventArgs e)
    {
        ServiceStatusWindow.ShowOrActivate();
    }

    private void OnOpenServiceOverviewClick(object sender, RoutedEventArgs e)
    {
        ServiceOverviewWebWindow.ShowOrActivate();
    }

    private void OnOpenMcpWindowClick(object sender, RoutedEventArgs e)
    {
        McpWindow.ShowOrActivate();
    }

    #endregion
}