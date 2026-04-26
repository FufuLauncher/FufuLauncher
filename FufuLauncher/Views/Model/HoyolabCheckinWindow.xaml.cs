using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views
{
    public sealed partial class HoyolabCheckinWindow : Window
    {
        private readonly string _rawCookie;
        private AppWindow _appWindow;

        public HoyolabCheckinWindow(string cookie)
        {
            InitializeComponent();
            _rawCookie = cookie;
            
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            
            _appWindow.IsShownInSwitchers = false;
            
            _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(-10000, -10000, 1, 1));
            
            Activated += (s, e) =>
            {
                if (_appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Minimize();
                }
            };

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await CheckinWebView.EnsureCoreWebView2Async();

            if (!string.IsNullOrEmpty(_rawCookie))
            {
                var cookieManager = CheckinWebView.CoreWebView2.CookieManager;
                var domains = new[] { ".hoyolab.com", ".hoyoverse.com", ".mihoyo.com" };
                
                var cookiePairs = _rawCookie.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var domain in domains)
                {
                    foreach (var pair in cookiePairs)
                    {
                        var parts = pair.Trim().Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var cookie = cookieManager.CreateCookie(parts[0], parts[1], domain, "/");
                            cookieManager.AddOrUpdateCookie(cookie);
                        }
                    }
                }
            }

            CheckinWebView.CoreWebView2.Navigate("https://act.hoyolab.com/ys/event/signin-sea-v3/index.html?act_id=e202102251931481&lang=zh-cn");
            CheckinWebView.NavigationCompleted += OnNavigationCompleted;
        }

        private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess) return;

            var webView = sender as WebView2;
            if (webView == null) return;
            
            await Task.Delay(5000);

            string js = @"(async function() { 
                const divs = document.querySelectorAll('div');
                for (const el of divs) {
                    const dot = el.querySelector('span');
                    if (dot && window.getComputedStyle(dot).display !== 'none') {
                        let p = dot.parentElement;
                        while (p && p !== document.body) {
                            if (p.textContent.includes('第') && p.querySelector('img')) {
                                p.click();
                                return 'SUCCESS';
                            }
                            p = p.parentElement;
                        }
                    }
                }
                return 'NOT_FOUND';
            })()";

            try
            {
                await webView.CoreWebView2.ExecuteScriptAsync(js);
            }
            catch
            {
                // ignored
            }

            await Task.Delay(3000);
            Close();
        }
    }
}