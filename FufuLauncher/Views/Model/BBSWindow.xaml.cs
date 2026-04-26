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
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Views
{
    public sealed partial class BBSWindow : Window
    {
        private AppWindow m_AppWindow;
        private string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        
        private byte[] _screenshotBytes;

        private const string CNVersion = "2.102.1";
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
                UserAgent = $"Mozilla/5.0 (Linux; Android 15) Mobile miHoYoBBS/{CNVersion}",
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

        public BBSWindow()
        {
            InitializeComponent();
            
            _deviceId36 = GetStableGuid();
            _deviceId = GenerateDeviceId40(_deviceId36);
            
            _currentConfig = _clientConfigs["2"]; 
            
            InitializeWindowStyle();
            UrlTextBox.Text = DefaultUrl;
            _ = InitializeWebViewAsync();
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
                    
                    headers.SetHeader("x-rpc-app_id", "bll8iq97cem8");
                    headers.SetHeader("x-rpc-client_type", _currentConfig.ClientType);
                    headers.SetHeader("x-rpc-app_version", _currentConfig.AppVersion);
                    headers.SetHeader("x-rpc-device_id", _deviceId36);
                    headers.SetHeader("x-rpc-sdk_version", "2.16.0");

                    if (cookieDic.TryGetValue("DEVICEFP", out var fp) && !string.IsNullOrWhiteSpace(fp))
                    {
                        headers.SetHeader("x-rpc-device_fp", fp);
                    }
                    
                    string ds;
                    if (_currentConfig.UseDS2)
                    {
                        string query = GetSortedQuery(uri);
                        string body = "";
                        if (args.Request.Method == "POST" && args.Request.Content != null)
                        {
                            body = await GetJsonBodyAsync(args.Request.Content);
                        }
                        ds = CalculateDS2(_currentConfig.Salt, query, body);
                    }
                    else
                    {
                        ds = CalculateDS1(_currentConfig.Salt);
                    }

                    headers.SetHeader("DS", ds);
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
            var data = new Dictionary<string, object>
            {
                ["x-rpc-app_id"] = "bll8iq97cem8",
                ["x-rpc-client_type"] = _currentConfig.ClientType,
                ["x-rpc-app_version"] = _currentConfig.AppVersion,
                ["x-rpc-device_id"] = _deviceId36,
                ["x-rpc-sdk_version"] = "2.16.0"
            };
            if (cookieDic.TryGetValue("DEVICEFP", out var fp)) data["x-rpc-device_fp"] = fp;
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
            if (File.Exists(ConfigPath))
            {
                try {
                    var json = await File.ReadAllTextAsync(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    ParseCookie(cfg?.Account?.Cookie ?? "");
                } catch { }
            }
            
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
            BBSWebView.CoreWebView2.Navigate(url);
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
        
        public class AppConfig { public AccountConfig Account { get; set; } }
        public class AccountConfig { public string Cookie { get; set; } }
    }
}