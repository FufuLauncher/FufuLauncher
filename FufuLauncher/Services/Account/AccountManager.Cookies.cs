using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using FufuLauncher.Services.Security;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class AccountManager
{
    private static readonly JsonSerializerOptions CookieJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record CookieReadResult(Dictionary<string, string> Cookies, bool NeedsRewrite);

    private async Task WriteCookieFileAsync(string path, Dictionary<string, string> cookies)
    {
        var file = new AccountCookieFile(cookies);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(file, CookieJsonOptions);
        byte[] encrypted;
        try
        {
            encrypted = CookieVault.Encrypt(DataDir, Path.GetFileName(path), plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        try
        {
            await WriteBytesAtomicAsync(path, encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    private static async Task WriteBytesAtomicAsync(string path, byte[] bytes)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException("Account_CookiePathInvalid".GetLocalized());

        Directory.CreateDirectory(directory);
        string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch
            {
            }
        }
    }

    private async Task<CookieReadResult?> ReadCookieFileAsync(string path)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Cookies",
                $"[AccountManager] Failed to read cookie file {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }

        try
        {
            if (CookieVault.IsEncryptedFile(bytes))
            {
                var plaintext = CookieVault.Decrypt(DataDir, Path.GetFileName(path), bytes);
                try
                {
                    var file = JsonSerializer.Deserialize<AccountCookieFile>(plaintext, CookieJsonOptions)
                               ?? throw new JsonException("Account_CookieContentInvalid".GetLocalized());
                    return new CookieReadResult(file.Cookies ?? new Dictionary<string, string>(),
                        CookieVault.IsLegacyJsonEnvelope(bytes)
                        || CookieVault.IsLegacyBinaryEnvelope(bytes));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }

            var cookies = ParseLegacyCookieJson(bytes);
            if (cookies == null)
            {
                RemoveUnreadableCookieFile(path);
                return null;
            }

            return new CookieReadResult(cookies, true);
        }
        catch (Exception ex) when (ex is JsonException or CookieVaultException or CryptographicException
                                       or NotSupportedException)
        {
            DebugLog.Info("AccountManager.Cookies",
                $"[AccountManager] Failed to parse cookie file {Path.GetFileName(path)}: {ex.Message}");
            RemoveUnreadableCookieFile(path);
            return null;
        }
    }

    private static void RemoveUnreadableCookieFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            File.Delete(path);
            DebugLog.Info("AccountManager.Cookies",
                $"[AccountManager] Removed undecryptable cookie file: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Cookies",
                $"[AccountManager] Failed to remove undecryptable cookie file {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static Dictionary<string, string>? ParseLegacyCookieJson(byte[] bytes)
    {
        try
        {
            ReadOnlyMemory<byte> memory = bytes;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                memory = memory[3..];

            using var document = JsonDocument.Parse(memory);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return JsonSerializer.Deserialize<Dictionary<string, string>>(memory.Span);

            if (TryGetPropertyIgnoreCase(root, "format", out var formatProp)
                && formatProp.ValueKind == JsonValueKind.String
                && string.Equals(formatProp.GetString(), CookieVault.FormatName, StringComparison.Ordinal))
                return null;

            if (TryGetPropertyIgnoreCase(root, "cookies", out var cookiesProp)
                && cookiesProp.ValueKind == JsonValueKind.Object)
                return ReadStringDictionary(cookiesProp);

            if (TryGetPropertyIgnoreCase(root, "values", out var valuesProp)
                && valuesProp.ValueKind == JsonValueKind.Object)
                return ReadStringDictionary(valuesProp);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(memory.Span);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<Dictionary<string, string>> LoadCookiesAsync(string accountId)
    {
        var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (entry == null) return null;

        string path;
        try
        {
            path = ResolveCookiePath(entry.CookieFilePath);
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Cookies",
                $"[AccountManager] Invalid cookie file path {accountId}: {ex.Message}");
            return null;
        }

        if (!File.Exists(path)) return null;

        var result = await ReadCookieFileAsync(path);
        if (result == null)
        {
            if (!File.Exists(path) && entry.CookieVersion != 0)
            {
                entry.CookieVersion = 0;
                entry.UpdatedAt = DateTime.Now;
            }

            return null;
        }

        if (!result.NeedsRewrite) return result.Cookies;

        await _lock.WaitAsync();
        try
        {
            var current = await ReadCookieFileAsync(path);
            if (current == null)
            {
                if (!File.Exists(path) && entry.CookieVersion != 0)
                {
                    entry.CookieVersion = 0;
                    entry.UpdatedAt = DateTime.Now;
                }

                return null;
            }

            if (!current.NeedsRewrite) return current.Cookies;

            try
            {
                await WriteCookieFileAsync(path, current.Cookies);
                entry.CookieVersion = CookieFileVersion;
                entry.UpdatedAt = DateTime.Now;
                DebugLog.Info("AccountManager.Cookies",
                    $"[AccountManager] Updated cookie file storage format: {Path.GetFileName(path)}");
                return current.Cookies;
            }
            catch (Exception ex)
            {
                DebugLog.Info("AccountManager.Cookies",
                    $"[AccountManager] Cookie file auto-encryption failed {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> UpdateCookiesAsync(string accountId, IReadOnlyDictionary<string, string> expectedCookies,
        Dictionary<string, string> newCookies)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (entry == null) return false;

            string cookiePath = ResolveCookiePath(entry.CookieFilePath);
            if (!File.Exists(cookiePath)) return false;
            var current = await ReadCookieFileAsync(cookiePath);
            if (current == null || current.Cookies.Count != expectedCookies.Count ||
                !expectedCookies.All(pair => current.Cookies.TryGetValue(pair.Key, out var value) && value == pair.Value))
                return false;

            await WriteCookieFileAsync(cookiePath, newCookies);
            entry.CookieVersion = CookieFileVersion;
            entry.UpdatedAt = DateTime.Now;
            await SaveAccountListAsync();
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void TryDeleteCookieKeyIfUnused()
    {
        try
        {
            if (Directory.Exists(CookiesDir)
                && Directory.EnumerateFiles(CookiesDir, "*.json", SearchOption.AllDirectories).Any())
                return;
            CookieVault.DeleteKey(DataDir);
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Cookies",
                $"[AccountManager] Failed to clean up cookie encryption key: {ex.Message}");
        }
    }
}
