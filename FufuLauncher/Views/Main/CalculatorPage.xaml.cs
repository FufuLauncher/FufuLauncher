using System.Diagnostics;
using System.Numerics;
using System.Text.Json.Nodes;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.Web.WebView2.Core;
using Timer = System.Timers.Timer;

namespace FufuLauncher.Views
{
    public sealed partial class CalculatorPage : Page
    {
        public CalculatorViewModel ViewModel
        {
            get;
        }

        private Timer _loadingTimeoutTimer;
        private Timer _minDisplayTimer;
        private bool _isNavigationEventsSubscribed = false;
        private bool _isPageLoaded = false;

        public CalculatorPage()
        {
            ViewModel = App.GetService<CalculatorViewModel>();
            DataContext = ViewModel;
            InitializeComponent();
            Loaded += (s, e) => EntranceStoryboard.Begin();

            Debug.WriteLine("=== CalculatorPage 初始化 ===");
            _ = InitializeWebViewAsync();
        }

        private void WebCardContainer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (CalculatorWebView != null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(CalculatorWebView);
                var compositor = visual.Compositor;

                var clipGeometry = compositor.CreateRoundedRectangleGeometry();

                clipGeometry.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);

                clipGeometry.CornerRadius = new Vector2(16, 16);

                visual.Clip = compositor.CreateGeometricClip(clipGeometry);
            }
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                Debug.WriteLine("开始初始化 WebView2...");
                ViewModel.IsLoading = true;
                ViewModel.StatusMessage = "正在初始化 WebView2 运行时...";

                await CalculatorWebView.EnsureCoreWebView2Async();

                Debug.WriteLine("WebView2 初始化成功！");

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

                    _isNavigationEventsSubscribed = true;
                }

                await LoadCalculatorPageAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("WebView2 初始化失败: " + ex.Message);
                ViewModel.StatusMessage = "组件初始化失败，请检查 Edge WebView2 Runtime";
                ViewModel.IsLoading = false;
            }
        }

        private async Task LoadCalculatorPageAsync()
        {
            try
            {
                Debug.WriteLine("开始加载计算器页面...");
                _isPageLoaded = false;
                ViewModel.IsLoading = true;
                ViewModel.StatusMessage = "正在同步养成数据...";

                var configPath = Helpers.AppPaths.ConfigFile;
                if (File.Exists(configPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(configPath);
                        var node = JsonNode.Parse(json);
                        var cookieStr = node?["Account"]?["Cookie"]?.ToString() ?? "";

                        if (!string.IsNullOrWhiteSpace(cookieStr))
                        {
                            var manager = CalculatorWebView.CoreWebView2.CookieManager;
                            var cookies = await manager.GetCookiesAsync("https://act.mihoyo.com");
                            foreach (var c in cookies) manager.DeleteCookie(c);

                            foreach (var item in cookieStr.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            {
                                var kv = item.Split('=', 2);
                                if (kv.Length == 2)
                                {
                                    var cookie = manager.CreateCookie(kv[0].Trim(), kv[1].Trim(), ".mihoyo.com", "/");
                                    manager.AddOrUpdateCookie(cookie);
                                }
                            }
                            Debug.WriteLine($"已注入 {cookieStr.Split(';').Length} 个 cookie");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("读取 cookie 失败: " + ex.Message);
                    }
                }

                StartLoadingTimeout();
                StartMinDisplayTimer();

                CalculatorWebView.CoreWebView2.Navigate("https://act.mihoyo.com/ys/event/calculator/index.html");
            }
            catch (Exception ex)
            {
                ViewModel.StatusMessage = "加载异常: " + ex.Message;
                ViewModel.IsLoading = false;
                StopLoadingTimeout();
                StopMinDisplayTimer();
            }
        }

        private void StartLoadingTimeout()
        {
            StopLoadingTimeout();
            _loadingTimeoutTimer = new Timer(15000);
            _loadingTimeoutTimer.Elapsed += OnLoadingTimeout;
            _loadingTimeoutTimer.Start();
        }

        private void StartMinDisplayTimer()
        {
            StopMinDisplayTimer();
            _minDisplayTimer = new Timer(800);
            _minDisplayTimer.Elapsed += OnMinDisplayTimeComplete;
            _minDisplayTimer.Start();
        }

        private void OnMinDisplayTimeComplete(object sender, System.Timers.ElapsedEventArgs e)
        {
            StopMinDisplayTimer();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isPageLoaded)
                {
                    ViewModel.IsLoading = false;
                }
            });
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

        private void OnLoadingTimeout(object sender, System.Timers.ElapsedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.IsLoading)
                {
                    ViewModel.StatusMessage = "连接超时，请检查网络设置";
                    ViewModel.IsLoading = false;
                }
            });
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

        private void OnDOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
        {
            Debug.WriteLine("DOM内容加载完成！");
            _isPageLoaded = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.StatusMessage = "加载完成";
            });
            StopLoadingTimeout();
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
                bool isAllowed = uri.Host.EndsWith("mihoyo.com", StringComparison.OrdinalIgnoreCase);

                if (!isAllowed)
                {
                    Debug.WriteLine("[阻止] 外部链接: " + uri.Host);
                    args.Cancel = true;
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!ViewModel.IsLoading) ViewModel.StatusMessage = "已拦截外部链接";
                    });
                }
            }
            catch
            {
                args.Cancel = true;
            }
        }
    }
}