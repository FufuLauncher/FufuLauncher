/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using Microsoft.Web.WebView2.Core;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class BBSWindow
{
    #region WebView2 初始化与设置

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

    private const string TabKeyInterceptorScript = """
                                                   window.addEventListener('keydown', function(e) {
                                                       if (e.key === 'Tab') {
                                                           e.preventDefault();
                                                           window.chrome.webview.postMessage('{"method":"toggleTopBar"}');
                                                       }
                                                   });
                                                   """;

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await EnsureDeviceFpAsync();

            await BBSWebView.EnsureCoreWebView2Async();
            UpdateWebViewSettings();

            BBSWebView.CoreWebView2.AddWebResourceRequestedFilter(ApiEndpoints.MihoyoWebSubdomainFilter,
                CoreWebView2WebResourceContext.All);
            BBSWebView.CoreWebView2.AddWebResourceRequestedFilter(ApiEndpoints.HoyolabWebSubdomainFilter,
                CoreWebView2WebResourceContext.All);

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
            DebugLog.Info("BBSWindow.WebView", $"WebView Init Failed: {ex.Message}");
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

    private async void CoreWebView2_DOMContentLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args)
    {
        await sender.ExecuteScriptAsync(HideScrollBarScript);
        await sender.ExecuteScriptAsync(ConvertMouseToTouchScript);
    }

    private void CoreWebView2_SourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        if (UrlTextBox != null) UrlTextBox.Text = sender.Source;
    }

    #endregion
}