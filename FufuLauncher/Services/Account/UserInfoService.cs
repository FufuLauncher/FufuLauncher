/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo;
using Microsoft.Extensions.Logging;

namespace FufuLauncher.Services;

public class UserInfoService : IUserInfoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserInfoService> _logger;
    private readonly IHoyolabRoleResolverService _hoyolabRoleResolverService;

    public UserInfoService(
        ILogger<UserInfoService> logger,
        IHoyolabRoleResolverService hoyolabRoleResolverService)
    {
        _logger = logger;
        _hoyolabRoleResolverService = hoyolabRoleResolverService;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    private void ApplyCommonHeaders(HttpRequestMessage request, string cookie)
    {
        var keys = new[]
        {
            "ltoken", "ltuid", "cookie_token", "account_id", "ltoken_v2", "ltuid_v2", "cookie_token_v2", "account_id_v2"
        };
        var found = keys.Where(k => cookie.Contains(k + "=", StringComparison.OrdinalIgnoreCase)).ToArray();
        var missing = keys.Where(k => !found.Contains(k, StringComparer.OrdinalIgnoreCase)).ToArray();
        DebugLog.Info("UserInfoService",
            $"[UserInfoService] Cookie length={cookie.Length}, found=[{string.Join(", ", found)}], missing=[{string.Join(", ", missing)}]");

        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("DS", GenerateDS());
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("Referer", ApiEndpoints.ActMihoyoBase + "/");
        request.Headers.TryAddWithoutValidation("Origin", ApiEndpoints.ActMihoyoBase);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36 miHoYoBBS/2.93.1");
    }

    private void ApplyOverseaHeaders(HttpRequestMessage request, string cookie)
    {
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", HeaderVersions.BbsOs254);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-language", "zh-cn");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgents.WindowsBbsOversea254);
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

    public async Task<GameRolesResponse> GetUserGameRolesAsync(string cookie, string serverType)
    {
        try
        {
            bool isOs = string.Equals(serverType, "os", StringComparison.OrdinalIgnoreCase);

            if (isOs)
            {
                var bindingRoles = await TryGetOverseaRolesFromBindingAsync(cookie);
                if (bindingRoles != null)
                    return new GameRolesResponse(0, "OK", new GameRolesData(bindingRoles));

                var rolesResult = await _hoyolabRoleResolverService.ResolveRolesAsync(cookie);
                return new GameRolesResponse(rolesResult.RetCode, rolesResult.Message,
                    new GameRolesData(rolesResult.Roles));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.MihoyoBbsUserGameRolesUrl);
            ApplyCommonHeaders(request, cookie);
            using var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            DebugLog.Info("UserInfoService",
                $"[UserInfoService] GameRoles HTTP {response.StatusCode} | Body({json?.Length ?? 0}): {(json?.Length > 300 ? json[..300] : json ?? "(null)")}");
            return JsonSerializer.Deserialize<GameRolesResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get role info");
            return new GameRolesResponse(-1, ex.Message, null);
        }
    }

    private async Task<List<GameRoleInfo>?> TryGetOverseaRolesFromBindingAsync(string cookie)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.OverseaUserGameRolesUrl);
            ApplyOverseaHeaders(request, cookie);
            using var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GameRolesResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null || result.retcode != 0 || result.data?.list == null || result.data.list.Count == 0)
                return null;

            return result.data.list
                .Select(role => string.IsNullOrEmpty(role.region)
                    ? role with { region = ServerRegion.Resolve(role.game_uid) }
                    : role)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "International binding role parse failed, falling back to RoleResolver");
            return null;
        }
    }


    public async Task<UserFullInfoResponse> GetUserFullInfoAsync(string cookie, string serverType)
    {
        try
        {
            bool isOs = string.Equals(serverType, "os", StringComparison.OrdinalIgnoreCase);
            var url = isOs ? ApiEndpoints.OverseaUserFullInfoUrl : ApiEndpoints.MiyousheUserFullInfoUrl;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (isOs)
                ApplyOverseaHeaders(request, cookie);
            else
                ApplyCommonHeaders(request, cookie);

            using var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            DebugLog.Info("UserInfoService",
                $"[UserInfoService] UserFullInfo HTTP {response.StatusCode} | URL: {url} | Body({json?.Length ?? 0}): {(json?.Length > 300 ? json[..300] : json ?? "(null)")}");
            return JsonSerializer.Deserialize<UserFullInfoResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info");
            return new UserFullInfoResponse(-1, ex.Message, null);
        }
    }

    public async Task<GameRecordCardResponse> GetGameRecordCardAsync(string stuid, string cookie)
    {
        return await Task.FromResult(new GameRecordCardResponse(-1, "UserInfo_FeatureRemoved".GetLocalized(), null));
    }
}