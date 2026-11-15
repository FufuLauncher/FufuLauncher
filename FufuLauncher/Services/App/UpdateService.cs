/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public class UpdateService : IUpdateService
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IDevBuildDetectionService _devBuildDetectionService;
    private readonly HttpClient _httpClient;
    private static readonly string CurrentVersion = AppVersionHelper.NumericVersion;

    public UpdateService(ILocalSettingsService localSettingsService, IDevBuildDetectionService devBuildDetectionService)
    {
        _localSettingsService = localSettingsService;
        _devBuildDetectionService = devBuildDetectionService;

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
            DebugLog.Info("UpdateService", $"[UpdateService] Version check started");
            DebugLog.Info("UpdateService", $"[UpdateService] Local version: {CurrentVersion}");
            DebugLog.Info("UpdateService", $"[UpdateService] Timeout: {_httpClient.Timeout.TotalSeconds}s");

            var updateInfo = await FetchManifestAsync();
            if (updateInfo == null)
            {
                DebugLog.Info("UpdateService",
                    "[UpdateService] Server temporarily unreachable, skipping version check");
                return new UpdateCheckResult { ShouldShowUpdate = false };
            }

            var serverVersion = updateInfo.Version ?? CurrentVersion;
            var updateInfoUrl = updateInfo?.UpdateInfoUrl ?? ApiEndpoints.UpdateHtmlUrl;
            var previewVersion = updateInfo?.PreReleaseVersion ?? string.Empty;
            var previewUpdateInfoUrl = string.IsNullOrEmpty(updateInfo?.PreReleaseUpdateInfoUrl)
                ? ApiEndpoints.UpdatePreHtmlUrl
                : updateInfo!.PreReleaseUpdateInfoUrl;

            DebugLog.Info("UpdateService", $"[UpdateService] Parsed server version: {serverVersion}");
            DebugLog.Info("UpdateService", $"[UpdateService] Parsed preview version: '{previewVersion}'");
            DebugLog.Info("UpdateService", $"[UpdateService] Update announcement URL: {updateInfoUrl}");
            DebugLog.Info("UpdateService", $"[UpdateService] Preview update announcement URL: {previewUpdateInfoUrl}");

            var isDevBuild = await _devBuildDetectionService.DetectAsync(serverVersion);
            DebugLog.Info("UpdateService", $"[UpdateService] Is dev build: {isDevBuild}");

            var lastVersionObj =
                await _localSettingsService.ReadSettingAsync(LocalSettingsService.LastAnnouncedVersionKey);
            var lastVersion = lastVersionObj?.ToString() ?? string.Empty;

            DebugLog.Info("UpdateService", $"[UpdateService] Last recorded version: '{lastVersion}'");

            if (AppVersionHelper.IsNewerVersion(serverVersion, CurrentVersion) && serverVersion != lastVersion)
            {
                DebugLog.Info("UpdateService", $"[UpdateService] New version found, showing update window");
                await _localSettingsService.SaveSettingAsync(LocalSettingsService.LastAnnouncedVersionKey,
                    serverVersion);

                return new UpdateCheckResult
                {
                    ShouldShowUpdate = true,
                    IsDevBuild = isDevBuild,
                    ServerVersion = serverVersion,
                    UpdateInfoUrl = updateInfoUrl
                };
            }

            var previewAnnouncementEnabledObj =
                await _localSettingsService.ReadSettingAsync("IsPreviewUpdateAnnouncementEnabled");
            var previewAnnouncementEnabled = previewAnnouncementEnabledObj == null ||
                                             Convert.ToBoolean(previewAnnouncementEnabledObj);

            if (previewAnnouncementEnabled &&
                AppVersionHelper.IsNewerVersion(previewVersion, serverVersion) &&
                AppVersionHelper.IsNewerVersion(previewVersion, CurrentVersion))
            {
                var lastPreviewVersionObj =
                    await _localSettingsService.ReadSettingAsync(LocalSettingsService.LastAnnouncedPreviewVersionKey);
                var lastPreviewVersion = lastPreviewVersionObj?.ToString() ?? string.Empty;

                DebugLog.Info("UpdateService",
                    $"[UpdateService] Last recorded preview version: '{lastPreviewVersion}'");

                if (previewVersion != lastPreviewVersion)
                {
                    DebugLog.Info("UpdateService", $"[UpdateService] New preview version found, showing update window");
                    await _localSettingsService.SaveSettingAsync(LocalSettingsService.LastAnnouncedPreviewVersionKey,
                        previewVersion);

                    return new UpdateCheckResult
                    {
                        ShouldShowUpdate = true,
                        IsPreview = true,
                        IsDevBuild = isDevBuild,
                        ServerVersion = previewVersion,
                        UpdateInfoUrl = previewUpdateInfoUrl
                    };
                }

                DebugLog.Info("UpdateService",
                    $"[UpdateService] Preview version already shown, skipping duplicate prompt");
            }

            DebugLog.Info("UpdateService", $"[UpdateService] No update available, skipping");
            return new UpdateCheckResult { ShouldShowUpdate = false, IsDevBuild = isDevBuild };
        }
        catch (Exception ex)
        {
            DebugLog.Info("UpdateService", $"[UpdateService] Check failed: {ex.GetType().Name} - {ex.Message}");
            DebugLog.Info("UpdateService", $"[UpdateService] Stack trace: {ex.StackTrace}");
            return new UpdateCheckResult { ShouldShowUpdate = false };
        }
    }

    public async Task<UpdateProbeResult> ProbeUpdateAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = AppVersionHelper.NumericVersion;
        var result = new UpdateProbeResult
        {
            CurrentVersion = currentVersion,
            IsPreviewBuild = AppVersionHelper.IsPreviewBuild,
            UpdateInfoUrl = ApiEndpoints.UpdateHtmlUrl,
            PreviewUpdateInfoUrl = ApiEndpoints.UpdatePreHtmlUrl
        };

        try
        {
            var updateInfo = await FetchManifestAsync().WaitAsync(TimeSpan.FromSeconds(40), cancellationToken);
            if (updateInfo == null)
            {
                result.Message = "UpdateService_Unreachable".GetLocalized();
                return result;
            }

            result.Succeeded = true;
            result.ServerVersion = updateInfo.Version ?? string.Empty;
            result.PreviewVersion = updateInfo.PreReleaseVersion ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(updateInfo.UpdateInfoUrl))
            {
                result.UpdateInfoUrl = updateInfo.UpdateInfoUrl;
            }

            if (!string.IsNullOrWhiteSpace(updateInfo.PreReleaseUpdateInfoUrl))
            {
                result.PreviewUpdateInfoUrl = updateInfo.PreReleaseUpdateInfoUrl;
            }

            result.IsDevBuild = await _devBuildDetectionService.DetectAsync(result.ServerVersion);
            result.HasUpdate = AppVersionHelper.IsNewerVersion(result.ServerVersion, currentVersion);
            result.HasPreviewUpdate = AppVersionHelper.IsNewerVersion(result.PreviewVersion, currentVersion);
            result.Message = result.HasUpdate ? "UpdateService_NewVersionFound".GetLocalized() : "UpdateService_UpToDate".GetLocalized();

            DebugLog.Info("UpdateService",
                $"[UpdateService] Probe: current={currentVersion}, server={result.ServerVersion}, preview={result.PreviewVersion}, hasUpdate={result.HasUpdate}");
        }
        catch (OperationCanceledException)
        {
            result.Message = "UpdateService_CancelledOrTimeout".GetLocalized();
        }
        catch (Exception ex)
        {
            DebugLog.Info("UpdateService", $"[UpdateService] Probe failed: {ex.GetType().Name} - {ex.Message}");
            result.Message = string.Format("UpdateService_CheckFailed_Format".GetLocalized(), ex.Message);
        }

        return result;
    }

    private async Task<UpdateInfo?> FetchManifestAsync()
    {
        if (!await IsServerReachableAsync())
        {
            DebugLog.Info("UpdateService", "[UpdateService] Server temporarily unreachable, skipping version check");
            return null;
        }

        var json = await GetWithRetryAsync(ApiEndpoints.UpdateJsonUrl, ApiEndpoints.UpdateJsonFallbackUrl,
            maxRetries: 3);
        DebugLog.Info("UpdateService", $"[UpdateService] Server response: {json}");

        return JsonSerializer.Deserialize<UpdateInfo>(json);
    }

    public async Task<string> GetUpdateAnnouncementUrlAsync()
    {
        try
        {
            var json = await GetWithRetryAsync(ApiEndpoints.UpdateJsonUrl, ApiEndpoints.UpdateJsonFallbackUrl,
                maxRetries: 2);
            var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(json);
            var previewUrl = updateInfo?.PreReleaseUpdateInfoUrl;
            var announcementUrl = AppVersionHelper.IsPreviewBuild && !string.IsNullOrWhiteSpace(previewUrl)
                ? previewUrl
                : updateInfo?.UpdateInfoUrl;

            if (!string.IsNullOrWhiteSpace(announcementUrl))
            {
                announcementUrl = announcementUrl.Trim();
                if (Uri.TryCreate(announcementUrl, UriKind.Absolute, out var uri) &&
                    uri.Scheme is "http" or "https")
                    return announcementUrl;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("UpdateService",
                $"[UpdateService] Failed to get update announcement URL: {ex.GetType().Name} - {ex.Message}");
        }

        return string.Empty;
    }

    private async Task<bool> IsServerReachableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.GetAsync(ApiEndpoints.AgreementUrl,
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (response.IsSuccessStatusCode) return true;
        }
        catch
        {
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _httpClient.GetAsync(ApiEndpoints.AgreementFallbackUrl,
                HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GetWithRetryAsync(string url, int maxRetries)
    {
        return await GetWithRetryAsync(url, null, maxRetries);
    }

    private async Task<string> GetWithRetryAsync(string url, string? fallbackUrl, int maxRetries)
    {
        Exception? lastException = null;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                DebugLog.Info("UpdateService", $"[UpdateService] Request attempt {i + 1}/{maxRetries}: {url}");
                return await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                DebugLog.Info("UpdateService", $"[UpdateService] Attempt {i + 1} failed: {ex.Message}");
                lastException = ex;

                if (i == 0 && fallbackUrl != null)
                {
                    DebugLog.Info("UpdateService", $"[UpdateService] Switching to fallback URL: {fallbackUrl}");
                    url = fallbackUrl;
                    fallbackUrl = null;
                    continue;
                }

                if (i < maxRetries - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)));
                }
            }
        }

        throw lastException ?? new Exception("Update_AllRetriesFailed".GetLocalized());
    }

    private class UpdateInfo
    {
        [JsonPropertyName("Version")]
        public string Version
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("updateInfoUrl")]
        public string UpdateInfoUrl
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("PreReleaseVersion")]
        public string PreReleaseVersion
        {
            get;
            set;
        } = string.Empty;

        [JsonPropertyName("PreReleaseUpdateInfoUrl")]
        public string PreReleaseUpdateInfoUrl
        {
            get;
            set;
        } = string.Empty;
    }
}