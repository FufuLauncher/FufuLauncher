using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class AnnouncementService : IAnnouncementService
{
    private const string ApiUrl = "https://philia093.cyou/announcement.json";
    private readonly HttpClient _httpClient;
    private readonly ILocalSettingsService _localSettingsService;
    
    public AnnouncementService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<string?> CheckForNewAnnouncementAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync(ApiUrl);
            var data = JsonSerializer.Deserialize<AnnouncementData>(json);

            if (data == null || string.IsNullOrEmpty(data.Info))
            {
                return null;
            }

            var remoteUrl = data.Info;
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
            System.Diagnostics.Debug.WriteLine($"[AnnouncementService] Error: {ex.Message}");
            return null;
        }
    }
}