/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Windows.Storage.Streams;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class BBSWindow
{
    #region 请求头注入与 DS 签名

    private const string CNVersion = "2.109.0";
    private const string CNK2 = "lX8m5VO5at5JG7hR8hzqFwzyL5aB1tYo";
    private const string CNLK2 = "yBh10ikxtLPoIhgwgPZSv5dmfaOTSJ6a";
    private const string CNX4 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string CNX6 = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    private const string ToolVersion = "v6.6.1-gr-cn";
    private const string Page = "v6.6.1-gr-cn_#/ys";

    private class ClientConfig
    {
        public string ClientType
        {
            get;
            set;
        }

        public string AppVersion
        {
            get;
            set;
        }

        public string Salt
        {
            get;
            set;
        }

        public string UserAgent
        {
            get;
            set;
        }

        public bool UseDS2
        {
            get;
            set;
        }
    }

    private readonly Dictionary<string, ClientConfig> _clientConfigs = new()
    {
        ["2"] = new ClientConfig
        {
            ClientType = "2",
            AppVersion = CNVersion,
            Salt = CNLK2,
            UserAgent = "",
            UseDS2 = false
        },
        ["5"] = new ClientConfig
        {
            ClientType = "5",
            AppVersion = CNVersion,
            Salt = CNX4,
            UserAgent = "",
            UseDS2 = true
        }
    };


    private static readonly (string prefix, string clientType)[] ApiRouteMap =
    {
        (ApiEndpoints.BbsProtocolGameRecordApiPrefix, "5"),
        (ApiEndpoints.BbsProtocolRecordPath, "5"),
        (ApiEndpoints.BbsProtocolGameRecordPath, "5"),
        (ApiEndpoints.BbsProtocolEventPath, "2"),
        (ApiEndpoints.BbsProtocolCommunityPath, "2"),
    };

    private ClientConfig _currentConfig;

    private ClientConfig SelectConfig(string uri)
    {
        foreach (var (prefix, clientType) in ApiRouteMap)
        {
            if (uri.Contains(prefix))
                return _clientConfigs[clientType];
        }

        return _clientConfigs["2"];
    }

    private async void CoreWebView2_WebResourceRequested(CoreWebView2 sender,
        CoreWebView2WebResourceRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            if (args.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var uri = args.Request.Uri;
            bool isApiRequest = uri.Contains(ApiEndpoints.BbsProtocolApiPath) ||
                                uri.Contains(ApiEndpoints.BbsProtocolCommunityPath) ||
                                uri.Contains(ApiEndpoints.BbsProtocolRecordPath) ||
                                uri.Contains(ApiEndpoints.BbsProtocolEventPath);

            if (isApiRequest && (uri.Contains(ApiEndpoints.MihoyoDomain) || uri.Contains(ApiEndpoints.HoyolabDomain)))
            {
                var headers = args.Request.Headers;
                var config = SelectConfig(uri);


                headers.RemoveHeader("x-rpc-client_type");
                headers.RemoveHeader("x-rpc-app_version");
                headers.RemoveHeader("DS");
                headers.RemoveHeader("x-rpc-device_id");
                headers.RemoveHeader("x-rpc-device_fp");
                headers.RemoveHeader("x-rpc-device_name");
                headers.RemoveHeader("x-rpc-sys_version");
                headers.RemoveHeader("x-rpc-tool_verison");
                headers.RemoveHeader("x-rpc-page");
                headers.RemoveHeader("x-rpc-sdk_version");
                headers.RemoveHeader("X-Requested-With");


                headers.SetHeader("x-rpc-client_type", config.ClientType);
                headers.SetHeader("x-rpc-app_version", config.AppVersion);
                headers.SetHeader("x-rpc-device_id", _deviceId);
                headers.SetHeader("x-rpc-device_name", _deviceName);
                headers.SetHeader("x-rpc-sys_version", _sysVersion);
                headers.SetHeader("x-rpc-tool_verison", ToolVersion);
                headers.SetHeader("x-rpc-page", Page);
                headers.SetHeader("x-rpc-app_id", "bll8iq97cem8");
                headers.SetHeader("x-rpc-sdk_version", "2.16.0");
                headers.SetHeader("X-Requested-With", "com.mihoyo.hyperion");

                string fp = _activeDeviceFp;
                if (string.IsNullOrEmpty(fp))
                {
                    DebugLog.Info("BBSWindow.Protocol",
                        "[BBSWindow] Warning: _activeDeviceFp is empty, using a temporary random value");
                    fp = Convert.ToHexString(RandomNumberGenerator.GetBytes(7)).ToLowerInvariant();
                }

                headers.SetHeader("x-rpc-device_fp", fp);


                string ds;
                if (config.UseDS2)
                {
                    string query = GetSortedQuery(uri);
                    string body = "";
                    if (args.Request.Method == "POST" && args.Request.Content != null)
                    {
                        body = await GetJsonBodyAsync(args.Request.Content);
                    }

                    ds = CalculateDS2(config.Salt, query, body);
                }
                else
                {
                    ds = CalculateDS1(config.Salt);
                }

                headers.SetHeader("DS", ds);


                headers.SetHeader("Origin", ApiEndpoints.WebstaticRefererUrl);
                headers.SetHeader("Referer", ApiEndpoints.WebstaticRefererUrl + "/");
                headers.SetHeader("Accept", "application/json, text/plain, */*");
                headers.SetHeader("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private JsResult GetHttpRequestHeader()
    {
        string fp = _activeDeviceFp;
        if (string.IsNullOrEmpty(fp))
        {
            DebugLog.Info("BBSWindow.Protocol",
                "[BBSWindow] GetHttpRequestHeader: _activeDeviceFp is empty, using random fallback");
            fp = Convert.ToHexString(RandomNumberGenerator.GetBytes(7)).ToLowerInvariant();
        }

        var data = new Dictionary<string, object>
        {
            ["x-rpc-app_id"] = "bll8iq97cem8",
            ["x-rpc-client_type"] = _currentConfig.ClientType,
            ["x-rpc-app_version"] = _currentConfig.AppVersion,
            ["x-rpc-device_id"] = _deviceId,
            ["x-rpc-sdk_version"] = "2.16.0",
            ["x-rpc-device_fp"] = fp
        };
        return new JsResult { Data = data };
    }

    private JsResult GetCookieInfoMinimal()
    {
        string fp = _activeDeviceFp;
        if (string.IsNullOrEmpty(fp))
        {
            DebugLog.Info("BBSWindow.Protocol",
                "[BBSWindow] GetCookieInfoMinimal: _activeDeviceFp is empty, using random fallback");
            fp = Convert.ToHexString(RandomNumberGenerator.GetBytes(7)).ToLowerInvariant();
        }

        return new JsResult
        {
            Data = new Dictionary<string, object>
            {
                ["ltuid"] = cookieDic.GetValueOrDefault("ltuid") ?? "",
                ["ltoken"] = cookieDic.GetValueOrDefault("ltoken") ?? "",
                ["cookie_token"] = cookieDic.GetValueOrDefault("cookie_token") ?? "",
                ["account_id"] = cookieDic.GetValueOrDefault("account_id") ?? "",
                ["ltuid_v2"] = cookieDic.GetValueOrDefault("ltuid_v2") ?? "",
                ["ltoken_v2"] = cookieDic.GetValueOrDefault("ltoken_v2") ?? "",
                ["account_mid_v2"] = cookieDic.GetValueOrDefault("account_mid_v2") ?? "",
                ["cookie_token_v2"] = cookieDic.GetValueOrDefault("cookie_token_v2") ?? "",
                ["DEVICEFP"] = fp
            }
        };
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
        catch
        {
            return "";
        }
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
            else
                sb.Append(value?.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }));
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
        catch
        {
            return "";
        }
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

    #endregion
}