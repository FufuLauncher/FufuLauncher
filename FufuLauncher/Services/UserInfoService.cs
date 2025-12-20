using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using Microsoft.Extensions.Logging;

namespace FufuLauncher.Services;

public class UserInfoService : IUserInfoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserInfoService> _logger;
    private readonly IUserConfigService _userConfigService;

    public UserInfoService(
        ILogger<UserInfoService> logger,
        IUserConfigService userConfigService)
    {
        _logger = logger;
        _userConfigService = userConfigService;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    private void ApplyCommonHeaders(string cookie)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookie);
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("DS", GenerateDS());
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-rpc-device_id", Guid.NewGuid().ToString("N"));
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("x-rpc-client_type", "5");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://act.mihoyo.com/");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://act.mihoyo.com");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36 miHoYoBBS/2.93.1"
        );
    }

    private string GenerateDS()
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var r = new Random().Next(100000, 200000).ToString();
        var c = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"salt=xV8v4Qu54lUKrEYFZkJhB8cuoh9NXmz9&t={t}&r={r}")
        );
        return $"{t},{r},{BitConverter.ToString(c).Replace("-", "").ToLower()}";
    }

    private string? ExtractCookieValue(string cookie, string key)
    {
        try
        {
            var pattern = $@"{key}=([^;]+)";
            var match = Regex.Match(cookie, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<GameRolesResponse> GetUserGameRolesAsync(string cookie)
    {
        try
        {
            ApplyCommonHeaders(cookie);
            var url = "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn";

            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<GameRolesResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new GameRolesResponse(-1, "解析失败", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取角色信息失败");
            return new GameRolesResponse(-1, ex.Message, null);
        }
    }

    public async Task<UserFullInfoResponse> GetUserFullInfoAsync(string cookie)
    {
        try
        {
            ApplyCommonHeaders(cookie);
            var url = "https://bbs-api.miyoushe.com/user/wapi/getUserFullInfo";

            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<UserFullInfoResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UserFullInfoResponse(-1, "解析失败", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取用户信息失败");
            return new UserFullInfoResponse(-1, ex.Message, null);
        }
    }

    public async Task<GameRecordCardResponse> GetGameRecordCardAsync(string stuid, string cookie)
    {
        return await Task.FromResult(new GameRecordCardResponse(-1, "功能已移除", null));
    }

    public async Task SaveUserDataAsync(string cookie, string stuid)
    {
        try
        {
            var rolesTask = GetUserGameRolesAsync(cookie);
            var userInfoTask = GetUserFullInfoAsync(cookie);

            await Task.WhenAll(rolesTask, userInfoTask);

            var roles = await rolesTask;
            var userInfo = await userInfoTask;

            await SaveAuthConfigAsync(cookie, stuid);

            if (roles?.data?.list?.FirstOrDefault() is { } role)
            {
                var displayConfig = new UserDisplayConfig
                {
                    Nickname = role.nickname,
                    GameUid = role.game_uid,
                    Server = role.region_name,
                    AvatarUrl = userInfo?.data?.user_info?.avatar_url ?? "ms-appx:///Assets/DefaultAvatar.png",
                    Level = role.level.ToString()
                };

                await _userConfigService.SaveDisplayConfigAsync(displayConfig);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存用户数据失败");
            throw;
        }
    }

    private async Task SaveAuthConfigAsync(string cookie, string stuid)
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            HoyoverseCheckinConfig oldConfig = new();

            if (File.Exists(configPath))
            {
                var json = await File.ReadAllTextAsync(configPath);
                oldConfig = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new HoyoverseCheckinConfig();
            }

            oldConfig.Account.Cookie = cookie;
            oldConfig.Account.Stuid = stuid;

            if (cookie.Contains("stoken="))
            {
                var match = Regex.Match(cookie, @"stoken=([^;]+)");
                if (match.Success) oldConfig.Account.Stoken = match.Groups[1].Value;
            }

            if (cookie.Contains("mid="))
            {
                var match = Regex.Match(cookie, @"mid=([^;]+)");
                if (match.Success) oldConfig.Account.Mid = match.Groups[1].Value;
            }

            var newJson = JsonSerializer.Serialize(oldConfig, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(configPath, newJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存认证配置失败");
        }
    }
}