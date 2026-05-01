using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;

namespace FufuLauncher.Services.Background
{
    public class BackgroundUrlInfo
    {
        public string Url { get; set; }
        public bool IsVideo { get; set; }
        public string ThumbnailUrl { get; set; }
        public string TypeText => IsVideo ? "视频" : "图片";
    }

    public interface IHoyoverseBackgroundService
    {
        Task<BackgroundUrlInfo> GetBackgroundUrlAsync(ServerType server, bool preferVideo);
        Task<List<BackgroundUrlInfo>> GetAvailableBackgroundsAsync(ServerType server);
    }

    public class HoyoverseBackgroundService : IHoyoverseBackgroundService
    {
        private static readonly HttpClient _httpClient = new();

        static HoyoverseBackgroundService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        private string ComputeMD5(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        public async Task<BackgroundUrlInfo> GetBackgroundUrlAsync(ServerType server, bool preferVideo)
        {
            try
            {
                var apiUrl = server switch
                {
                    ServerType.CN => ApiEndpoints.BackgroundCnApi,
                    ServerType.OS => ApiEndpoints.BackgroundOsApi,
                    _ => ApiEndpoints.BackgroundCnApi
                };

                var response = await _httpClient.GetStringAsync(apiUrl);
                var currentHash = ComputeMD5(response);
                
                var localSettings = App.GetService<ILocalSettingsService>();
                var savedHashObj = await localSettings.ReadSettingAsync("BackgroundJsonHash");
                string savedHash = savedHashObj?.ToString();
                
                if (!string.IsNullOrEmpty(savedHash) && savedHash != currentHash)
                {
                    Debug.WriteLine("HoyoverseBackgroundService: 识别到 JSON 发生变更，清空原先的背景切换");
                    await localSettings.SaveSettingAsync("SelectedOnlineBackgroundUrl", "");
                    await localSettings.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
                }
                
                await localSettings.SaveSettingAsync("BackgroundJsonHash", currentHash);
                
                var specificUrlObj = await localSettings.ReadSettingAsync("SelectedOnlineBackgroundUrl");
                string specificUrl = specificUrlObj?.ToString();
                if (!string.IsNullOrEmpty(specificUrl))
                {
                    var isVideoObj = await localSettings.ReadSettingAsync("SelectedOnlineBackgroundIsVideo");
                    bool isVideo = isVideoObj != null && Convert.ToBoolean(isVideoObj);
                    return new BackgroundUrlInfo { Url = specificUrl, IsVideo = isVideo };
                }
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var result = JsonSerializer.Deserialize<HoyoverseBackgroundResponse>(response, options);

                if (result?.Retcode != 0) return null;

                if (result.Data?.GameInfoList?.Length > 0)
                {
                    var backgrounds = result.Data.GameInfoList[0].Backgrounds;
                    if (backgrounds?.Length > 0)
                    {
                        var videoBg = backgrounds.FirstOrDefault(b => b.Type == "BACKGROUND_TYPE_VIDEO" && !string.IsNullOrEmpty(b.Video?.Url));
                        var staticBgs = backgrounds.Where(b => !string.IsNullOrEmpty(b.Background?.Url)).ToList();

                        if (preferVideo && videoBg != null)
                        {
                            return new BackgroundUrlInfo { Url = videoBg.Video.Url, IsVideo = true };
                        }

                        if (staticBgs.Count > 0)
                        {
                            var random = new Random();
                            var selectedBg = staticBgs[random.Next(staticBgs.Count)];
                            return new BackgroundUrlInfo { Url = selectedBg.Background.Url, IsVideo = false };
                        }
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HoyoverseBackgroundService: 请求异常 - {ex.Message}");
                return null;
            }
        }

        public async Task<List<BackgroundUrlInfo>> GetAvailableBackgroundsAsync(ServerType server)
        {
            try
            {
                var apiUrl = server switch
                {
                    ServerType.CN => ApiEndpoints.BackgroundCnApi,
                    ServerType.OS => ApiEndpoints.BackgroundOsApi,
                    _ => ApiEndpoints.BackgroundCnApi
                };

                var response = await _httpClient.GetStringAsync(apiUrl);
                
                var currentHash = ComputeMD5(response);
                var localSettings = App.GetService<ILocalSettingsService>();
                await localSettings.SaveSettingAsync("BackgroundJsonHash", currentHash);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var result = JsonSerializer.Deserialize<HoyoverseBackgroundResponse>(response, options);
                var list = new List<BackgroundUrlInfo>();

                if (result?.Retcode == 0 && result.Data?.GameInfoList?.Length > 0)
                {
                    var backgrounds = result.Data.GameInfoList[0].Backgrounds;
                    if (backgrounds?.Length > 0)
                    {
                        foreach (var b in backgrounds)
                        {
                            if (b.Type == "BACKGROUND_TYPE_VIDEO" && !string.IsNullOrEmpty(b.Video?.Url))
                            {
                                list.Add(new BackgroundUrlInfo 
                                { 
                                    Url = b.Video.Url, 
                                    IsVideo = true, 
                                    ThumbnailUrl = b.Background?.Url ?? b.Video.Url 
                                });
                            }
                            else if (!string.IsNullOrEmpty(b.Background?.Url))
                            {
                                list.Add(new BackgroundUrlInfo 
                                { 
                                    Url = b.Background.Url, 
                                    IsVideo = false, 
                                    ThumbnailUrl = b.Background.Url 
                                });
                            }
                        }
                    }
                }
                return list;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取可选背景异常: {ex.Message}");
                return new List<BackgroundUrlInfo>();
            }
        }
    }
}