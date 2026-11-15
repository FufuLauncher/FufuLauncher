/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.MiHoYo.Networking;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow : Window
{
    #region 字段、常量、构造函数

    private const string Salt = "dDIQHbKOdaPaLuvQKVzUzqdeCaxjtaPV";
    private const string SaltGame = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    private readonly string _gameDeviceId = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();
    private HttpClient _httpClient;

    public bool DidLoginSucceed() => IsLoginSuccessful;

    private string _gameAppId = "7";
    private LoginSession _currentSession;

    private TaskCompletionSource<(Dictionary<string, string> Cookies, string ServerType)>? _loginTcs;
    private ContentDialog _statusDialog;
    private bool _isDialogOpen;
    private bool _isLoginCompleting;

    public bool IsLoginSuccessful
    {
        get;
        private set;
    }

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public LoginQrWindow()
    {
        var handler = new HttpClientHandler { UseCookies = false };
        _httpClient = new HttpClient(handler);

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (Content is FrameworkElement rootContent)
        {
            rootContent.Loaded += RootContent_Loaded;
        }

        Closed += LoginQrWindow_Closed;
    }

    #endregion


    #region 窗口生命周期

    private async void RootContent_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement rootContent)
        {
            rootContent.Loaded -= RootContent_Loaded;
        }

        await StartLoginFlowAsync(false);
    }

    private void LoginQrWindow_Closed(object sender, WindowEventArgs args)
    {
        _loginTcs?.TrySetCanceled();

        _currentSession?.Cancel();


        _httpClient?.Dispose();

        if (PassportWebView != null && PassportWebView.CoreWebView2 != null)
        {
            PassportWebView.CoreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived;
            PassportWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            PassportWebView.CoreWebView2.Stop();
            PassportWebView.Close();
        }
    }

    #endregion


    #region 登录流程控制

    private async Task RestartLoginFlowAsync(bool isGameLogin = false)
    {
        _currentSession?.Cancel();

        var session = new LoginSession
        {
            Type = isGameLogin ? LoginType.GameQr : LoginType.AppQr,
            GameAppId = _gameAppId,
            GameDevice = Guid.NewGuid().ToString("N")
        };
        _currentSession = session;

        if (isGameLogin)
            await StartGameLoginFlowAsync(session);
        else
            await StartAppLoginFlowAsync(session);
    }

    private async Task StartLoginFlowAsync(bool isGameLogin = false)
    {
        var session = _currentSession;
        if (session == null)
        {
            session = new LoginSession
            {
                Type = isGameLogin ? LoginType.GameQr : LoginType.AppQr,
                GameAppId = _gameAppId,
                GameDevice = Guid.NewGuid().ToString("N")
            };
            _currentSession = session;
        }

        if (isGameLogin)
        {
            await StartGameLoginFlowAsync(session);
        }
        else if (LoginMethodComboBox.SelectedIndex == 0)
        {
            await StartAppLoginFlowAsync(session);
        }
    }

    private void OnLoginSuccess(Dictionary<string, string> cookies, string serverType)
    {
        _isLoginCompleting = true;
        IsLoginSuccessful = true;
        _currentSession?.Cancel();
        UpdateStatus("", false, true);

        var tcs = _loginTcs;
        if (tcs == null)
            return;

        tcs.TrySetResult((cookies, serverType));
        DispatcherQueue.TryEnqueue(() => Close());
    }

    private void OnLoginFailed(Exception? ex = null)
    {
        var tcs = _loginTcs;
        if (tcs == null)
            return;

        if (ex != null)
            tcs.TrySetException(ex);
        else
            tcs.TrySetCanceled();

        DispatcherQueue.TryEnqueue(() => Close());
    }

    #endregion


    #region 公共

    public Task<(Dictionary<string, string> Cookies, string ServerType)> ShowAndWaitAsync()
    {
        _loginTcs?.TrySetCanceled();
        _loginTcs = new TaskCompletionSource<(Dictionary<string, string>, string)>();
        this.Activate();
        return _loginTcs.Task;
    }

    private void AddCommonHeaders(HttpRequestMessage request, DeviceIdentity device, string body, string query, string clientType,
        string appId, string sdkVersion, string cookie = "", string referer = "")
    {
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 miHoYoBBS/2.90.1 Capture/2.2.0");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-cn");

        if (!string.IsNullOrEmpty(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);
        if (!string.IsNullOrEmpty(referer)) request.Headers.TryAddWithoutValidation("Referer", referer);

        request.Headers.TryAddWithoutValidation("x-rpc-client_type", clientType);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.90.1");
        MiHoYoHeaderFactory.ApplyDeviceIdentityHeaders(request, device, device.DeviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", appId);
        request.Headers.TryAddWithoutValidation("x-rpc-sdk_version", sdkVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-account_version", "2.90.1");

        request.Headers.TryAddWithoutValidation("DS", GenerateDS(body, query));
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

    private Dictionary<string, string> ParseCookieString(string cookieString)
    {
        var dict = new Dictionary<string, string>();
        foreach (var part in cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            var idx = trimmed.IndexOf('=');
            if (idx > 0)
            {
                var key = trimmed.Substring(0, idx).Trim();
                var value = trimmed.Substring(idx + 1).Trim();
                if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                    dict[key] = value;
            }
        }

        return dict;
    }

    #endregion


    #region 状态对话框管理

    private void UpdateStatus(string message, bool isProgress = false, bool closeDialog = false)
    {
        if (closeDialog && DispatcherQueue.HasThreadAccess)
        {
            CloseStatusDialog();
            return;
        }

        DispatcherQueue.TryEnqueue(async () =>
        {
            if (closeDialog)
            {
                CloseStatusDialog();
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

            sp.Children.Add(new TextBlock
                { Text = message, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });

            _statusDialog.Content = sp;
            _statusDialog.CloseButtonText = isProgress ? "" : "确定";

            if (!_isDialogOpen)
            {
                _isDialogOpen = true;
                try
                {
                    await _statusDialog.ShowAsync();
                }
                catch
                {
                    _isDialogOpen = false;
                }
            }
        });
    }

    private void CloseStatusDialog()
    {
        if (_isDialogOpen && _statusDialog != null)
        {
            _statusDialog.Hide();
            _isDialogOpen = false;
        }
    }

    #endregion


    #region 二维码渲染

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


    #region 二维码自动刷新

    private static readonly TimeSpan QrAutoRefreshInterval = TimeSpan.FromSeconds(60);
    private const int MaxQrAutoRefreshCount = 5;

    private bool IsSessionActive(LoginSession session)
        => ReferenceEquals(_currentSession, session) && !session.Cts.IsCancellationRequested;

    private bool IsQrAutoRefreshDue(LoginSession session)
        => session.QrCreatedAt != default
           && DateTimeOffset.UtcNow - session.QrCreatedAt >= QrAutoRefreshInterval;

    private void SetQrHint(string resourceKey)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (QrRefreshHintTextBlock == null) return;
            QrRefreshHintTextBlock.Text = resourceKey.GetLocalized();
        });
    }

    #endregion
}
