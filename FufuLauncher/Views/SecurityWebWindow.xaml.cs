using Windows.Graphics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class SecurityWebWindow : Window
{
    private readonly string _cookieString;
    private readonly string _targetUrl;

    public SecurityWebWindow(string cookieString, string targetUrl)
    {
        _cookieString = cookieString;
        _targetUrl = targetUrl;
        InitializeComponent();
    
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();
        AppWindow.Resize(new SizeInt32(1280, 720));

        InitializeWebViewAsync();
    }

    private async void InitializeWebViewAsync()
    {
        await SecurityWebView.EnsureCoreWebView2Async();
    
        var cookieManager = SecurityWebView.CoreWebView2.CookieManager;
        var cookies = _cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var cookieKV in cookies)
        {
            var parts = cookieKV.Split('=', 2);
            if (parts.Length == 2)
            {
                var cookie = cookieManager.CreateCookie(parts[0], parts[1], ".mihoyo.com", "/");
                cookieManager.AddOrUpdateCookie(cookie);
            }
        }
        
        SecurityWebView.Source = new Uri(_targetUrl);
    }
}