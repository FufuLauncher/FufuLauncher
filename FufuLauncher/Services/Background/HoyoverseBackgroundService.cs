using System.Diagnostics;
using System.Text.Json;
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

        private const string CN_API = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo?launcher_id=jGHBHlcOq1&language=zh-cn&game_id=1Z8W5NHUQb";
        private const string OS_API = "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getAllGameBasicInfo?launcher_id=VYTpXlbWo8&game_id=gopR6Cufr3&language=zh-cn";

        public async Task<BackgroundUrlInfo> GetBackgroundUrlAsync(ServerType server, bool preferVideo)
        {
            try
            {
                Debug.WriteLine($"HoyoverseBackgroundService: 开始请求 {server} 背景");

                var apiUrl = server switch
                {
                    ServerType.CN => CN_API,
                    ServerType.OS => OS_API,
                    _ => CN_API
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

                        var staticBg = backgrounds.FirstOrDefault(b =>
                            !string.IsNullOrEmpty(b.Background?.Url));

                        if (preferVideo && videoBg != null)
                        {
                            Debug.WriteLine($"HoyoverseBackgroundService: 返回视频 URL: {videoBg.Video.Url}");
                            return new BackgroundUrlInfo { Url = videoBg.Video.Url, IsVideo = true };
                        }
                        else if (staticBg != null)
                        {
                            Debug.WriteLine($"HoyoverseBackgroundService: 返回静态 URL: {staticBg.Background.Url}");
                            return new BackgroundUrlInfo { Url = staticBg.Background.Url, IsVideo = false };
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