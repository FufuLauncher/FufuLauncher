using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FufuLauncher.Services;

internal static class DynamicSecretProvider
{
    private const string Salt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";

    public static string Create(string query = "", object? body = null)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GetRandomString();

        string bodyStr = body != null ?
            JsonSerializer.Serialize(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            }) :
            "";

        string rawString = $"{t}&{r}&{query}&{bodyStr}&{Salt}";

        string c = MD5Hash(rawString);
        return $"{t},{r},{c}";
    }

    private static string GetRandomString()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new System.Random();
        return new string(Enumerable.Repeat(chars, 6)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }

    private static string MD5Hash(string input)
    {
        using var md5 = MD5.Create();
        byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}