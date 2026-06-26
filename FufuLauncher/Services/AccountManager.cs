/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using Microsoft.Extensions.DependencyInjection;
using MihoyoBBS;

namespace FufuLauncher.Services;

public class AccountManager
{

    private readonly string _dataDir;
    private readonly string _cookiesDir;
    private readonly string _accountsFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private AccountList _accountList;
    private string? _activeAccountId;
    public string? ActiveAccountId => _activeAccountId;
    public AccountManager()
    {
        _dataDir = Helpers.AppPaths.DataDir;
        _cookiesDir = Path.Combine(_dataDir, "cookies");
        _accountsFilePath = Path.Combine(_dataDir, "accounts.json");

        Directory.CreateDirectory(_cookiesDir);
        _accountList = new AccountList();
    }

    public async Task InitializeAsync()
    {
        await LoadAccountListAsync();

        // 检查并迁移旧账号数据
        if (HasLegacyAccounts())
        {
            await MigrateLegacyAccountsAsync();
        }
    }



    public AccountEntry GetActiveAccountEntry() =>
        _accountList.Accounts.FirstOrDefault(a => a.Id == _activeAccountId);

    public List<AccountEntry> GetAllAccounts() => _accountList.Accounts;

  
    private async Task LoadAccountListAsync()
    {
        if (File.Exists(_accountsFilePath))
        {
            var json = await File.ReadAllTextAsync(_accountsFilePath);
            _accountList = JsonSerializer.Deserialize<AccountList>(json) ?? new AccountList();
        }
        else
        {
            _accountList = new AccountList();
        }

        var normalizedAccounts = _accountList.Accounts
            .Where(a => !string.IsNullOrWhiteSpace(a?.Id))
            .GroupBy(a => a.Id)
            .Select(g => g.Last())
            .ToList();

        if (normalizedAccounts.Count != _accountList.Accounts.Count)
        {
            _accountList.Accounts = normalizedAccounts;
            await SaveAccountListAsync();
        }
        else
        {
            _accountList.Accounts = normalizedAccounts;
        }

        var settings = App.GetService<ILocalSettingsService>();
        try
        {
            var savedObj = await settings.ReadSettingAsync("ActiveAccountId");
            var savedId = savedObj as string;
            _activeAccountId = savedId ?? _accountList.Accounts.FirstOrDefault()?.Id;
        }
        catch
        {
            
            _activeAccountId = _accountList.Accounts.FirstOrDefault()?.Id;
        }
    }
    public async Task SetActiveAccountIdAsync(string? accountId)
    {
        _activeAccountId = accountId;
        var settings = App.GetService<ILocalSettingsService>();
        if (settings != null)
            await settings.SaveSettingAsync("ActiveAccountId", accountId ?? string.Empty);
    }
    public async Task LogoutAsync()
    {
        await SetActiveAccountIdAsync(null);
    }
    private async Task SaveAccountListAsync()
    {
        var json = JsonSerializer.Serialize(_accountList, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_accountsFilePath, json);
    }


    public async Task<AccountEntry> AddAccountAsync(
        Dictionary<string, string> cookies, string serverType, string nickname = "")
    {
        await _lock.WaitAsync();
        try
        {
            string stuid = ExtractStuid(cookies, serverType);
            string id = $"{serverType}_{stuid}";

            var existingEntry = _accountList.Accounts.FirstOrDefault(a => a.Id == id);
            if (existingEntry != null)
            {
                string existingCookiePath = Path.Combine(_cookiesDir, existingEntry.CookieFilePath);
                var existingCookiesJson = JsonSerializer.Serialize(cookies);
                await File.WriteAllTextAsync(existingCookiePath, existingCookiesJson);
                existingEntry.LastLoginTime = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(nickname))
                    existingEntry.Nickname = nickname;
                await SaveAccountListAsync();
                return existingEntry;
            }

            string cookieFileName = $"{id}.json";
            string cookiePath = Path.Combine(_cookiesDir, cookieFileName);
            var cookieJson = JsonSerializer.Serialize(cookies);
            await File.WriteAllTextAsync(cookiePath, cookieJson);

            var entry = new AccountEntry
            {
                Id = id,
                Stuid = stuid,
                Nickname = nickname,
                ServerType = serverType,
                CookieFilePath = cookieFileName,
                LastLoginTime = DateTime.Now
            };

            _accountList.Accounts.Add(entry);
            await SaveAccountListAsync();
            return entry;
        }
        finally
        {
            _lock.Release();
        }
    }


    public async Task<Dictionary<string, string>> LoadCookiesAsync(string accountId)
    {
        var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
        if (entry == null) return null;

        string path = Path.Combine(_cookiesDir, entry.CookieFilePath);
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }


    public async Task DeleteAccountAsync(string accountId)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (entry == null) return;

            string path = Path.Combine(_cookiesDir, entry.CookieFilePath);
            if (File.Exists(path)) File.Delete(path);

            _accountList.Accounts.Remove(entry);
            await SaveAccountListAsync();

            if (_activeAccountId == accountId)
            {
                var next = _accountList.Accounts.FirstOrDefault();
                await SetActiveAccountIdAsync(next?.Id);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

   
    public async Task<bool> SwitchAccountAsync(string accountId)
    {
        if (_accountList.Accounts.All(a => a.Id != accountId)) return false;
        await SetActiveAccountIdAsync(accountId);

        var entry = GetActiveAccountEntry();
        if (entry != null)
        {
            entry.LastLoginTime = DateTime.Now;
            await SaveAccountListAsync();
        }
        return true;
    }

   
    public async Task UpdateAccountMetaAsync(string accountId, string nickname, string avatarUrl, string gameUid = "")
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (entry != null)
            {
                entry.Nickname = nickname;
                entry.AvatarUrl = avatarUrl;
                if (!string.IsNullOrEmpty(gameUid))
                    entry.GameUid = gameUid;
                await SaveAccountListAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    
    private string ExtractStuid(Dictionary<string, string> cookies, string serverType)
    {
        if (serverType == "cn")
        {
            if (cookies.TryGetValue("ltuid", out var ltuid)) return ltuid;
            if (cookies.TryGetValue("stuid", out var stuid)) return stuid;
        }
        else
        {
            if (cookies.TryGetValue("ltuid_v2", out var ltuidV2)) return ltuidV2;
        }
        throw new ArgumentException("无法提取账户 ID");
    }
    public async Task UpdateCookiesAsync(string accountId, Dictionary<string, string> newCookies)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (entry == null) return;

            string cookiePath = Path.Combine(_cookiesDir, entry.CookieFilePath);
            var json = JsonSerializer.Serialize(newCookies);
            await File.WriteAllTextAsync(cookiePath, json);
        }
        finally
        {
            _lock.Release();
        }
    }

    #region 旧账号数据迁移

    private static Dictionary<string, string> ParseCookieString(string cookieString)
    {
        var cookieDict = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(cookieString))
            return cookieDict;

        var parts = cookieString.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex > 0)
            {
                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim();
                if (!string.IsNullOrEmpty(key))
                    cookieDict[key] = value;
            }
        }
        return cookieDict;
    }

    private bool HasLegacyAccounts()
    {
        if (!Directory.Exists(_dataDir))
            return false;

        var configFiles = Directory.GetFiles(_dataDir, "config*.json")
            .Where(f => !Path.GetFileName(f).Equals("accounts.json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return configFiles.Count > 0;
    }

    private static string DetermineServerTypeByFileName(string fileName)
    {
        return fileName.Contains(".lab", StringComparison.OrdinalIgnoreCase) ? "os" : "cn";
    }

    private async Task MigrateLegacyAccountsAsync()
    {
        System.Diagnostics.Debug.WriteLine("[AccountManager] 开始迁移旧账号数据...");

        try
        {
            var backupDir = Path.Combine(_dataDir, "legacy_accounts_backup");
            Directory.CreateDirectory(backupDir);

            var subAccountFiles = new List<string>();
            if (Directory.Exists(_dataDir))
            {
                subAccountFiles.AddRange(
                    Directory.GetFiles(_dataDir, "config*.json")
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
                        continue;

                    string serverType = DetermineServerTypeByFileName(fileName);
                    string accountId = $"{serverType}_{stuid}";

                    if (_accountList.Accounts.Any(a => a.Id == accountId))
                    {
                        System.Diagnostics.Debug.WriteLine($"[AccountManager] 账号 {accountId} 已存在，跳过迁移");
                        processed.Add(stuid);
                        continue;
                    }

                    string cookieFileName = $"{accountId}.json";
                    string cookiePath = Path.Combine(_cookiesDir, cookieFileName);
                    var cookieJson = JsonSerializer.Serialize(cookieDict);
                    await File.WriteAllTextAsync(cookiePath, cookieJson);

                    var entry = new AccountEntry
                    {
                        Id = accountId,
                        Stuid = stuid,
                        ServerType = serverType,
                        CookieFilePath = cookieFileName,
                        Nickname = config.Display?.Nickname ?? "",
                        AvatarUrl = config.Display?.AvatarUrl ?? "",
                        GameUid = config.Display?.GameUid ?? "",
                        LastLoginTime = DateTime.Now
                    };

                    _accountList.Accounts.Add(entry);
                    processed.Add(stuid);
                    migratedCount++;

                    // 迁移云游戏凭证到 LocalSettings
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
                            System.Diagnostics.Debug.WriteLine(
                                $"[AccountManager] 迁移云游戏凭证失败: {ex.Message}");
                        }
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[AccountManager] 已迁移账号: {accountId} ({entry.Nickname}) [{serverType}]");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AccountManager] 迁移文件 {configFile} 失败: {ex.Message}");
                }
            }

            if (migratedCount > 0)
            {
                await SaveAccountListAsync();
                System.Diagnostics.Debug.WriteLine(
                    $"[AccountManager] 迁移完成，共迁移 {migratedCount} 个账号");

                await MigrateActiveAccountAsync();
            }
            else if (hasOnlyMainConfig)
            {
                // 没有子账号文件，尝试从主 config.json / config.lab.json 迁移唯一账号
                var settings = App.GetService<ILocalSettingsService>();
                bool isInternationalAccount = false;
                try
                {
                    var isOsObj = await settings.ReadSettingAsync("IsInternationalAccount");
                    isInternationalAccount = isOsObj is bool b && b;
                }
                catch { }

                string mainConfigPath = isInternationalAccount
                    ? Path.Combine(_dataDir, "config.lab.json")
                    : Path.Combine(_dataDir, "config.json");

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
                                string cookiePath = Path.Combine(_cookiesDir, cookieFileName);
                                await File.WriteAllTextAsync(cookiePath, JsonSerializer.Serialize(cookieDict));

                                var entry = new AccountEntry
                                {
                                    Id = accountId,
                                    Stuid = stuid,
                                    ServerType = serverType,
                                    CookieFilePath = cookieFileName,
                                    Nickname = config.Display?.Nickname ?? "",
                                    AvatarUrl = config.Display?.AvatarUrl ?? "",
                                    GameUid = config.Display?.GameUid ?? "",
                                    LastLoginTime = DateTime.Now
                                };

                                _accountList.Accounts.Add(entry);
                                await SetActiveAccountIdAsync(accountId);
                                await SaveAccountListAsync();
                                migratedCount = 1;

                                // 迁移云游戏凭证
                                var cloudToken = config.Account.CloudComboToken;
                                if (!string.IsNullOrWhiteSpace(cloudToken))
                                {
                                    try
                                    {
                                        await settings.SaveSettingAsync($"CloudComboToken_{stuid}", cloudToken);
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine(
                                            $"[AccountManager] 迁移云游戏凭证失败: {ex.Message}");
                                    }
                                }

                                System.Diagnostics.Debug.WriteLine(
                                    $"[AccountManager] 已从主配置迁移唯一账号: {accountId} ({entry.Nickname}) [{serverType}]");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[AccountManager] 从主配置迁移账号失败: {ex.Message}");
                    }
                }

                if (migratedCount == 0)
                    System.Diagnostics.Debug.WriteLine("[AccountManager] 未找到需要迁移的账号");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[AccountManager] 未找到需要迁移的账号");
            }

            var allConfigFiles = Directory.GetFiles(_dataDir, "config*.json")
                .Where(f => !Path.GetFileName(f).Equals("accounts.json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var file in allConfigFiles)
            {
                try
                {
                    var backupFile = Path.Combine(backupDir, Path.GetFileName(file));
                    if (File.Exists(backupFile))
                        File.Delete(backupFile);
                    File.Move(file, backupFile);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AccountManager] 移动文件 {file} 失败: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"[AccountManager] 迁移流程结束，旧配置文件已移动到 legacy_accounts_backup/");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AccountManager] 迁移过程发生错误: {ex.Message}");
        }
    }

    private async Task MigrateActiveAccountAsync()
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
            catch { }

            string mainConfigPath = isInternationalAccount
                ? Path.Combine(_dataDir, "config.lab.json")
                : Path.Combine(_dataDir, "config.json");

            if (!File.Exists(mainConfigPath))
                return;

            var json = await File.ReadAllTextAsync(mainConfigPath);
            var config = JsonSerializer.Deserialize<Config>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config?.Account == null || string.IsNullOrWhiteSpace(config.Account.Stuid))
                return;

            string stuid = config.Account.Stuid;
            string serverType = isInternationalAccount ? "os" : "cn";
            string accountId = $"{serverType}_{stuid}";

            if (_accountList.Accounts.Any(a => a.Id == accountId))
            {
                await SetActiveAccountIdAsync(accountId);
                System.Diagnostics.Debug.WriteLine(
                    $"[AccountManager] 已迁移活跃账号: {accountId}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AccountManager] 旧活跃账号 {accountId} 不在迁移列表中，使用默认账号");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AccountManager] 迁移活跃账号失败: {ex.Message}");
        }
    }

    #endregion

}
