using System.Diagnostics;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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

            Debug.WriteLine("=== CalculatorPage 初始化 ===");

            _ = InitializeWebViewAsync();
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

                CalculatorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

                if (!_isNavigationEventsSubscribed)
                {

                    CalculatorWebView.CoreWebView2.NavigationStarting += OnWebViewNavigationStarting;

                    CalculatorWebView.CoreWebView2.FrameNavigationStarting += OnWebViewFrameNavigationStarting;

                    CalculatorWebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

                    CalculatorWebView.CoreWebView2.DOMContentLoaded += OnDOMContentLoaded;

                    _isNavigationEventsSubscribed = true;
                    Debug.WriteLine("导航事件已订阅");
                }

                ViewModel.StatusMessage = "正在加载养成计算器...";

                CalculatorWebView.CoreWebView2.Settings.UserAgent =
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

                LoadCalculatorPage();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("WebView2 初始化失败: " + ex.Message);
                ViewModel.StatusMessage = "WebView2 运行时未安装。请下载并安装";
                ViewModel.IsLoading = false;
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            Debug.WriteLine("页面导航到 CalculatorPage");
        }

        private void LoadCalculatorPage()
        {
            try
            {
                Debug.WriteLine("开始加载计算器页面...");
                _isPageLoaded = false;
                ViewModel.IsLoading = true;
                ViewModel.StatusMessage = "正在连接米游社服务器...";

                StartLoadingTimeout();

                StartMinDisplayTimer();

                var targetUri = new Uri("https://act.mihoyo.com/ys/event/calculator/index.html");
                Debug.WriteLine("导航到: " + targetUri.ToString());
                CalculatorWebView.Source = targetUri;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("加载失败: " + ex.Message);
                ViewModel.StatusMessage = "加载失败: " + ex.Message;
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
            _minDisplayTimer = new Timer(1000);
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
                    Debug.WriteLine("最小显示时间到，页面已加载，隐藏提示");
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
                    Debug.WriteLine("加载超时！");
                    ViewModel.StatusMessage = "加载超时，请检查网络连接";
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
            Debug.WriteLine("DOM内容加载完成！页面已可显示");
            _isPageLoaded = true;

            DispatcherQueue.TryEnqueue(() =>
            {

                ViewModel.StatusMessage = "";
            });

            StopLoadingTimeout();
        }

        private void OnWebViewNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            Debug.WriteLine("[主框架] 导航请求: " + args.Uri);
            CheckAndBlockNavigation(args);
        }

        private void OnWebViewFrameNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            Debug.WriteLine("[子框架] 导航请求: " + args.Uri);
            CheckAndBlockNavigation(args);
        }

        private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            Debug.WriteLine("[阻止] 新窗口请求: " + args.Uri);
            args.Handled = true;

            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.StatusMessage = "已阻止新窗口打开";
            });
        }

        private void CheckAndBlockNavigation(CoreWebView2NavigationStartingEventArgs args)
        {
            try
            {
                var uri = new Uri(args.Uri);

                var allowedHost = "act.mihoyo.com";
                var allowedPath = "/ys/event/calculator/index.html";

                bool isAllowed = uri.Host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase) &&
                                uri.AbsolutePath.Equals(allowedPath, StringComparison.OrdinalIgnoreCase);

                if (!isAllowed)
                {
                    Debug.WriteLine("[阻止] 不允许的导航: " + uri.Host + uri.AbsolutePath);
                    args.Cancel = true;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        ViewModel.StatusMessage = "已阻止外部链接: " + uri.Host;
                    });
                }
                else
                {
                    Debug.WriteLine("[允许] 导航到: " + uri.ToString());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("导航检查异常: " + ex.Message);
                args.Cancel = true;
            }
        }

        private void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            StopLoadingTimeout();

            if (!args.IsSuccess)
            {
                Debug.WriteLine("导航失败: " + args.WebErrorStatus.ToString());
                ViewModel.StatusMessage = "页面加载失败: " + args.WebErrorStatus.ToString();
                ViewModel.IsLoading = false;
                _isPageLoaded = false;
            }
            else
            {
                Debug.WriteLine("导航完成（但DOM可能还在加载）");

            }
        }
    }
}