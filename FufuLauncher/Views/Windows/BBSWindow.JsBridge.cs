/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views;

public sealed partial class BBSWindow
{
    #region JS 桥消息分发

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
            "getCookieToken" => new JsResult
                { Data = new() { ["cookie_token"] = cookieDic.GetValueOrDefault("cookie_token") ??
                                                       cookieDic.GetValueOrDefault("cookie_token_v2") ?? "" } },
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

    private async void CoreWebView2_WebMessageReceived(CoreWebView2 sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            string message = args.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message)) return;
            var param = JsonSerializer.Deserialize<JsParam>(message,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (param == null) return;

            JsResult? result = await HandleJsMessageAsync(param);

            if (result != null && !string.IsNullOrEmpty(param.Callback))
            {
                await ExecuteCallback(param.Callback, result);
            }
        }
        catch
        {
        }
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
        string payload = JsonSerializer.Serialize(result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        string script = $"javascript:mhyWebBridge(\"{callback}\", {payload})";
        await BBSWebView.CoreWebView2.ExecuteScriptAsync(script);
    }

    private class JsParam
    {
        [JsonPropertyName("method")]
        public string Method
        {
            get;
            set;
        } = "";

        [JsonPropertyName("payload")]
        public JsonNode? Payload
        {
            get;
            set;
        }

        [JsonPropertyName("callback")]
        public string? Callback
        {
            get;
            set;
        }
    }

    private class JsResult
    {
        [JsonPropertyName("retcode")]
        public int Code
        {
            get;
            set;
        } = 0;

        [JsonPropertyName("message")]
        public string Message
        {
            get;
            set;
        } = "";

        [JsonPropertyName("data")]
        public Dictionary<string, object> Data
        {
            get;
            set;
        } = new();
    }

    #endregion
}
