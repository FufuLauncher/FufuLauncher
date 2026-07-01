/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services;
using FufuLauncher.Services.MiHoYo;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Views
{
    public sealed partial class BBSWindow : Window
    {
        #region 
        private AppWindow m_AppWindow;

        private byte[] _screenshotBytes;

        private const string CNVersion = "2.109.0";
        private const string CNK2 = "lX8m5VO5at5JG7hR8hzqFwzyL5aB1tYo";
        private const string CNLK2 = "yBh10ikxtLPoIhgwgPZSv5dmfaOTSJ6a";
        private const string CNX4 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
        private const string CNX6 = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
        private const string ToolVersion = "v6.6.1-gr-cn";
        private const string Page = "v6.6.1-gr-cn_#/ys";

        private class ClientConfig
        {
            public string ClientType { get; set; }
            public string AppVersion { get; set; }
            public string Salt { get; set; }
            public string UserAgent { get; set; }
            public bool UseDS2 { get; set; }
        }

        private readonly Dictionary<string, ClientConfig> _clientConfigs = new()
        {
            ["2"] = new ClientConfig
            {
                ClientType = "2",
                AppVersion = CNVersion,
                Salt = CNLK2,
                UserAgent = "",
                UseDS2 = false
            },
            ["5"] = new ClientConfig
            {
                ClientType = "5",
                AppVersion = CNVersion,
                Salt = CNX4,
                UserAgent = "",
                UseDS2 = true
            }
        };

       
        private static readonly (string prefix, string clientType)[] ApiRouteMap =
        {
            ("/game_record/app/genshin/api/", "5"),
            ("/record/", "5"),
            ("/game_record/", "5"),
            ("/event/", "2"),
            ("/community/", "2"),
        };

        private ClientConfig _currentConfig;
        private string _deviceId = "";          
        private Dictionary<string, string> cookieDic = new();

        private readonly IDeviceFingerprintService _fingerprintService;
        private static readonly DeviceProfileService _deviceProfileService = new();
        private string _deviceName = "";
        private string _sysVersion = "";
        private string _deviceUserAgent = "";

        private string _activeDeviceFp = string.Empty;
        
        private const string DefaultUrl = ApiEndpoints.BbsDefaultUrl;

        private const string HideScrollBarScript = """
            let hideStyle = document.createElement('style');
            hideStyle.innerHTML = '::-webkit-scrollbar{ display:none }';
            document.querySelector('body').appendChild(hideStyle);
            """;

        private const string MiHoYoJSInterfaceScript = """
            if (typeof window.MiHoYoJSInterface === 'undefined') {
                window.MiHoYoJSInterface = {
                    postMessage: function(arg) { window.chrome.webview.postMessage(arg) },
                    closePage: function() { this.postMessage('{"method":"closePage"}') }
                };
            }
            """;

        private const string ConvertMouseToTouchScript = """
            function mouseListener (e, event) {
                let touch = new Touch({ identifier: Date.now(), target: e.target, clientX: e.clientX, clientY: e.clientY, screenX: e.screenX, screenY: e.screenY, pageX: e.pageX, pageY: e.pageY });
                let touchEvent = new TouchEvent(event, { cancelable: true, bubbles: true, touches: [touch], targetTouches: [touch], changedTouches: [touch] });
                e.target.dispatchEvent(touchEvent);
            }
            let mouseMoveListener = (e) => { mouseListener(e, 'touchmove'); };
            let mouseUpListener = (e) => { mouseListener(e, 'touchend'); document.removeEventListener('mousemove', mouseMoveListener); document.removeEventListener('mouseup', mouseUpListener); };
            let mouseDownListener = (e) => { mouseListener(e, 'touchstart'); document.addEventListener('mousemove', mouseMoveListener); document.addEventListener('mouseup', mouseUpListener); };
            document.addEventListener('mousedown', mouseDownListener);
            """;

        private const string HideWebViewTracesScript = """
            Object.defineProperty(navigator, 'webdriver', { get: () => false });
            Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
            """;
        #endregion

        #region
        public BBSWindow() : this(true)
        {
        }

        private BBSWindow(bool autoInitialize)
        {
            InitializeComponent();

            _fingerprintService = App.GetService<IDeviceFingerprintService>();

            // 统一设备档案：和 DailyNoteService 使用同一套
            var accountManager = App.GetService<AccountManager>();
            var activeId = accountManager.ActiveAccountId;
            if (!string.IsNullOrEmpty(activeId))
            {
                _deviceId = DeviceProfileService.GetDeviceIdForAccount(activeId);
                var profile = _deviceProfileService.SelectProfile(activeId);
                _deviceName = profile.DeviceName;
                _sysVersion = profile.SysVersion;
                _deviceUserAgent = profile.UserAgent;
            }
            else
            {
                
                _deviceId = Guid.NewGuid().ToString();
                _deviceName = "Xiaomi%2024031PN0DC";
                _sysVersion = "12";
                _deviceUserAgent = $"Mozilla/5.0 (Linux; Android 12; 24031PN0DC Build/V417IR; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/110.0.5481.154 Safari/537.36 miHoYoBBS/{CNVersion}";
            }

          
            foreach (var config in _clientConfigs.Values)
            {
                config.UserAgent = _deviceUserAgent;
            }

            _currentConfig = _clientConfigs["2"];

            InitializeWindowStyle();
            UrlTextBox.Text = DefaultUrl;

            if (autoInitialize)
            {
                _ = InitializeWebViewAsync();
            }
        }

        private void InitializeWindowStyle()
        {
            m_AppWindow = AppWindow;
            var displayArea = DisplayArea.GetFromWindowId(m_AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var targetHeight = (int)(displayArea.WorkArea.Height * 0.8);
                var targetWidth = (int)(targetHeight * 9.0 / 16.0);

                m_AppWindow.Resize(new SizeInt32(targetWidth, targetHeight));
                m_AppWindow.Move(new PointInt32(
                    (displayArea.WorkArea.Width - targetWidth) / 2 + displayArea.WorkArea.X,
                    (displayArea.WorkArea.Height - targetHeight) / 2 + displayArea.WorkArea.Y
                ));
            }
            if (AppTitleBar != null)
            {
                m_AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                m_AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                m_AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                SetTitleBar(AppTitleBar);
            }
        }

        #endregion

        private async Task InitializeWebViewAsync()
        {
            try
            {
               
                await EnsureDeviceFpAsync();

                await BBSWebView.EnsureCoreWebView2Async();
                UpdateWebViewSettings();

                BBSWebView.CoreWebView2.AddWebResourceRequestedFilter("*://*.mihoyo.com/*", CoreWebView2WebResourceContext.All);
                BBSWebView.CoreWebView2.AddWebResourceRequestedFilter("*://*.hoyolab.com/*", CoreWebView2WebResourceContext.All);

                BBSWebView.CoreWebView2.WebResourceRequested += CoreWebView2_WebResourceRequested;
                BBSWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                BBSWebView.CoreWebView2.DOMContentLoaded += CoreWebView2_DOMContentLoaded;
                BBSWebView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;

                await BBSWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(MiHoYoJSInterfaceScript);
                await BBSWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(TabKeyInterceptorScript);
                await BBSWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(HideWebViewTracesScript);

                await LoadPageAsync(DefaultUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView Init Failed: {ex.Message}");
            }
        }


        private async Task EnsureDeviceFpAsync()
        {
            try
            {
                var accountManager = App.GetService<AccountManager>();
                var activeId = accountManager.ActiveAccountId;
                if (string.IsNullOrEmpty(activeId))
                {
                    System.Diagnostics.Debug.WriteLine("[BBSWindow] 无活跃账号，跳过指纹获取");
                    return;
                }

                var cookies = await accountManager.LoadCookiesAsync(activeId);
                if (cookies == null || cookies.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[BBSWindow] 无可用 Cookies，跳过指纹获取");
                    return;
                }

                _activeDeviceFp = await _fingerprintService.GetOrRegisterFingerprintAsync(activeId, cookies);
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] 活跃账号指纹已获取: {_activeDeviceFp}");

                if (!string.IsNullOrEmpty(_activeDeviceFp))
                {
                    cookieDic["DEVICEFP"] = _activeDeviceFp;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] EnsureDeviceFpAsync 失败: {ex.Message}");
            }
        }

        private void UpdateWebViewSettings()
        {
            if (BBSWebView?.CoreWebView2 != null)
            {
                BBSWebView.CoreWebView2.Settings.UserAgent = _currentConfig.UserAgent;
                BBSWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                BBSWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                BBSWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            }
        }

        private void ToggleTopBar()
        {
            TopBarGrid.Visibility = TopBarGrid.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void RootGrid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Tab)
            {
                ToggleTopBar();
                e.Handled = true;
            }
        }

        private const string TabKeyInterceptorScript = """
                                                       window.addEventListener('keydown', function(e) {
                                                           if (e.key === 'Tab') {
                                                               e.preventDefault();
                                                               window.chrome.webview.postMessage('{"method":"toggleTopBar"}');
                                                           }
                                                       });
                                                       """;


        private ClientConfig SelectConfig(string uri)
        {
            foreach (var (prefix, clientType) in ApiRouteMap)
            {
                if (uri.Contains(prefix))
                    return _clientConfigs[clientType];
            }
            return _clientConfigs["2"]; // 默认 DS1
        }

        private async void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                if (args.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var uri = args.Request.Uri;
                bool isApiRequest = uri.Contains("/api/") || uri.Contains("/community/") || uri.Contains("/record/") || uri.Contains("/event/");

                if (isApiRequest && (uri.Contains("mihoyo.com") || uri.Contains("hoyolab.com")))
                {
                    var headers = args.Request.Headers;
                    var config = SelectConfig(uri);

                   
                    headers.RemoveHeader("x-rpc-client_type");
                    headers.RemoveHeader("x-rpc-app_version");
                    headers.RemoveHeader("DS");
                    headers.RemoveHeader("x-rpc-device_id");
                    headers.RemoveHeader("x-rpc-device_fp");
                    headers.RemoveHeader("x-rpc-device_name");
                    headers.RemoveHeader("x-rpc-sys_version");
                    headers.RemoveHeader("x-rpc-tool_verison");
                    headers.RemoveHeader("x-rpc-page");
                    headers.RemoveHeader("x-rpc-sdk_version");
                    headers.RemoveHeader("X-Requested-With");

                  
                    headers.SetHeader("x-rpc-client_type", config.ClientType);
                    headers.SetHeader("x-rpc-app_version", config.AppVersion);
                    headers.SetHeader("x-rpc-device_id", _deviceId);
                    headers.SetHeader("x-rpc-device_name", _deviceName);
                    headers.SetHeader("x-rpc-sys_version", _sysVersion);
                    headers.SetHeader("x-rpc-tool_verison", ToolVersion);
                    headers.SetHeader("x-rpc-page", Page);
                    headers.SetHeader("x-rpc-app_id", "bll8iq97cem8");
                    headers.SetHeader("x-rpc-sdk_version", "2.16.0");
                    headers.SetHeader("X-Requested-With", "com.mihoyo.hyperion");

                    string fp = _activeDeviceFp;
                    if (string.IsNullOrEmpty(fp))
                    {
                        System.Diagnostics.Debug.WriteLine("[BBSWindow] 警告：_activeDeviceFp 为空，使用临时随机值");
                        fp = Convert.ToHexString(RandomNumberGenerator.GetBytes(7)).ToLowerInvariant();
                    }
                    headers.SetHeader("x-rpc-device_fp", fp);


                    string ds;
                    if (config.UseDS2)
                    {
                        string query = GetSortedQuery(uri);
                        string body = "";
                        if (args.Request.Method == "POST" && args.Request.Content != null)
                        {
                            body = await GetJsonBodyAsync(args.Request.Content);
                        }
                        ds = CalculateDS2(config.Salt, query, body);
                    }
                    else
                    {
                        ds = CalculateDS1(config.Salt);
                    }
                    headers.SetHeader("DS", ds);

                   
                    headers.SetHeader("Origin", "https://webstatic.mihoyo.com");
                    headers.SetHeader("Referer", "https://webstatic.mihoyo.com/");
                    headers.SetHeader("Accept", "application/json, text/plain, */*");
                    headers.SetHeader("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        private async Task<JsResult?> HandleJsMessageAsync(JsParam param)
        {
            if (param.Method == "getDS" || param.Method == "getDS2")
            {
                string ds;
                if (_currentConfig.UseDS2)
                {
                    string q = "", b = "";
                    if (param.Payload != null)
                    {
                        if (param.Payload["query"] is JsonObject queryObj) q = GetSortedQueryFromJson(queryObj);
                        if (param.Payload["body"] is JsonObject bodyObj) b = SortJson(bodyObj);
                        else if (param.Payload["body"] != null) b = param.Payload["body"]!.ToString();
                    }
                    ds = CalculateDS2(_currentConfig.Salt, q, b);
                }
                else
                {
                    ds = CalculateDS1(_currentConfig.Salt);
                }
                return new JsResult { Data = new() { ["DS"] = ds } };
            }

            return param.Method switch
            {
                "closePage" => HandleClosePage(),
                "getHTTPRequestHeaders" => GetHttpRequestHeader(),
                "getCookieInfo" => GetCookieInfoMinimal(),
                "getCookieToken" => new JsResult { Data = new() { ["cookie_token"] = cookieDic.GetValueOrDefault("cookie_token") ?? "" } },
                "getStatusBarHeight" => new JsResult { Data = new() { ["statusBarHeight"] = 0 } },
                "getUserInfo" => GetUserInfo(),
                "getCurrentLocale" => new JsResult { Data = new() { ["language"] = "zh-cn", ["timeZone"] = "GMT+8" } },
                "pushPage" => HandlePushPage(param),
                "share" => await HandleShareAsync(param),
                "toggleTopBar" => HandleToggleTopBar(),
                _ => null
            };
        }

        private JsResult? HandleToggleTopBar()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ToggleTopBar();
            });
            return null;
        }
        private JsResult GetCookieInfoMinimal()
        {
            string fp = _activeDeviceFp;
            if (string.IsNullOrEmpty(fp))
            {
                System.Diagnostics.Debug.WriteLine("[BBSWindow] GetCookieInfoMinimal: _activeDeviceFp 为空，使用随机回退");
                fp = Convert.ToHexString(RandomNumberGenerator.GetBytes(7)).ToLowerInvariant();
            }
            return new JsResult
            {
                Data = new Dictionary<string, object>
                {
                    ["ltuid"] = cookieDic.GetValueOrDefault("ltuid") ?? "",
                    ["ltoken"] = cookieDic.GetValueOrDefault("ltoken") ?? "",
                    ["cookie_token"] = cookieDic.GetValueOrDefault("cookie_token") ?? "",
                    ["account_id"] = cookieDic.GetValueOrDefault("account_id") ?? "",
                    ["ltuid_v2"] = cookieDic.GetValueOrDefault("ltuid_v2") ?? "",
                    ["ltoken_v2"] = cookieDic.GetValueOrDefault("ltoken_v2") ?? "",
                    ["account_mid_v2"] = cookieDic.GetValueOrDefault("account_mid_v2") ?? "",
                    ["cookie_token_v2"] = cookieDic.GetValueOrDefault("cookie_token_v2") ?? "",
                    ["DEVICEFP"] = fp 
                }
            };
        }

        private async Task<string> GetJsonBodyAsync(IRandomAccessStream stream)
        {
            try
            {
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                var jsonStr = reader.ReadString(reader.UnconsumedBufferLength);
                if (string.IsNullOrWhiteSpace(jsonStr)) return "";

                var jsonNode = JsonNode.Parse(jsonStr);
                if (jsonNode is JsonObject jsonObj) return SortJson(jsonObj);
                return jsonNode?.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) ?? "";
            }
            catch { return ""; }
        }

        private string SortJson(JsonObject jsonObj)
        {
            var sortedKeys = jsonObj.Select(k => k.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var sb = new StringBuilder();
            sb.Append('{');
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                var key = sortedKeys[i];
                var value = jsonObj[key];
                sb.Append($"\"{key}\":");
                if (value is JsonObject nestedObj) sb.Append(SortJson(nestedObj));
                else sb.Append(value?.ToJsonString(new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                if (i < sortedKeys.Count - 1) sb.Append(',');
            }
            sb.Append('}');
            return sb.ToString();
        }

        private string GetSortedQueryFromJson(JsonObject queryObj)
        {
            var sortedKeys = queryObj.Select(k => k.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            var pairs = new List<string>();
            foreach (var key in sortedKeys)
            {
                pairs.Add($"{key}={queryObj[key]?.ToString()}");
            }
            return string.Join("&", pairs);
        }

        private string CalculateDS1(string salt)
        {
            var t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var r = GetRandomString(6);
            var check = GetMd5($"salt={salt}&t={t}&r={r}");
            return $"{t},{r},{check}";
        }

        private string CalculateDS2(string salt, string query, string body)
        {
            var t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var r = new Random().Next(100000, 200000).ToString();
            var check = GetMd5($"salt={salt}&t={t}&r={r}&b={body}&q={query}");
            return $"{t},{r},{check}";
        }

        private string GetSortedQuery(string url)
        {
            try
            {
                var uriObj = new Uri(url);
                var query = uriObj.Query.TrimStart('?');
                if (string.IsNullOrEmpty(query)) return "";
                var dict = System.Web.HttpUtility.ParseQueryString(query);

                var sortedKeys = dict.AllKeys.Where(k => k != null).OrderBy(k => k, StringComparer.Ordinal).ToList();
                var pairs = new List<string>();
                foreach (var key in sortedKeys)
                {
                    pairs.Add($"{key}={dict[key]}");
                }
                return string.Join("&", pairs);
            }
            catch { return ""; }
        }

        private static string GetRandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private string GetMd5(string input)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private void CoreWebView2_DOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
        {
            sender.ExecuteScriptAsync(HideScrollBarScript);
            sender.ExecuteScriptAsync(ConvertMouseToTouchScript);
        }

        private void CoreWebView2_SourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
        {
            if (UrlTextBox != null) UrlTextBox.Text = sender.Source;
        }

        private async void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string message = args.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(message)) return;
                var param = JsonSerializer.Deserialize<JsParam>(message, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (param == null) return;

                JsResult? result = await HandleJsMessageAsync(param);

                if (result != null && !string.IsNullOrEmpty(param.Callback))
                {
                    await ExecuteCallback(param.Callback, result);
                }
            }
            catch { }
        }

        private JsResult GetHttpRequestHeader()
        {
            string fp = _activeDeviceFp;
            if (string.IsNullOrEmpty(fp))
            {
                System.Diagnostics.Debug.WriteLine("[BBSWindow] GetHttpRequestHeader: _activeDeviceFp 为空，使用随机回退");
                fp = Convert.ToHexString(RandomNumberGenerator.GetBytes(7)).ToLowerInvariant();
            }
            var data = new Dictionary<string, object>
            {
                ["x-rpc-app_id"] = "bll8iq97cem8",
                ["x-rpc-client_type"] = _currentConfig.ClientType,
                ["x-rpc-app_version"] = _currentConfig.AppVersion,
                ["x-rpc-device_id"] = _deviceId,
                ["x-rpc-sdk_version"] = "2.16.0",
                ["x-rpc-device_fp"] = fp   
            };
            return new JsResult { Data = data };
        }

        private JsResult? HandleClosePage()
        {
            if (BBSWebView.CoreWebView2.CanGoBack) BBSWebView.CoreWebView2.GoBack();
            else Close();
            return null;
        }

        private JsResult? HandlePushPage(JsParam param)
        {
            string? url = param.Payload?["page"]?.ToString();
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (url.StartsWith("mihoyobbs://article/"))
                {
                    url = url.Replace("mihoyobbs://article/", ApiEndpoints.MiyousheArticleUrl);
                }
                else if (url.StartsWith("mihoyobbs://webview?link="))
                {
                    url = Uri.UnescapeDataString(url.Replace("mihoyobbs://webview?link=", ""));
                }
                BBSWebView.CoreWebView2.Navigate(url);
            }
            return null;
        }

        private async Task<JsResult?> HandleShareAsync(JsParam param)
        {
            string type = param.Payload?["type"]?.ToString();
            if (type == "screenshot")
            {
                try
                {
                    string resultJson = await BBSWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", """{"format":"png","captureBeyondViewport":true}""");
                    var node = JsonNode.Parse(resultJson);
                    string base64 = node?["data"]?.ToString();
                    if (!string.IsNullOrEmpty(base64)) await ShowScreenshotAsync(base64);
                }
                catch { }
            }
            else if (type == "image")
            {
                string base64 = param.Payload?["content"]?["image_base64"]?.ToString();
                if (!string.IsNullOrEmpty(base64)) await ShowScreenshotAsync(base64);
            }
            return new JsResult { Data = new() { ["type"] = type } };
        }

        private async Task ShowScreenshotAsync(string base64)
        {
            try
            {
                _screenshotBytes = Convert.FromBase64String(base64);
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(_screenshotBytes.AsBuffer());
                stream.Seek(0);
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                ScreenshotImage.Source = bitmap;
                ScreenshotGrid.Visibility = Visibility.Visible;
            }
            catch { }
        }

        private async void SaveScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (_screenshotBytes == null) return;
            try
            {
                var picker = new FileSavePicker();
                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
                picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                picker.FileTypeChoices.Add("PNG Image", new List<string>() { ".png" });
                picker.SuggestedFileName = $"mihoyo_bbs_{DateTime.Now:yyyyMMddHHmmss}";

                StorageFile file = await picker.PickSaveFileAsync();
                if (file != null)
                {
                    await File.WriteAllBytesAsync(file.Path, _screenshotBytes);
                    CloseScreenshot_Click(null, null);
                }
            }
            catch { }
        }

        private async void CopyScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (_screenshotBytes == null) return;
            try
            {
                var dataPackage = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(_screenshotBytes.AsBuffer());
                stream.Seek(0);
                dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(dataPackage);
                CloseScreenshot_Click(null, null);
            }
            catch { }
        }

        private void CloseScreenshot_Click(object sender, RoutedEventArgs e)
        {
            ScreenshotGrid.Visibility = Visibility.Collapsed;
            _screenshotBytes = null;
            ScreenshotImage.Source = null;
        }

        private JsResult GetUserInfo()
        {
            var uid = cookieDic.GetValueOrDefault("ltuid_v2") ?? cookieDic.GetValueOrDefault("ltuid") ?? "";
            return new JsResult
            {
                Data = new() { ["id"] = uid, ["gender"] = 0, ["nickname"] = "", ["introduce"] = "", ["avatar_url"] = "" }
            };
        }

        private async Task ExecuteCallback(string callback, JsResult result)
        {
            string payload = JsonSerializer.Serialize(result, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string script = $"javascript:mhyWebBridge(\"{callback}\", {payload})";
            await BBSWebView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private void GoButton_Click(object sender, RoutedEventArgs e) => NavigateToUrl();
        private void UrlTextBox_KeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) NavigateToUrl(); }

        private void NavigateToUrl()
        {
            var url = UrlTextBox.Text;
            if (!string.IsNullOrEmpty(url) && !url.StartsWith("http")) url = "https://" + url;
            if (!string.IsNullOrEmpty(url)) BBSWebView.CoreWebView2.Navigate(url);
        }

        private void ClientTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BBSWebView == null) return;
            if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item && item.Tag is string type)
            {
                if (_clientConfigs.TryGetValue(type, out var config))
                {
                    _currentConfig = config;
                    UpdateWebViewSettings();
                    BBSWebView.Reload();
                }
            }
        }

        private async Task LoadPageAsync(string url)
        {
            System.Diagnostics.Debug.WriteLine($"[BBSWindow] LoadPageAsync called with URL: {url}");
            await LoadActiveAccountCookiesAsync();

            var manager = BBSWebView.CoreWebView2.CookieManager;
            if (BBSWebView.Source == null || BBSWebView.Source.ToString() == "about:blank")
            {
                var cookies = await manager.GetCookiesAsync("https://webstatic.mihoyo.com");
                foreach (var c in cookies) manager.DeleteCookie(c);
            }

            foreach (var kv in cookieDic)
            {
                var cookie = manager.CreateCookie(kv.Key, kv.Value, ".mihoyo.com", "/");
                manager.AddOrUpdateCookie(cookie);
            }
            System.Diagnostics.Debug.WriteLine($"[BBSWindow] Added {cookieDic.Count} cookies to WebView2");
            BBSWebView.CoreWebView2.Navigate(url);
        }

        private async Task LoadActiveAccountCookiesAsync()
        {
            cookieDic.Clear();

            var accountManager = App.GetService<AccountManager>();
            var activeId = accountManager.ActiveAccountId;
            if (activeId == null) return;

            var cookies = await accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0) return;

            foreach (var kv in cookies)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                {
                    cookieDic[kv.Key] = kv.Value;
                }
            }
        }

        private void ParseCookie(string cookieStr)
        {
            cookieDic.Clear();
            if (string.IsNullOrWhiteSpace(cookieStr)) return;
            foreach (var item in cookieStr.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = item.Split('=', 2);
                if (kv.Length == 2) cookieDic[kv[0].Trim()] = kv[1].Trim();
            }
        }

        private class JsParam
        {
            [JsonPropertyName("method")] public string Method { get; set; } = "";
            [JsonPropertyName("payload")] public JsonNode? Payload { get; set; }
            [JsonPropertyName("callback")] public string? Callback { get; set; }
        }

        private class JsResult
        {
            [JsonPropertyName("retcode")] public int Code { get; set; } = 0;
            [JsonPropertyName("message")] public string Message { get; set; } = "";
            [JsonPropertyName("data")] public Dictionary<string, object> Data { get; set; } = new();
        }

        public class AppConfig { public AccountConfig Account { get; set; }
 }
        public class AccountConfig { public string Cookie { get; set; } }
    }
}
