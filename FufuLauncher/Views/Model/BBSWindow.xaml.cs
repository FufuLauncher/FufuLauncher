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
using FufuLauncher.Services;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Views
{
    public sealed partial class BBSWindow : Window
    {
        private AppWindow m_AppWindow;
        private static readonly System.Threading.SemaphoreSlim _fetchApiSemaphore = new System.Threading.SemaphoreSlim(1, 1);
        private string _lastSeedId = "";
        private string _lastSeedTime = "";
        
        private byte[] _screenshotBytes;

        private const string CNVersion = "2.109.0";
        private const string CNK2 = "lX8m5VO5at5JG7hR8hzqFwzyL5aB1tYo";
        private const string CNLK2 = "yBh10ikxtLPoIhgwgPZSv5dmfaOTSJ6a";
        private const string CNX4 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";

        private class ClientConfig
        {
            public string ClientType { get; set; }     
            public string AppVersion { get; set; }     
            public string Salt { get; set; }           
            public string UserAgent { get; set; }      
            public string DeviceModel { get; set; }    
            public string SysVersion { get; set; }     
            public bool UseDS2 { get; set; }           
        }
        
        private readonly Dictionary<string, ClientConfig> _clientConfigs = new()
        {
            ["2"] = new ClientConfig 
            {
                ClientType = "2",
                AppVersion = CNVersion, 
                Salt = CNLK2, 
                UserAgent = $"Mozilla/5.0 (Linux; Android 15) Mobile miHoYoBBS/{CNVersion}",
                DeviceModel = "Mi 10",
                SysVersion = "15",
                UseDS2 = false
            },
            ["5"] = new ClientConfig
            {
                ClientType = "5",
                AppVersion = CNVersion,
                Salt = CNX4,
                UserAgent = $"Mozilla/5.0 (Linux; Android 12; 24031PN0DC Build/V417IR; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/110.0.5481.154 Safari/537.36 miHoYoBBS/{CNVersion}",
                DeviceModel = "Mi 10",
                SysVersion = "15",
                UseDS2 = true
            }
        };

        private ClientConfig _currentConfig;
        private readonly string _deviceId;
        private readonly string _deviceId36;
        private Dictionary<string, string> cookieDic = new();

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

        public BBSWindow() : this(true)
        {
        }

        private BBSWindow(bool autoInitialize)
        {
            InitializeComponent();
            
            _deviceId36 = GetStableGuid();
            _deviceId = GenerateDeviceId40(_deviceId36);
            
            _currentConfig = _clientConfigs["5"]; 
            
            InitializeWindowStyle();
            UrlTextBox.Text = DefaultUrl;

            if (autoInitialize)
            {
                _ = InitializeWebViewAsync();
            }
        }

        private string GetStableGuid()
        {
            string machineName = Environment.MachineName;
            string userName = Environment.UserName;
            byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(machineName + userName + "FufuBBS"));
            return new Guid(hash).ToString();
        }

        private static string GenerateDeviceId40(string baseGuid)
        {
            byte[] namespaceBytes = Guid.Parse("9450ea74-be9c-35c0-9568-f97407856768").ToByteArray();
            byte[] nameBytes = Encoding.UTF8.GetBytes(baseGuid);
            byte[] hash = SHA1.HashData(namespaceBytes.Concat(nameBytes).ToArray());
            Array.Resize(ref hash, 16);
            hash[7] = (byte)((hash[7] & 0x0F) | 0x50);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            
            byte[] finalHash = SHA1.HashData(hash);
            return Convert.ToHexString(finalHash).ToLowerInvariant();
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

        private async Task InitializeWebViewAsync()
        {
            try
            {
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

                await LoadPageAsync(DefaultUrl);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebView Init Failed: {ex.Message}");
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

        private void UpdateWebViewSettings()
        {
            if (BBSWebView?.CoreWebView2 != null)
            {
                BBSWebView.CoreWebView2.Settings.UserAgent = _currentConfig.UserAgent;
            }
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
                    headers.RemoveHeader("x-rpc-client_type");
                    headers.RemoveHeader("x-rpc-app_version");
                    headers.RemoveHeader("DS");
                    // 覆盖 WebView2 自动注入的 Client Hints，伪装成 Android WebView
                    headers.SetHeader("sec-ch-ua", "\"Android WebView\";v=\"110\", \"Chromium\";v=\"110\", \"Not?A_Brand\";v=\"24\"");
                    headers.SetHeader("sec-ch-ua-mobile", "?1");
                    headers.SetHeader("sec-ch-ua-platform", "\"Android\"");

                    headers.SetHeader("x-rpc-client_type", _currentConfig.ClientType);
                    headers.SetHeader("x-rpc-app_version", _currentConfig.AppVersion);
                    headers.SetHeader("x-rpc-app_id", "bll8iq97cem8");
                    headers.SetHeader("x-rpc-sdk_version", "2.16.0");
                    // BBS API (client_type=2) 用 hex，Game Record (client_type=5) 用 UUID v3
                    var deviceIdForApi = uri.Contains("api-takumi") ? GenGameRecordDeviceId() : GenDeviceId();
                    headers.SetHeader("x-rpc-device_id", deviceIdForApi);
                    headers.SetHeader("x-rpc-device_name", GenDeviceName());
                    headers.SetHeader("x-rpc-sys_version", "12");
                    headers.SetHeader("x-rpc-tool_verison", "v6.6.1-gr-cn");
                    headers.SetHeader("x-rpc-page", "v6.6.1-gr-cn_#/ys");
                    headers.SetHeader("X-Requested-With", "com.mihoyo.hyperion");
                    headers.SetHeader("Referer", "https://webstatic.mihoyo.com/");
                    headers.SetHeader("Origin", "https://webstatic.mihoyo.com");
                    var deviceFp = GetDeviceFpHeader();
                    headers.SetHeader("x-rpc-device_fp", deviceFp);

                    // 覆盖 WebView2 浏览器默认的 Accept，伪装成 API 客户端
                    headers.SetHeader("Accept", "application/json, text/plain, */*");
                    headers.SetHeader("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");

                    string ds;
                    if (_currentConfig.UseDS2)
                    {
                        // api-takumi-record（game_record 系列 API）的 DS2 要求 q 和 b 为空
                        // 参照 GenshinApiClient.CreateSecret2
                        var isTakumiApi = uri.Contains("api-takumi-record");
                        string query = isTakumiApi ? "" : GetSortedQuery(uri);
                        string body = "";
                        if (!isTakumiApi && args.Request.Method == "POST" && args.Request.Content != null)
                        {
                            body = await GetJsonBodyAsync(args.Request.Content);
                        }
                        ds = CalculateDS2(_currentConfig.Salt, query, body);
                        System.Diagnostics.Debug.WriteLine($"[BBSWindow] DS2: {ds} | salt={_currentConfig.Salt[..Math.Min(8, _currentConfig.Salt.Length)]}... | takumi={isTakumiApi} | q={query} | b={body[..Math.Min(50, body.Length)]}");
                    }
                    else
                    {
                        ds = CalculateDS1(_currentConfig.Salt);
                        System.Diagnostics.Debug.WriteLine($"[BBSWindow] DS1: {ds} | salt={_currentConfig.Salt[..Math.Min(8, _currentConfig.Salt.Length)]}...");
                    }
                    System.Diagnostics.Debug.WriteLine($"[BBSWindow] API请求: method={args.Request.Method} url={uri[..Math.Min(120, uri.Length)]} client_type={_currentConfig.ClientType} device_id={GenDeviceId()} device_name={GenDeviceName()} device_fp={deviceFp}");

                    headers.SetHeader("DS", ds);

                    // === 调试：完整 dump 所有请求头，方便对比抓包 ===
                    if (uri.Contains("dailyNote"))
                    {
                        try
                        {
                            var allHeaders = new System.Text.StringBuilder();
                            allHeaders.AppendLine($"[BBSWindow] === 完整请求 DUMP: {args.Request.Method} {uri} ===");
                            // 逐个打印已知关键 header
                            var keyHeaders = new[] {
                                "DS","x-rpc-client_type","x-rpc-app_version","x-rpc-app_id","x-rpc-sdk_version",
                                "x-rpc-device_id","x-rpc-device_name","x-rpc-device_fp","x-rpc-sys_version",
                                "x-rpc-tool_verison","x-rpc-page","X-Requested-With","Referer","Origin",
                                "User-Agent","Accept","Accept-Language","Accept-Encoding","sec-ch-ua",
                                "sec-ch-ua-mobile","sec-ch-ua-platform","Sec-Fetch-Site","Sec-Fetch-Mode",
                                "Sec-Fetch-Dest","Sec-Fetch-User","Cookie","Content-Type","Content-Length"
                            };
                            foreach (var name in keyHeaders)
                            {
                                try { allHeaders.AppendLine($"  {name}: {headers.GetHeader(name)}"); } catch { }
                            }
                            allHeaders.AppendLine("=== DUMP 结束 ===");
                            System.Diagnostics.Debug.WriteLine(allHeaders.ToString());
                        }
                        catch { }
                    }
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
                "getCookieInfo" => new JsResult { Data = cookieDic.ToDictionary(x => x.Key, x => (object)x.Value) },
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

        private static string CalculateDS1(string salt)
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

        private static string GetMd5(string input)
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
            var data = new Dictionary<string, object>
            {
                ["x-rpc-app_id"] = "bll8iq97cem8",
                ["x-rpc-client_type"] = _currentConfig.ClientType,
                ["x-rpc-app_version"] = _currentConfig.AppVersion,
                ["x-rpc-device_id"] = GenDeviceId(),
                ["x-rpc-sdk_version"] = "2.16.0"
            };
            data["x-rpc-device_fp"] = GetDeviceFpHeader();
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
            if (activeId == null)
            {
                System.Diagnostics.Debug.WriteLine("[BBSWindow] LoadCookies: 没有活跃账号");
                return;
            }

            var cookies = await accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] LoadCookies: 账号 {activeId} 无已保存的 Cookie");
                return;
            }

            foreach (var kv in cookies)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
                {
                    cookieDic[kv.Key] = kv.Value;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[BBSWindow] LoadCookies: 从文件加载了 {cookieDic.Count} 个 Cookie，账号={activeId}");

            // 判断 DEVICEFP 是服务器签发还是旧版本本地生成：服务器签发会伴随 SEED_ID
            var hasLocalFp = cookieDic.ContainsKey("DEVICEFP") && !cookieDic.ContainsKey("DEVICEFP_SEED_ID");

            if (!cookieDic.ContainsKey("DEVICEFP"))
            {
                await FetchDeviceFpAndPersistAsync(accountManager, activeId);
            }
            else if (hasLocalFp)
            {
                // 旧版本本地 SHA256 生成的假指纹，服务器不认，清除后重新获取
                cookieDic.TryGetValue("DEVICEFP", out var oldFp);
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] DEVICEFP: 检测到旧版本地生成指纹 {oldFp}，清除并重新向服务器获取");
                cookieDic.Remove("DEVICEFP");
                await FetchDeviceFpAndPersistAsync(accountManager, activeId);
            }
            else if (cookieDic.TryGetValue("DEVICEFP", out var cachedFp))
            {
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] DEVICEFP: 已有服务器签发值 {cachedFp}");
            }

            System.Diagnostics.Debug.WriteLine($"[BBSWindow] Cookie keys: [{string.Join(", ", cookieDic.Keys)}]");
        }

        private async Task FetchDeviceFpAndPersistAsync(AccountManager accountManager, string activeId)
        {
            System.Diagnostics.Debug.WriteLine("[BBSWindow] DEVICEFP: 未缓存，开始向服务器请求设备指纹...");
            var fp = await FetchDeviceFingerprintAsync();
            if (!string.IsNullOrEmpty(fp))
            {
                cookieDic["DEVICEFP"] = fp;
                cookieDic["DEVICEFP_SEED_ID"] = _lastSeedId;
                cookieDic["DEVICEFP_SEED_TIME"] = _lastSeedTime;
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] DEVICEFP: 服务器返回 {fp}，持久化到 Cookie 文件");
                // 持久化 DEVICEFP 和 seed，避免后续实例重新请求
                await accountManager.SaveCookiesAsync(activeId, cookieDic);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[BBSWindow] DEVICEFP: 服务器返回空，将使用本地生成值");
            }
        }

        /// <summary>
        /// 复制 Java UUID.nameUUIDFromBytes() 行为。
        /// 官方 App: androidId → MD5 → UUID v3 (big-endian)。
        /// Windows 无 Android ID，用 machine+user 替代做持久化标识。
        /// </summary>
        private static Guid NameUuidFromBytes(byte[] name)
        {
            byte[] hash = MD5.HashData(name);
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30); // UUID v3
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // variant

            // Java UUID 用 big-endian，C# Guid(byte[]) 用 mixed-endian
            // 转换: [0..3] LE, [4..5] LE, [6..7] LE, [8..15] BE
            return new Guid(new byte[] {
                hash[3], hash[2], hash[1], hash[0],  // time_low (LE)
                hash[5], hash[4],                     // time_mid (LE)
                hash[7], hash[6],                     // time_hi_version (LE)
                hash[8], hash[9], hash[10], hash[11], hash[12], hash[13], hash[14], hash[15]
            });
        }

        private static string GetOrCreatePersistentDeviceId()
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher", "Data");
            var path = System.IO.Path.Combine(dir, "device_id.txt");
            try
            {
                if (System.IO.File.Exists(path))
                {
                    var existing = System.IO.File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(existing)) return existing;
                }
                // 生成随机 16 位 hex 模拟 Android ID（如 "1d5ae13a85817497"）
                var bytes = new byte[8];
                System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
                var id = Convert.ToHexString(bytes).ToLower();
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(path, id);
                return id;
            }
            catch { return Guid.NewGuid().ToString("N")[..16]; }
        }

        /// <summary>BBS/Community API (client_type=2) 用原始 hex device_id</summary>
        private string GenDeviceId() => GetOrCreatePersistentDeviceId();

        /// <summary>Game Record API (client_type=5) 用 UUID v3 派生 device_id</summary>
        private static string GenGameRecordDeviceId()
        {
            var hexId = GetOrCreatePersistentDeviceId();
            return NameUuidFromBytes(Encoding.UTF8.GetBytes(hexId)).ToString();
        }

        private static readonly string[] XiaomiModels = { "Xiaomi%2013", "Xiaomi%2012", "Xiaomi%20Redmi%20Note%2012", "Xiaomi%20Redmi%20K60" };
        private string GenDeviceName()
        {
            var model = ExtractDeviceModelFromUA();
            if (!string.IsNullOrEmpty(model))
            {
                var brand = "Xiaomi";
                if (_currentConfig.UserAgent.Contains("Vivo") || _currentConfig.UserAgent.Contains("vivo"))
                    brand = "Vivo";
                return $"{brand}%20{model}";
            }
            var a = cookieDic.GetValueOrDefault("account_id") ?? "0";
            var idx = Math.Abs(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(a + "_name")), 0)) % XiaomiModels.Length;
            return XiaomiModels[idx];
        }

        /// <summary>
        /// 从 User-Agent 中提取 Android 设备型号。
        /// UA 格式: "...Linux; Android 12; 24031PN0DC Build/V417IR; wv)..."
        /// </summary>
        private string ExtractDeviceModelFromUA()
        {
            try
            {
                var ua = _currentConfig.UserAgent;
                // 匹配 "Android <version>; <model> Build/"
                var androidIdx = ua.IndexOf("Android ");
                if (androidIdx < 0) return "";
                var afterAndroid = ua[(androidIdx + "Android ".Length)..];
                var semicolonIdx = afterAndroid.IndexOf(';');
                if (semicolonIdx < 0) return "";
                var afterSemicolon = afterAndroid[(semicolonIdx + 1)..].TrimStart();
                var buildIdx = afterSemicolon.IndexOf(" Build/");
                if (buildIdx < 0) return "";
                return afterSemicolon[..buildIdx];
            }
            catch { return ""; }
        }

        private static string GenDeviceFp()
        {
            var rnd = new Random();
            var sb = new StringBuilder();
            sb.Append(rnd.Next(1, 10));
            for (int i = 0; i < 9; i++) sb.Append(rnd.Next(0, 10));
            return sb.ToString();
        }

        /// <summary>
        /// 获取 x-rpc-device_fp 请求头：DEVICEFP cookie + timestamp 后4位 做 MD5 取前13位
        /// 注意：不能直接发送 DEVICEFP 原始值，必须经过此派生算法
        /// </summary>
        private string GetDeviceFpHeader()
        {
            if (cookieDic.TryGetValue("DEVICEFP", out var serverFp) && !string.IsNullOrEmpty(serverFp))
            {
                var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                var t4 = t[^Math.Min(4, t.Length)..];
                var h = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(serverFp + t4))).ToLower()[..13];
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] device_fp 派生: DEVICEFP={serverFp} ts末4位={t4} → {h}");
                return h;
            }
            var localFp = GenDeviceFp();
            System.Diagnostics.Debug.WriteLine($"[BBSWindow] device_fp 回退本地: {localFp}");
            return localFp;
        }

        private static readonly HttpClient _fpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
        private async Task<string?> FetchDeviceFingerprintAsync()
        {
            try
            {
                // Step 1: 获取 ext_list
                var extResp = await _fpClient.GetStringAsync("https://public-data-api.mihoyo.com/device-fp/api/getExtList?platform=2&app_name=bbs_cn");
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] getExtList (full): {extResp}");

                // 解析 ext_list 并收集对应的设备信息（值使用原生 JSON 类型）
                var extListNode = JsonNode.Parse(extResp);
                var extList = extListNode?["data"]?["ext_list"];
                var extFields = new Dictionary<string, object>();
                if (extList is JsonArray extArray)
                {
                    foreach (var item in extArray)
                    {
                        var fieldName = item?.ToString();
                        if (!string.IsNullOrEmpty(fieldName))
                        {
                            extFields[fieldName] = GetExtFieldValue(fieldName);
                        }
                    }
                }
                var extFieldsJson = JsonSerializer.Serialize(extFields);
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] ext_fields (full): {extFieldsJson}");

                // Step 2: 用 ext_list + seed 请求指纹
                // SDK 证实：所有参数都是 String，ext_fields 是 JSON 字符串
                _lastSeedId = Guid.NewGuid().ToString();
                _lastSeedTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                var requestBody = new JsonObject
                {
                    ["device_id"] = GenDeviceId(),
                    ["seed_id"] = _lastSeedId,
                    ["seed_time"] = _lastSeedTime,
                    ["platform"] = "2",
                    ["device_fp"] = GenDeviceFp(),
                    ["app_name"] = "bbs_cn",
                    ["bbs_device_id"] = GenGameRecordDeviceId(),
                    ["ext_fields"] = extFieldsJson
                };
                var body = requestBody.ToJsonString();
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] getFp 请求体 (full): {body}");

                var req = new HttpRequestMessage(HttpMethod.Post, "https://public-data-api.mihoyo.com/device-fp/api/getFp")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                req.Headers.Add("User-Agent", _currentConfig.UserAgent);
                var resp = await _fpClient.SendAsync(req);
                var result = await resp.Content.ReadAsStringAsync();
                // SDK 从根节点读取 device_fp；我们的响应嵌套在 data 内，两者都尝试
                var fpNode = JsonNode.Parse(result);
                var fp = fpNode?["device_fp"]?.ToString();  // SDK 方式：根节点直接取
                if (string.IsNullOrEmpty(fp))
                    fp = fpNode?["data"]?["device_fp"]?.ToString();  // 兼容嵌套格式
                var fpCode = fpNode?["data"]?["code"]?.ToString();
                var fpMsg = fpNode?["data"]?["msg"]?.ToString();
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] 服务器下发指纹: {(string.IsNullOrEmpty(fp) ? "失败" : fp)} | HTTP={resp.StatusCode} | code={fpCode} msg={fpMsg} | body={result}");
                // code!=200 说明服务器拒绝了（如 500 内部错误），应返回 null 而非使用无效值
                if (fpCode != null && fpCode != "200")
                {
                    System.Diagnostics.Debug.WriteLine($"[BBSWindow] getFp 被拒绝: code={fpCode} msg={fpMsg}");
                    return null;
                }
                return string.IsNullOrEmpty(fp) ? null : fp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BBSWindow] 获取指纹失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 根据 ext_list 返回的字段名收集对应的设备/浏览器信息（与 _currentConfig 保持一致）
        /// </summary>
        private object GetExtFieldValue(string fieldName)
        {
            return fieldName switch
            {
                // ===== WebView/hk4e 字段 (platform=5) =====
                "userAgent" => _currentConfig.UserAgent,
                "browserScreenSize" => "412x915",
                "maxTouchPoints" => 5,
                "isTouchSupported" => true,
                "browserLanguage" => "zh-CN",
                "browserPlat" => "Linux armv8l",
                "browserTimeZone" => "Asia/Shanghai",
                "browserPlugins" => "",
                "browserPlatform" => "Linux armv8l",
                "webGlRender" => "Adreno (TM) 650",
                "webglRenderer" => "Adreno (TM) 650",
                "webGlVendor" => "Qualcomm",
                "webglVendor" => "Qualcomm",
                "webgl" => "",
                "numOfPlugins" => 5,
                "listOfPlugins" => "Chrome PDF Plugin,Chrome PDF Viewer,Native Client,Chromium PDF Plugin,Microsoft Edge PDF Plugin",
                "screenRatio" => 2.0,
                "colorDepth" => 24,
                "pixelRatio" => 3.0,
                "hardwareConcurrency" => 8,
                "deviceMemory" => 8,
                "cpuClass" => "",
                "ifNotTrack" => false,
                "ifAdBlock" => false,
                "hasLiedLanguage" => false,
                "hasLiedResolution" => false,
                "hasLiedOs" => false,
                "hasLiedBrowser" => false,
                "canvas" => "",
                "webDriver" => false,

                // ===== Android 设备字段 (platform=2, bbs_cn) =====
                "board" => "24031PN0DC",
                "brand" => "Xiaomi",
                "hardware" => "Xiaomi",
                "cpuType" => "arm64-v8a",
                "deviceType" => "aurora",
                "display" => "V417IR release-keys",
                "manufacturer" => "Xiaomi",
                "productName" => "aurora",
                "model" => "24031PN0DC",
                "deviceInfo" => "Xiaomi/aurora/aurora:12/V417IR/1747:user/release-keys",
                "hostname" => "6b29a8384f29",
                "sdkVersion" => "32",
                "osVersion" => "12",
                "devId" => "REL",
                "buildTags" => "release-keys",
                "buildType" => "user",
                "buildUser" => "abc",
                "buildTime" => "1779448087000",
                "screenSize" => "1440x2560",
                "vendor" => "unknown",
                "romCapacity" => "512",
                "romRemain" => "478",
                "ramCapacity" => "127991",
                "ramRemain" => "126327",
                "appMemory" => "512",
                "sdCapacity" => 127991,
                "sdRemain" => 119757,
                "accelerometer" => "0.10001241x9.800007x0.1999938",
                "gyroscope" => "0.0x0.0x0.0",
                "magnetometer" => "15.625x-28.25x-32.625",
                "isRoot" => 1,
                "debugStatus" => 0,
                "proxyStatus" => 1,
                "emulatorStatus" => 0,
                "isTablet" => 1,
                "simState" => 5,
                "ui_mode" => "UI_MODE_TYPE_NORMAL",
                "hasKeyboard" => 1,
                "isMockLocation" => 0,
                "ringMode" => 2,
                "isAirMode" => 0,
                "batteryStatus" => 79,
                "chargeStatus" => 1,
                "deviceName" => "24031PN0DC",
                "appInstallTimeDiff" => 1782396402635L,
                "appUpdateTimeDiff" => 1782396402635L,
                "networkType" => "WiFi",
                "oaid" => "error_1008008",
                "vaid" => "error_1008008",
                "aaid" => "error_1008008",

                // 应用信息
                "packageName" => "com.mihoyo.hyperion",
                "packageVersion" => "2.42.0", // SDK 版本，非 App 版本
                _ => ""
            };
        }

        private static string? GetOrCreateDeviceFingerprint()
        {
            var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher", "device_fp.txt");
            try
            {
                if (System.IO.File.Exists(path))
                    return System.IO.File.ReadAllText(path).Trim();

                // 基于机器名+用户名生成唯一DEVICEFP
                var raw = Environment.MachineName + Environment.UserName;
                var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
                var fp = BitConverter.ToString(hash).Replace("-", "").ToLower()[..13];
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path, fp);
                return fp;
            }
            catch { return null; }
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

public static async Task<string> FetchApiJsonAsync(string apiUrl)
    {
        await _fetchApiSemaphore.WaitAsync();
        try
        {
            var accountManager = App.GetService<AccountManager>();
            var activeId = accountManager.ActiveAccountId;
            if (activeId == null) throw new InvalidOperationException("无活跃账号");

            var cookies = await accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0) throw new InvalidOperationException("无法加载Cookie");

            // DEVICEFP 缺失时通过 getFp API 获取（使用当前持久化 device_id 注册）
            if (!cookies.ContainsKey("DEVICEFP"))
            {
                var tempWindow = new BBSWindow(false);
                tempWindow.AppWindow?.Hide();
                await tempWindow.BBSWebView.EnsureCoreWebView2Async();
                await tempWindow.LoadActiveAccountCookiesAsync();
                if (tempWindow.cookieDic.TryGetValue("DEVICEFP", out var newFp) && !string.IsNullOrEmpty(newFp))
                {
                    cookies["DEVICEFP"] = newFp;
                    cookies["DEVICEFP_SEED_ID"] = tempWindow.cookieDic.GetValueOrDefault("DEVICEFP_SEED_ID") ?? "";
                    cookies["DEVICEFP_SEED_TIME"] = tempWindow.cookieDic.GetValueOrDefault("DEVICEFP_SEED_TIME") ?? "";
                    await accountManager.SaveCookiesAsync(activeId, cookies);
                }
                tempWindow.Close();
            }

            var gameDeviceId = GenGameRecordDeviceId();
            var fp = cookies.GetValueOrDefault("DEVICEFP") ?? "";

            var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var ts4 = ts[^Math.Min(4, ts.Length)..];
            var deviceFp = string.IsNullOrEmpty(fp) ? "" : Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(fp + ts4))).ToLower()[..13];

            var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var r = new Random().Next(100000, 200000).ToString();
            var dsInput = $"salt={CNX4}&t={t}&r={r}&b=&q=";
            var c = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(dsInput))).ToLower();
            var ds = $"{t},{r},{c}";

            var cookieStr = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));

            using var http = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(15) };
            var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.TryAddWithoutValidation("Cookie", cookieStr);
            req.Headers.TryAddWithoutValidation("DS", ds);
            req.Headers.TryAddWithoutValidation("x-rpc-device_id", gameDeviceId);
            req.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
            req.Headers.TryAddWithoutValidation("x-rpc-app_version", CNVersion);
            req.Headers.TryAddWithoutValidation("x-rpc-device_fp", deviceFp);
            req.Headers.TryAddWithoutValidation("x-rpc-device_name", "Xiaomi%2024031PN0DC");
            req.Headers.TryAddWithoutValidation("x-rpc-sys_version", "12");
            req.Headers.TryAddWithoutValidation("x-rpc-page", "v6.6.1-gr-cn_#/ys");
            req.Headers.TryAddWithoutValidation("x-rpc-tool_verison", "v6.6.1-gr-cn");
            req.Headers.TryAddWithoutValidation("Referer", "https://webstatic.mihoyo.com/");
            req.Headers.TryAddWithoutValidation("Origin", "https://webstatic.mihoyo.com");
            req.Headers.TryAddWithoutValidation("X-Requested-With", "com.mihoyo.hyperion");
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Linux; Android 12; 24031PN0DC Build/V417IR; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/110.0.5481.154 Safari/537.36 miHoYoBBS/2.109.0");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");

            System.Diagnostics.Debug.WriteLine($"[BBSWindow] HttpClient[HttpClient] device_id={gameDeviceId} fp_cookie={fp} fp_hdr={deviceFp} ds={ds}");

            var resp = await http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[BBSWindow] HttpClient 响应: HTTP={(int)resp.StatusCode} | {json[..Math.Min(500, json.Length)]}");

            return json;
        }
        finally
        {
            _fetchApiSemaphore.Release();
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
        
        public class AppConfig { public AccountConfig Account { get; set; } }
        public class AccountConfig { public string Cookie { get; set; } }
    }
}