using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using MihoyoBBS;

namespace FufuLauncher.Views;

public sealed partial class LoginWebViewDialog : Window
{
    private bool _loginCompleted = false;
    private AppWindow _appWindow;

    public LoginWebViewDialog()
    {
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {

            _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(new Grid() { Height = 0 });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void CompleteButton_Click(object sender, RoutedEventArgs e)
    {
        var cookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.miyoushe.com");
        var latestCookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));

        if (!string.IsNullOrEmpty(latestCookieString))
        {
            StatusText.Text = "正在保存...";
            await SaveCookiesAsync(latestCookieString);
            _loginCompleted = true;
            StatusText.Text = "登录信息已保存！";
        }
        else
        {
            StatusText.Text = "未检测到登录信息";
        }

        await Task.Delay(300);
        Close();
    }

    private async void LoginWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingRing.IsActive = false;

        try
        {
            if (sender.Source?.AbsoluteUri.Contains("miyoushe.com") == true)
            {

                var cookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.miyoushe.com");

                if (cookies.Count > 0)
                {
                    StatusText.Text = "检测到登录信息，点击'完成登录'保存";
                    CompleteButton.IsEnabled = true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Cookie检测失败: {ex.Message}");
            StatusText.Text = "登录检测失败，请手动点击完成";
            CompleteButton.IsEnabled = true;
        }
    }

    private async Task SaveCookiesAsync(string cookieString)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var config = new Config();
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
            }

            if (config.Account == null) config.Account = new AccountConfig();
            config.Account.Cookie = cookieString;

            if (cookieString.Contains("account_id="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookieString, @"account_id=(\d+)");
                if (match.Success) config.Account.Stuid = match.Groups[1].Value;
            }
            else if (cookieString.Contains("ltuid="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookieString, @"ltuid=(\d+)");
                if (match.Success) config.Account.Stuid = match.Groups[1].Value;
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var newJson = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(path, newJson);

            StatusText.Text = "登录信息已保存！";
            Debug.WriteLine($" 文件已保存: {path}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" 保存失败: {ex.Message}");
            StatusText.Text = $"保存失败: {ex.Message}";
        }
    }

    public bool DidLoginSucceed() => _loginCompleted;
}