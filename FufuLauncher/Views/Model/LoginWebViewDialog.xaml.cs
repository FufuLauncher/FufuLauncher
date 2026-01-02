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
    private DispatcherTimer _autoCheckTimer;
    private bool _isChecking = false; // 防止重复检查的标志

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

        // 初始化自动检测定时器
        _autoCheckTimer = new DispatcherTimer();
        _autoCheckTimer.Interval = TimeSpan.FromSeconds(3); // 每3秒检查一次
        _autoCheckTimer.Tick += AutoCheckTimer_Tick;
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        // 停止自动检测
        _autoCheckTimer?.Stop();

        // 清除本次登录的Cookie（只清除米游社的）
        await ClearMiyousheCookiesAsync();

        Close();
    }

    private async void AutoCheckTimer_Tick(object sender, object e)
    {
        await CheckAndSaveLoginStatus();
    }

    private async Task ClearMiyousheCookiesAsync()
    {
        try
        {
            if (LoginWebView?.CoreWebView2?.CookieManager != null)
            {
                // 只清除米游社域名的Cookie
                var cookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.miyoushe.com");
                foreach (var cookie in cookies)
                {
                    LoginWebView.CoreWebView2.CookieManager.DeleteCookie(cookie);
                }
                Debug.WriteLine("已清除米游社Cookie");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"清除Cookie失败: {ex.Message}");
        }
    }

    private async void LoginWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingRing.IsActive = false;

        try
        {
            if (args.IsSuccess)
            {
                await sender.ExecuteScriptAsync("""
                    if (window.location.host === 'www.miyoushe.com') {
                        var openLoginDialogIntervalId = setInterval(function() {
                            var ele = document.getElementsByClassName('header__avatarwrp');
                            if (ele.length > 0) {
                                clearInterval(openLoginDialogIntervalId);
                                ele[0].click();
                            }
                        }, 100);
                    }
                """);
            }

            if (sender.Source?.AbsoluteUri.Contains("miyoushe.com") == true)
            {
                var cookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.miyoushe.com");

                if (cookies.Count > 0)
                {
                    StatusText.Text = "点击'完成登录'保存";
                }
            }
            if (sender.Source?.AbsoluteUri.Contains("miyoushe.com") == true)
            {
                // 启动自动检测定时器
                if (!_autoCheckTimer.IsEnabled)
                {
                    _autoCheckTimer.Start();
                    Debug.WriteLine("开始自动检测登录状态");
                }

                // 立即检查一次Cookie
                await CheckAndSaveLoginStatus();
            }
        }

        catch (Exception ex)
        {
            Debug.WriteLine($"操作失败: {ex.Message}");
            StatusText.Text = "自动操作失败，请手动登录";
        }
    }

    private async Task CheckAndSaveLoginStatus()
    {
        if (_isChecking || _loginCompleted || LoginWebView?.CoreWebView2?.CookieManager == null)
            return;

        _isChecking = true;

        try
        {
            // 获取所有米游社的Cookie
            var cookies = await LoginWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://www.miyoushe.com");

            // 调试：显示所有Cookie信息
            Debug.WriteLine($"检测到 {cookies.Count} 个Cookie");
            foreach (var cookie in cookies)
            {
                Debug.WriteLine($"  {cookie.Name}: {cookie.Value}");
            }

            // 关键登录Cookie检查
            var loginCookieNames = new[] { "account_id", "ltuid", "ltoken", "cookie_token", "login_ticket", "stuid", "stoken" };
            var hasKeyCookies = cookies.Any(c => loginCookieNames.Contains(c.Name));

            // 检查是否有足够数量的Cookie且包含关键登录Cookie
            if (cookies.Count >= 3 && hasKeyCookies)
            {
                var latestCookieString = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));

                if (!string.IsNullOrEmpty(latestCookieString))
                {
                    StatusText.Text = "检测到登录成功，正在保存...";
                    await SaveCookiesAsync(latestCookieString);
                    await ClearMiyousheCookiesAsync();
                    _loginCompleted = true;
                    StatusText.Text = "登录成功！正在关闭...";

                    // 停止定时器
                    _autoCheckTimer.Stop();

                    // 延迟关闭，让用户看到成功提示
                    await Task.Delay(2000);

                    // 关闭窗口前不清除Cookie（因为已经保存了）
                    Close();
                }
            }
            else if (cookies.Count > 0)
            {
                StatusText.Text = "等待登录完成...";
            }
            else
            {
                StatusText.Text = "请登录米游社账号";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检查登录状态失败: {ex.Message}");
        }
        finally
        {
            _isChecking = false;
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

            // 提取account_id或ltuid作为Stuid
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
            else if (cookieString.Contains("stuid="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookieString, @"stuid=(\d+)");
                if (match.Success) config.Account.Stuid = match.Groups[1].Value;
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var newJson = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(path, newJson);

            Debug.WriteLine($"文件已保存: {path}");
            Debug.WriteLine($"保存的Cookie: {cookieString}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存失败: {ex.Message}");
            StatusText.Text = $"保存失败: {ex.Message}";
        }
    }

    public bool DidLoginSucceed() => _loginCompleted;

    // 窗口关闭时停止定时器
    private void Window_Closed(object sender, WindowEventArgs args)
    {
        _autoCheckTimer?.Stop();
    }
}