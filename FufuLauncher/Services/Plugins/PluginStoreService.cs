/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using FufuLauncher.Models;
using FufuLauncher.Helpers;
using FufuLauncher.Services.PluginMirror;

namespace FufuLauncher.Services;

public class PluginStoreService
{
    private readonly HttpClient _httpClient;
    private readonly PluginMirrorDownloadService _mirrorDownloadService;

    private static readonly string ClientVersion =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PluginStoreService(PluginMirrorDownloadService mirrorDownloadService)
    {
        _mirrorDownloadService = mirrorDownloadService;

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = false
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders =
            {
                UserAgent =
                {
                    new System.Net.Http.Headers.ProductInfoHeaderValue("Fufu-Launcher", ClientVersion)
                },
                Accept = { new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json") }
            }
        };
    }

    private static string GetCurrentLang()
    {
        var culture = ResourceExtensions.CurrentCulture;
        return string.IsNullOrEmpty(culture) ? "" : culture;
    }

    private static string AppendTokens(string url, string? dlToken, string? accessToken)
    {
        var uriBuilder = new UriBuilder(url);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);

        if (!string.IsNullOrWhiteSpace(dlToken))
            query["dl_token"] = dlToken;
        if (!string.IsNullOrWhiteSpace(accessToken))
            query["access_token"] = accessToken;

        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    private static string NormalizePluginUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        var baseUrl = ApiEndpoints.PluginStoreBaseUrl;
        var isLocal = baseUrl.Contains(ApiEndpoints.LocalhostHost) || baseUrl.Contains(ApiEndpoints.LoopbackHost);
        if (!isLocal) return url;

        try
        {
            var uri = new Uri(url);
            var baseUri = new Uri(baseUrl);
            var builder = new UriBuilder(uri)
            {
                Scheme = baseUri.Scheme,
                Host = baseUri.Host,
                Port = baseUri.Port
            };
            return builder.ToString();
        }
        catch
        {
            return url;
        }
    }

    private static void NormalizePluginUrls(PluginStoreItem item)
    {
        if (item == null) return;
        item.LuaInstallUrl = NormalizePluginUrl(item.LuaInstallUrl);
        item.LuaUninstallUrl = NormalizePluginUrl(item.LuaUninstallUrl);
        item.DownloadUrl = NormalizePluginUrl(item.DownloadUrl);
    }

    private static void CheckBodyForErrorGate(string body, string pluginId)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith("{"))
            return;

        try
        {
            var gateResponse = JsonSerializer.Deserialize<GateErrorResponse>(body, JsonOptions);
            if (gateResponse == null || gateResponse.Retcode == 0)
                return;

            if (gateResponse.Retcode == 403)
            {
                if (gateResponse.Data?.VerifyUrl != null)
                {
                    DebugLog.Info("PluginStoreService",
                        $"[PluginStoreService] Captcha gate detected for plugin {pluginId}: {gateResponse.Data.VerifyUrl}");
                    throw new CaptchaRequiredException(gateResponse.Data.VerifyUrl, pluginId);
                }

                throw new InvalidOperationException(gateResponse.Message ?? "PluginStoreAccessDenied".GetLocalized());
            }

            if (gateResponse.Retcode == 404)
            {
                throw new PrivatePluginAccessException(pluginId,
                    gateResponse.Message ?? "PluginStorePrivateAccessRequired".GetLocalized());
            }

            throw new InvalidOperationException(
                gateResponse.Message ?? $"Server error (retcode={gateResponse.Retcode})");
        }
        catch (JsonException)
        {
        }
        catch (CaptchaRequiredException)
        {
            throw;
        }
        catch (PrivatePluginAccessException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
    }

    public async Task<PluginListData> GetPluginListAsync(
        string? category = null,
        string? search = null,
        string sort = "popular",
        int page = 1,
        int pageSize = 20)
    {
        var queryParams = new List<string>
        {
            $"sort={Uri.EscapeDataString(sort)}",
            $"page={page}",
            $"page_size={pageSize}"
        };

        var lang = GetCurrentLang();
        if (!string.IsNullOrEmpty(lang))
            queryParams.Add($"lang={Uri.EscapeDataString(lang)}");

        if (!string.IsNullOrWhiteSpace(category))
            queryParams.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrWhiteSpace(search))
            queryParams.Add($"search={Uri.EscapeDataString(search)}");

        var url = $"{ApiEndpoints.PluginStoreListUrl}?{string.Join("&", queryParams)}";

        try
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Fetching plugin list: {url}");
            var response = await _httpClient.GetStringAsync(url);
            var result = DeserializeResponse<PluginListData>(response);
            var data = result?.Data ?? new PluginListData();
            if (data.Plugins != null)
            {
                foreach (var p in data.Plugins)
                    NormalizePluginUrls(p);
            }

            return data;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Server unreachable: {ex.Message}");
            throw new InvalidOperationException("PluginStoreServerUnreachable".GetLocalized(), ex);
        }
        catch (TaskCanceledException)
        {
            DebugLog.Info("PluginStoreService", "[PluginStoreService] Request timed out");
            throw new InvalidOperationException("PluginStoreServerTimeout".GetLocalized());
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error fetching plugin list: {ex.Message}");
            throw new InvalidOperationException(string.Format("PluginStoreLoadListFailed".GetLocalized(), ex.Message),
                ex);
        }
    }

    public async Task<PluginStoreItem?> GetPluginDetailAsync(string pluginId, string? accessToken = null)
    {
        var url = $"{ApiEndpoints.PluginStoreDetailUrl}?id={Uri.EscapeDataString(pluginId)}";
        var lang = GetCurrentLang();
        if (!string.IsNullOrEmpty(lang))
            url += $"&lang={Uri.EscapeDataString(lang)}";
        if (!string.IsNullOrWhiteSpace(accessToken))
            url += $"&access_token={Uri.EscapeDataString(accessToken)}";

        try
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Fetching plugin detail: {url}");
            var response = await _httpClient.GetStringAsync(url);
            var result = DeserializeResponse<PluginStoreItem>(response);
            var plugin = result?.Data;
            if (plugin != null) NormalizePluginUrls(plugin);
            return plugin;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Server unreachable: {ex.Message}");
            throw new InvalidOperationException("PluginStoreServerUnreachable".GetLocalized(), ex);
        }
        catch (TaskCanceledException)
        {
            DebugLog.Info("PluginStoreService", "[PluginStoreService] Request timed out");
            throw new InvalidOperationException("PluginStoreServerTimeout".GetLocalized());
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error fetching plugin detail: {ex.Message}");
            throw;
        }
    }

    public async Task<List<PluginStoreCategory>> GetCategoriesAsync()
    {
        try
        {
            DebugLog.Info("PluginStoreService",
                $"[PluginStoreService] Fetching categories: {ApiEndpoints.PluginStoreCategoriesUrl}");
            var url = ApiEndpoints.PluginStoreCategoriesUrl;
            var lang = GetCurrentLang();
            if (!string.IsNullOrEmpty(lang))
                url += $"?lang={Uri.EscapeDataString(lang)}";
            var response = await _httpClient.GetStringAsync(url);
            var result = DeserializeResponse<CategoriesData>(response);
            return result?.Data?.Categories ?? new List<PluginStoreCategory>();
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            DebugLog.Info("PluginStoreService",
                $"[PluginStoreService] Server unreachable for categories, using defaults");
            return new List<PluginStoreCategory>();
        }
        catch (TaskCanceledException)
        {
            DebugLog.Info("PluginStoreService", "[PluginStoreService] Categories request timed out, using defaults");
            return new List<PluginStoreCategory>();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error fetching categories: {ex.Message}");
            throw;
        }
    }

    public async Task<PluginStatsData> GetPluginStatsAsync(string pluginId, string? accessToken = null)
    {
        var url = $"{ApiEndpoints.PluginStoreStatsUrl}?id={Uri.EscapeDataString(pluginId)}";
        if (!string.IsNullOrWhiteSpace(accessToken))
            url += $"&access_token={Uri.EscapeDataString(accessToken)}";

        try
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Fetching plugin stats: {url}");
            var response = await _httpClient.GetStringAsync(url);
            var result = DeserializeResponse<PluginStatsData>(response);
            return result?.Data ?? new PluginStatsData();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error fetching plugin stats: {ex.Message}");
            return new PluginStatsData();
        }
    }

    public async Task<LeaderboardData> GetLeaderboardAsync(int limit = 20)
    {
        var url = $"{ApiEndpoints.PluginStoreLeaderboardUrl}?limit={limit}";

        try
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Fetching leaderboard: {url}");
            var response = await _httpClient.GetStringAsync(url);
            var result = DeserializeResponse<LeaderboardData>(response);
            return result?.Data ?? new LeaderboardData();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error fetching leaderboard: {ex.Message}");
            return new LeaderboardData();
        }
    }

    public async Task<DownloadTokenResponse> GetDownloadTokenAsync(string pluginId,
        string geetestCaptchaId, string lotNumber, string passToken,
        string genTime, string captchaOutput)
    {
        var url = ApiEndpoints.PluginStoreDownloadTokenUrl;
        var body = new
        {
            id = pluginId,
            geetest_captcha_id = geetestCaptchaId,
            geetest_lot_number = lotNumber,
            geetest_pass_token = passToken,
            geetest_gen_time = genTime,
            geetest_captcha_output = captchaOutput
        };

        try
        {
            DebugLog.Info("PluginStoreService",
                $"[PluginStoreService] Requesting download token for plugin {pluginId}");
            var json = JsonSerializer.Serialize(body, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            var result = DeserializeResponse<DownloadTokenResponse>(responseBody);
            if (result?.Retcode == 0 && result.Data != null)
            {
                DebugLog.Info("PluginStoreService",
                    $"[PluginStoreService] Download token obtained: {result.Data.DlToken[..8]}...");
                return result.Data;
            }

            throw new InvalidOperationException(result?.Message ?? "PluginStoreCaptchaFailed".GetLocalized());
        }
        catch (CaptchaRequiredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error getting download token: {ex.Message}");
            throw new InvalidOperationException(string.Format("PluginStoreCaptchaFailed".GetLocalized(), ex.Message),
                ex);
        }
    }

    public async Task<PrivateAccessResponse> GetPrivateAccessAsync(string pluginId, string accessKey)
    {
        var url = ApiEndpoints.PluginStorePrivateAccessUrl;
        var body = new
        {
            id = pluginId,
            access_key = accessKey
        };

        try
        {
            DebugLog.Info("PluginStoreService",
                $"[PluginStoreService] Requesting private access for plugin {pluginId}");
            var json = JsonSerializer.Serialize(body, JsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            var result = DeserializeResponse<PrivateAccessResponse>(responseBody);
            if (result?.Retcode == 0 && result.Data != null)
            {
                DebugLog.Info("PluginStoreService",
                    $"[PluginStoreService] Private access granted for plugin {pluginId}");
                if (result.Data.Plugin != null)
                    NormalizePluginUrls(result.Data.Plugin);
                return result.Data;
            }

            throw new InvalidOperationException(result?.Message ?? "PluginStorePrivateAccessDenied".GetLocalized());
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error getting private access: {ex.Message}");
            throw new InvalidOperationException(
                string.Format("PluginStorePrivateAccessDenied".GetLocalized(), ex.Message), ex);
        }
    }

    public async Task<string> DownloadLuaScriptAsync(string luaUrl, string? expectedHash = null,
        string? dlToken = null, string? accessToken = null)
    {
        var url = AppendTokens(luaUrl, dlToken, accessToken);

        try
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Downloading Lua script: {url}");
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead);
            var body = await response.Content.ReadAsStringAsync();

            CheckBodyForErrorGate(body, ExtractPluginIdFromUrl(luaUrl));

            if (!string.IsNullOrWhiteSpace(expectedHash))
            {
                DebugLog.Info("PluginStoreService", $"[PluginStoreService] Verifying Lua script hash...");
                PluginVerifier.VerifyLuaHash(body, expectedHash);
            }

            return body;
        }
        catch (CaptchaRequiredException)
        {
            throw;
        }
        catch (PrivatePluginAccessException)
        {
            throw;
        }
        catch (HashMismatchException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            throw new InvalidOperationException("PluginStoreDownloadLuaFailed".GetLocalized(), ex);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error downloading Lua script: {ex.Message}");
            throw new InvalidOperationException(string.Format("PluginStoreDownloadLuaError".GetLocalized(), ex.Message),
                ex);
        }
    }

    public async Task DownloadFileAsync(string fileUrl, string destinationPath,
        IProgress<DownloadProgressInfo>? progress = null, string? expectedHash = null,
        string? dlToken = null, string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        var url = AppendTokens(fileUrl, dlToken, accessToken);

        if (await _mirrorDownloadService.TryDownloadViaMirrorAsync(
                this, url, destinationPath, progress, expectedHash, cancellationToken))
        {
            return;
        }

        await DownloadFileCoreAsync(url, destinationPath, progress, expectedHash,
            checkErrorGate: true, cancellationToken);
    }

    internal async Task DownloadFileCoreAsync(string url, string destinationPath,
        IProgress<DownloadProgressInfo>? progress, string? expectedHash,
        bool checkErrorGate, CancellationToken cancellationToken)
    {
        try
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Downloading file: {url} -> {destinationPath}");

            using var response =
                await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("json"))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (checkErrorGate)
                {
                    CheckBodyForErrorGate(body, ExtractPluginIdFromUrl(url));
                }

                throw new InvalidOperationException(
                    string.Format("PluginStoreHttpError".GetLocalized(), (int)response.StatusCode,
                        body.Length > 200 ? body[..200] : body));
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 8192, useAsync: true);

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var buffer = new byte[8192];
            var totalRead = 0L;
            int bytesRead;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastReportBytes = 0L;
            var lastReportMs = 0L;
            const long reportIntervalMs = 200;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);

                if (!string.IsNullOrWhiteSpace(expectedHash))
                {
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                totalRead += bytesRead;

                var elapsedMs = stopwatch.ElapsedMilliseconds;
                if (progress != null && (elapsedMs - lastReportMs >= reportIntervalMs || totalRead == totalBytes))
                {
                    var deltaBytes = totalRead - lastReportBytes;
                    var deltaMs = elapsedMs - lastReportMs;
                    var speed = deltaMs > 0 ? (long)(deltaBytes * 1000.0 / deltaMs) : 0;

                    var percent = totalBytes > 0 ? (double)totalRead / totalBytes * 100.0 : 0.0;

                    progress.Report(new DownloadProgressInfo
                    {
                        Percent = Math.Round(percent, 1),
                        BytesDownloaded = totalRead,
                        TotalBytes = totalBytes,
                        SpeedBytesPerSecond = speed,
                        StatusText = string.Format("PluginStoreDownloading".GetLocalized(), (int)percent)
                    });

                    lastReportBytes = totalRead;
                    lastReportMs = elapsedMs;
                }
            }

            await fileStream.FlushAsync();

            if (!string.IsNullOrWhiteSpace(expectedHash))
            {
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var actualHash = PluginVerifier.BytesToHex(sha256.Hash!);

                DebugLog.Info("PluginStoreService", $"[PluginStoreService] Verifying downloaded file hash...");
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    DebugLog.Info("PluginStoreService",
                        $"[PluginStoreService] HASH MISMATCH: expected={expectedHash[..16]}... actual={actualHash[..16]}...");

                    await fileStream.DisposeAsync();

                    try
                    {
                        File.Delete(destinationPath);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("PluginStoreService",
                            $"[PluginStoreService] Failed to delete bad file: {ex.Message}");
                    }

                    throw new HashMismatchException("PluginStoreHashMismatch".GetLocalized());
                }

                DebugLog.Info("PluginStoreService", $"[PluginStoreService] Hash verified OK");
            }
        }
        catch (CaptchaRequiredException)
        {
            throw;
        }
        catch (PrivatePluginAccessException)
        {
            throw;
        }
        catch (HashMismatchException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            DebugLog.Info("PluginStoreService",
                $"[PluginStoreService] Download cancelled, cleaning up partial file: {destinationPath}");
            try
            {
                File.Delete(destinationPath);
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginStoreService",
                    $"[PluginStoreService] Failed to delete partial file: {ex.Message}");
            }

            throw;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            DebugLog.Info("PluginStoreService",
                $"[PluginStoreService] Server unreachable during file download: {ex.Message}");
            throw new InvalidOperationException("PluginStoreDownloadFileFailed".GetLocalized(), ex);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreService", $"[PluginStoreService] Error downloading file: {ex.Message}");
            throw new InvalidOperationException(
                string.Format("PluginStoreDownloadFileError".GetLocalized(), ex.Message), ex);
        }
    }

    private static string ExtractPluginIdFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Trim('/').Split('/');
            if (segments.Length >= 3)
            {
                var fileName = segments[^1];
                var dotIndex = fileName.LastIndexOf('.');
                return dotIndex > 0 ? fileName[..dotIndex] : fileName;
            }
        }
        catch
        {
        }

        return "unknown";
    }

    private static PluginStoreResponse<T>? DeserializeResponse<T>(string json)
    {
        return JsonSerializer.Deserialize<PluginStoreResponse<T>>(json, JsonOptions);
    }
}

public class CaptchaRequiredException : Exception
{
    public string VerifyUrl
    {
        get;
    }

    public string PluginId
    {
        get;
    }

    public CaptchaRequiredException(string verifyUrl, string pluginId)
        : base($"Captcha required for plugin '{pluginId}'")
    {
        VerifyUrl = verifyUrl;
        PluginId = pluginId;
    }
}

public class PrivatePluginAccessException : Exception
{
    public string PluginId
    {
        get;
    }

    public PrivatePluginAccessException(string pluginId, string message)
        : base(message)
    {
        PluginId = pluginId;
    }
}

public class PluginListData
{
    [JsonPropertyName("total")]
    public int Total
    {
        get;
        set;
    }

    [JsonPropertyName("page")]
    public int Page
    {
        get;
        set;
    }

    [JsonPropertyName("plugins")]
    public List<PluginStoreItem> Plugins
    {
        get;
        set;
    } = new();
}

public class PluginListResponse : PluginListData
{
}

public class CategoriesData
{
    [JsonPropertyName("categories")]
    public List<PluginStoreCategory> Categories
    {
        get;
        set;
    } = new();
}

public class PluginStatsData
{
    [JsonPropertyName("id")]
    public string Id
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("name")]
    public string Name
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("total")]
    public long Total
    {
        get;
        set;
    }

    [JsonPropertyName("daily")]
    public List<DailyStat> Daily
    {
        get;
        set;
    } = new();

    [JsonPropertyName("monthly")]
    public List<MonthlyStat> Monthly
    {
        get;
        set;
    } = new();
}

public class DailyStat
{
    [JsonPropertyName("date")]
    public string Date
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("count")]
    public long Count
    {
        get;
        set;
    }
}

public class MonthlyStat
{
    [JsonPropertyName("month")]
    public string Month
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("count")]
    public long Count
    {
        get;
        set;
    }
}

public class LeaderboardData
{
    [JsonPropertyName("leaderboard")]
    public List<LeaderboardEntry> Leaderboard
    {
        get;
        set;
    } = new();
}

public class LeaderboardEntry
{
    [JsonPropertyName("id")]
    public string Id
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("name")]
    public string Name
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("total")]
    public long Total
    {
        get;
        set;
    }
}

public class DownloadTokenResponse
{
    [JsonPropertyName("dl_token")]
    public string DlToken
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("plugin_id")]
    public string PluginId
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn
    {
        get;
        set;
    }
}

public class PrivateAccessResponse
{
    [JsonPropertyName("archive_password")]
    public string? ArchivePassword
    {
        get;
        set;
    }

    [JsonPropertyName("plugin")]
    public PluginStoreItem? Plugin
    {
        get;
        set;
    }

    [JsonPropertyName("access_token")]
    public string AccessToken
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn
    {
        get;
        set;
    }
}

public class GateErrorResponse
{
    [JsonPropertyName("retcode")]
    public int Retcode
    {
        get;
        set;
    }

    [JsonPropertyName("message")]
    public string Message
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("data")]
    public GateErrorData? Data
    {
        get;
        set;
    }
}

public class GateErrorData
{
    [JsonPropertyName("verify_url")]
    public string? VerifyUrl
    {
        get;
        set;
    }
}