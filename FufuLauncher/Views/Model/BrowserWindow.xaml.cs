/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinRT.Interop;
using System.Globalization;

namespace FufuLauncher.Views
{
    public sealed partial class BrowserWindow : Window
    {
        private AppWindow _appWindow;
        private BrowserConfig _config;
        private string _currentScriptId;

        public BrowserWindow()
        {
            InitializeComponent();
            _config = BrowserConfig.Load();
            InitializeWindow();
            InitializeWebView();
            ApplyProcessPriority();
        }
        
        private void ApplyProcessPriority()
        {
            try
            {
                using (var process = System.Diagnostics.Process.GetCurrentProcess())
                {
                    process.PriorityClass = _config.EnableHighPriority 
                        ? System.Diagnostics.ProcessPriorityClass.High 
                        : System.Diagnostics.ProcessPriorityClass.Normal;
                }
            }
            catch
            {
                // ignored
            }
        }

        private void InitializeWindow()
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(wndId);

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            _appWindow.Resize(new Windows.Graphics.SizeInt32(800, 600));
        }

        private async void InitializeWebView()
        {
            await WebView.EnsureCoreWebView2Async();

            WebView.NavigationStarting += (s, e) => LoadingBar.Visibility = Visibility.Visible;
            
            WebView.NavigationCompleted += async (s, e) => {
                LoadingBar.Visibility = Visibility.Collapsed;
                UrlTextBox.Text = WebView.Source.ToString();
                
                await SetWebZoomAsync(_config.ZoomFactor);
            };
            
            ApplyScriptsToWebView();
            
            if (Uri.TryCreate(_config.HomePage, UriKind.Absolute, out var uri))
            {
                WebView.Source = uri;
            }
        }
        
        private async void ApplyScriptsToWebView()
        {
            if (WebView.CoreWebView2 == null) return;
            
            if (!string.IsNullOrEmpty(_currentScriptId))
            {
                WebView.CoreWebView2.RemoveScriptToExecuteOnDocumentCreated(_currentScriptId);
            }
            
            string script = $@"
                document.addEventListener('keydown', (e) => {{
                    const videos = document.getElementsByTagName('video');
                    if (videos.length > 0) {{
                        if (e.code === '{_config.RewindKey}') {{
                            for(let v of videos) v.currentTime -= 5;
                        }}
                        else if (e.code === '{_config.FastForwardKey}') {{
                            for(let v of videos) v.currentTime += 5;
                        }}
                    }}
                }});
            ";

            _currentScriptId = await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
            
            await SetWebZoomAsync(_config.ZoomFactor);
        }
        
        private async Task SetWebZoomAsync(double zoom)
        {
            if (WebView.CoreWebView2 != null)
            {
                string js = $"if (document.body) document.body.style.zoom = '{zoom.ToString(CultureInfo.InvariantCulture)}';";
                try
                {
                    await WebView.CoreWebView2.ExecuteScriptAsync(js);
                }
                catch
                {
                    // ignored
                }
            }
        }

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingUrlBox.Text = _config.HomePage;
            SettingZoomSlider.Value = _config.ZoomFactor;
            SettingRewindBox.Text = _config.RewindKey;
            SettingForwardBox.Text = _config.FastForwardKey;
            SettingHighPriorityToggle.IsOn = _config.EnableHighPriority;

            SettingsDialog.XamlRoot = Content.XamlRoot;
            var result = await SettingsDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                _config.HomePage = SettingUrlBox.Text;
                _config.ZoomFactor = SettingZoomSlider.Value;
                _config.RewindKey = SettingRewindBox.Text;
                _config.FastForwardKey = SettingForwardBox.Text;
                _config.EnableHighPriority = SettingHighPriorityToggle.IsOn;
                _config.Save();
                
                ApplyProcessPriority();
                ApplyScriptsToWebView();
            }
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = (sender as ToggleButton)!.IsChecked ?? false;
                PinButton.Background = presenter.IsAlwaysOnTop 
                    ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                    : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorTransparentBrush"];
            }
        }

        private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                var url = UrlTextBox.Text;
                if (!url.StartsWith("http") && !url.StartsWith("https"))
                {
                    url = "https://" + url;
                }
                try { WebView.Source = new Uri(url); }
                catch
                {
                    // ignored
                }
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            WebView.Reload();
        }
    }
}
