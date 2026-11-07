/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using FufuLauncher.Services.Security;
using MihoyoBBS;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class AccountManager
{
    #region 旧账号数据迁移

    private async Task<bool> EnsureCookieFileEncryptedAsync(AccountEntry account)
    {
        if (string.IsNullOrEmpty(account.CookieFilePath))
            return false;

        string path;
        try
        {
            path = ResolveCookiePath(account.CookieFilePath);
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Invalid cookie file path {account.CookieFilePath}: {ex.Message}");
            return false;
        }

        if (!File.Exists(path))
            return false;

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (CookieVault.IsEncryptedFile(bytes))
            {
                var read = await ReadCookieFileAsync(path);
                if (read == null)
                {
                    if (account.CookieVersion == 0)
                        return false;
                    account.CookieVersion = 0;
                    return true;
                }

                if (read.NeedsRewrite)
                {
                    await WriteCookieFileAsync(path, read.Cookies);
                    account.CookieVersion = CookieFileVersion;
                    DebugLog.Info("AccountManager.Migration",
                        $"[AccountManager] Converted cookie file storage format: {account.CookieFilePath}");
                    return true;
                }

                if (account.CookieVersion == CookieFileVersion)
                    return false;

                account.CookieVersion = CookieFileVersion;
                return true;
            }

            var cookies = ParseLegacyCookieJson(bytes);
            if (cookies == null)
            {
                RemoveUnreadableCookieFile(path);
                if (account.CookieVersion == 0)
                    return false;
                account.CookieVersion = 0;
                return true;
            }

            await WriteCookieFileAsync(path, cookies);
            account.CookieVersion = CookieFileVersion;
            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Auto-encrypted legacy cookie file: {account.CookieFilePath}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Cookie file auto-encryption failed {account.CookieFilePath}: {ex.Message}");
            return false;
        }
    }

    private static readonly EnumerationOptions CookieScanOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
    };

    private async Task EncryptOrphanCookieFilesAsync()
    {
        try
        {
            if (!Directory.Exists(CookiesDir))
                return;

            foreach (var path in Directory.EnumerateFiles(CookiesDir, "*.json", CookieScanOptions))
            {
                await TryEncryptOrphanCookieFileAsync(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportStorageWarning(CookiesDir, ex);
            DebugLog.Warn("AccountManager.Migration",
                $"[AccountManager] Skipped orphan cookie file scan: {CookiesDir} ({ex.Message})");
        }
    }

    private async Task TryEncryptOrphanCookieFileAsync(string path)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (CookieVault.IsEncryptedFile(bytes))
            {
                var read = await ReadCookieFileAsync(path);
                if (read?.NeedsRewrite == true)
                {
                    await WriteCookieFileAsync(path, read.Cookies);
                    DebugLog.Info("AccountManager.Migration",
                        $"[AccountManager] Converted cookie file storage format: {Path.GetFileName(path)}");
                }

                return;
            }

            var cookies = ParseLegacyCookieJson(bytes);
            if (cookies == null)
            {
                RemoveUnreadableCookieFile(path);
                return;
            }

            await WriteCookieFileAsync(path, cookies);
            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Auto-encrypted legacy cookie file: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Failed to auto-encrypt legacy cookie file {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static void DeleteLegacyFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return;

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                var buffer = new byte[65536];
                long remaining = stream.Length;
                while (remaining > 0)
                {
                    int count = (int)Math.Min(buffer.Length, remaining);
                    RandomNumberGenerator.Fill(buffer.AsSpan(0, count));
                    stream.Write(buffer, 0, count);
                    remaining -= count;
                }

                stream.Flush(true);
            }
            catch (Exception ex)
            {
                DebugLog.Info("AccountManager.Migration",
                    $"[AccountManager] Failed to overwrite legacy config {Path.GetFileName(path)}: {ex.Message}");
            }

            File.Delete(path);
            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Removed migrated legacy config: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Failed to remove legacy config {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private bool HasLegacyAccounts()
    {
        try
        {
            if (!Directory.Exists(DataDir))
                return false;

            return Directory.EnumerateFiles(DataDir, "config*.json", SearchOption.TopDirectoryOnly)
                .Any(f => !Path.GetFileName(f).Equals("accounts.json", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ReportStorageWarning(DataDir, ex);
            DebugLog.Warn("AccountManager.Migration",
                $"[AccountManager] Skipped legacy account data scan: {DataDir} ({ex.Message})");
            return false;
        }
    }

    private async Task MigrateLegacyAccountsAsync()
    {
        DebugLog.Info("AccountManager.Migration", "[AccountManager] Starting legacy account data migration...");

        try
        {
            var subAccountFiles = new List<string>();
            if (Directory.Exists(DataDir))
            {
                subAccountFiles.AddRange(
                    Directory.GetFiles(DataDir, "config*.json")
                        .Where(f =>
                        {
                            var name = Path.GetFileName(f);
                            return !name.Equals("config.json", StringComparison.OrdinalIgnoreCase) &&
                                   !name.Equals("config.lab.json", StringComparison.OrdinalIgnoreCase) &&
                                   !name.Equals("accounts.json", StringComparison.OrdinalIgnoreCase);
                        })
                );
            }

            bool hasOnlyMainConfig = subAccountFiles.Count == 0;

            var processed = new HashSet<string>();
            var migratedConfigFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int migratedCount = 0;

            foreach (var configFile in subAccountFiles)
            {
                try
                {
                    var fileName = Path.GetFileName(configFile);
                    var json = await File.ReadAllTextAsync(configFile);
                    var config = JsonSerializer.Deserialize<Config>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (config?.Account == null || string.IsNullOrWhiteSpace(config.Account.Cookie))
                        continue;

                    var cookieDict = ParseCookieString(config.Account.Cookie);
                    if (cookieDict.Count == 0)
                        continue;

                    string stuid = config.Account.Stuid;
                    if (string.IsNullOrWhiteSpace(stuid))
                    {
                        if (cookieDict.TryGetValue("ltuid", out var ltuid))
                            stuid = ltuid;
                        else if (cookieDict.TryGetValue("ltuid_v2", out var ltuidV2))
                            stuid = ltuidV2;
                    }

                    if (string.IsNullOrWhiteSpace(stuid))
                        continue;

                    if (processed.Contains(stuid))
                    {
                        migratedConfigFiles.Add(configFile);
                        continue;
                    }

                    string serverType = DetermineServerTypeByFileName(fileName);
                    string accountId = $"{serverType}_{stuid}";

                    if (_accountList.Accounts.Any(a => a.Id == accountId))
                    {
                        DebugLog.Info("AccountManager.Migration",
                            $"[AccountManager] Account {accountId} already exists, migration confirmed");
                        processed.Add(stuid);
                        migratedConfigFiles.Add(configFile);
                        continue;
                    }

                    string cookieFileName = $"{accountId}.json";
                    string cookiePath = ResolveCookiePath(cookieFileName);
                    await WriteCookieFileAsync(cookiePath, cookieDict);

                    var entry = new AccountEntry
                    {
                        Id = accountId,
                        Stuid = stuid,
                        ServerType = serverType,
                        CookieFilePath = cookieFileName,
                        Nickname = config.Display?.Nickname ?? "",
                        AvatarUrl = config.Display?.AvatarUrl ?? "",
                        GameUid = config.Display?.GameUid ?? "",
                        LastLoginTime = DateTime.Now,
                        CookieVersion = CookieFileVersion,
                        UpdatedAt = DateTime.Now
                    };

                    _accountList.Accounts.Add(entry);
                    processed.Add(stuid);
                    migratedConfigFiles.Add(configFile);
                    migratedCount++;


                    var cloudToken = config.Account.CloudComboToken;
                    if (!string.IsNullOrWhiteSpace(cloudToken))
                    {
                        try
                        {
                            var settings = App.GetService<ILocalSettingsService>();
                            await settings.SaveSettingAsync($"CloudComboToken_{stuid}", cloudToken);
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Info("AccountManager.Migration",
                                $"[AccountManager] Failed to migrate cloud game credentials: {ex.Message}");
                        }
                    }

                    DebugLog.Info("AccountManager.Migration",
                        $"[AccountManager] Migrated account: {accountId} ({entry.Nickname}) [{serverType}]");
                }
                catch (Exception ex)
                {
                    DebugLog.Info("AccountManager.Migration",
                        $"[AccountManager] Failed to migrate file {configFile}: {ex.Message}");
                }
            }

            if (migratedCount > 0)
            {
                await SaveAccountListAsync();
                DebugLog.Info("AccountManager.Migration",
                    $"[AccountManager] Migration complete, {migratedCount} account(s) migrated");
            }

            if (migratedCount > 0 || migratedConfigFiles.Count > 0)
            {
                var activeConfigFile = await MigrateActiveAccountAsync();
                if (activeConfigFile != null)
                    migratedConfigFiles.Add(activeConfigFile);
            }
            else if (hasOnlyMainConfig)
            {
                var settings = App.GetService<ILocalSettingsService>();
                bool isInternationalAccount = false;
                try
                {
                    var isOsObj = await settings.ReadSettingAsync("IsInternationalAccount");
                    isInternationalAccount = isOsObj is bool b && b;
                }
                catch
                {
                }

                string mainConfigPath = isInternationalAccount
                    ? Path.Combine(DataDir, "config.lab.json")
                    : Path.Combine(DataDir, "config.json");

                if (File.Exists(mainConfigPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(mainConfigPath);
                        var config = JsonSerializer.Deserialize<Config>(json,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (config?.Account != null && !string.IsNullOrWhiteSpace(config.Account.Cookie))
                        {
                            var cookieDict = ParseCookieString(config.Account.Cookie);
                            string stuid = config.Account.Stuid;
                            if (string.IsNullOrWhiteSpace(stuid))
                            {
                                if (cookieDict.TryGetValue("ltuid", out var ltuid))
                                    stuid = ltuid;
                                else if (cookieDict.TryGetValue("ltuid_v2", out var ltuidV2))
                                    stuid = ltuidV2;
                            }

                            if (!string.IsNullOrWhiteSpace(stuid) && cookieDict.Count > 0)
                            {
                                string serverType = isInternationalAccount ? "os" : "cn";
                                string accountId = $"{serverType}_{stuid}";

                                string cookieFileName = $"{accountId}.json";
                                string cookiePath = ResolveCookiePath(cookieFileName);
                                await WriteCookieFileAsync(cookiePath, cookieDict);

                                var entry = new AccountEntry
                                {
                                    Id = accountId,
                                    Stuid = stuid,
                                    ServerType = serverType,
                                    CookieFilePath = cookieFileName,
                                    Nickname = config.Display?.Nickname ?? "",
                                    AvatarUrl = config.Display?.AvatarUrl ?? "",
                                    GameUid = config.Display?.GameUid ?? "",
                                    LastLoginTime = DateTime.Now,
                                    CookieVersion = CookieFileVersion,
                                    UpdatedAt = DateTime.Now
                                };

                                _accountList.Accounts.Add(entry);
                                await SaveAccountListAsync();
                                await SetActiveAccountIdAsync(accountId);
                                migratedConfigFiles.Add(mainConfigPath);
                                migratedCount = 1;


                                var cloudToken = config.Account.CloudComboToken;
                                if (!string.IsNullOrWhiteSpace(cloudToken))
                                {
                                    try
                                    {
                                        await settings.SaveSettingAsync($"CloudComboToken_{stuid}", cloudToken);
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLog.Info("AccountManager.Migration",
                                            $"[AccountManager] Failed to migrate cloud game credentials: {ex.Message}");
                                    }
                                }

                                DebugLog.Info("AccountManager.Migration",
                                    $"[AccountManager] Migrated single account from main config: {accountId} ({entry.Nickname}) [{serverType}]");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("AccountManager.Migration",
                            $"[AccountManager] Failed to migrate account from main config: {ex.Message}");
                    }
                }

                if (migratedCount == 0)
                    DebugLog.Info("AccountManager.Migration", "[AccountManager] No accounts found to migrate");
            }
            else
            {
                DebugLog.Info("AccountManager.Migration", "[AccountManager] No accounts found to migrate");
            }

            foreach (var file in migratedConfigFiles)
            {
                DeleteLegacyFile(file);
            }

            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Migration finished, {migratedCount} account(s) migrated");
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Migration", $"[AccountManager] Error during migration: {ex.Message}");
        }
    }

    private async Task<string?> MigrateActiveAccountAsync()
    {
        try
        {
            var settings = App.GetService<ILocalSettingsService>();

            bool isInternationalAccount = false;
            try
            {
                var isOsObj = await settings.ReadSettingAsync("IsInternationalAccount");
                isInternationalAccount = isOsObj is bool b && b;
            }
            catch
            {
            }

            string mainConfigPath = isInternationalAccount
                ? Path.Combine(DataDir, "config.lab.json")
                : Path.Combine(DataDir, "config.json");

            if (!File.Exists(mainConfigPath))
                return null;

            var json = await File.ReadAllTextAsync(mainConfigPath);
            var config = JsonSerializer.Deserialize<Config>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config?.Account == null || string.IsNullOrWhiteSpace(config.Account.Stuid))
                return null;

            string stuid = config.Account.Stuid;
            string serverType = isInternationalAccount ? "os" : "cn";
            string accountId = $"{serverType}_{stuid}";

            if (_accountList.Accounts.Any(a => a.Id == accountId))
            {
                await SetActiveAccountIdAsync(accountId);
                DebugLog.Info("AccountManager.Migration",
                    $"[AccountManager] Migrated active account: {accountId}");
                return mainConfigPath;
            }

            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Legacy active account {accountId} not in migration list, using default account");
            return null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountManager.Migration",
                $"[AccountManager] Failed to migrate active account: {ex.Message}");
            return null;
        }
    }

    #endregion
}