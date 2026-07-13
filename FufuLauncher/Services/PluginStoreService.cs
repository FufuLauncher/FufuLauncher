/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class PluginStoreService
{
    private readonly HttpClient _httpClient;
    private static readonly string ClientVersion =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PluginStoreService()
    {
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

        if (!string.IsNullOrWhiteSpace(category))
            queryParams.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrWhiteSpace(search))
            queryParams.Add($"search={Uri.EscapeDataString(search)}");

        var url = $"{ApiEndpoints.PluginStoreListUrl}?{string.Join("&", queryParams)}";

        try
        {
            Debug.WriteLine($"[PluginStoreService] Fetching plugin list: {url}");
            var response = await _httpClient.GetStringAsync(url);
            var result = DeserializeResponse<PluginListData>(response);
            return result?.Data ?? new PluginListData();
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            Debug.WriteLine($"[PluginStoreService] Server unreachable: {ex.Message}");
            throw new InvalidOperationException("无法连接到插件商城服务器，请检查网络或服务器状态", ex);
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine("[PluginStoreService] Request timed out");
            throw new InvalidOperationException("连接插件商城超时，请稍后重试");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreService] Error fetching plugin list: {ex.Message}");
            throw new InvalidOperationException($"加载插件列表失败: {ex.Message}", ex);
        }
    }
    
    public async Task<PluginStoreItem?> GetPluginDetailAsync(string pluginId)
    {
        var url = $"{ApiEndpoints.PluginStoreDetailUrl}?id={Uri.EscapeDataString(pluginId)}";

        try
        {
            Debug.WriteLine($"[PluginStoreService] Fetching plugin detail: {url}");
            var response = await _httpClient.GetStringAsync(url);
            var result = DeserializeResponse<PluginStoreItem>(response);
            return result?.Data;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            Debug.WriteLine($"[PluginStoreService] Server unreachable: {ex.Message}");
            throw new InvalidOperationException("无法连接到插件商城服务器，请检查网络或服务器状态", ex);
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine("[PluginStoreService] Request timed out");
            throw new InvalidOperationException("连接插件商城超时，请稍后重试");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreService] Error fetching plugin detail: {ex.Message}");
            throw;
        }
    }
    
    public async Task<List<PluginStoreCategory>> GetCategoriesAsync()
    {
        try
        {
            Debug.WriteLine($"[PluginStoreService] Fetching categories: {ApiEndpoints.PluginStoreCategoriesUrl}");
            var response = await _httpClient.GetStringAsync(ApiEndpoints.PluginStoreCategoriesUrl);
            var result = DeserializeResponse<CategoriesData>(response);
            return result?.Data?.Categories ?? new List<PluginStoreCategory>();
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            Debug.WriteLine($"[PluginStoreService] Server unreachable for categories, using defaults");
            return new List<PluginStoreCategory>();
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine("[PluginStoreService] Categories request timed out, using defaults");
            return new List<PluginStoreCategory>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreService] Error fetching categories: {ex.Message}");
            throw;
        }
    }
    
    public async Task<string> DownloadLuaScriptAsync(string luaUrl, string? expectedHash = null)
    {
        try
        {
            Debug.WriteLine($"[PluginStoreService] Downloading Lua script: {luaUrl}");
            var script = await _httpClient.GetStringAsync(luaUrl);
            
            if (!string.IsNullOrWhiteSpace(expectedHash))
            {
                Debug.WriteLine($"[PluginStoreService] Verifying Lua script hash...");
                PluginVerifier.VerifyLuaHash(script, expectedHash);
            }

            return script;
        }
        catch (HashMismatchException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            throw new InvalidOperationException("无法下载安装脚本，服务器不可用", ex);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreService] Error downloading Lua script: {ex.Message}");
            throw new InvalidOperationException($"下载安装脚本失败: {ex.Message}", ex);
        }
    }
    
    public async Task DownloadFileAsync(string fileUrl, string destinationPath,
        IProgress<(int percent, string status)>? progress = null, string? expectedHash = null)
    {
        try
        {
            Debug.WriteLine($"[PluginStoreService] Downloading file: {fileUrl} -> {destinationPath}");

            using var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 8192, useAsync: true);
            
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var buffer = new byte[8192];
            var totalRead = 0L;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                
                if (!string.IsNullOrWhiteSpace(expectedHash))
                {
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                totalRead += bytesRead;

                if (totalBytes > 0 && progress != null)
                {
                    var percent = (int)(totalRead * 100 / totalBytes);
                    progress.Report((percent, $"下载中 {percent}%"));
                }
            }
            
            await fileStream.FlushAsync();
            
            if (!string.IsNullOrWhiteSpace(expectedHash))
            {
                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var actualHash = PluginVerifier.BytesToHex(sha256.Hash!);

                Debug.WriteLine($"[PluginStoreService] Verifying downloaded file hash...");
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[PluginStoreService] HASH MISMATCH: expected={expectedHash[..16]}... actual={actualHash[..16]}...");
                    
                    await fileStream.DisposeAsync();

                    try { File.Delete(destinationPath); }
                    catch (Exception ex) { Debug.WriteLine($"[PluginStoreService] Failed to delete bad file: {ex.Message}"); }

                    throw new HashMismatchException(
                        $"文件完整性校验失败，下载可能已损坏或被篡改。");
                }
                Debug.WriteLine($"[PluginStoreService] Hash verified OK");
            }
        }
        catch (HashMismatchException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
        {
            Debug.WriteLine($"[PluginStoreService] Server unreachable during file download: {ex.Message}");
            throw new InvalidOperationException("无法下载插件文件，服务器不可用", ex);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginStoreService] Error downloading file: {ex.Message}");
            throw new InvalidOperationException($"下载插件文件失败: {ex.Message}", ex);
        }
    }

    private static Response<T>? DeserializeResponse<T>(string json)
    {
        return JsonSerializer.Deserialize<Response<T>>(json, JsonOptions);
    }
}

public class PluginListData
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("plugins")]
    public List<PluginStoreItem> Plugins { get; set; } = new();
}

public class PluginListResponse : PluginListData { }

public class CategoriesData
{
    [JsonPropertyName("categories")]
    public List<PluginStoreCategory> Categories { get; set; } = new();
}
