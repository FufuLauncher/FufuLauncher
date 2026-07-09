/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class AnnouncementService : IAnnouncementService
{
    private readonly HttpClient _httpClient;
    private readonly ILocalSettingsService _localSettingsService;
    
    public AnnouncementService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }
    
    public async Task<string?> GetCurrentAnnouncementUrlAsync()
    {
        try
        {
            var json = await GetAnnouncementJsonAsync();
            var data = JsonSerializer.Deserialize<AnnouncementData>(json);

            if (data != null && !string.IsNullOrEmpty(data.Info))
            {
                return data.Info;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AnnouncementService] 获取当前公告URL异常: {ex.Message}");
            return null;
        }
    }

    private async Task<string> GetAnnouncementJsonAsync()
    {
        try
        {
            return await _httpClient.GetStringAsync(ApiEndpoints.AnnouncementUrl);
        }
        catch
        {
            return await _httpClient.GetStringAsync(ApiEndpoints.AnnouncementFallbackUrl);
        }
    }
    
    public async Task<string?> CheckForNewAnnouncementAsync()
    {
        try
        {
            var remoteUrl = await GetCurrentAnnouncementUrlAsync();

            if (string.IsNullOrEmpty(remoteUrl))
            {
                return null;
            }
            
            string localUrl = string.Empty;
            var cachedUrlObj = await _localSettingsService.ReadSettingAsync(LocalSettingsService.LastAnnouncementUrlKey);
            if (cachedUrlObj is string cachedUrl)
            {
                localUrl = cachedUrl;
            }
            
            if (!string.Equals(remoteUrl, localUrl, StringComparison.OrdinalIgnoreCase))
            {
                await _localSettingsService.SaveSettingAsync(LocalSettingsService.LastAnnouncementUrlKey, remoteUrl);
                return remoteUrl;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AnnouncementService] 检查公告更新逻辑异常: {ex.Message}");
            return null;
        }
    }
}
