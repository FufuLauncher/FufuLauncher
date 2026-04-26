using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Models;

namespace FufuLauncher.Services.Background
{
    public class BackgroundUrlInfo
    {
        public string Url
        {
            get; set;
        }
        public bool IsVideo
        {
            get; set;
        }
    }

    public interface IHoyoverseBackgroundService
    {
        Task<BackgroundUrlInfo> GetBackgroundUrlAsync(ServerType server, bool preferVideo);
    }

    public class HoyoverseBackgroundService : IHoyoverseBackgroundService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        static HoyoverseBackgroundService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            Debug.WriteLine("HoyoverseBackgroundService: HttpClient 初始化完成");
        }
        

        public async Task<BackgroundUrlInfo> GetBackgroundUrlAsync(ServerType server, bool preferVideo)
        {
            try
            {
                Debug.WriteLine($"HoyoverseBackgroundService: 开始请求 {server} 背景");
                
                var apiUrl = server switch
                {
                    ServerType.CN => ApiEndpoints.BackgroundCnApi,
                    ServerType.OS => ApiEndpoints.BackgroundOsApi,
                    _ => ApiEndpoints.BackgroundCnApi
                };

                Debug.WriteLine($"HoyoverseBackgroundService: 请求 URL: {apiUrl}");

                var response = await _httpClient.GetStringAsync(apiUrl);
                Debug.WriteLine($"HoyoverseBackgroundService: 响应长度 {response.Length}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var result = JsonSerializer.Deserialize<HoyoverseBackgroundResponse>(response, options);

                if (result?.Retcode != 0)
                {
                    Debug.WriteLine($"HoyoverseBackgroundService: API 错误代码 {result?.Retcode}");
                    return null;
                }

                if (result.Data?.GameInfoList?.Length > 0)
                {
                    var backgrounds = result.Data.GameInfoList[0].Backgrounds;

                    if (backgrounds?.Length > 0)
                    {
                        var videoBg = backgrounds.FirstOrDefault(b =>
                            b.Type == "BACKGROUND_TYPE_VIDEO" &&
                            !string.IsNullOrEmpty(b.Video?.Url));

                        // 核心修改点：获取所有包含有效背景图片的项，转为列表
                        var staticBgs = backgrounds
                            .Where(b => !string.IsNullOrEmpty(b.Background?.Url))
                            .ToList();

                        if (preferVideo && videoBg != null)
                        {
                            Debug.WriteLine($"HoyoverseBackgroundService: 返回视频 URL: {videoBg.Video.Url}");
                            return new BackgroundUrlInfo { Url = videoBg.Video.Url, IsVideo = true };
                        }
                        else if (staticBgs.Count > 0)
                        {
                            // 核心修改点：使用 Random 随机挑选一个背景
                            var random = new Random();
                            var selectedBg = staticBgs[random.Next(staticBgs.Count)];

                            Debug.WriteLine($"HoyoverseBackgroundService: 随机返回静态 URL: {selectedBg.Background.Url}");
                            return new BackgroundUrlInfo { Url = selectedBg.Background.Url, IsVideo = false };
                        }
                    }
                }

                Debug.WriteLine("HoyoverseBackgroundService: 未找到背景数据");
                return null;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"HoyoverseBackgroundService: JSON 解析失败 - {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HoyoverseBackgroundService: 请求异常 - {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}