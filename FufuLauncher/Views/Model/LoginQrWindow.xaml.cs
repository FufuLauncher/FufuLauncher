using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Constants;
using FufuLauncher.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;
using MihoyoBBS; 
using FufuLauncher.Services;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow : Window
{
    private const string Salt = "dDIQHbKOdaPaLuvQKVzUzqdeCaxjtaPV";
    private const string SaltGame = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    private readonly string _deviceId;
    private readonly string _deviceFp;
    private readonly HttpClient _httpClient;
    
    private string _appTicket;
    private string _gameTicket;
    private string _gameAppId = "7";
    private string _gameDevice;
    
    private CancellationTokenSource _pollingCts;

    private ContentDialog _statusDialog;
    private bool _isDialogOpen;

    public bool IsLoginSuccessful { get; private set; }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public LoginQrWindow()
    {
        InitializeComponent();
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        
        _deviceId = Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();
        _deviceFp = GenerateDeviceFingerprint();
        _gameDevice = GenerateRandomString(64, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789");
        
        var handler = new HttpClientHandler { UseCookies = false };
        _httpClient = new HttpClient(handler);
        
        if (Content is FrameworkElement rootContent)
        {
            rootContent.Loaded += RootContent_Loaded;
        }
        
        Closed += LoginQrWindow_Closed;
    }

    private async void RootContent_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement rootContent)
        {
            rootContent.Loaded -= RootContent_Loaded;
        }

        try
        {
            var localSettingsService = new LocalSettingsService();
            var savedConfigObj = await localSettingsService.ReadSettingAsync("AccountConfig");
            
            if (savedConfigObj != null)
            {
                var dialog = new ContentDialog
                {
                    Title = "发现已保存的配置",
                    Content = "本地数据库存在之前保存的账号配置，是否直接应用并完成登录？",
                    PrimaryButtonText = "是，直接应用",
                    CloseButtonText = "否，重新扫码",
                    XamlRoot = Content?.XamlRoot
                };

                if (dialog.XamlRoot != null)
                {
                    var result = await dialog.ShowAsync();
                    
                    if (result == ContentDialogResult.Primary)
                    {
                        UpdateStatus("正在应用本地配置...", true);

                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };

                        Config config = null;
                        if (savedConfigObj is JsonElement jsonElement)
                        {
                            config = JsonSerializer.Deserialize<Config>(jsonElement.GetRawText(), options);
                        }
                        else if (savedConfigObj is string jsonString)
                        {
                            config = JsonSerializer.Deserialize<Config>(jsonString, options);
                        }
                        else
                        {
                            var json = JsonSerializer.Serialize(savedConfigObj, options);
                            config = JsonSerializer.Deserialize<Config>(json, options);
                        }

                        if (config != null)
                        {
                            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
                            var dir = Path.GetDirectoryName(path);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);

                            var newJson = JsonSerializer.Serialize(config, options);
                            await File.WriteAllTextAsync(path, newJson);

                            Debug.WriteLine($"已应用本地配置并保存至: {path}");

                            IsLoginSuccessful = true;
                            UpdateStatus("应用成功", false, true);

                            await Task.Delay(1500);
                            Close();
                            return; 
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"检查或应用本地配置失败: {ex.Message}");
        
            WeakReferenceMessenger.Default.Send(
                new NotificationMessage(
                    "应用配置失败",
                    $"写入配置文件时发生错误: {ex.Message}",
                    NotificationType.Error,
                    4000
                )
            );
        }

        UpdateGameAppIdFromSelection();
        await StartLoginFlowAsync();
    }

    private void LoginQrWindow_Closed(object sender, WindowEventArgs args)
    {
        _pollingCts?.Cancel();
    }
    
    public bool DidLoginSucceed() => IsLoginSuccessful;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RestartLoginFlowAsync();
    }
    
    private async void ManualCookieButton_Click(object sender, RoutedEventArgs e)
    {
    TextBox inputTextBox = new()
    {
        AcceptsReturn = true,
        Height = 150,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "在此处粘贴Cookie"
    };

    TextBlock errorTextBlock = new()
    {
        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 10, 0, 0),
        TextWrapping = TextWrapping.Wrap
    };

    StackPanel dialogContent = new();
    dialogContent.Children.Add(inputTextBox);
    dialogContent.Children.Add(errorTextBlock);

    ContentDialog dialog = new()
    {
        Title = "手动输入Cookie",
        Content = dialogContent,
        PrimaryButtonText = "保存",
        CloseButtonText = "取消",
        XamlRoot = this.Content?.XamlRoot
    };

    dialog.PrimaryButtonClick += async (s, args) =>
    {
        string cookieStr = inputTextBox.Text.Trim();
        
        if (string.IsNullOrEmpty(cookieStr) || !cookieStr.Contains("="))
        {
            args.Cancel = true;
            errorTextBlock.Text = "Cookie无效";
            errorTextBlock.Visibility = Visibility.Visible;
            return;
        }
        
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        try
        {
            await SaveConfigForLauncherAsync(cookieStr);
            IsLoginSuccessful = true;
            UpdateStatus("登录成功", false, true);
            DispatcherQueue.TryEnqueue(() => Close());
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            errorTextBlock.Text = $"保存失败: {ex.Message}";
            errorTextBlock.Visibility = Visibility.Visible;
        }
        finally
        {
            deferral.Complete();
        }
    };
    
    errorTextBlock.Visibility = Visibility.Collapsed;
    inputTextBox.Text = string.Empty;

    await dialog.ShowAsync();
}

    private async void LoginMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameSelectionComboBox != null)
        {
            GameSelectionComboBox.Visibility = LoginMethodComboBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (QrCodeContainer != null && PassportWebView != null)
        {
            if (LoginMethodComboBox.SelectedIndex == 2)
            {
                QrCodeContainer.Visibility = Visibility.Collapsed;
                PassportWebViewBorder.Visibility = Visibility.Visible;
                await StartWebPassportLoginAsync();
                return;
            }
            else
            {
                PassportWebViewBorder.Visibility = Visibility.Collapsed;
                QrCodeContainer.Visibility = Visibility.Visible;
            }
        }

        await RestartLoginFlowAsync();
    }

    private async void GameSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LoginMethodComboBox != null && LoginMethodComboBox.SelectedIndex == 1)
        {
            UpdateGameAppIdFromSelection();
            await RestartLoginFlowAsync();
        }
    }

    private void UpdateGameAppIdFromSelection()
    {
        if (GameSelectionComboBox?.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            _gameAppId = item.Tag.ToString();
        }
    }

    private async Task RestartLoginFlowAsync()
    {
        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
        }
        UpdateStatus("", false, true); 
        await StartLoginFlowAsync();
    }

    private async Task StartLoginFlowAsync()
    {
        if (LoginMethodComboBox.SelectedIndex == 0)
        {
            await StartAppLoginFlowAsync();
        }
        else
        {
            await StartGameLoginFlowAsync();
        }
    }

    #region 米游社APP扫码登录
    
    private async Task StartAppLoginFlowAsync()
    {
        UpdateStatus("正在创建APP登录二维码...", true);
        
        var qrResult = await CreateAppQrCodeAsync();
        if (!qrResult.Success)
        {
            UpdateStatus($"创建失败: {qrResult.Message}", false);
            return;
        }

        RenderQrCode(qrResult.Url);
        UpdateStatus("请使用米游社APP扫描二维码", false, true);

        _pollingCts = new CancellationTokenSource();
        await PollAppLoginStatusAsync(_pollingCts.Token);
    }

    private async Task<(bool Success, string Url, string Message)> CreateAppQrCodeAsync()
    {
        string url = ApiEndpoints.PassportAppCreateQrLoginUrl;
        var body = new JsonObject();
        string bodyStr = body.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

        AddCommonHeaders(request, bodyStr, "", "3", "ddxf5dufpuyo", "2.90.1");

        try
        {
            var response = await _httpClient.SendAsync(request);
            string responseStr = await response.Content.ReadAsStringAsync();
            var result = JsonNode.Parse(responseStr);

            if (result["retcode"]?.GetValue<int>() == 0)
            {
                string qrUrl = result["data"]["url"]?.GetValue<string>();
                _appTicket = result["data"]["ticket"]?.GetValue<string>();
                return (true, qrUrl, "Success");
            }
            return (false, null, result["message"]?.GetValue<string>());
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private async Task PollAppLoginStatusAsync(CancellationToken ct)
    {
        string url = ApiEndpoints.PassportAppQueryQrLoginStatusUrl;
        int pollInterval = 3000;
        JsonNode confirmedData = null;

        while (!ct.IsCancellationRequested)
        {
            var body = new JsonObject { ["ticket"] = _appTicket };
            string bodyStr = body.ToJsonString(_jsonOptions);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

            AddCommonHeaders(request, bodyStr, "", "3", "ddxf5dufpuyo", "2.90.1");

            try
            {
                var response = await _httpClient.SendAsync(request, ct);
                string responseStr = await response.Content.ReadAsStringAsync();
                var result = JsonNode.Parse(responseStr);

                int retcode = result["retcode"]?.GetValue<int>() ?? -1;

                if (retcode == -3501 || retcode == -106)
                {
                    UpdateStatus("二维码已失效或过期", false);
                    return; 
                }

                if (retcode == 0)
                {
                    string status = result["data"]["status"]?.GetValue<string>();
                    
                    if (status == "Confirmed" || status == "confirmed")
                    {
                        UpdateStatus("APP扫码成功，正在换取...", true);
                        confirmedData = result["data"];
                        break; 
                    }

                    if (status == "Scanned" || status == "scanned")
                    {
                        UpdateStatus("已扫码，请在手机端确认登录...", true);
                    }
                }
                
                await Task.Delay(pollInterval, ct);
            }
            catch (TaskCanceledException) { return; }
            catch (Exception) { await Task.Delay(pollInterval, ct); }
        }

        if (confirmedData != null)
        {
            await ProcessAndExchangeV2TokensAsync(confirmedData);
        }
    }

    private async Task ProcessAndExchangeV2TokensAsync(JsonNode dataNode)
    {
        string stoken = "";
        string mid = dataNode["user_info"]?["mid"]?.GetValue<string>() ?? "";
        string aid = dataNode["user_info"]?["aid"]?.GetValue<string>() ?? "";

        var tokens = dataNode["tokens"]?.AsArray();
        if (tokens != null && tokens.Count > 0)
        {
            stoken = tokens[0]["token"]?.GetValue<string>();
        }

        if (string.IsNullOrEmpty(stoken) || string.IsNullOrEmpty(mid))
        {
            UpdateStatus("提取失败，请重试", false);
            return;
        }
        
        await ExchangeV2TokensAndSaveAsync(stoken, mid, aid);
    }
    #endregion

    #region 游戏扫码登录
    private async Task StartGameLoginFlowAsync()
    {
        UpdateStatus("正在创建游戏扫码二维码...", true);
        
        var qrResult = await CreateGameQrCodeAsync();
        if (!qrResult.Success)
        {
            UpdateStatus($"创建失败: {qrResult.Message}", false);
            return;
        }

        RenderQrCode(qrResult.Url);
        UpdateStatus("请使用米游社或对应游戏内扫描二维码", false, true);

        _pollingCts = new CancellationTokenSource();
        await PollGameLoginStatusAsync(_pollingCts.Token);
    }

    private async Task<(bool Success, string Url, string Message)> CreateGameQrCodeAsync()
    {
        string url = ApiEndpoints.Hk4eQrCodeFetchUrl;
        
        var requestBody = new JsonObject
        {
            ["app_id"] = _gameAppId,
            ["device"] = _gameDevice
        };
        string bodyStr = requestBody.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

        AddGameHeaders(request, bodyStr, "");

        try
        {
            var response = await _httpClient.SendAsync(request);
            string responseStr = await response.Content.ReadAsStringAsync();
            var result = JsonNode.Parse(responseStr);

            if (result["retcode"]?.GetValue<int>() == 0)
            {
                string qrUrl = result["data"]["url"]?.GetValue<string>();
                
                var uri = new Uri(qrUrl);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                _gameTicket = query["ticket"];

                if (string.IsNullOrEmpty(_gameTicket) && qrUrl.Contains("ticket="))
                {
                    var start = qrUrl.IndexOf("ticket=") + 7;
                    var end = qrUrl.IndexOf('&', start);
                    if (end == -1) end = qrUrl.Length;
                    _gameTicket = qrUrl.Substring(start, end - start);
                }

                return (true, qrUrl, "Success");
            }
            return (false, null, result["message"]?.GetValue<string>());
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    private async Task PollGameLoginStatusAsync(CancellationToken ct)
    {
        string url = ApiEndpoints.Hk4eQrCodeQueryUrl;
        int pollInterval = 3000;

        while (!ct.IsCancellationRequested)
        {
            var requestBody = new JsonObject
            {
                ["app_id"] = _gameAppId,
                ["device"] = _gameDevice,
                ["ticket"] = _gameTicket
            };
            string bodyStr = requestBody.ToJsonString(_jsonOptions);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

            AddGameHeaders(request, bodyStr, "");

            try
            {
                var response = await _httpClient.SendAsync(request, ct);
                string responseStr = await response.Content.ReadAsStringAsync();
                var result = JsonNode.Parse(responseStr);

                int retcode = result["retcode"]?.GetValue<int>() ?? -1;

                if (retcode == 0)
                {
                    string stat = result["data"]["stat"]?.GetValue<string>();
                    
                    if (stat == "Confirmed")
                    {
                        UpdateStatus("扫码成功，正在换取SToken...", true);
                        string raw = result["data"]["payload"]?["raw"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(raw))
                        {
                            var rawNode = JsonNode.Parse(raw);
                            string uid = rawNode["uid"]?.GetValue<string>();
                            string token = rawNode["token"]?.GetValue<string>();
                            await GetSTokenByGameTokenAsync(uid, token);
                            return;
                        }
                    }
                    else if (stat == "Scanned")
                    {
                        UpdateStatus("已扫码，请在手机端确认登录...", true);
                    }
                }
                else
                {
                    UpdateStatus($"二维码检查错误或过期: {result["message"]?.GetValue<string>()}", false);
                    return;
                }
                
                await Task.Delay(pollInterval, ct);
            }
            catch (TaskCanceledException) { return; }
            catch (Exception) { await Task.Delay(pollInterval, ct); }
        }
    }

    private async Task GetSTokenByGameTokenAsync(string accountId, string gameToken)
    {
        string url = ApiEndpoints.GetTokenByGameTokenUrl;
        
        var requestBody = new JsonObject
        {
            ["account_id"] = int.Parse(accountId),
            ["game_token"] = gameToken
        };
        string bodyStr = requestBody.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.71.1");
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-sys_version", "12");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", _deviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", "Xiaomi MI 6");
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", "MI 6");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", "bll8iq97cem8");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "4");
        request.Headers.TryAddWithoutValidation("User-Agent", "okhttp/4.9.3");
        request.Headers.TryAddWithoutValidation("DS", GenerateGameDS2(bodyStr, ""));

        try
        {
            var response = await _httpClient.SendAsync(request);
            string responseStr = await response.Content.ReadAsStringAsync();
            var result = JsonNode.Parse(responseStr);

            if (result["retcode"]?.GetValue<int>() == 0)
            {
                string stoken = result["data"]["token"]?["token"]?.GetValue<string>();
                string mid = result["data"]["user_info"]?["mid"]?.GetValue<string>();

                if (!string.IsNullOrEmpty(stoken) && !string.IsNullOrEmpty(mid))
                {
                    await ExchangeV2TokensAndSaveAsync(stoken, mid, accountId);
                    return;
                }
            }
            UpdateStatus($"SToken换取失败: {result["message"]?.GetValue<string>()}", false);
        }
        catch (Exception ex)
        {
            UpdateStatus($"SToken换取异常: {ex.Message}", false);
        }
    }

    private void AddGameHeaders(HttpRequestMessage request, string body, string query)
    {
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.71.1");
        request.Headers.TryAddWithoutValidation("x-rpc-aigis", "");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-sys_version", "12");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", _deviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", "Xiaomi MI 6");
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", "MI 6");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", "bll8iq97cem8");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "4");
        request.Headers.TryAddWithoutValidation("User-Agent", "okhttp/4.9.3");
    }

    private string GenerateGameDS2(string body, string query)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = new Random().Next(100001, 200000).ToString();
        string b = string.IsNullOrEmpty(body) ? "" : body;
        string q = string.IsNullOrEmpty(query) ? "" : query; 
        
        string signStr = $"salt={SaltGame}&t={t}&r={r}&b={b}&q={q}";
        string sign = CreateMD5(signStr);
        return $"{t},{r},{sign}";
    }
    #endregion

    #region 扫码换取V2Cookie
    
    private async Task ExchangeV2TokensAndSaveAsync(string stoken, string mid, string aid)
    {
        try
        {
            UpdateStatus("正在获取完整登录凭证...", true);
            var finalCookies = new Dictionary<string, string>
            {
                ["stoken"] = stoken,
                ["mid"] = mid,
                ["account_id"] = aid,
                ["ltuid"] = aid 
            };

            string cookieToken = await GetCookieAccountInfoBySTokenAsync(stoken);
            if (!string.IsNullOrEmpty(cookieToken))
            {
                finalCookies["cookie_token"] = cookieToken;
            }

            string webTicket = await CreateWebQrCodeAsync();
            if (string.IsNullOrEmpty(webTicket))
            {
                UpdateStatus("无法创建验证凭据");
                return;
            }

            string authCookie = $"stoken={stoken}; mid={mid}";

            bool scanResult = await SimulateAppActionAsync(ApiEndpoints.PassportScanQrLoginUrl, webTicket, authCookie);
            if (!scanResult)
            {
                UpdateStatus("扫描请求被拒绝");
                return;
            }

            await Task.Delay(1000);

            bool confirmResult = await SimulateAppActionAsync(ApiEndpoints.PassportConfirmQrLoginUrl, webTicket, authCookie);
            if (!confirmResult)
            {
                UpdateStatus("请求被拒绝");
                return;
            }

            var v2Cookies = await GetWebQrStatusAndExtractCookiesAsync(webTicket);
            if (v2Cookies != null && v2Cookies.Count > 0)
            {
                foreach (var kvp in v2Cookies)
                {
                    finalCookies[kvp.Key] = kvp.Value;
                }
                SaveCredentials(finalCookies);
            }
            else
            {
                UpdateStatus("未能从响应头提取出完整Cookie");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus($"凭证换取异常: {ex.Message}");
        }
    }

    private async Task<string> CreateWebQrCodeAsync()
    {
        string url = ApiEndpoints.PassportCreateQrLoginUrl;
        var body = new JsonObject();
        string bodyStr = body.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

        AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            if (result["retcode"]?.GetValue<int>() == 0) return result["data"]["ticket"]?.GetValue<string>();
        }
        catch { }

        return null!;
    }

    private async Task<bool> SimulateAppActionAsync(string url, string ticket, string authCookie)
    {
        var tokenTypes = new JsonArray { "4" }; 
        var body = new JsonObject { ["ticket"] = ticket, ["token_types"] = tokenTypes };
        string bodyStr = body.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1", authCookie);

        try
        {
            var response = await _httpClient.SendAsync(request);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            return result["retcode"]?.GetValue<int>() == 0;
        }
        catch { }
        return false;
    }

    private async Task<Dictionary<string, string>> GetWebQrStatusAndExtractCookiesAsync(string ticket)
    {
        string url = ApiEndpoints.PassportQueryQrLoginStatusUrl;
        var body = new JsonObject { ["ticket"] = ticket };
        string bodyStr = body.ToJsonString(_jsonOptions);

        for (int i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
            AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1");

            try
            {
                var response = await _httpClient.SendAsync(request);
                var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());

                if (result["retcode"]?.GetValue<int>() == 0)
                {
                    string status = result["data"]["status"]?.GetValue<string>();
                    if (status == "Confirmed" || status == "confirmed")
                    {
                        var cookieDict = new Dictionary<string, string>();
                        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                        {
                            foreach (var cookieStr in setCookies)
                            {
                                var mainPart = cookieStr.Split(';')[0];
                                var kv = mainPart.Split('=', 2);
                                if (kv.Length == 2) cookieDict[kv[0].Trim()] = kv[1].Trim();
                            }
                        }
                        return cookieDict;
                    }
                }
            }
            catch { }
            await Task.Delay(1000);
        }
        return null;
    }
    #endregion

    #region 公共
    private async Task<string> GetCookieAccountInfoBySTokenAsync(string stoken)
    {
        string url = $"{ApiEndpoints.GetCookieAccountInfoBySTokenUrl}?stoken={stoken}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddCommonHeaders(request, "", $"stoken={stoken}", "2", "bll8iq97cem8", "2.20.1", "", "https://user.mihoyo.com/");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            if (result["retcode"]?.GetValue<int>() == 0) return result["data"]?["cookie_token"]?.GetValue<string>() ?? "";
        }
        catch { }
        return "";
    }
    
    private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "警告",
            Content = "确定清除保存的历史登录数据吗？",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            XamlRoot = Content?.XamlRoot
        };

        var result = await dialog.ShowAsync();
    
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                UpdateStatus("正在清除数据库缓存...", true);

                var localSettingsService = new LocalSettingsService();
                await localSettingsService.RemoveSettingAsync("AccountConfig");

                UpdateStatus("清理完成", false, false);
                await Task.Delay(1000);
                UpdateStatus("", false, true); 
            }
            catch (Exception ex)
            {
                UpdateStatus($"清理失败: {ex.Message}", false, false);
                await Task.Delay(2000);
                UpdateStatus("", false, true);
            }
        }
    }
    
    private async void SaveCredentials(Dictionary<string, string> cookies)
    {
        var cookieList = new List<string>();
        foreach (var kvp in cookies)
        {
            cookieList.Add($"{kvp.Key}={kvp.Value}");
        }
        string cookieString = string.Join("; ", cookieList);

        await SaveConfigForLauncherAsync(cookieString);

        IsLoginSuccessful = true;
        UpdateStatus("登录成功", false, true);

        DispatcherQueue.TryEnqueue(() => Close());
    }

    private async Task SaveConfigForLauncherAsync(string cookieString)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var config = new Config(); 
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                config = JsonSerializer.Deserialize<Config>(json) ?? new Config();
            }

            config.Account.Cookie = cookieString;

            if (cookieString.Contains("account_id="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookieString, @"account_id=(\d+)");
                if (match.Success) config.Account.Stuid = match.Groups[1].Value;
            }
            else if (cookieString.Contains("ltuid="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookieString, @"ltuid=(\d+)");
                if (match.Success) config.Account.Stuid = match.Groups[1].Value;
            }
            else if (cookieString.Contains("stuid="))
            {
                var match = System.Text.RegularExpressions.Regex.Match(cookieString, @"stuid=(\d+)");
                if (match.Success) config.Account.Stuid = match.Groups[1].Value;
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var newJson = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(path, newJson);

            try
            {
                var localSettingsService = new LocalSettingsService();
                await localSettingsService.SaveSettingAsync("AccountConfig", config);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"配置数据库保存失败: {ex.Message}");
                
                WeakReferenceMessenger.Default.Send(
                    new NotificationMessage(
                        "保存状态异常",
                        $"配置数据库保存失败: {ex.Message}",
                        NotificationType.Error,
                        4000
                    )
                );
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"兼容配置保存失败: {ex.Message}");
            
            WeakReferenceMessenger.Default.Send(
                new NotificationMessage(
                    "写入配置失败",
                    $"无法保存配置文件，请检查权限: {ex.Message}",
                    NotificationType.Error,
                    4000
                )
            );
        }
    }

    private void AddCommonHeaders(HttpRequestMessage request, string body, string query, string clientType, string appId, string sdkVersion, string cookie = "", string referer = "")
    {
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 miHoYoBBS/2.90.1 Capture/2.2.0");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-cn");

        if (!string.IsNullOrEmpty(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (!string.IsNullOrEmpty(referer)) request.Headers.TryAddWithoutValidation("Referer", referer);

        request.Headers.TryAddWithoutValidation("x-rpc-client_type", clientType);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.90.1");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", _deviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_fp", _deviceFp);
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", appId);
        request.Headers.TryAddWithoutValidation("x-rpc-sdk_version", sdkVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-account_version", "2.90.1");
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", "Mi 14");
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", "Mihoyo Capture");

        request.Headers.TryAddWithoutValidation("DS", GenerateDS(body, query));
    }

    private string GenerateDeviceFingerprint()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string seedId = GenerateRandomString(16, "0123456789abcdef");

        var deviceInfo = new
        {
            device_id = _deviceId,
            seed_id = seedId,
            seed_time = timestamp,
            platform = "2",
            device_fp = "",
            app_name = "bbs_cn"
        };

        string fpStr = JsonSerializer.Serialize(deviceInfo, _jsonOptions);
        return CreateMD5(fpStr);
    }

    private string GenerateDS(string body, string query)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GenerateRandomString(6, "abcdefghijklmnopqrstuvwxyz0123456789");
        
        string b = string.IsNullOrEmpty(body) ? "" : body;
        string q = string.IsNullOrEmpty(query) ? "" : query; 

        string signStr = $"salt={Salt}&t={t}&r={r}&b={b}&q={q}";
        string sign = CreateMD5(signStr);

        return $"{t},{r},{sign}";
    }

    private string GenerateRandomString(int length, string chars)
    {
        var random = new Random();
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result);
    }

    private string CreateMD5(string input)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            
            StringBuilder sb = new();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }

    private void UpdateStatus(string message, bool isProgress = false, bool closeDialog = false)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (closeDialog)
            {
                if (_isDialogOpen && _statusDialog != null)
                {
                    _statusDialog.Hide();
                    _isDialogOpen = false;
                }
                return;
            }

            if (_statusDialog == null)
            {
                if (this.Content?.XamlRoot == null) return;
                _statusDialog = new ContentDialog { XamlRoot = this.Content.XamlRoot };
                _statusDialog.Closed += (s, e) => _isDialogOpen = false;
            }

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            if (isProgress)
            {
                sp.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24 });
            }
            sp.Children.Add(new TextBlock { Text = message, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });

            _statusDialog.Content = sp;
            _statusDialog.CloseButtonText = isProgress ? "" : "确定";

            if (!_isDialogOpen)
            {
                _isDialogOpen = true;
                try { await _statusDialog.ShowAsync(); }
                catch { _isDialogOpen = false; }
            }
        });
    }

    private void RenderQrCode(string url)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            using (QRCodeGenerator qrGenerator = new())
            {
                QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.L);
                PngByteQRCode qrCode = new(qrCodeData);
                byte[] qrCodeImageBytes = qrCode.GetGraphic(10);

                using (var stream = new MemoryStream(qrCodeImageBytes))
                {
                    BitmapImage bitmapImage = new();
                    stream.Position = 0;
                    bitmapImage.SetSource(stream.AsRandomAccessStream());

                    QrCodeImage.Opacity = 0;
                    QrCodeImage.Source = bitmapImage;
                    QrCodeFadeInStoryboard.Begin();
                }
            }
        });
    }
    #endregion
    
    #region 通行证网页登录

    private async Task StartWebPassportLoginAsync()
    {
        if (_pollingCts != null)
        {
            _pollingCts.Cancel();
        }

        UpdateStatus("正在加载通行证登录页面...", true);

        try
        {
            await PassportWebView.EnsureCoreWebView2Async();
            
            PassportWebView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
            
            PassportWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            
            PassportWebView.CoreWebView2.ContextMenuRequested -= CoreWebView2_ContextMenuRequested;
            PassportWebView.CoreWebView2.ContextMenuRequested += CoreWebView2_ContextMenuRequested;
            
            PassportWebView.CoreWebView2.Stop();
            
            PassportWebView.CoreWebView2.CookieManager.DeleteAllCookies();

            try
            {
                await PassportWebView.CoreWebView2.Profile.ClearBrowsingDataAsync();
            }
            catch
            {
                // ignored
            }

            PassportWebView.CoreWebView2.Navigate("about:blank");

            PassportWebView.CoreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived;
            PassportWebView.CoreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
        
            PassportWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            PassportWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
            
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string url = $"{ApiEndpoints.UserMihoyoLoginPlatformUrl}?app_id=dw9y09jqjpxc&theme=passport&token_type=4&game_biz=plat_cn&ux_mode=popup&iframe_level=1&t={timestamp}#/login";
            await Task.Delay(100);
            PassportWebView.CoreWebView2.Navigate(url);

            UpdateStatus("请在网页中完成登录验证", false, true);
        }
        catch (Exception ex)
        {
            UpdateStatus($"加载通行证网页失败: {ex.Message}", false);
        }
    }
    
    private void CoreWebView2_ContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var allowedItems = new HashSet<string> 
        { 
            "selectAll", 
            "copy", 
            "cut", 
            "paste" 
        };
        
        for (int i = e.MenuItems.Count - 1; i >= 0; i--)
        {
            if (!allowedItems.Contains(e.MenuItems[i].Name))
            {
                e.MenuItems.RemoveAt(i);
            }
        }
    }
    
    private async void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        string script = @"
        document.body.style.overflow = 'hidden';
        document.documentElement.style.overflow = 'hidden';
        document.body.style.width = '100vw';
        document.body.style.height = '100vh';
        document.body.style.margin = '0';
        document.body.style.padding = '0';
    ";
        await PassportWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

private async void CoreWebView2_WebResourceResponseReceived(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
{
    string uri = e.Request.Uri;
    
    if (uri.Contains("/ma-cn-passport/web/loginByPassword") || 
        uri.Contains("/ma-cn-passport/web/loginByMobileCaptcha") || 
        uri.Contains("/ma-cn-passport/web/queryQRLoginStatus"))
    {
        if (e.Response.StatusCode == 200)
        {
            var cookies = await PassportWebView.CoreWebView2.CookieManager.GetCookiesAsync("https://mihoyo.com");
            var cookieDict = new Dictionary<string, string>();
            
            foreach (var cookie in cookies)
            {
                cookieDict[cookie.Name] = cookie.Value;
            }
            
            if (cookieDict.ContainsKey("cookie_token") || cookieDict.ContainsKey("cookie_token_v2"))
            {
                PassportWebView.CoreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived;
                
                DispatcherQueue.TryEnqueue(() =>
                {
                    UpdateStatus("凭证提取成功，正在保存", true);
                    SaveCredentials(cookieDict);
                });
            }
        }
    }
}

#endregion
}