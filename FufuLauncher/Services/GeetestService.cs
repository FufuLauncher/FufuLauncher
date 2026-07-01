// Copyright (c) FufuLauncher Dev Team. All rights reserved.
// By kyxsan.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services.MiHoYo;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace FufuLauncher.Services;

public sealed class GeetestService
{
    private const string CreateVerificationUrl = "https://api-takumi-record.mihoyo.com/game_record/app/card/wapi/createVerification?is_high=true";
    private const string VerifyVerificationUrl = "https://api-takumi-record.mihoyo.com/game_record/app/card/wapi/verifyVerification";
    private const string DailyNoteChallengePath = "/game_record/app/genshin/api/dailyNote";

    private const string CNVersion = "2.109.0";
    private const string CNX4 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string Referer = "https://webstatic.mihoyo.com";

    private static string MobileUserAgent => DailyNoteService.GetCurrentUserAgent();

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly IDeviceFingerprintService _fingerprintService;
    private readonly AccountManager _accountManager;

    public GeetestService()
    {
        _fingerprintService = App.GetService<IDeviceFingerprintService>()
            ?? throw new InvalidOperationException("无法获取设备指纹服务");
        _accountManager = App.GetService<AccountManager>()
            ?? throw new InvalidOperationException("无法获取账号管理服务");
    }

    public async Task<string> TryVerifyForDailyNoteAsync(Dictionary<string, string> cookies)
    {
        string createJson = await CallCreateVerificationAsync(cookies);
        string gt = null;
        string challenge = null;

        using (JsonDocument doc = JsonDocument.Parse(createJson))
        {
            int retcode = doc.RootElement.TryGetProperty("retcode", out JsonElement rc) ? rc.GetInt32() : -1;
            if (retcode != 0 || !doc.RootElement.TryGetProperty("data", out JsonElement data))
            {
                Debug.WriteLine($"[GeetestService] TryVerifyForDailyNote: createVerification 失败 retcode={retcode}");
                return null;
            }

            gt = data.TryGetProperty("gt", out JsonElement gtProp) ? gtProp.GetString() : null;
            challenge = data.TryGetProperty("challenge", out JsonElement chProp) ? chProp.GetString() : null;
            Debug.WriteLine($"[GeetestService] TryVerifyForDailyNote: createVerification 成功 gt={gt}, challenge={challenge}");
        }

        if (string.IsNullOrEmpty(gt) || string.IsNullOrEmpty(challenge))
            return null;

        GeetestResult result = await ShowGeetestWebViewAsync(gt, challenge);
        if (result == null || string.IsNullOrEmpty(result.Validate))
        {
            Debug.WriteLine($"[GeetestService] TryVerifyForDailyNote: 用户未完成验证 (result=null) 或 validate 为空");
            return null;
        }
        Debug.WriteLine($"[GeetestService] TryVerifyForDailyNote: 验证码完成 challenge={result.Challenge}, validate={result.Validate}");

        string verifyJson = await CallVerifyVerificationAsync(cookies, result.Challenge, result.Validate);
        using (JsonDocument doc = JsonDocument.Parse(verifyJson))
        {
            int retcode = doc.RootElement.TryGetProperty("retcode", out JsonElement rc) ? rc.GetInt32() : -1;
            if (retcode != 0 || !doc.RootElement.TryGetProperty("data", out JsonElement data))
            {
                Debug.WriteLine($"[GeetestService] TryVerifyForDailyNote: verifyVerification 失败 retcode={retcode}");
                return null;
            }

            string finalChallenge = data.TryGetProperty("challenge", out JsonElement chProp) ? chProp.GetString() : null;
            Debug.WriteLine($"[GeetestService] TryVerifyForDailyNote: verifyVerification 成功 xrpc_challenge={finalChallenge}");
            return finalChallenge;
        }
    }

    private async Task<string> CallCreateVerificationAsync(Dictionary<string, string> cookies)
    {
        string activeId = _accountManager.ActiveAccountId ?? throw new InvalidOperationException("无活跃账号");
        string cookieStr = DailyNoteService.BuildCookieString(cookies, DailyNoteService.CookieMode.Cookie);
        string ds = DailyNoteService.CalculateDS2(CNX4, "is_high=true", "");
        string fp = await _fingerprintService.GetOrRegisterFingerprintAsync(activeId, cookies);
        Debug.WriteLine($"[GeetestService] CallCreateVerification: device_fp={fp}");

        using HttpRequestMessage req = new(HttpMethod.Get, CreateVerificationUrl);
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", CNVersion);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", DailyNoteService.GetGameRecordDeviceId());
        req.Headers.Add("x-rpc-device_fp", fp);
        req.Headers.Add("x-rpc-challenge_game", "2");
        req.Headers.Add("x-rpc-challenge_path", DailyNoteChallengePath);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.UserAgent.ParseAdd(MobileUserAgent);

        HttpResponseMessage resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> CallVerifyVerificationAsync(Dictionary<string, string> cookies, string challenge, string validate)
    {
        string activeId = _accountManager.ActiveAccountId ?? throw new InvalidOperationException("无活跃账号");
        string cookieStr = DailyNoteService.BuildCookieString(cookies, DailyNoteService.CookieMode.Cookie);
        GeetestWebResponse body = new()
        {
            Challenge = challenge,
            Validate = validate,
            Seccode = $"{validate}|jordan"
        };
        string bodyJson = JsonSerializer.Serialize(body);
        string ds = DailyNoteService.CalculateDS2(CNX4, "", bodyJson);
        string fp = await _fingerprintService.GetOrRegisterFingerprintAsync(activeId, cookies);
        Debug.WriteLine($"[GeetestService] CallVerifyVerification: device_fp={fp}");

        using HttpRequestMessage req = new(HttpMethod.Post, VerifyVerificationUrl);
        req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", CNVersion);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", DailyNoteService.GetGameRecordDeviceId());
        req.Headers.Add("x-rpc-device_fp", fp);
        req.Headers.Add("x-rpc-challenge_game", "2");
        req.Headers.Add("x-rpc-challenge_path", DailyNoteChallengePath);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", Referer);
        req.Headers.UserAgent.ParseAdd(MobileUserAgent);

        HttpResponseMessage resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    // ShowGeetestWebViewAsync 保持不变
    private static async Task<GeetestResult> ShowGeetestWebViewAsync(string gt, string challenge)
    {
        TaskCompletionSource<GeetestResult> tcs = new();

        App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            Window geetestWindow = new();
            geetestWindow.SystemBackdrop = new MicaBackdrop();
            geetestWindow.Title = "人机验证";

            Grid rootGrid = new() { Background = new SolidColorBrush(Colors.Transparent) };
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Grid titleBar = new() { Height = 32 };
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Image icon = new()
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/WindowIcon.ico")),
                Height = 16,
                Width = 16,
                Margin = new Thickness(16, 0, 12, 0)
            };
            Grid.SetColumn(icon, 0);
            titleBar.Children.Add(icon);

            TextBlock titleText = new()
            {
                Text = "人机验证",
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
            };
            Grid.SetColumn(titleText, 1);
            titleBar.Children.Add(titleText);

            Grid.SetRow(titleBar, 0);
            rootGrid.Children.Add(titleBar);

            WebView2 webView = new()
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Grid.SetRow(webView, 1);
            rootGrid.Children.Add(webView);

            geetestWindow.Content = rootGrid;

            AppWindow appWindow = geetestWindow.AppWindow;
            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            appWindow.Resize(new SizeInt32(1270, 720));

            AppWindow mainAppWindow = App.MainWindow.AppWindow;
            PointInt32 mainPos = mainAppWindow.Position;
            SizeInt32 mainSize = mainAppWindow.Size;
            appWindow.Move(new PointInt32(
                mainPos.X + (mainSize.Width - 400) / 2,
                mainPos.Y + (mainSize.Height - 450) / 2));

            geetestWindow.SetTitleBar(titleBar);

            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

            webView.CoreWebView2.WebMessageReceived += (s, e) =>
            {
                try
                {
                    string msg = e.WebMessageAsJson;
                    GeetestResult result = JsonSerializer.Deserialize<GeetestResult>(msg);
                    tcs.TrySetResult(result);
                    geetestWindow.Close();
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            };

            geetestWindow.Closed += (s, e) =>
            {
                tcs.TrySetResult(null);
            };

            string html = GetGeetestHtml(gt, challenge);
            webView.NavigateToString(html);
            geetestWindow.Activate();
        });

        return await tcs.Task;
    }

    private static string GetGeetestHtml(string gt, string challenge)
    {
        return $$"""
            <html>
                <head>
                    <meta charset="utf-8"/>
                    <title>人机验证</title>
                    <style>
                        * { margin:0; padding:0; box-sizing:border-box; }
                        body {
                            background: transparent;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            height: 100vh;
                            font-family: 'Segoe UI', sans-serif;
                        }
                        #geetest-div { }
                    </style>
                </head>
                <body>
                    <div id="geetest-div"></div>
                </body>
                <script src="https://static.geetest.com/static/js/gt.0.5.2.js"></script>
                <script>
                    initGeetest(
                        {
                            protocol: "https://",
                            gt: "{{gt}}",
                            challenge: "{{challenge}}",
                            new_captcha: true,
                            product: 'bind',
                            api_server: 'api.geetest.com'
                        },
                        function (captchaObj) {
                            captchaObj.onReady(function () {
                                captchaObj.verify();
                            });
                            captchaObj.onSuccess(function () {
                                var result = captchaObj.getValidate();
                                chrome.webview.postMessage(result);
                            });
                        }
                    );
                </script>
            </html>
            """;
    }

    private sealed class GeetestWebResponse
    {
        [JsonPropertyName("geetest_challenge")]
        public string Challenge
        {
            get; set;
        }

        [JsonPropertyName("geetest_validate")]
        public string Validate
        {
            get; set;
        }

        [JsonPropertyName("geetest_seccode")]
        public string Seccode
        {
            get; set;
        }
    }
}

public sealed class GeetestResult
{
    [JsonPropertyName("geetest_challenge")]
    public string Challenge
    {
        get; set;
    }

    [JsonPropertyName("geetest_validate")]
    public string Validate
    {
        get; set;
    }
}