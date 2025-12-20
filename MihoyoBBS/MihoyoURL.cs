using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PuppeteerSharp;

namespace MihoyoBBS
{
    public static class Logger
    {
        private static readonly object lockObject = new();
        private static string logFileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

        public static void LogInfo(string message)
        {
            Log("INFO", message);
        }

        public static void LogError(string message)
        {
            Log("ERROR", message);
        }

        public static void LogWarning(string message)
        {
            Log("WARN", message);
        }

        public static void LogRequest(string method, string url, string headers, string body = "")
        {
            var logMessage = $"[REQUEST] {method} {url}\nHeaders:\n{headers}";
            if (!string.IsNullOrEmpty(body))
            {
                logMessage += $"\nBody:\n{body}";
            }
            Log("REQUEST", logMessage);
        }

        public static void LogResponse(string url, int statusCode, string headers, string body = "")
        {
            var logMessage = $"[RESPONSE] {url} - Status: {statusCode}\nHeaders:\n{headers}";
            if (!string.IsNullOrEmpty(body))
            {
                logMessage += $"\nBody:\n{body}";
            }
            Log("RESPONSE", logMessage);
        }

        private static void Log(string level, string message)
        {
            try
            {
                lock (lockObject)
                {
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}\n";
                    var fullPath = Path.GetFullPath(logFileName);
                    Console.WriteLine(logEntry.TrimEnd());

                    File.AppendAllText(fullPath, logEntry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] 写入日志失败: {ex.Message}");
            }
        }

        public static string GetLogFilePath()
        {
            return Path.GetFullPath(logFileName);
        }
    }
    public class Config
    {
        public AccountConfig Account
        {
            get;
            set;
        } = new();

        public DeviceConfig Device
        {
            get;
            set;
        } = new();

        public GamesConfig Games
        {
            get;
            set;
        } = new();
    }

    public class AccountConfig
    {
        public string Cookie
        {
            get;
            set;
        } = "";
    }

    public class DeviceConfig
    {
        public string Id
        {
            get;
        } = "";
    }

    public class GamesConfig
    {
        public CnConfig Cn
        {
            get;
        } = new();
    }

    public class CnConfig
    {
        public string UserAgent
        {
            get;
        } =
            "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36";
    }

    public class RewardItem
    {
        [JsonPropertyName("name")]
        public string Name
        {
            get;
        }

        [JsonPropertyName("cnt")]
        public int Count
        {
            get;
        }
    }
    public class QrCodeResponse
    {
        [JsonPropertyName("retcode")]
        public int RetCode
        {
            get; set;
        }

        [JsonPropertyName("message")]
        public string Message
        {
            get; set;
        }

        [JsonPropertyName("data")]
        public QrCodeData Data
        {
            get; set;
        }
    }

    public class QrCodeData
    {
        [JsonPropertyName("url")]
        public string Url
        {
            get; set;
        }

        [JsonPropertyName("ticket")]
        public string Ticket
        {
            get; set;
        }

        public string AppId { get; set; } = "1";

        public string Device
        {
            get; set;
        }

        public string DeviceId
        {
            get; set;
        }
    }

    public class CheckQrResponse
    {
        [JsonPropertyName("retcode")]
        public int RetCode
        {
            get; set;
        }

        [JsonPropertyName("message")]
        public string Message
        {
            get; set;
        }

        [JsonPropertyName("data")]
        public CheckQrData Data
        {
            get; set;
        }
    }

    public class CheckQrData
    {
        [JsonPropertyName("stat")]
        public string Stat
        {
            get; set;
        }

        [JsonPropertyName("payload")]
        public QrPayload Payload
        {
            get; set;
        }

        [JsonPropertyName("realname_info")]
        public object RealnameInfo
        {
            get; set;
        }
    }

    public class QrPayload
    {
        [JsonPropertyName("proto")]
        public string Proto
        {
            get; set;
        }

        [JsonPropertyName("raw")]
        public string Raw
        {
            get; set;
        }

        [JsonPropertyName("ext")]
        public string Ext
        {
            get; set;
        }
    }

    public class StokenResponse
    {
        [JsonPropertyName("retcode")]
        public int RetCode
        {
            get; set;
        }

        [JsonPropertyName("message")]
        public string Message
        {
            get; set;
        }

        [JsonPropertyName("data")]
        public StokenData Data
        {
            get; set;
        }
    }

    public class StokenData
    {
        [JsonPropertyName("token")]
        public TokenInfo Token
        {
            get; set;
        }

        [JsonPropertyName("user_info")]
        public UserInfo UserInfo
        {
            get; set;
        }
    }

    public class TokenInfo
    {
        [JsonPropertyName("token")]
        public string Token
        {
            get; set;
        }
    }

    public class UserInfo
    {
        [JsonPropertyName("mid")]
        public string Mid
        {
            get; set;
        }
    }
    public static class Tools
    {
        public static string Md5(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hashBytes = md5.ComputeHash(bytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        public static string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = chars[random.Next(chars.Length)];
            }

            return new string(result);
        }

        public static long Timestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public static string GetDs(bool web = true)
        {
            var salt = web ? "G1ktdwFL4IyGkHuuWSmz0wUe9Db9scyK" : "idMMaGYmVgPzh3wxmWudUXKUPGidO7GM";
            var t = Timestamp().ToString();
            var r = RandomString(6);
            var c = Md5($"salt={salt}&t={t}&r={r}");
            return $"{t},{r},{c}";
        }

        public static string GetDeviceId(string cookie)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(cookie);
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        public static string GetUserAgent(string useragent)
        {
            if (string.IsNullOrEmpty(useragent))
            {
                return "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36 miHoYoBBS/2.93.1";
            }

            useragent = useragent.Replace("; ", " ").Replace(";", " ");

            if (useragent.Contains("miHoYoBBS"))
            {
                int i = useragent.IndexOf("miHoYoBBS");
                if (i > 0 && useragent[i - 1] == ' ')
                    i = i - 1;
                return $"{useragent.Substring(0, i)} miHoYoBBS/2.93.1";
            }

            return $"{useragent} miHoYoBBS/2.93.1";
        }

        public static string TidyCookie(string cookies)
        {
            var cookieDict = new Dictionary<string, string>();
            var splitCookie = cookies.Split(';');

            if (splitCookie.Length < 2)
                return cookies;

            foreach (var cookie in splitCookie)
            {
                var trimmedCookie = cookie.Trim();
                if (string.IsNullOrEmpty(trimmedCookie))
                    continue;

                var parts = trimmedCookie.Split('=', 2);
                if (parts.Length == 2)
                {
                    cookieDict[parts[0]] = parts[1];
                }
            }

            return string.Join("; ", cookieDict.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        public static string GetDs2(string query = "", string body = "")
        {
            const string salt = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
            var t = Timestamp().ToString();
            var r = new Random().Next(100001, 200000).ToString();
            var b = string.IsNullOrEmpty(body) ? "" : body;
            var q = string.IsNullOrEmpty(query) ? "" : query;
            var c = Md5($"salt={salt}&t={t}&r={r}&b={b}&q={q}");
            return $"{t},{r},{c}";
        }
        public static string ExtractTicketFromUrl(string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                    return null;

                var uri = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                return query["ticket"];
            }
            catch
            {
                if (url.Contains("ticket="))
                {
                    var start = url.IndexOf("ticket=") + 7;
                    var end = url.IndexOf('&', start);
                    if (end == -1) end = url.Length;
                    return url.Substring(start, end - start);
                }
                return null;
            }
        }
        public static void SaveQrCodeToFile(string qrUrl, string filename = "login_qrcode.html")
        {
            try
            {
                Logger.LogInfo($"正在生成二维码文件: {filename}");
                SaveQrCodeAsHtml(qrUrl, filename);
            }
            catch (Exception ex)
            {
                Logger.LogError($"生成二维码图片失败: {ex.Message}");
            }
        }
        private static void SaveQrCodeAsHtml(string qrUrl, string filename = "login_qrcode.html")
        {
            try
            {
                var ticket = ExtractTicketFromUrl(qrUrl);
                var htmlContent = $@"<!DOCTYPE html>
<html>
<head>
    <title>登录</title>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        body {{
            font-family: Arial, sans-serif;
            display: flex;
            flex-direction: column;
            align-items: center;
            justify-content: center;
            min-height: 100vh;
            margin: 0;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            padding: 30px;
            border-radius: 10px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
            max-width: 500px;
            width: 100%;
        }}
        h1 {{
            color: #333;
            text-align: center;
            margin-bottom: 20px;
        }}
        .qr-container {{
            text-align: center;
            margin: 20px 0;
        }}
        .instructions {{
            background-color: #fff3cd;
            border: 1px solid #ffeaa7;
            border-radius: 5px;
            padding: 15px;
            margin: 20px 0;
        }}
        .url-container {{
            background-color: #f8f9fa;
            border: 1px solid #e9ecef;
            border-radius: 5px;
            padding: 10px;
            margin: 15px 0;
            word-wrap: break-word;
            font-family: monospace;
            font-size: 12px;
        }}
        .btn {{
            display: inline-block;
            background-color: #007bff;
            color: white;
            padding: 10px 20px;
            border-radius: 5px;
            text-decoration: none;
            margin-top: 10px;
        }}
        .btn:hover {{
            background-color: #0056b3;
        }}
        .warning {{
            color: #856404;
            background-color: #fff3cd;
            border: 1px solid #ffeaa7;
            padding: 10px;
            border-radius: 5px;
            margin-top: 20px;
        }}
        .info {{
            color: #0c5460;
            background-color: #d1ecf1;
            border: 1px solid #bee5eb;
            padding: 10px;
            border-radius: 5px;
            margin-top: 10px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>登录</h1>

        <div class='qr-container'>
            <img src='https://api.qrserver.com/v1/create-qr-code/?size=300x300&data={Uri.EscapeDataString(qrUrl)}' 
                 alt='登录二维码' 
                 style='width: 300px; height: 300px; border: 1px solid #ddd;'>
        </div>
        
        <div style='text-align: center; margin: 20px 0;'>
            <a href='{qrUrl}' target='_blank' class='btn'>直接打开登录链接</a>
        </div>
        
        <div class='info'>
            <strong>Debug：</strong><br>
            Ticket: {ticket}<br>
            时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
        </div>
        
        <div class='url-container'>
            <strong>URL：</strong><br>
            <a href='{qrUrl}' target='_blank'>{qrUrl}</a>
        </div>
        
    </div>
    
    <script>
        setInterval(function() {{
            var img = document.querySelector('.qr-container img');
            if (img) {{
                var src = img.src.split('?')[0];
                img.src = src + '?t=' + new Date().getTime() + '&size=300x300&data={Uri.EscapeDataString(qrUrl)}';
            }}
        }}, 30000);
    </script>
</body>
</html>";

                File.WriteAllText(filename, htmlContent, Encoding.UTF8);
                Logger.LogInfo($"二维码HTML已保存: {Path.GetFullPath(filename)}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"保存二维码HTML失败: {ex.Message}");
                throw;
            }
        }
        public static void GenerateAsciiQrCode(string qrUrl)
        {
            try
            {
                Logger.LogInfo("生成ASCII二维码");
                Console.WriteLine($"URL: {qrUrl,-50}");

            }
            catch (Exception ex)
            {
                Logger.LogError($"生成ASCII二维码失败: {ex.Message}");
            }
        }
    }
    public class BrowserSimulator : IDisposable
    {
        private IBrowser _browser;
        private IPage _page;
        private Config _config;
        private bool _isHeadless;

        public BrowserSimulator(Config config, bool headless = false)
        {
            _config = config;
            _isHeadless = headless;
        }

        public async Task InitializeAsync()
        {
            try
            {
                Logger.LogInfo("正在初始化浏览器模拟环境...");

                var chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

                if (!File.Exists(chromePath))
                {
                    Logger.LogError($"找不到Chrome浏览器: {chromePath}");
                    Console.WriteLine($"错误: 找不到Chrome浏览器路径: {chromePath}");
                    Console.WriteLine("请确保已安装Google Chrome浏览器");
                    throw new FileNotFoundException($"Chrome浏览器不存在: {chromePath}");
                }

                Logger.LogInfo($"使用已安装的Chrome浏览器: {chromePath}");
                var launchOptions = new LaunchOptions
                {
                    Headless = _isHeadless,
                    ExecutablePath = chromePath,
                    Args = new[]
                    {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-web-security",
                "--disable-features=IsolateOrigins,site-per-process",
                "--disable-blink-features=AutomationControlled"
            },
                    IgnoredDefaultArgs = new[]
                    {
                "--enable-automation",
                "--disable-popup-blocking"
            }
                };

                _browser = await Puppeteer.LaunchAsync(launchOptions);
                _page = await _browser.NewPageAsync();
                await SetupUserAgentAsync();
                await SetupCookiesAsync();
                await _page.EvaluateExpressionOnNewDocumentAsync(@"
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined
            });
            Object.defineProperty(navigator, 'plugins', {
                get: () => [1, 2, 3, 4, 5]
            });
            Object.defineProperty(navigator, 'languages', {
                get: () => ['zh-CN', 'zh', 'en']
            });
        ");

                Logger.LogInfo("浏览器模拟环境初始化完成");
            }
            catch (Exception ex)
            {
                Logger.LogError($"初始化浏览器模拟环境失败: {ex.Message}");
                Console.WriteLine($"初始化浏览器模拟环境失败: {ex.Message}");
                throw;
            }
        }



        private async Task SetupUserAgentAsync()
        {
            var userAgent = _config.Games.Cn.UserAgent;
            if (string.IsNullOrEmpty(userAgent))
            {
                userAgent = "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36";
            }

            await _page.SetUserAgentAsync(userAgent);
            await _page.SetViewportAsync(new ViewPortOptions
            {
                Width = 360,
                Height = 640,
                DeviceScaleFactor = 2,
                IsMobile = true,
                HasTouch = true,
                IsLandscape = false
            });
        }

        private async Task SetupCookiesAsync()
        {
            if (!string.IsNullOrEmpty(_config.Account.Cookie))
            {
                try
                {
                    var cookies = ParseCookies(_config.Account.Cookie);
                    await _page.SetCookieAsync(cookies.ToArray());
                    Logger.LogInfo($"已设置 {cookies.Count} 个Cookie");
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"设置Cookie失败: {ex.Message}");
                }
            }
        }

        private List<CookieParam> ParseCookies(string cookieString)
        {
            var cookies = new List<CookieParam>();
            var parts = cookieString.Split(';');

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex > 0)
                {
                    var name = trimmed.Substring(0, separatorIndex);
                    var value = trimmed.Substring(separatorIndex + 1);

                    cookies.Add(new CookieParam
                    {
                        Name = name,
                        Value = value,
                        Domain = ".mihoyo.com",
                        Path = "/"
                    });
                }
            }

            return cookies;
        }

        public async Task<string> BrowsePageAsync(string url, bool waitForNetworkIdle = true, int timeoutSeconds = 30)
        {
            try
            {
                Logger.LogInfo($"正在访问页面: {url}");
                Console.WriteLine($"\n访问页面: {url}");
                await _page.SetRequestInterceptionAsync(true);
                _page.Request += async (sender, e) =>
                {
                    var request = e.Request;
                    var headers = new Dictionary<string, string>(request.Headers)
                    {
                        ["x-rpc-device_id"] = _config.Device.Id,
                        ["x-rpc-app_version"] = "2.93.1",
                        ["x-rpc-client_type"] = "5",
                        ["x-rpc-signgame"] = "hk4e",
                        ["DS"] = Tools.GetDs(true),
                        ["Referer"] = "https://act.mihoyo.com/",
                        ["Origin"] = "https://act.mihoyo.com"
                    };

                    await request.ContinueAsync(new Payload { Headers = headers });
                };
                _page.Console += (sender, e) =>
                {
                    Console.WriteLine($"[浏览器控制台] {e.Message.Type}: {e.Message.Text}");
                };
                _page.Response += (sender, e) =>
                {
                    var response = e.Response;
                    var url = response.Url;
                    var status = response.Status;

                    if (url.Contains("mihoyo.com"))
                    {
                        Console.WriteLine($"[响应] {status} {url}");
                    }
                };
                var navigationOptions = new NavigationOptions
                {
                    Timeout = timeoutSeconds * 1000,
                    WaitUntil = new[]
                    {
                        waitForNetworkIdle ?
                        WaitUntilNavigation.Networkidle0 :
                        WaitUntilNavigation.Load
                    }
                };

                var response = await _page.GoToAsync(url, navigationOptions);
                await WaitForPageLoadAsync();
                var content = await _page.GetContentAsync();

                Logger.LogInfo($"页面访问完成: {url}");

                return content;
            }
            catch (Exception ex)
            {
                Logger.LogError($"访问页面失败: {ex.Message}");
                throw;
            }
        }

        private async Task WaitForPageLoadAsync()
        {
            await _page.WaitForExpressionAsync(@"
                () => {
                    return document.readyState === 'complete' &&
                           (typeof jQuery === 'undefined' || jQuery.active === 0) &&
                           (typeof miHoYo === 'undefined' || miHoYo.loaded === true);
                }
            ", new WaitForFunctionOptions { Timeout = 10000 });
            await Task.Delay(2000);
        }

        public async Task<string> ExecuteJavaScriptAsync(string script)
        {
            try
            {
                Logger.LogInfo($"执行JavaScript脚本: {script.Substring(0, Math.Min(50, script.Length))}...");

                var result = await _page.EvaluateExpressionAsync<object>(script);

                if (result != null)
                {
                    var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });

                    return resultJson;
                }

                return "执行完成，无返回值";
            }
            catch (Exception ex)
            {
                Logger.LogError($"执行JavaScript失败: {ex.Message}");
                return $"执行失败: {ex.Message}";
            }
        }

        public async Task ClickElementAsync(string selector)
        {
            try
            {
                await _page.ClickAsync(selector);
                Logger.LogInfo($"点击元素: {selector}");
                await Task.Delay(1000);
                await WaitForPageLoadAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError($"点击元素失败: {ex.Message}");
                throw;
            }
        }

        public async Task TypeTextAsync(string selector, string text)
        {
            try
            {
                await _page.TypeAsync(selector, text);
                Logger.LogInfo($"在元素 {selector} 中输入文本: {text}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"输入文本失败: {ex.Message}");
                throw;
            }
        }

        public async Task<string> TakeScreenshotAsync(string fileName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                }

                await _page.ScreenshotAsync(fileName);
                var fullPath = Path.GetFullPath(fileName);

                Logger.LogInfo($"截图已保存: {fullPath}");
                return fullPath;
            }
            catch (Exception ex)
            {
                Logger.LogError($"截图失败: {ex.Message}");
                return null;
            }
        }

        public async Task<Dictionary<string, string>> GetCookiesAsync()
        {
            try
            {
                var cookies = await _page.GetCookiesAsync();
                var cookieDict = cookies.ToDictionary(c => c.Name, c => c.Value);

                return cookieDict;
            }
            catch (Exception ex)
            {
                Logger.LogError($"获取Cookie失败: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        public async Task CloseAsync()
        {
            try
            {
                if (_browser != null)
                {
                    await _browser.CloseAsync();
                    Logger.LogInfo("浏览器已关闭");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"关闭浏览器失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            CloseAsync().Wait();
        }
    }
    public class QRLoginHelper
    {
        private readonly HttpClient _httpClient;
        private const string AppVersion = "2.71.1";
        private const string DeviceName = "Xiaomi MI 6";
        private const string DeviceModel = "MI 6";
        private const string QrUrl = "https://hk4e-sdk.mihoyo.com/hk4e_cn/combo/panda/qrcode/fetch";
        private const string CheckQrUrl = "https://hk4e-sdk.mihoyo.com/hk4e_cn/combo/panda/qrcode/query";
        private const string TokenByGameTokenUrl = "https://api-takumi.mihoyo.com/account/ma-cn-session/app/getTokenByGameToken";

        private string _deviceId;
        private string _device;

        public QRLoginHelper()
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            });
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _deviceId = Guid.NewGuid().ToString("N");
            _device = Tools.RandomString(64);
        }

        public async Task<QrCodeData> GetQrCodeAsync()
        {
            try
            {
                var appId = "7";
                var device = _device;

                var requestBody = new
                {
                    app_id = appId,
                    device = device
                };

                var jsonBody = JsonSerializer.Serialize(requestBody);
                var headers = new Dictionary<string, string>
                {
                    ["x-rpc-app_version"] = AppVersion,
                    ["DS"] = "",
                    ["x-rpc-aigis"] = "",
                    ["Content-Type"] = "application/json",
                    ["Accept"] = "application/json",
                    ["x-rpc-game_biz"] = "bbs_cn",
                    ["x-rpc-sys_version"] = "12",
                    ["x-rpc-device_id"] = _deviceId,
                    ["x-rpc-device_name"] = DeviceName,
                    ["x-rpc-device_model"] = DeviceModel,
                    ["x-rpc-app_id"] = "bll8iq97cem8",
                    ["x-rpc-client_type"] = "4",
                    ["User-Agent"] = "okhttp/4.9.3"
                };
                var ds = Tools.GetDs2(body: jsonBody);
                headers["DS"] = ds;

                var request = new HttpRequestMessage(HttpMethod.Post, QrUrl);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                request.Content = content;
                var headersText = string.Join("\n", headers.Select(h => $"{h.Key}: {h.Value}"));
                Logger.LogRequest("POST", QrUrl, headersText, jsonBody);
                foreach (var header in headers)
                {
                    if (header.Key != "Content-Type")
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                var response = await _httpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();
                var responseHeaders = string.Join("\n", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"));
                Logger.LogResponse(QrUrl, (int)response.StatusCode, responseHeaders, responseText);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError($"获取二维码失败: HTTP {response.StatusCode}");
                    return null;
                }

                var result = JsonSerializer.Deserialize<QrCodeResponse>(responseText);

                if (result.RetCode != 0)
                {
                    Logger.LogError($"获取二维码失败: {result.Message} (代码: {result.RetCode})");
                    return null;
                }

                var ticket = Tools.ExtractTicketFromUrl(result.Data.Url);

                var qrData = new QrCodeData
                {
                    Url = result.Data.Url,
                    AppId = appId,
                    Ticket = ticket,
                    Device = device,
                    DeviceId = _deviceId
                };

                Logger.LogInfo($"获取二维码成功，Ticket: {ticket}");
                return qrData;
            }
            catch (Exception ex)
            {
                Logger.LogError($"获取二维码时异常: {ex.Message}");
                return null;
            }
        }

        public async Task<(string Uid, string GameToken)> CheckLoginAsync(QrCodeData qrData)
        {
            try
            {
                Logger.LogInfo("开始检查登录状态");
                Console.WriteLine("\n正在等待扫码登录...");

                int checkCount = 0;
                while (true)
                {
                    checkCount++;
                    await Task.Delay(2000);

                    var requestBody = new
                    {
                        app_id = qrData.AppId,
                        ticket = qrData.Ticket,
                        device = qrData.Device
                    };

                    var jsonBody = JsonSerializer.Serialize(requestBody);
                    var headers = new Dictionary<string, string>
                    {
                        ["x-rpc-app_version"] = AppVersion,
                        ["DS"] = "",
                        ["x-rpc-aigis"] = "",
                        ["Content-Type"] = "application/json",
                        ["Accept"] = "application/json",
                        ["x-rpc-game_biz"] = "bbs_cn",
                        ["x-rpc-sys_version"] = "12",
                        ["x-rpc-device_id"] = qrData.DeviceId,
                        ["x-rpc-device_name"] = DeviceName,
                        ["x-rpc-device_model"] = DeviceModel,
                        ["x-rpc-app_id"] = "bll8iq97cem8",
                        ["x-rpc-client_type"] = "4",
                        ["User-Agent"] = "okhttp/4.9.3"
                    };
                    var ds = Tools.GetDs2(body: jsonBody);
                    headers["DS"] = ds;

                    var request = new HttpRequestMessage(HttpMethod.Post, CheckQrUrl);
                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    request.Content = content;
                    foreach (var header in headers)
                    {
                        if (header.Key != "Content-Type")
                        {
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }

                    var response = await _httpClient.SendAsync(request);
                    var responseText = await response.Content.ReadAsStringAsync();

                    var result = JsonSerializer.Deserialize<CheckQrResponse>(responseText);
                    if (!response.IsSuccessStatusCode || result.RetCode != 0)
                    {
                        if (result.RetCode == -3503 || result.RetCode == -102)
                        {
                            Logger.LogWarning($"第{checkCount}次检查被拦截({result.RetCode})，继续重试...");
                            await Task.Delay(3000);
                            continue;
                        }
                        else
                        {
                            Logger.LogError($"检查登录失败: {result.Message} (retcode: {result.RetCode})");
                            break;
                        }
                    }

                    var stat = result.Data?.Stat;
                    Logger.LogInfo($"登录状态: {stat}");

                    switch (stat)
                    {
                        case "Init":
                            if (checkCount % 10 == 1)
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 等待扫码...");
                            break;
                        case "Scanned":
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 已扫码，等待确认...");
                            break;
                        case "Confirmed":
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 登录成功！");
                            if (!string.IsNullOrEmpty(result.Data?.Payload?.Raw))
                            {
                                try
                                {
                                    var rawData = JsonSerializer.Deserialize<Dictionary<string, string>>(result.Data.Payload.Raw);
                                    if (rawData != null && rawData.ContainsKey("uid") && rawData.ContainsKey("token"))
                                    {
                                        return (rawData["uid"], rawData["token"]);
                                    }
                                }
                                catch { }
                            }
                            break;
                        case "Expired":
                            Console.WriteLine("二维码已过期。");
                            return (null, null);
                        default:
                            break;
                    }
                    if (checkCount > 150)
                    {
                        Console.WriteLine("等待超时。");
                        return (null, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"检查登录异常: {ex.Message}");
            }
            return (null, null);
        }

        public async Task<(string Mid, string Stoken)> GetStokenByGameTokenAsync(string uid, string gameToken)
        {
            try
            {
                Logger.LogInfo($"开始获取SToken, UID: {uid}");

                var requestBody = new
                {
                    account_id = int.Parse(uid),
                    game_token = gameToken
                };

                var jsonBody = JsonSerializer.Serialize(requestBody);
                var ds = Tools.GetDs2(body: jsonBody);

                var headers = new Dictionary<string, string>
                {
                    ["x-rpc-app_version"] = AppVersion,
                    ["DS"] = ds,
                    ["x-rpc-aigis"] = "",
                    ["Accept"] = "application/json",
                    ["x-rpc-game_biz"] = "bbs_cn",
                    ["x-rpc-sys_version"] = "12",
                    ["x-rpc-device_id"] = _deviceId,
                    ["x-rpc-device_name"] = DeviceName,
                    ["x-rpc-device_model"] = DeviceModel,
                    ["x-rpc-app_id"] = "bll8iq97cem8",
                    ["x-rpc-client_type"] = "4",
                    ["User-Agent"] = "okhttp/4.9.3"
                };

                var request = new HttpRequestMessage(HttpMethod.Post, TokenByGameTokenUrl);
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                request.Content = content;
                var headersText = string.Join("\n", headers.Select(h => $"{h.Key}: {h.Value}"));
                Logger.LogRequest("POST", TokenByGameTokenUrl, headersText, jsonBody);
                foreach (var header in headers)
                {
                    if (header.Key != "Content-Type")
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                var response = await _httpClient.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();
                var responseHeaders = string.Join("\n", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"));
                Logger.LogResponse(TokenByGameTokenUrl, (int)response.StatusCode, responseHeaders, responseText);

                if (!response.IsSuccessStatusCode)
                {
                    Logger.LogError($"获取SToken失败: HTTP {response.StatusCode}");
                    Logger.LogError($"响应: {responseText}");
                    Console.WriteLine($"获取SToken失败: HTTP {response.StatusCode}");
                    return (null, null);
                }

                var result = JsonSerializer.Deserialize<StokenResponse>(responseText);

                if (result.RetCode != 0)
                {
                    Logger.LogError($"获取SToken失败: {result.Message} (代码: {result.RetCode})");
                    Console.WriteLine($"获取SToken失败: {result.Message} (代码: {result.RetCode})");
                    return (null, null);
                }

                var mid = result.Data?.UserInfo?.Mid;
                var stoken = result.Data?.Token?.Token;

                if (string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(stoken))
                {
                    Logger.LogError("获取的SToken或MID为空");
                    return (null, null);
                }

                Logger.LogInfo($"获取SToken成功: MID={mid}, SToken={stoken}");
                return (mid, stoken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"获取SToken时发生异常: {ex.Message}");
                Logger.LogError($"异常详情: {ex}");
                Console.WriteLine($"获取SToken时发生异常: {ex.Message}");
                return (null, null);
            }
        }
    }
    public class PseudoClient
    {
        private HttpClient HttpClient;
        private Dictionary<string, string> Headers;
        private Config Config;

        public PseudoClient(Config config)
        {
            Config = config;
            HttpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            HttpClient.Timeout = TimeSpan.FromSeconds(30);
            SetupHeaders();
        }

        private void SetupHeaders()
        {
            var deviceId = string.IsNullOrEmpty(Config.Device.Id)
                ? Tools.GetDeviceId(Config.Account.Cookie)
                : Config.Device.Id;
            var cookie = Tools.TidyCookie(Config.Account.Cookie);
            var cookieParts = cookie.Split(';')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var hasCookieToken = cookieParts.Any(p => p.StartsWith("cookie_token="));
            if (!hasCookieToken)
            {
                Logger.LogWarning("Cookie中缺少cookie_token字段");
            }

            var userAgent = Tools.GetUserAgent(Config.Games.Cn.UserAgent);

            Headers = new Dictionary<string, string>
            {
                ["Accept"] = "application/json, text/plain, */*",
                ["DS"] = Tools.GetDs(true),
                ["x-rpc-channel"] = "miyousheluodi",
                ["Origin"] = "https://act.mihoyo.com",
                ["x-rpc-app_version"] = "2.93.1",
                ["x-rpc-client_type"] = "5",
                ["Referer"] = "https://act.mihoyo.com/",
                ["Accept-Encoding"] = "gzip, deflate",
                ["Accept-Language"] = "zh-CN,en-US;q=0.8",
                ["X-Requested-With"] = "com.mihoyo.hyperion",
                ["Cookie"] = cookie,
                ["x-rpc-device_id"] = deviceId,
                ["User-Agent"] = userAgent,
                ["x-rpc-signgame"] = "hk4e"
            };

            Logger.LogInfo($"设置请求头完成，DeviceId: {deviceId}");
        }
        private void AddHeadersToRequest(HttpRequestMessage request)
        {
            foreach (var header in Headers)
            {
                AddHeaderToRequest(request, header.Key, header.Value);
            }
        }

        private void AddHeaderToRequest(HttpRequestMessage request, string key, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            switch (key.ToLower())
            {
                case "cookie":
                    request.Headers.Add("Cookie", value);
                    break;
                case "user-agent":
                    request.Headers.UserAgent.ParseAdd(value);
                    break;
                case "referer":
                    request.Headers.Referrer = new Uri(value);
                    break;
                case "accept-encoding":
                case "accept-language":
                    request.Headers.TryAddWithoutValidation(key, value);
                    break;
                default:
                    request.Headers.Add(key, value);
                    break;
            }
        }
        public async Task RequestUrlAndPrintInfo(string url)
        {
            try
            {
                Logger.LogInfo($"请求URL: {url}");

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    AddHeadersToRequest(request);
                    var headersText = string.Join("\n", Headers.Select(h => $"{h.Key}: {h.Value}"));
                    Logger.LogRequest("GET", url, headersText);
                    var stopwatch = Stopwatch.StartNew();
                    var response = await HttpClient.SendAsync(request);
                    stopwatch.Stop();
                    var responseHeaders = string.Join("\n", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"));
                    var responseText = await response.Content.ReadAsStringAsync();
                    Logger.LogResponse(url, (int)response.StatusCode, responseHeaders, responseText);
                    Console.WriteLine($"\n========== 响应信息 ==========");
                    Console.WriteLine($"响应时间: {stopwatch.ElapsedMilliseconds}ms");
                    Console.WriteLine($"状态码: {(int)response.StatusCode} ({response.StatusCode})");

                    Console.WriteLine("\n响应内容:");
                    try
                    {
                        if (responseText.TrimStart().StartsWith("{"))
                        {
                            var jsonDocument = JsonDocument.Parse(responseText);
                            var formattedJson = JsonSerializer.Serialize(jsonDocument, new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                            });
                            Console.WriteLine(formattedJson);
                        }
                        else if (responseText.TrimStart().StartsWith("<"))
                        {
                            Console.WriteLine("HTML内容 (显示前500字符):");
                            Console.WriteLine(responseText.Substring(0, Math.Min(500, responseText.Length)));
                            if (responseText.Length > 500)
                                Console.WriteLine("... (内容过长，已截断)");
                        }
                        else
                        {
                            Console.WriteLine("原始文本 (显示前500字符):");
                            Console.WriteLine(responseText.Substring(0, Math.Min(500, responseText.Length)));
                            if (responseText.Length > 500)
                                Console.WriteLine("... (内容过长，已截断)");
                        }
                    }
                    catch (JsonException)
                    {
                        Console.WriteLine("非JSON格式 (显示前500字符):");
                        Console.WriteLine(responseText.Substring(0, Math.Min(500, responseText.Length)));
                        if (responseText.Length > 500)
                            Console.WriteLine("... (内容过长，已截断)");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"请求异常: {ex.Message}");
                Logger.LogError($"异常类型: {ex.GetType().FullName}");
                if (ex.InnerException != null)
                {
                    Logger.LogError($"内部异常: {ex.InnerException.Message}");
                }
                Console.WriteLine($"请求异常: {ex.Message}");
            }
        }
        public async Task RequestHomePageAndPrintInfo()
        {
            Logger.LogInfo("开始请求主页");
            Console.WriteLine("========== 开始请求主页 ==========");

            var urls = new string[]
            {
                "https://api-takumi.mihoyo.com/event/luna/home?act_id=e202311201442471&lang=zh-cn",
                "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn",
                "https://bbs-api.mihoyo.com/user/wapi/getUserFullInfo"
            };

            foreach (var url in urls)
            {
                await RequestUrlAndPrintInfo(url);
                Console.WriteLine("\n" + new string('-', 80) + "\n");
                await Task.Delay(2000);
            }

            Logger.LogInfo("主页请求完成");
        }
        public async Task TestAccountAPIs()
        {
            Logger.LogInfo("开始测试账号相关API");
            Console.WriteLine("========== 测试账号相关API ==========");

            await TestGetAccountInfo();

            Console.WriteLine("\n" + new string('=', 80) + "\n");

            await TestGetRewardsList();

            Logger.LogInfo("账号相关API测试完成");
        }

        private async Task TestGetAccountInfo()
        {
            Logger.LogInfo("测试: 获取账号信息");
            var url = "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn";
            await RequestUrlAndPrintInfo(url);
        }

        private async Task TestGetRewardsList()
        {
            Logger.LogInfo("测试: 获取奖励列表");
            var url = "https://api-takumi.mihoyo.com/event/luna/home?lang=zh-cn&act_id=e202311201442471";
            await RequestUrlAndPrintInfo(url);
        }
    }
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("客户端测试");
            Console.WriteLine("版本: 2.0.0");
            Console.WriteLine("==========");

            Logger.LogInfo("程序启动");
            var config = LoadConfig();

            if (config == null)
            {
                Logger.LogError("配置文件加载失败");
                Console.WriteLine("配置文件加载失败，请确保config.json文件存在且格式正确");
                return;
            }

            try
            {
                Console.WriteLine("\n选择模式:");
                Console.WriteLine("1. 请求主页和API（传统方式）");
                Console.WriteLine("2. 测试账号相关API");
                Console.WriteLine("3. 自定义URL测试");
                Console.WriteLine("4. 扫码登录获取SToken");
                Console.WriteLine("5. 浏览器模拟模式");
                Console.Write("请输入选项 (1-5): ");

                var choice = Console.ReadLine();
                Logger.LogInfo($"用户选择模式: {choice}");

                switch (choice)
                {
                    case "1":
                        if (string.IsNullOrEmpty(config.Account.Cookie))
                        {
                            Logger.LogWarning("Cookie为空，无法执行请求");
                            Console.WriteLine("请先在config.json中配置米游社Cookie或使用扫码登录获取SToken");
                            return;
                        }
                        var client1 = new PseudoClient(config);
                        await client1.RequestHomePageAndPrintInfo();
                        break;
                    case "2":
                        if (string.IsNullOrEmpty(config.Account.Cookie))
                        {
                            Logger.LogWarning("Cookie为空，无法执行请求");
                            Console.WriteLine("请先在config.json中配置米游社Cookie或使用扫码登录获取SToken");
                            return;
                        }
                        var client2 = new PseudoClient(config);
                        await client2.TestAccountAPIs();
                        break;
                    case "3":
                        if (string.IsNullOrEmpty(config.Account.Cookie))
                        {
                            Logger.LogWarning("Cookie为空，无法执行请求");
                            Console.WriteLine("请先在config.json中配置米游社Cookie或使用扫码登录获取SToken");
                            return;
                        }
                        var client3 = new PseudoClient(config);
                        Console.Write("请输入要测试的URL: ");
                        var customUrl = Console.ReadLine();
                        Logger.LogInfo($"自定义URL: {customUrl}");
                        if (!string.IsNullOrEmpty(customUrl))
                        {
                            await client3.RequestUrlAndPrintInfo(customUrl);
                        }
                        else
                        {
                            Logger.LogWarning("用户输入了空的URL");
                            Console.WriteLine("URL不能为空");
                        }
                        break;
                    case "4":
                        await PerformQRLogin(config);
                        break;
                    case "5":
                        await RunBrowserSimulator(config);
                        break;
                    default:
                        Logger.LogWarning($"无效选项: {choice}");
                        Console.WriteLine("无效选项");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"程序运行异常: {ex.Message}");
                Logger.LogError($"详细异常: {ex.StackTrace}");
                Console.WriteLine($"程序运行异常: {ex.Message}");
                Console.WriteLine($"详细异常: {ex.StackTrace}");
            }

            Console.WriteLine("\n==========");
            Console.WriteLine("操作完成");
            Logger.LogInfo("程序结束");

            Console.WriteLine($"\n详细日志已保存到: {Logger.GetLogFilePath()}");

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }

        static async Task PerformQRLogin(Config config)
        {
            Logger.LogInfo("开始扫码登录流程");
            Console.WriteLine("\n========== 扫码登录获取SToken ==========");
            Console.WriteLine("注意: 请确保已安装米游社APP并已登录账号");

            var qrLoginHelper = new QRLoginHelper();
            Console.WriteLine("\n正在获取登录二维码...");
            Logger.LogInfo("正在获取登录二维码");
            var qrData = await qrLoginHelper.GetQrCodeAsync();

            if (qrData == null)
            {
                Logger.LogError("获取二维码失败");
                Console.WriteLine("获取二维码失败！");
                return;
            }
            Console.WriteLine("\n========== 请打开以下链接扫码登录 ==========");
            Console.WriteLine($"URL: {qrData.Url}");
            Console.WriteLine($"Ticket: {qrData.Ticket}");
            Console.WriteLine("=============================================");
            Logger.LogInfo($"二维码URL: {qrData.Url}");
            Logger.LogInfo($"Ticket: {qrData.Ticket}");
            string qrFileName = $"login_qrcode_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            Tools.SaveQrCodeToFile(qrData.Url, qrFileName);
            Tools.GenerateAsciiQrCode(qrData.Url);

            Console.WriteLine("\n操作说明:");
            Console.WriteLine($"1. 打开文件: {qrFileName}");
            Console.WriteLine("2. 使用米游社APP扫描二维码");
            Console.WriteLine("3. 在手机上确认登录");
            Console.WriteLine("注意: 二维码15分钟内有效");
            Logger.LogInfo("开始检查登录状态");
            var (uid, gameToken) = await qrLoginHelper.CheckLoginAsync(qrData);

            if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(gameToken))
            {
                Logger.LogError("登录失败");
                Console.WriteLine("登录失败！");
                return;
            }

            Logger.LogInfo($"登录成功，UID: {uid}, GameToken: {gameToken}");
            Console.WriteLine($"\n登录成功！");
            Console.WriteLine($"UID: {uid}");
            Console.WriteLine($"GameToken: {gameToken}");
            Console.WriteLine("\n正在获取SToken...");
            Logger.LogInfo("正在获取SToken");
            var (mid, stoken) = await qrLoginHelper.GetStokenByGameTokenAsync(uid, gameToken);

            if (string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(stoken))
            {
                Logger.LogError("获取SToken失败");
                Console.WriteLine("获取SToken失败！");
                return;
            }

            Logger.LogInfo($"获取SToken成功: MID={mid}, SToken={stoken}");
            Console.WriteLine($"获取SToken成功！");
            Console.WriteLine($"MID: {mid}");
            Console.WriteLine($"SToken: {stoken}");
            config.Account.Cookie = $"stuid={uid}; stoken={stoken}; mid={mid}";
            Logger.LogInfo($"生成Cookie: {config.Account.Cookie}");
            if (SaveConfig(config))
            {
                Logger.LogInfo("配置已保存到文件");
                Console.WriteLine("\n配置已保存到 config.json 文件！");
                Console.WriteLine($"配置文件位置: {Path.GetFullPath("config.json")}");
                Console.WriteLine("\n========== 使用示例 ==========");
                Console.WriteLine("现在可以使用以下Cookie进行API请求:");
                Console.WriteLine($"Cookie: {config.Account.Cookie}");
                Console.WriteLine("\n重新运行程序，选择模式1测试API请求");
            }
            else
            {
                Logger.LogError("保存配置文件失败");
                Console.WriteLine("\n保存配置文件失败！");
                Console.WriteLine("请手动保存以下信息:");
                Console.WriteLine($"UID: {uid}");
                Console.WriteLine($"MID: {mid}");
                Console.WriteLine($"SToken: {stoken}");
                Console.WriteLine($"GameToken: {gameToken}");
                Console.WriteLine($"Cookie: stuid={uid}; stoken={stoken}; mid={mid}");
            }
        }

        static async Task RunBrowserSimulator(Config config)
        {
            Console.WriteLine("\n========== 浏览器模拟模式 ==========");
            Console.WriteLine("1. 无头模式（后台运行）");
            Console.WriteLine("2. 可视化模式（需要图形界面）");
            Console.Write("请选择模式: ");

            var modeChoice = Console.ReadLine();
            var isHeadless = modeChoice == "1";

            using var browser = new BrowserSimulator(config, isHeadless);

            try
            {
                await browser.InitializeAsync();

                while (true)
                {
                    Console.WriteLine("\n========== 浏览器操作菜单 ==========");
                    Console.WriteLine("1. 访问网页");
                    Console.WriteLine("2. 执行JavaScript");
                    Console.WriteLine("3. 点击元素");
                    Console.WriteLine("4. 输入文本");
                    Console.WriteLine("5. 截图");
                    Console.WriteLine("6. 获取Cookie");
                    Console.WriteLine("7. 获取页面HTML");
                    Console.WriteLine("8. 测试米哈游相关页面");
                    Console.WriteLine("0. 退出");
                    Console.Write("请选择操作: ");

                    var actionChoice = Console.ReadLine();

                    switch (actionChoice)
                    {
                        case "1":
                            Console.Write("请输入URL: ");
                            var url = Console.ReadLine();
                            if (!string.IsNullOrEmpty(url))
                            {
                                var content = await browser.BrowsePageAsync(url);
                                Console.WriteLine("\n页面HTML（前1000字符）:");
                                Console.WriteLine(content.Substring(0, Math.Min(1000, content.Length)));
                                if (content.Length > 1000)
                                    Console.WriteLine("... (内容过长，已截断)");
                            }
                            break;

                        case "2":
                            Console.Write("请输入JavaScript代码: ");
                            var js = Console.ReadLine();
                            if (!string.IsNullOrEmpty(js))
                            {
                                var result = await browser.ExecuteJavaScriptAsync(js);
                                Console.WriteLine("\n执行结果:");
                                Console.WriteLine(result);
                            }
                            break;

                        case "3":
                            Console.Write("请输入CSS选择器: ");
                            var selector = Console.ReadLine();
                            if (!string.IsNullOrEmpty(selector))
                            {
                                await browser.ClickElementAsync(selector);
                                Console.WriteLine("点击完成");
                            }
                            break;

                        case "4":
                            Console.Write("请输入CSS选择器: ");
                            var inputSelector = Console.ReadLine();
                            Console.Write("请输入文本: ");
                            var text = Console.ReadLine();
                            if (!string.IsNullOrEmpty(inputSelector) && !string.IsNullOrEmpty(text))
                            {
                                await browser.TypeTextAsync(inputSelector, text);
                                Console.WriteLine("输入完成");
                            }
                            break;

                        case "5":
                            var screenshotPath = await browser.TakeScreenshotAsync();
                            if (!string.IsNullOrEmpty(screenshotPath))
                            {
                                Console.WriteLine($"截图已保存: {screenshotPath}");
                            }
                            break;

                        case "6":
                            var cookies = await browser.GetCookiesAsync();
                            Console.WriteLine($"\n当前Cookie ({cookies.Count}个):");
                            foreach (var cookie in cookies)
                            {
                                Console.WriteLine($"{cookie.Key}: {cookie.Value}");
                            }
                            break;

                        case "7":
                            var jsContent = "document.documentElement.outerHTML";
                            var html = await browser.ExecuteJavaScriptAsync(jsContent);
                            Console.WriteLine("\n页面HTML（前2000字符）:");
                            Console.WriteLine(html.Substring(0, Math.Min(2000, html.Length)));
                            if (html.Length > 2000)
                                Console.WriteLine("... (内容过长，已截断)");
                            break;

                        case "8":
                            await TestMihoyoPages(browser);
                            break;

                        case "0":
                            Console.WriteLine("退出浏览器模拟模式");
                            return;

                        default:
                            Console.WriteLine("无效选项");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"浏览器模拟器错误: {ex.Message}");
                Logger.LogError($"浏览器模拟器错误: {ex.Message}");
            }
        }

        static async Task TestMihoyoPages(BrowserSimulator browser)
        {
            Console.WriteLine("\n测试");

            var testUrls = new Dictionary<string, string>
            {
                { "1", "https://bbs.mihoyo.com/ys/" },
                { "2", "https://user.mihoyo.com/#/account/home" },
                { "3", "https://api-takumi.mihoyo.com/event/luna/home?act_id=e202311201442471&lang=zh-cn" },
                { "4", "https://webstatic.mihoyo.com/bbs/event/signin-ys/index.html" }
            };

            foreach (var testUrl in testUrls)
            {
                Console.WriteLine($"{testUrl.Key}. {testUrl.Value}");
            }

            Console.Write("\n请选择要测试的页面: ");
            var choice = Console.ReadLine();

            if (testUrls.ContainsKey(choice))
            {
                var url = testUrls[choice];

                Console.WriteLine($"\n正在访问: {url}");

                try
                {
                    var content = await browser.BrowsePageAsync(url);
                    var jsToExecute = @"
                        (function() {
                            var info = {
                                title: document.title,
                                url: window.location.href,
                                userAgent: navigator.userAgent,
                                cookieEnabled: navigator.cookieEnabled,
                                language: navigator.language,
                                platform: navigator.platform,
                                screen: {
                                    width: window.screen.width,
                                    height: window.screen.height
                                }
                            };
                            if (typeof miHoYo !== 'undefined') {
                                info.mihoyo = '检测到miHoYo对象';
                            }
                            
                            if (typeof game_fp !== 'undefined') {
                                info.game_fp = game_fp;
                            }
                            
                            if (typeof getQueryString !== 'undefined') {
                                info.queryString = getQueryString();
                            }
                            
                            return info;
                        })();
                    ";

                    var jsResult = await browser.ExecuteJavaScriptAsync(jsToExecute);
                    Console.WriteLine("\n页面JavaScript执行结果:");
                    Console.WriteLine(jsResult);
                    var screenshotPath = await browser.TakeScreenshotAsync($"mihoyo_test_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    Console.WriteLine($"\n页面截图已保存: {screenshotPath}");

                    Console.WriteLine("\n页面访问完成！");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"访问页面失败: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine("无效选择");
            }
        }

        static async Task RunAutomationTasks(Config config)
        {
            Console.WriteLine("\n========== 自动化任务执行 ==========");
            Console.WriteLine("1. 自动签到任务");
            Console.WriteLine("2. 模拟完整用户流程");
            Console.Write("请选择任务: ");

            var taskChoice = Console.ReadLine();

            switch (taskChoice)
            {
                case "1":
                    await RunAutoSignTask(config);
                    break;

                case "2":
                    await RunFullUserFlow(config);
                    break;

                default:
                    Console.WriteLine("无效选择");
                    break;
            }
        }

        static async Task RunAutoSignTask(Config config)
        {
            Console.WriteLine("\n========== 自动签到任务 ==========");

            using var browser = new BrowserSimulator(config, true);

            try
            {
                await browser.InitializeAsync();
                Console.WriteLine("正在访问签到页面...");
                var signUrl = "https://act.mihoyo.com/ys/event/e202311201442471/index.html";
                await browser.BrowsePageAsync(signUrl);
                var checkSignJs = @"
                    var signButton = document.querySelector('.sign-btn, .sign-button, [class*=""sign""], button:contains(""签到"")');
                    var signedElement = document.querySelector('.signed, .signed-text, .已签到');
                    
                    return {
                        signButtonExists: signButton !== null,
                        signButtonText: signButton ? signButton.textContent : null,
                        signedElementExists: signedElement !== null,
                        signedElementText: signedElement ? signedElement.textContent : null
                    };
                ";

                var signStatus = await browser.ExecuteJavaScriptAsync(checkSignJs);
                Console.WriteLine($"签到状态: {signStatus}");
                var parseResult = JsonSerializer.Deserialize<Dictionary<string, object>>(signStatus);
                if (parseResult != null &&
                    parseResult.ContainsKey("signButtonExists") &&
                    bool.TryParse(parseResult["signButtonExists"].ToString(), out var exists) &&
                    exists)
                {
                    Console.WriteLine("检测到签到按钮，正在点击...");
                    await browser.ExecuteJavaScriptAsync(@"
                        var signBtn = document.querySelector('.sign-btn, .sign-button, [class*=""sign""], button:contains(""签到"")');
                        if (signBtn) {
                            signBtn.click();
                            return '点击成功';
                        }
                        return '未找到按钮';
                    ");
                    await Task.Delay(3000);
                    var resultCheck = await browser.ExecuteJavaScriptAsync(@"
                        var resultText = document.querySelector('.sign-result, .result-text, .success-text');
                        return resultText ? resultText.textContent : '未找到结果文本';
                    ");

                    Console.WriteLine($"签到结果: {resultCheck}");
                }
                else
                {
                    Console.WriteLine("未找到签到按钮或已签到");
                }
                var screenshotPath = await browser.TakeScreenshotAsync("auto_sign_result.png");
                Console.WriteLine($"任务截图已保存: {screenshotPath}");

                Console.WriteLine("\n自动签到任务完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"自动签到任务失败: {ex.Message}");
                Logger.LogError($"自动签到任务失败: {ex.Message}");
            }
        }

        static async Task RunFullUserFlow(Config config)
        {
            Console.WriteLine("\n========== 模拟完整用户流程 ==========");

            using var browser = new BrowserSimulator(config, false);

            try
            {
                await browser.InitializeAsync();
                Console.WriteLine("1. 访问米游社主页...");
                await browser.BrowsePageAsync("https://bbs.mihoyo.com/ys/");
                await Task.Delay(2000);
                Console.WriteLine("2. 获取当前Cookie...");
                var cookies = await browser.GetCookiesAsync();
                Console.WriteLine($"当前有 {cookies.Count} 个Cookie");
                Console.WriteLine("3. 访问用户中心...");
                await browser.BrowsePageAsync("https://user.mihoyo.com/#/account/home");
                await Task.Delay(2000);
                Console.WriteLine("4. 访问活动页面...");
                await browser.BrowsePageAsync("https://act.mihoyo.com/ys/event/e202311201442471/index.html");
                await Task.Delay(2000);
                Console.WriteLine("5. 模拟用户交互...");
                try
                {
                    await browser.ExecuteJavaScriptAsync(@"
                        window.scrollTo(0, 300);
                        var clickableElements = document.querySelectorAll('button, a, [onclick]');
                        if (clickableElements.length > 0) {
                            for (var i = 0; i < Math.min(3, clickableElements.length); i++) {
                                var element = clickableElements[i];
                                if (element.offsetWidth > 0 && element.offsetHeight > 0) {
                                    console.log('点击元素:', element.tagName, element.className);
                                    element.click();
                                    break;
                                }
                            }
                        }
                    ");
                }
                catch { }
                Console.WriteLine("6. 截图记录...");
                var screenshotPath = await browser.TakeScreenshotAsync("user_flow_complete.png");
                Console.WriteLine($"流程截图已保存: {screenshotPath}");

                Console.WriteLine("\n完整用户流程模拟完成！");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"用户流程模拟失败: {ex.Message}");
                Logger.LogError($"用户流程模拟失败: {ex.Message}");
            }
        }

        static Config LoadConfig()
        {
            try
            {
                var configPath = "config.json";
                if (!File.Exists(configPath))
                {
                    var defaultConfig = new Config();
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    var json = JsonSerializer.Serialize(defaultConfig, options);
                    File.WriteAllText(configPath, json);
                    Logger.LogInfo($"已创建默认配置文件: {configPath}");

                    var fullPath = Path.GetFullPath(configPath);
                    Console.WriteLine("已创建默认配置文件 config.json");
                    Console.WriteLine($"配置文件位置: {fullPath}");

                    return defaultConfig;
                }

                var jsonText = File.ReadAllText(configPath);
                jsonText = CleanJsonString(jsonText);

                var config = JsonSerializer.Deserialize<Config>(jsonText);
                Logger.LogInfo($"配置文件加载成功");
                return config;
            }
            catch (Exception ex)
            {
                Logger.LogError($"加载配置文件失败: {ex.Message}");
                Console.WriteLine($"加载配置文件失败: {ex.Message}");
                Console.WriteLine($"建议：请用记事本打开config.json文件，检查值是否有换行或特殊字符。");
                return null;
            }
        }

        static bool SaveConfig(Config config)
        {
            try
            {
                var configPath = "config.json";
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(configPath, json);
                Logger.LogInfo($"配置文件已保存: {configPath}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"保存配置文件失败: {ex.Message}");
                Console.WriteLine($"保存配置文件失败: {ex.Message}");
                return false;
            }
        }

        static string CleanJsonString(string jsonText)
        {
            try
            {
                JsonDocument.Parse(jsonText);
                return jsonText;
            }
            catch (JsonException)
            {
                Logger.LogWarning("检测到JSON格式问题，正在尝试清理...");

                var pattern = "\"Cookie\"\\s*:\\s*\"([^\"]*)\"";
                var regex = new System.Text.RegularExpressions.Regex(pattern);

                string cleanedJson = regex.Replace(jsonText, match =>
                {
                    var cookieValue = match.Groups[1].Value;
                    cookieValue = cookieValue.Replace("\r", "").Replace("\n", "").Replace("\t", " ");
                    cookieValue = System.Text.RegularExpressions.Regex.Replace(cookieValue, @"\s+", " ");
                    return $"\"Cookie\": \"{cookieValue}\"";
                });

                return cleanedJson;
            }
        }
    }
}