using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services.Miyoushe;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

internal sealed class MiyousheVerificationWindow : Window
{
    private const string DocumentSource = "about:blank";

    private readonly WebView2 _web = new();

    private readonly TaskCompletionSource<JsonElement?> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly JsonElement _parameters;
    private readonly string? _session;
    private readonly string _nonce = Guid.NewGuid().ToString("N");
    private readonly CancellationToken _cancellation;
    private CancellationTokenRegistration _registration;

    private MiyousheVerificationWindow(JsonElement parameters, string? session, CancellationToken ct)
    {
        _parameters = parameters;
        _session = session;
        _cancellation = ct;
        Title = "Miyoushe_Verify".GetLocalized();
        SystemBackdrop = new MicaBackdrop();
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var hint = new TextBlock
        {
            Text = "Miyoushe_VerifyHint".GetLocalized(), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 16, 20, 12)
        };
        root.Children.Add(hint);
        Grid.SetRow(_web, 1);
        root.Children.Add(_web);
        Content = root;
        WindowManagerHelper.ResizeWithDpi(AppWindow, this, 480, 550);
        WindowManagerHelper.CenterWindowOnScreen(AppWindow, 480, 550);
        _web.Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _registration.Dispose();
            _result.TrySetResult(null);
            _web.Close();
        };
    }

    public static async Task<JsonElement?> ShowAsync(JsonElement parameters, string? session, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var window = new MiyousheVerificationWindow(parameters.Clone(), session, ct);
        window.Activate();
        window._registration = ct.Register(() => window.DispatcherQueue.TryEnqueue(() => window.Close()));
        return await window._result.Task;
    }

    internal static Task<JsonElement?> ShowOnMainThreadAsync(JsonElement parameters, string? session)
    {
        var completion = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    completion.TrySetResult(await ShowAsync(parameters, session, CancellationToken.None));
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }) != true)
            completion.TrySetException(new InvalidOperationException("Miyoushe_VerifyLoadError".GetLocalized()));

        return completion.Task;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        _web.Loaded -= OnLoaded;
        try
        {
            await _web.EnsureCoreWebView2Async();
            if (_cancellation.IsCancellationRequested) return;
            var core = _web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) =>
                e.State = Microsoft.Web.WebView2.Core.CoreWebView2PermissionState.Deny;
            bool useV4 = _parameters.Flag("use_v4") || _parameters.Text("challenge").Length == 0;
            string html = CreateDocument(useV4);
            string documentUri = CommunityContent.ToDocumentUri(html);
            bool documentStarted = false;
            core.NavigationStarting += (_, e) =>
            {
                // NavigateToString starts with the generated data URI and exposes the page as about:blank.
                if (!documentStarted && (e.Uri == documentUri || e.Uri == DocumentSource))
                {
                    documentStarted = true;
                    return;
                }

                e.Cancel = true;
            };
            core.WebMessageReceived += (_, e) =>
            {
                if (!documentStarted || e.Source != DocumentSource || core.Source != DocumentSource) return;
                try
                {
                    using var message = JsonDocument.Parse(e.WebMessageAsJson);
                    if (message.RootElement.Text("nonce") != _nonce) return;

                    var result = message.RootElement.Get("result");
                    if (result.ValueKind == JsonValueKind.Object && (useV4
                            ? result.Text("captcha_id").Length > 0 && result.Text("lot_number").Length > 0 &&
                              result.Text("pass_token").Length > 0
                            : result.Text("geetest_challenge").Length > 0 &&
                              result.Text("geetest_validate").Length > 0 && result.Text("geetest_seccode").Length > 0))
                    {
                        if (_result.TrySetResult(result.Clone())) Close();
                        return;
                    }

                    FailVerification("Miyoushe_VerifyResultError");
                }
                catch (JsonException)
                {
                }
            };
            core.NavigateToString(html);
        }
        catch (Exception)
        {
            FailVerification("Miyoushe_VerifyLoadError");
        }
    }

    private string CreateDocument(bool useV4)
    {
        var config = useV4
            ? JsonSerializer.Serialize(new
            {
                captchaId = _parameters.Text("gt"), riskType = _parameters.Text("risk_type"), product = "popup",
                nextWidth = "300px", lang = "zho",
                userInfo = JsonSerializer.Serialize(new { session_id = _session }), https = true, protocol = "https"
            })
            : JsonSerializer.Serialize(new
            {
                gt = _parameters.Text("gt"), challenge = _parameters.Text("challenge"), offline = false,
                new_captcha = true,
                product = "embed", width = "300px", https = true
            });
        return $$"""
                 <!doctype html><html><head><meta charset="utf-8"><meta name="referrer" content="no-referrer">
                 <style>body{margin:0;padding:24px;background:#f7f7f7;color:#333;font:14px 'Segoe UI','Microsoft YaHei UI',sans-serif;text-align:center}#captcha{display:flex;justify-content:center}#error{margin:20px;color:#a13030}</style></head>
                 <body><div id="captcha"></div><p id="error"></p><script>
                 const nonce = {{JsonSerializer.Serialize(_nonce)}};
                 const fail = () => document.getElementById('error').textContent = {{JsonSerializer.Serialize("Miyoushe_VerifyLoadError".GetLocalized())}};
                 const sdk = document.createElement('script');
                 sdk.src = '{{(useV4 ? ApiEndpoints.GeetestScriptV4Url : ApiEndpoints.GeetestScriptV349Url)}}';
                 sdk.onerror = fail;
                 sdk.onload = () => {
                     try {
                         {{(useV4 ? "initGeetest4" : "initGeetest")}}({{config}}, captcha => {
                             let completed = false;
                             captcha.appendTo('#captcha');
                             captcha.onError(fail);
                             // The SDK can dismiss its widget before reporting success. The window owns cancellation.
                             captcha.onSuccess(() => {
                                 if (completed) return;
                                 completed = true;
                                 let result = null;
                                 try { result = captcha.getValidate(); } catch {}
                                 window.chrome.webview.postMessage({nonce, result});
                             });
                         });
                     } catch { fail(); }
                 };
                 document.head.appendChild(sdk);
                 setTimeout(() => { if (!document.querySelector('#captcha').children.length) fail(); }, 25000);
                 </script></body></html>
                 """;
    }

    private void FailVerification(string resourceKey)
    {
        if (_result.TrySetException(new InvalidOperationException(resourceKey.GetLocalized()))) Close();
    }
}
