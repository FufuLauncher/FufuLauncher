/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views
{
    public sealed partial class AnnouncementWindow : Window
    {
        private LocalFontServer _fontServer;
        private AppWindow _appWindow;

        public AnnouncementWindow()
        {
            InitializeComponent();

            InitializeAppWindow();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            Title = "游戏公告";

            StartFontServer();

            Closed += (s, e) =>
            {
                _fontServer?.Stop();
            };

            ContentFrame.Navigated += ContentFrame_Navigated;

            ContentFrame.Navigate(typeof(AnnouncementPage));
        }

        private void InitializeAppWindow()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
        }

        private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
        {
            if (e.Content is AnnouncementPage page)
            {
                page.ResizeRequested += OnResizeRequested;

                page.CloseRequested += () =>
                {
                    Close();
                };
            }
        }

        private void OnResizeRequested(double contentWidth, double contentHeight)
        {
            if (_appWindow == null) return;

            double scale = this.Content.XamlRoot.RasterizationScale;

            int targetWidth = (int)(Math.Max(contentWidth, 800) * scale);

            int targetHeight = (int)((contentHeight + 32) * scale);

            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                int maxHeight = (int)(displayArea.WorkArea.Height * 0.9);
                if (targetHeight > maxHeight) targetHeight = maxHeight;
            }

            _appWindow.Resize(new Windows.Graphics.SizeInt32(targetWidth, targetHeight));

            CenterAppWindow(displayArea);
        }

        private void CenterAppWindow(DisplayArea displayArea)
        {
            if (displayArea == null) return;
            var centeredPosition = _appWindow.Position;
            centeredPosition.X = (displayArea.WorkArea.Width - _appWindow.Size.Width) / 2;
            centeredPosition.Y = (displayArea.WorkArea.Height - _appWindow.Size.Height) / 2;
            _appWindow.Move(centeredPosition);
        }

        private void StartFontServer()
        {
            try
            {
                string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "zh-cn.ttf");

                try
                {
                    if (!File.Exists(fontPath))
                    {
                        fontPath = Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "zh-cn.ttf");
                    }
                }
                catch
                {
                }

                if (!File.Exists(fontPath))
                {
                    DebugLog.Info("AnnouncementWindow", $"[Error] Font file not found: {fontPath}");
                    return;
                }

                _fontServer = new LocalFontServer(fontPath);
                _fontServer.Start();
            }
            catch (Exception ex)
            {
                DebugLog.Info("AnnouncementWindow", $"[Fatal] Server startup flow exception: {ex.Message}");
            }
        }
    }

    public class LocalFontServer
    {
        private HttpListener _listener = null!;
        private readonly string _fontFilePath;
        private bool _isRunning;

        private const string ServerUrl = ApiEndpoints.AnnouncementFontServerUrl;

        public LocalFontServer(string fontFilePath)
        {
            _fontFilePath = fontFilePath;
        }

        public async void Start()
        {
            Stop();

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(ServerUrl);
                _listener.Start();
                _isRunning = true;

                DebugLog.Info("AnnouncementWindow", $"Font service started at {ServerUrl}");

                while (_isRunning && _listener.IsListening)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        ProcessRequest(context);
                    }
                    catch (HttpListenerException)
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("AnnouncementWindow", $"Failed to start HTTP listener: {ex.Message}");
                _isRunning = false;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            if (_listener != null)
            {
                try
                {
                    if (_listener.IsListening)
                    {
                        _listener.Stop();
                    }

                    _listener.Close();
                }
                catch
                {
                }
                finally
                {
                    _listener = null;
                }
            }
        }

        private async void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var response = context.Response;
                var requestPath = context.Request.Url.AbsolutePath.ToLower();

                response.AppendHeader("Access-Control-Allow-Origin", "*");
                response.AppendHeader("Access-Control-Allow-Methods", "GET");

                if (requestPath.Contains("zh-cn.ttf"))
                {
                    byte[] buffer = await File.ReadAllBytesAsync(_fontFilePath);
                    response.ContentType = "font/ttf";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
                else
                {
                    response.StatusCode = 404;
                }

                response.OutputStream.Close();
            }
            catch
            {
                try
                {
                    context.Response.Abort();
                }
                catch
                {
                }
            }
        }
    }
}