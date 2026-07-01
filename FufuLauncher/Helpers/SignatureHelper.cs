/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Security.Cryptography;
using System.Text;

namespace FufuLauncher.Helpers;

public static class SignatureHelper
{
    public static string Md5(string input)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = md5.ComputeHash(bytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
    }

    public static string GetDsX6(string query = "", string body = "")
    {
        var salt = Constants.GenshinApiEndpoints.BbsX6Salt;
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var r = new Random().Next(100001, 200000).ToString();
        var b = string.IsNullOrEmpty(body) ? "" : body;
        var q = string.IsNullOrEmpty(query) ? "" : query;
        var c = Md5($"salt={salt}&t={t}&r={r}&b={b}&q={q}");
        return $"{t},{r},{c}";
    }

    public static string RandomString(int length)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var random = new Random();
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result);
    }
}

