// FufuLauncher/Services/AnnouncementService.cs
using System.IO;
using System.Net.Http;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class AnnouncementService : IAnnouncementService
{
    private const string ApiUrl = "https://philia093.cyou/announcement.json";
    private const string CacheFileName = "announcement_cache.txt";
    private readonly string _cacheFilePath;
    private readonly HttpClient _httpClient;

    public AnnouncementService()
    {
        _cacheFilePath = Path.Combine(AppContext.BaseDirectory, CacheFileName);
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10); // 设置超时防止卡住
    }

    public async Task<string?> CheckForNewAnnouncementAsync()
    {
        try
        {
            // 1. 获取远程 JSON
            var json = await _httpClient.GetStringAsync(ApiUrl);
            var data = JsonSerializer.Deserialize<AnnouncementData>(json);

            if (data == null || string.IsNullOrEmpty(data.Info))
            {
                return null;
            }

            var remoteUrl = data.Info;

            // 2. 读取本地缓存 URL
            string localUrl = string.Empty;
            if (File.Exists(_cacheFilePath))
            {
                localUrl = await File.ReadAllTextAsync(_cacheFilePath);
            }

            // 3. 比较 URL
            // 如果 URL 不同，或者是第一次运行（本地没有文件），则返回 URL 并保存
            if (!string.Equals(remoteUrl, localUrl, StringComparison.OrdinalIgnoreCase))
            {
                // 保存新的 URL 到本地
                await File.WriteAllTextAsync(_cacheFilePath, remoteUrl);
                return remoteUrl;
            }

            return null; // URL 一致，不显示
        }
        catch (Exception ex)
        {
            // 网络错误或解析错误，忽略，不显示弹窗
            System.Diagnostics.Debug.WriteLine($"[AnnouncementService] Error: {ex.Message}");
            return null;
        }
    }
}