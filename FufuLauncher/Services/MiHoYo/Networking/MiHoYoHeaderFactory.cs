/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Models.MiHoYo.Identity;

namespace FufuLauncher.Services.MiHoYo.Networking;

public static class MiHoYoHeaderFactory
{
    public static string CalculateDs1(string salt, string query = "", string body = "")
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int rand = Random.Shared.Next(100000, 200000);
        string r = (rand == 100000 ? 642367 : rand).ToString();
        string hash = Convert
            .ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"salt={salt}&t={t}&r={r}&b={body}&q={query}")))
            .ToLowerInvariant();
        return $"{t},{r},{salt},{hash}";
    }

    public static string CalculateDs2(string salt, string query = "", string body = "")
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int rand = Random.Shared.Next(100000, 200000);
        string r = (rand == 100000 ? 642367 : rand).ToString();
        string hash = Convert
            .ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"salt={salt}&t={t}&r={r}&b={body}&q={query}")))
            .ToLowerInvariant();
        return $"{t},{r},{hash}";
    }

    public static string CalculateDs(string salt, string query, string body, string clientType)
        => clientType == ClientTypes.Other ? CalculateDs2(salt, query, body) : CalculateDs1(salt, query, body);

    public static string CalculateDsGen2(string salt, string body = "", string query = "")
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GenerateLowerAndNumberString(6);
        string hash = Convert
            .ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"salt={salt}&t={t}&r={r}&b={body}&q={query}")))
            .ToLowerInvariant();
        return $"{t},{r},{hash}";
    }

    private static string GenerateLowerAndNumberString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return string.Create(length, chars, static (span, state) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = state[Random.Shared.Next(state.Length)];
            }
        });
    }

    public static void ApplyGameRecordHeaders(HttpRequestMessage request, GameRecordHeaderOptions options)
    {
        request.Headers.Add(HeaderNames.Cookie, options.Cookie);
        request.Headers.Add(HeaderNames.RpcAppVersion, options.AppVersion);
        request.Headers.Add(HeaderNames.RpcClientType, ClientTypes.Other);
        request.Headers.Add(HeaderNames.RpcDeviceId, options.DeviceId);
        request.Headers.Add(HeaderNames.RpcDeviceName, options.DeviceName);
        request.Headers.Add(HeaderNames.RpcDeviceFp, options.DeviceFp);
        request.Headers.Add(HeaderNames.RpcSysVersion, options.SysVersion);
        if (!string.IsNullOrEmpty(options.ToolVersion))
            request.Headers.Add(HeaderNames.RpcToolVerison, options.ToolVersion);
        if (!string.IsNullOrEmpty(options.Page)) request.Headers.Add(HeaderNames.RpcPage, options.Page);
        request.Headers.Add("X-Requested-With", "com.mihoyo.hyperion");
        request.Headers.Add(HeaderNames.Origin, UserAgents.WebstaticReferer);
        if (options.ChallengeGame.HasValue)
            request.Headers.Add(HeaderNames.RpcChallengeGame, options.ChallengeGame.Value.ToString());
        if (!string.IsNullOrEmpty(options.ChallengePath))
            request.Headers.Add(HeaderNames.RpcChallengePath, options.ChallengePath);
        if (!string.IsNullOrEmpty(options.Challenge)) request.Headers.Add(HeaderNames.RpcChallenge, options.Challenge);
        request.Headers.Add(HeaderNames.DS, CalculateDs2(options.DsSalt, options.SortedQuery, options.Body));
        request.Headers.Add(HeaderNames.Referer, UserAgents.WebstaticReferer);
        request.Headers.Add(HeaderNames.Accept, "application/json, text/plain, */*");
        request.Headers.UserAgent.ParseAdd(options.UserAgent);
    }

    public static void ApplyDeviceFpHeaders(HttpRequestMessage request, string userAgent = UserAgents.OkHttp)
    {
        request.Headers.UserAgent.ParseAdd(userAgent);
    }

    internal static void ApplyDeviceIdentityHeaders(HttpRequestMessage request, DeviceIdentity device, string deviceId)
    {
        request.Headers.Add(HeaderNames.RpcDeviceId, deviceId);
        if (!string.IsNullOrEmpty(device.DeviceFp))
            request.Headers.Add(HeaderNames.RpcDeviceFp, device.DeviceFp);
        request.Headers.Add(HeaderNames.RpcDeviceName, Uri.EscapeDataString(device.DeviceName));
        request.Headers.Add(HeaderNames.RpcDeviceModel, Uri.EscapeDataString(device.Model));
        request.Headers.Add(HeaderNames.RpcSysVersion, device.SysVersion);
    }
}

public sealed record GameRecordHeaderOptions(
    string AppVersion,
    string UserAgent,
    string DeviceId,
    string DeviceFp,
    string DeviceName,
    string SysVersion,
    string Cookie,
    string DsSalt,
    string SortedQuery,
    string Body = "",
    string? Challenge = null,
    int? ChallengeGame = null,
    string? ChallengePath = null,
    string? ToolVersion = null,
    string? Page = null);
