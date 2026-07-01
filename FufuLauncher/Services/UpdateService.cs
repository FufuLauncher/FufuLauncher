/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Services;

public class UpdateService : IUpdateService
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly HttpClient _httpClient;
    private static readonly string CurrentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";

    public UpdateService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;

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
                UserAgent = { new System.Net.Http.Headers.ProductInfoHeaderValue("Fufu-Launcher", CurrentVersion) },
                Accept = { new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json") }
            }
        };
    }

    public async Task<UpdateCheckResult> CheckUpdateAsync()
    {
        try
        {
            Debug.WriteLine($"[UpdateService] === 版本检查开始 ===");
            Debug.WriteLine($"[UpdateService] 本地版本: {CurrentVersion}");
            Debug.WriteLine($"[UpdateService] 超时设置: {_httpClient.Timeout.TotalSeconds} 秒");

            if (!await IsServerReachableAsync())
            {
                Debug.WriteLine("[UpdateService] 服务器暂时不可达，跳过版本检查");
                return new UpdateCheckResult { ShouldShowUpdate = false };
            }

            var json = await GetWithRetryAsync(ApiEndpoints.UpdateJsonUrl, maxRetries: 3);
            Debug.WriteLine($"[UpdateService] 服务器响应: {json}");

            var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json);
            var serverVersion = updateInfo?.Version ?? CurrentVersion;
            var updateInfoUrl = updateInfo?.UpdateInfoUrl ?? ApiEndpoints.UpdateHtmlUrl;

            Debug.WriteLine($"[UpdateService] 解析后的服务器版本: {serverVersion}");
            Debug.WriteLine($"[UpdateService] 更新公告URL: {updateInfoUrl}");

            var lastVersionObj = await _localSettingsService.ReadSettingAsync(LocalSettingsService.LastAnnouncedVersionKey);
            var lastVersion = lastVersionObj?.ToString() ?? string.Empty;

            Debug.WriteLine($"[UpdateService] 上次记录版本: '{lastVersion}'");

            if (!IsNewerVersion(serverVersion, CurrentVersion))
            {
                Debug.WriteLine($"[UpdateService] 服务器版本不比本地版本新，跳过更新");
                return new UpdateCheckResult { ShouldShowUpdate = false };
            }

            if (serverVersion == lastVersion)
            {
                Debug.WriteLine($"[UpdateService] 已显示过此版本，跳过重复提示");
                return new UpdateCheckResult { ShouldShowUpdate = false };
            }

            Debug.WriteLine($"[UpdateService] 发现新版本，准备显示更新窗口");
            await _localSettingsService.SaveSettingAsync(LocalSettingsService.LastAnnouncedVersionKey, serverVersion);

            return new UpdateCheckResult
            {
                ShouldShowUpdate = true,
                ServerVersion = serverVersion,
                UpdateInfoUrl = updateInfoUrl
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UpdateService] 检查失败: {ex.GetType().Name} - {ex.Message}");
            Debug.WriteLine($"[UpdateService] 堆栈: {ex.StackTrace}");
            return new UpdateCheckResult { ShouldShowUpdate = false };
        }
    }

    internal static bool IsNewerVersion(string serverVersion, string currentVersion)
    {
        try
        {
            var serverVer = new Version(serverVersion);
            var currentVer = new Version(currentVersion);
            return serverVer > currentVer;
        }
        catch
        {
            return false;
        }
    }



    private async Task<bool> IsServerReachableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.GetAsync(ApiEndpoints.AgreementUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GetWithRetryAsync(string url, int maxRetries)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                Debug.WriteLine($"[UpdateService] 请求尝试 {i + 1}/{maxRetries}");
                return await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] 尝试 {i + 1} 失败: {ex.Message}");

                if (i == maxRetries - 1) throw;

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
            }
        }

        throw new Exception("所有重试均失败");
    }

    private class UpdateInfo
    {
        [JsonPropertyName("Version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("updateInfoUrl")]
        public string UpdateInfoUrl { get; set; } = string.Empty;
    }
}
