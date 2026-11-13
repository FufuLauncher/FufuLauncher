/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Models.MiHoYo.Passport;
using FufuLauncher.Services.MiHoYo;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Services.MiHoYo.Transport;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using FufuLauncher.Constants;

namespace FufuLauncher.Services;

public sealed class GeetestService
{
    private const string CreateVerificationUrl = ApiEndpoints.GeetestCreateVerificationUrl;

    private const string VerifyVerificationUrl = ApiEndpoints.GeetestVerifyVerificationUrl;

    private const string DailyNoteChallengePath = ApiEndpoints.DailyNoteChallengePath;

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public GeetestService()
    {
    }


    public async Task<string> TryVerifyForDailyNoteAsync(AccountContext ctx, string? challengePath = null)
    {
        string path = string.IsNullOrWhiteSpace(challengePath) ? DailyNoteChallengePath : challengePath;
        bool showNotice = await ReadCaptchaNoticeSettingAsync();

        string createJson = await CallCreateVerificationAsync(ctx, path);
        string gt = null;
        string challenge = null;

        using (JsonDocument doc = JsonDocument.Parse(createJson))
        {
            int retcode = doc.RootElement.TryGetProperty("retcode", out JsonElement rc) ? rc.GetInt32() : -1;
            if (retcode != 0 || !doc.RootElement.TryGetProperty("data", out JsonElement data))
            {
                DebugLog.Info("GeetestService",
                    $"[GeetestService] TryVerifyForDailyNote: createVerification failed retcode={retcode}");
                return null;
            }

            gt = data.TryGetProperty("gt", out JsonElement gtProp) ? gtProp.GetString() : null;
            challenge = data.TryGetProperty("challenge", out JsonElement chProp) ? chProp.GetString() : null;
            DebugLog.Info("GeetestService",
                $"[GeetestService] TryVerifyForDailyNote: createVerification succeeded gt={gt}, challenge={challenge}");
        }

        if (string.IsNullOrEmpty(gt) || string.IsNullOrEmpty(challenge))
            return null;

        GeetestResult result = await ShowGeetestWebViewAsync(gt, challenge, isOversea: false, showNotice: showNotice);
        if (result == null || string.IsNullOrEmpty(result.Validate))
        {
            DebugLog.Info("GeetestService",
                $"[GeetestService] TryVerifyForDailyNote: user did not complete verification (result=null) or validate is empty");
            return null;
        }

        DebugLog.Info("GeetestService",
            $"[GeetestService] TryVerifyForDailyNote: captcha completed challenge={result.Challenge}, validate={result.Validate}");

        string verifyJson = await CallVerifyVerificationAsync(ctx, result.Challenge, result.Validate, path);
        using (JsonDocument doc = JsonDocument.Parse(verifyJson))
        {
            int retcode = doc.RootElement.TryGetProperty("retcode", out JsonElement rc) ? rc.GetInt32() : -1;
            if (retcode != 0 || !doc.RootElement.TryGetProperty("data", out JsonElement data))
            {
                DebugLog.Info("GeetestService",
                    $"[GeetestService] TryVerifyForDailyNote: verifyVerification failed retcode={retcode}");
                return null;
            }

            string finalChallenge =
                data.TryGetProperty("challenge", out JsonElement chProp) ? chProp.GetString() : null;
            DebugLog.Info("GeetestService",
                $"[GeetestService] TryVerifyForDailyNote: verifyVerification succeeded xrpc_challenge={finalChallenge}");
            return finalChallenge;
        }
    }

    private static async Task<bool> ReadCaptchaNoticeSettingAsync()
    {
        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var noticeJson = await localSettingsService.ReadSettingAsync("IsCaptchaNoticeEnabled");
            return noticeJson == null || Convert.ToBoolean(noticeJson);
        }
        catch (Exception ex)
        {
            DebugLog.Info("GeetestService",
                $"[GeetestService] Failed to read captcha dialog description setting, using default display: {ex.Message}");
            return true;
        }
    }

    private async Task<string> CallCreateVerificationAsync(AccountContext ctx, string challengePath)
    {
        string cookieStr = BbsRequestBuilder.BuildCookieString(ctx.Cookies, BbsRequestBuilder.CookieMode.Cookie);
        string ds = MiHoYoHeaderFactory.CalculateDs2(HeaderSalts.CnX4, "is_high=true", "");
        DebugLog.Info("GeetestService", $"[GeetestService] CallCreateVerification: device_fp={ctx.Device.DeviceFp}");

        using HttpRequestMessage req = new(HttpMethod.Get, CreateVerificationUrl);
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", HeaderVersions.MobileCnLogin);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", ctx.Device.BbsDeviceId);
        req.Headers.Add("x-rpc-device_fp", ctx.Device.DeviceFp);
        req.Headers.Add("x-rpc-device_name", Uri.EscapeDataString(ctx.Device.DeviceName));
        req.Headers.Add("x-rpc-sys_version", ctx.Device.SysVersion);
        req.Headers.Add("x-rpc-challenge_game", "2");
        req.Headers.Add("x-rpc-challenge_path", challengePath);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", UserAgents.WebstaticReferer);
        req.Headers.UserAgent.ParseAdd(ctx.UserAgent.Mobile);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> CallVerifyVerificationAsync(AccountContext ctx, string challenge, string validate,
        string challengePath)
    {
        string cookieStr = BbsRequestBuilder.BuildCookieString(ctx.Cookies, BbsRequestBuilder.CookieMode.Cookie);
        GeetestWebResponse body = new()
        {
            Challenge = challenge,
            Validate = validate,
            Seccode = $"{validate}|jordan"
        };
        string bodyJson = JsonSerializer.Serialize(body);
        string ds = MiHoYoHeaderFactory.CalculateDs2(HeaderSalts.CnX4, "", bodyJson);
        DebugLog.Info("GeetestService", $"[GeetestService] CallVerifyVerification: device_fp={ctx.Device.DeviceFp}");

        using HttpRequestMessage req = new(HttpMethod.Post, VerifyVerificationUrl);
        req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        req.Headers.Add("Cookie", cookieStr);
        req.Headers.Add("x-rpc-app_version", HeaderVersions.MobileCnLogin);
        req.Headers.Add("x-rpc-client_type", "5");
        req.Headers.Add("x-rpc-device_id", ctx.Device.BbsDeviceId);
        req.Headers.Add("x-rpc-device_fp", ctx.Device.DeviceFp);
        req.Headers.Add("x-rpc-device_name", Uri.EscapeDataString(ctx.Device.DeviceName));
        req.Headers.Add("x-rpc-sys_version", ctx.Device.SysVersion);
        req.Headers.Add("x-rpc-challenge_game", "2");
        req.Headers.Add("x-rpc-challenge_path", challengePath);
        req.Headers.Add("DS", ds);
        req.Headers.Add("Referer", UserAgents.WebstaticReferer);
        req.Headers.UserAgent.ParseAdd(ctx.UserAgent.Mobile);

        using HttpResponseMessage resp = await _httpClient.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    private static async Task<GeetestResult> ShowGeetestWebViewAsync(string gt, string challenge, bool isOversea,
        string? apiServerOverride = null, bool showNotice = false)
    {
        string apiServer = apiServerOverride ??
                           (isOversea ? ApiEndpoints.GeetestOverseaApiServer : ApiEndpoints.GeetestCnApiServer);
        TaskCompletionSource<GeetestResult> tcs = new();

        if (App.MainWindow == null)
        {
            DebugLog.Info("GeetestService", "[GeetestService] App.MainWindow is null, cannot show captcha window");
            tcs.TrySetResult(null);
            return await tcs.Task;
        }

        App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                Window geetestWindow = new();
                geetestWindow.SystemBackdrop = new MicaBackdrop();
                geetestWindow.Title = "Geetest_CaptchaTitle".GetLocalized();

                Grid rootGrid = new() { Background = new SolidColorBrush(Colors.Transparent) };
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
                rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid titleBar = new() { Height = 32 };
                titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Image icon = new()
                {
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri("ms-appx:///Assets/WindowIcon.ico")),
                    Height = 16,
                    Width = 16,
                    Margin = new Thickness(16, 0, 12, 0)
                };
                Grid.SetColumn(icon, 0);
                titleBar.Children.Add(icon);

                TextBlock titleText = new()
                {
                    Text = "Geetest_CaptchaTitle".GetLocalized(),
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
                if (appWindow != null)
                {
                    appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
                    appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                    appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                    appWindow.Resize(new SizeInt32(1270, 720));

                    AppWindow mainAppWindow = App.MainWindow.AppWindow;
                    if (mainAppWindow != null)
                    {
                        PointInt32 mainPos = mainAppWindow.Position;
                        SizeInt32 mainSize = mainAppWindow.Size;
                        appWindow.Move(new PointInt32(
                            mainPos.X + (mainSize.Width - 400) / 2,
                            mainPos.Y + (mainSize.Height - 450) / 2));
                    }
                }

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

                string html = GetGeetestHtml(gt, challenge, apiServer, showNotice);
                webView.NavigateToString(html);
                geetestWindow.Activate();
                try
                {
                    geetestWindow.AppWindow.MoveInZOrderAtTop();
                }
                catch (Exception ex)
                {
                    DebugLog.Info("GeetestService", $"[GeetestService] MoveInZOrderAtTop failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("GeetestService", $"[GeetestService] Failed to create captcha window: {ex.Message}");
                tcs.TrySetResult(null);
            }
        });

        return await tcs.Task;
    }

    private static string GetGeetestHtml(string gt, string challenge, string apiServer, bool showNotice)
    {
        var captchaTitle = "Geetest_CaptchaTitle".GetLocalized();

        string noticeStyle = string.Empty;
        string noticeMarkup = string.Empty;
        if (showNotice)
        {
            noticeStyle = """
                              .captcha-notice, .captcha-notice * {
                                  pointer-events: none;
                                  user-select: none;
                                  -webkit-user-select: none;
                              }
                              .captcha-notice {
                                  position: fixed;
                                  inset: 0;
                                  z-index: -1;
                                  overflow: hidden;
                                  display: flex;
                                  align-items: center;
                                  justify-content: center;
                              }
                              .captcha-notice .notice-watermark {
                                  font-size: clamp(44px, 7.5vw, 118px);
                                  font-weight: 900;
                                  letter-spacing: .06em;
                                  white-space: nowrap;
                                  color: transparent;
                                  -webkit-text-stroke: 2px rgba(128, 128, 128, .35);
                              }
                              .captcha-notice .notice-card {
                                  position: fixed;
                                  top: 16px;
                                  left: 50%;
                                  transform: translateX(-50%);
                                  width: min(1080px, 94vw);
                                  padding: 12px 26px 14px;
                                  border: 1px solid rgba(150, 150, 150, .55);
                                  border-radius: 14px;
                                  background: rgba(28, 28, 30, .82);
                                  text-align: center;
                                  color: #ffffff;
                              }
                              .captcha-notice .notice-card-title {
                                  font-size: clamp(20px, 1.9vw, 30px);
                                  font-weight: 800;
                                  letter-spacing: .02em;
                                  color: #ffffff;
                                  margin-bottom: 6px;
                              }
                              .captcha-notice .notice-card-line {
                                  font-size: clamp(14px, 1.15vw, 19px);
                                  line-height: 1.6;
                                  color: rgba(255, 255, 255, .88);
                              }
                          """;

            string watermark = "CaptchaNoticeWatermark".GetLocalized();
            string noticeTitle = "CaptchaNoticeTitle".GetLocalized();
            string noticeLine1 = "CaptchaNoticeLine1".GetLocalized();
            string noticeLine2 = "CaptchaNoticeLine2".GetLocalized();

            noticeMarkup = $"""
                                <div class="captcha-notice">
                                    <div class="notice-watermark">{watermark}</div>
                                    <div class="notice-card">
                                        <div class="notice-card-title">{noticeTitle}</div>
                                        <div class="notice-card-line">{noticeLine1}</div>
                                        <div class="notice-card-line">{noticeLine2}</div>
                                    </div>
                                </div>
                            """;
        }

        return $$"""
                 <html>
                     <head>
                         <meta charset="utf-8"/>
                         <title>{{captchaTitle}}</title>
                         <style>
                             * { margin:0; padding:0; box-sizing:border-box; }
                             body {
                                 background: transparent;
                                 display: flex;
                                 align-items: center;
                                 justify-content: center;
                                 height: 100vh;
                                 overflow: hidden;
                                 font-family: 'Segoe UI', 'Microsoft YaHei UI', sans-serif;
                             }
                             #geetest-div { }
                 {{noticeStyle}}
                         </style>
                     </head>
                     <body>
                 {{noticeMarkup}}
                         <div id="geetest-div"></div>
                     </body>
                     <script src="{{ApiEndpoints.GeetestScriptV052Url}}"></script>
                     <script>
                         initGeetest(
                             {
                                 protocol: "https://",
                                 gt: "{{gt}}",
                                 challenge: "{{challenge}}",
                                 new_captcha: true,
                                 product: 'bind',
                                 api_server: '{{apiServer}}'
                             },
                             function (captchaObj) {
                                 captchaObj.onReady(function () {
                                     captchaObj.verify();
                                 });
                                 captchaObj.onSuccess(function () {
                                     var result = captchaObj.getValidate();
                                     chrome.webview.postMessage(result);
                                 });
                                 captchaObj.onError(function () {
                                     chrome.webview.postMessage('{"geetest_error":1}');
                                 });
                             }
                         );
                     </script>
                 </html>
                 """;
    }

    public async Task<bool> TryVerifyAigisSessionAsync(IAigisProvider provider, string? rawSession, bool isOversea)
    {
        if (string.IsNullOrEmpty(rawSession))
        {
            return false;
        }

        DebugLog.Info("GeetestService",
            $"[GeetestService] TryVerifyAigisSession: rawSession={Truncate(rawSession, 500)}");

        AigisSession? session = Deserialize<AigisSession>(rawSession);
        if (session is null || string.IsNullOrEmpty(session.SessionId))
        {
            DebugLog.Info("GeetestService", "[GeetestService] TryVerifyAigisSession: Aigis session parse failed");
            return false;
        }

        GeetestVerification? verification = session.Data.ValueKind switch
        {
            JsonValueKind.String => Deserialize<GeetestVerification>(session.Data.GetString() ?? string.Empty),
            JsonValueKind.Object => DeserializeElement<GeetestVerification>(session.Data),
            _ => null
        };
        if (verification is null || string.IsNullOrEmpty(verification.Gt) ||
            string.IsNullOrEmpty(verification.Challenge))
        {
            DebugLog.Info("GeetestService",
                $"[GeetestService] TryVerifyAigisSession: failed to parse Geetest params data={Truncate(session.Data.ToString(), 500)}");
            return false;
        }

        DebugLog.Info("GeetestService",
            $"[GeetestService] TryVerifyAigisSession: gt={Truncate(verification.Gt, 32)}, challenge={Truncate(verification.Challenge, 32)}, isOversea={isOversea}");

        GeetestResult? result = await ShowGeetestWebViewAsync(verification.Gt, verification.Challenge, isOversea);

        if (isOversea && (result is null || (result.Error == 1 && string.IsNullOrEmpty(result.Validate))))
        {
            DebugLog.Info("GeetestService",
                $"[GeetestService] TryVerifyAigisSession: api-na load failed, falling back to {ApiEndpoints.GeetestCnApiServer}");
            result = await ShowGeetestWebViewAsync(verification.Gt, verification.Challenge, isOversea,
                apiServerOverride: ApiEndpoints.GeetestCnApiServer);
        }

        if (result is null || string.IsNullOrEmpty(result.Validate))
        {
            DebugLog.Info("GeetestService", "[GeetestService] TryVerifyAigisSession: Geetest not completed");
            return false;
        }

        var webResponse = new GeetestWebResponse
        {
            Challenge = result.Challenge,
            Validate = result.Validate,
            Seccode = string.IsNullOrEmpty(result.Seccode) ? $"{result.Validate}|jordan" : result.Seccode,
        };

        provider.Aigis =
            $"{session.SessionId};{Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(webResponse))}";
        return true;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static T? DeserializeElement<T>(JsonElement element)
    {
        try
        {
            return element.Deserialize<T>();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed class GeetestWebResponse
    {
        [JsonPropertyName("geetest_challenge")]
        public string Challenge
        {
            get;
            set;
        }

        [JsonPropertyName("geetest_validate")]
        public string Validate
        {
            get;
            set;
        }

        [JsonPropertyName("geetest_seccode")]
        public string Seccode
        {
            get;
            set;
        }
    }
}

public sealed class GeetestResult
{
    [JsonPropertyName("geetest_challenge")]
    public string Challenge
    {
        get;
        set;
    }

    [JsonPropertyName("geetest_validate")]
    public string Validate
    {
        get;
        set;
    }

    [JsonPropertyName("geetest_seccode")]
    public string Seccode
    {
        get;
        set;
    }

    [JsonPropertyName("geetest_error")]
    public int Error
    {
        get;
        set;
    }
}