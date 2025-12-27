using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;
using Windows.Graphics;

namespace FufuLauncher.Views
{
    public sealed partial class MapPage : Page
    {
        private Window _hostWindow;

        public MapPage()
        {
            this.InitializeComponent();
            InitializeMap();
        }

        private async void InitializeMap()
        {
            await MapWebView.EnsureCoreWebView2Async();
            MapWebView.NavigationCompleted += MapWebView_NavigationCompleted;
            MapWebView.Source = new Uri("https://act.mihoyo.com/ys/app/interactive-map/index.html");
        }
        
        private async void MapWebView_NavigationCompleted(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
            {
                string removeQrScript = @"
                    (function() {
                        const targetUrlPart = 'e8d52e7e0f4842ec70e9c7b1a22a2ad5_3623455812133914954.png';
                        function cleanElements() {
                            const qrCodeDivs = document.querySelectorAll('.bbs-qr');
                            qrCodeDivs.forEach(el => el.remove());
                            const allDivs = document.querySelectorAll('div[style*=""background-image""]');
                            allDivs.forEach(div => {
                                const style = div.getAttribute('style');
                                if (style && style.includes(targetUrlPart)) {
                                    div.style.display = 'none';
                                    div.remove();
                                }
                            });
                        }
                        cleanElements();
                        const observer = new MutationObserver((mutations) => {
                            cleanElements();
                        });
                        observer.observe(document.body, { childList: true, subtree: true });
                    })();
                ";

                try
                {
                    await sender.ExecuteScriptAsync(removeQrScript);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"脚本注入失败: {ex.Message}");
                }
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            if (e.Parameter is Window window)
            {
                _hostWindow = window;
                
                
                _hostWindow.ExtendsContentIntoTitleBar = true;
                
                _hostWindow.SetTitleBar(AppTitleBar);
                
                ResizeWindowBasedOnResolution(0.85);

                _hostWindow.Closed += (s, args) => MapWebView.Close();
            }
        }

        private void ResizeWindowBasedOnResolution(double scaleFactor)
        {
            if (_hostWindow == null) return;
            
            var hWnd = WindowNative.GetWindowHandle(_hostWindow);
            var winId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(winId);

            if (appWindow != null)
            {
                var displayArea = DisplayArea.GetFromWindowId(winId, DisplayAreaFallback.Primary);
                
                var screenWidth = displayArea.WorkArea.Width;
                var screenHeight = displayArea.WorkArea.Height;
                
                int newWidth = (int)(screenWidth * scaleFactor);
                int newHeight = (int)(screenHeight * scaleFactor);
                
                newWidth = Math.Max(newWidth, 800);
                newHeight = Math.Max(newHeight, 600);
                
                int posX = (screenWidth - newWidth) / 2 + displayArea.WorkArea.X;
                int posY = (screenHeight - newHeight) / 2 + displayArea.WorkArea.Y;
                
                appWindow.MoveAndResize(new RectInt32(posX, posY, newWidth, newHeight));
            }
        }

        private void TopMostToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_hostWindow == null) return;

            var hWnd = WindowNative.GetWindowHandle(_hostWindow);
            var winId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(winId);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                bool isTop = TopMostToggle.IsChecked == true;
                presenter.IsAlwaysOnTop = isTop;
                TopMostToggle.Content = isTop ? "窗口置顶 (开)" : "窗口置顶 (关)";
            }
        }

        private void LockMapToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isLocked = LockMapToggle.IsChecked == true;
            
            MapWebView.IsHitTestVisible = !isLocked;
            
            LockMapToggle.Content = isLocked ? "锁定地图 (开)" : "锁定地图 (关)";
        }
    }
}