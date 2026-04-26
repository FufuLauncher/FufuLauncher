using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Models;

namespace FufuLauncher.Services.Background
{
    public interface IHoyoverseContentService
    {
        Task<ContentInfo> GetGameContentAsync(ServerType server);
    }

    public class HoyoverseContentService : IHoyoverseContentService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        static HoyoverseContentService()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            Debug.WriteLine("HoyoverseContentService: HttpClient 初始化完成");
        }
        

        public async Task<ContentInfo> GetGameContentAsync(ServerType server)
        {
            try
            {
                Debug.WriteLine($"HoyoverseContentService: 开始请求 {server} 公告内容");

                var apiUrl = server switch
                {
                    ServerType.CN => ApiEndpoints.ContentCnApi,
                    ServerType.OS => ApiEndpoints.ContentOsApi,
                    _ => ApiEndpoints.ContentCnApi
                };

                var response = await _httpClient.GetStringAsync(apiUrl);
                Debug.WriteLine($"HoyoverseContentService: 响应长度 {response.Length}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var result = JsonSerializer.Deserialize<HoyoverseContentResponse>(response, options);

                if (result?.Retcode != 0)
                {
                    Debug.WriteLine($"HoyoverseContentService: API 错误代码 {result?.Retcode}");
                    return null;
                }

                return result.Data?.Content;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HoyoverseContentService: 请求异常 - {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}