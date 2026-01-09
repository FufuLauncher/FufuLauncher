using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Graphics;
using Microsoft.Web.WebView2.Core; 

namespace FufuLauncher.Views;

public sealed partial class AnnouncementWindowL : Window
{
    public AnnouncementWindowL(string url)
    {
        InitializeComponent();
        
        Title = "公告";
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        
        SetWindowSizeAndCenter();
        
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            AnnouncementWebView.Source = uri;
        }
    }

    private void SetWindowSizeAndCenter()
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            
            int newWidth = (int)(displayArea.WorkArea.Width * 0.6);
            int newHeight = (int)(displayArea.WorkArea.Height * 0.75);

            newWidth = Math.Max(newWidth, 800); 
            newHeight = Math.Max(newHeight, 600);

            int x = (displayArea.WorkArea.Width - newWidth) / 2;
            int y = (displayArea.WorkArea.Height - newHeight) / 2;

            appWindow.MoveAndResize(new RectInt32(x, y, newWidth, newHeight));
        }
    }

    private void AnnouncementWebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        LoadingOverlay.Visibility = Visibility.Visible;
    }

    private void AnnouncementWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }
}