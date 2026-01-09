using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;
using Windows.Graphics;
using System.Threading.Tasks;

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
            await MapWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                if (window.location.hostname === 'act.mihoyo.com') {
                    localStorage.setItem('user-guide-passed', 'true');
                    localStorage.setItem('async-announcement-hidden-ts', Date.now().toString());  
                }
            ");
            MapWebView.NavigationStarting += MapWebView_NavigationStarting;
            MapWebView.NavigationCompleted += MapWebView_NavigationCompleted;
            MapWebView.Source = new Uri("https://act.mihoyo.com/ys/app/interactive-map/index.html");
        }
        
        private async void MapWebView_NavigationStarting(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
        {
            if (args.Uri.Contains("act.mihoyo.com"))
            {
                string setStorageScript = @"
                    localStorage.setItem('user-guide-passed', 'true');
                    localStorage.setItem('async-announcement-hidden-ts', Date.now().toString());  
                }";
                try
                {
                    await sender.ExecuteScriptAsync(setStorageScript);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"脚本注入失败: {ex.Message}");
                }
            }
        }

        private async void MapWebView_NavigationCompleted(WebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.IsSuccess)
            {
                string removeQrScript = @"
                    (function() {
                    const viewport = document.querySelector('meta[name=""viewport""]');
                    if (viewport) {
                        viewport.content = 'width=device-width, initial-scale=1.0, minimum-scale=1.0, maximum-scale=5.0';
                    }
                    function removeQrCode() {
                        const targetUrlPart = 'e8d52e7e0f4842ec70e9c7b1a22a2ad5_3623455812133914954.png';
                        document.querySelectorAll('.bbs-qr').forEach(el => el?.remove());
                        document.querySelectorAll('div, img, span').forEach(el => {
                            const style = el?.getAttribute('style');
                            if (style && style.includes(targetUrlPart)) {
                                el.remove();
                            }
                        });
                    }
                    removeQrCode();
                    setTimeout(removeQrCode, 1000);
                    if (!document.getElementById('custom-zoom-style')) {
                        const styleEl = document.createElement('style');
                        styleEl.id = 'custom-zoom-style';
                        styleEl.textContent = `
                            .leaflet-control-zoomslider.leaflet-bar.leaflet-control {
                                margin-bottom: -150px !important;
                            }
                            .leaflet-bottom .leaflet-control {
                                margin-bottom: -150px !important;
                            }
                        `;
                        document.head.appendChild(styleEl);
                    }
                    const zoomSlider = document.querySelector('.leaflet-control-zoomslider.leaflet-bar.leaflet-control');
                    if (zoomSlider) {
                        zoomSlider.setAttribute('style', `margin-bottom: -150px !important; ${zoomSlider.getAttribute('style') || ''}`);
                    }
                    const observer = new MutationObserver(() => {
                        if (zoomSlider) zoomSlider.style.marginBottom = '-150px !important';
                        removeQrCode();
                    });
                    observer.observe(document.body, { 
                        childList: true, 
                        subtree: true,
                        attributes: true,
                        attributeFilter: ['style', 'class']
                    });
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

        private void RefreshMapToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshMapToggle.IsEnabled = false;
                MapWebView.Reload();
                Task.Delay(1000);
                RefreshMapToggle.IsEnabled = true;
            }
            catch (Exception ex)
            {
                RefreshMapToggle.IsEnabled = true;
                System.Diagnostics.Debug.WriteLine($"刷新地图失败: {ex.Message}");
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