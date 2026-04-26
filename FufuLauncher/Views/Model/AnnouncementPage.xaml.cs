using System.Text.Json;
using FufuLauncher.Constants;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views
{
    public sealed partial class AnnouncementPage : Page
    {
        public event Action<double, double> ResizeRequested;
        public event Action CloseRequested;
        
        public AnnouncementPage()
        {
            InitializeComponent();
            Loaded += AnnouncementPage_Loaded;
        }

        private async void AnnouncementPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await AnnouncementWebView.EnsureCoreWebView2Async();
                AnnouncementWebView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

                var core = AnnouncementWebView.CoreWebView2;

                core.FrameNavigationStarting += (s, args) =>
                {
                    if (HandleUniWebView(args.Uri))
                    {
                        args.Cancel = true;
                    }
                };

                core.NavigationStarting += (s, args) =>
                {
                    if (HandleUniWebView(args.Uri))
                    {
                        args.Cancel = true;
                    }
                };

                core.NewWindowRequested += (s, args) =>
                {
                    if (HandleUniWebView(args.Uri))
                    {
                        args.Handled = true;
                    }
                };

                AnnouncementWebView.NavigationCompleted += AnnouncementWebView_NavigationCompleted;
                AnnouncementWebView.Source = new Uri(ApiEndpoints.Hk4eAnnouncementPageUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView2 Error: {ex.Message}");
                LoadingBar.Visibility = Visibility.Collapsed;
            }
        }

        private bool HandleUniWebView(string uriString)
        {
            if (string.IsNullOrEmpty(uriString)) return false;

            if (uriString.StartsWith("uniwebview://", StringComparison.OrdinalIgnoreCase))
            {
                if (uriString.Equals("uniwebview://close", StringComparison.OrdinalIgnoreCase) ||
                    uriString.StartsWith("uniwebview://close/", StringComparison.OrdinalIgnoreCase) ||
                    uriString.StartsWith("uniwebview://close?", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine("【捕获关闭指令】正在关闭窗口...");

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        CloseRequested?.Invoke();
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"【拦截其他指令】{uriString}");
                }

                return true;
            }

            return false;
        }

        private async void AnnouncementWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            if (args.IsSuccess)
            {
                await Task.Delay(300);
                await CalculateAndTriggerResize();
            }
        }

        private async Task CalculateAndTriggerResize()
        {
            try
            {
                string script = @"
                    (function() {
                        var root = document.getElementById('root');
                        var target = root ? (root.firstElementChild || root) : document.body;
                        return JSON.stringify({ 
                            width: target.scrollWidth, 
                            height: target.scrollHeight 
                        });
                    })();
                ";
                var jsonResult = await AnnouncementWebView.ExecuteScriptAsync(script);
                if (!string.IsNullOrEmpty(jsonResult) && jsonResult != "null")
                {
                    var dimensions = JsonSerializer.Deserialize<WebDimensions>(jsonResult);
                    if (dimensions != null && dimensions.height > 0)
                    {
                        ResizeRequested?.Invoke(dimensions.width, dimensions.height);
                    }
                }
            }
            catch { }
        }

        private class WebDimensions
        {
            public double width
            {
                get; set;
            }
            public double height
            {
                get; set;
            }
        }
    }
}