/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using FufuLauncher.Services.Security;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed record AccountCookieFile(
    [property: System.Text.Json.Serialization.JsonPropertyName("cookies")]
    Dictionary<string, string> Cookies);

public sealed record AccountStorageWarning(string Path, string Reason);

public partial class AccountManager
{
    private string DataDir => Helpers.AppPaths.DataDir;
    private string CookiesDir => Path.Combine(DataDir, "cookies");
    private string AccountsFilePath => Path.Combine(DataDir, "accounts.json");
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const int CookieFileVersion = 2;

    private readonly ConcurrentDictionary<string, List<GameRoleInfo>> _sessionGameRoles = new();
    private AccountList _accountList;
    private string? _activeAccountId;
    private AccountStorageWarning? _storageWarning;
    public string? ActiveAccountId => _activeAccountId;
    public AccountStorageWarning? StorageWarning => _storageWarning;

    public AccountManager()
    {
        try
        {
            Directory.CreateDirectory(CookiesDir);
        }
        catch (UnauthorizedAccessException ex)
        {
            DebugLog.Info("AccountManager", $"[AccountManager] Failed to create cookies directory: {ex.Message}");
        }
        catch (IOException ex)
        {
            DebugLog.Info("AccountManager",
                $"[AccountManager] IO error while creating cookies directory: {ex.Message}");
        }

        _accountList = new AccountList();
    }

    public async Task InitializeAsync()
    {
        CookieVault.EnsureKeyProtectionUpgrade(DataDir);
        await LoadAccountListAsync();

        if (HasLegacyAccounts())
        {
            await MigrateLegacyAccountsAsync();
        }
    }

    private void ReportStorageWarning(string path, Exception ex) =>
        _storageWarning ??= new AccountStorageWarning(path, ex.Message);

    public AccountEntry GetActiveAccountEntry() =>
        _accountList.Accounts.FirstOrDefault(a => a.Id == _activeAccountId);

    public List<AccountEntry> GetAllAccounts() => _accountList.Accounts;

    public List<GameRoleInfo>? GetSessionGameRoles(string accountId) =>
        _sessionGameRoles.TryGetValue(accountId, out var roles) ? roles : null;

    public void CacheSessionGameRoles(string accountId, IEnumerable<GameRoleInfo> roles) =>
        _sessionGameRoles[accountId] = roles.ToList();

    private async Task LoadAccountListAsync()
    {
        var accountListLoaded = true;

        if (File.Exists(AccountsFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(AccountsFilePath);
                _accountList = JsonSerializer.Deserialize<AccountList>(json) ?? new AccountList();
            }
            catch (JsonException ex)
            {
                DebugLog.Info("AccountManager",
                    $"[AccountManager] Failed to parse accounts.json, resetting account list: {ex.Message}");
                try
                {
                    var backupPath = AccountsFilePath + $".corrupt.{DateTime.Now:yyyyMMddHHmmss}.bak";
                    File.Copy(AccountsFilePath, backupPath, overwrite: true);
                }
                catch
                {
                }

                _accountList = new AccountList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                accountListLoaded = false;
                ReportStorageWarning(AccountsFilePath, ex);
                DebugLog.Warn("AccountManager",
                    $"[AccountManager] Unable to read accounts.json, continuing with an empty account list: {ex.Message}");
                _accountList = new AccountList();
            }
        }
        else
        {
            _accountList = new AccountList();
        }

        if (_accountList.Accounts == null)
            _accountList.Accounts = new List<AccountEntry>();

        var normalizedAccounts = _accountList.Accounts
            .Where(a => !string.IsNullOrWhiteSpace(a?.Id))
            .GroupBy(a => a.Id)
            .Select(g => g.Last())
            .ToList();

        bool metadataChanged = false;
        foreach (var account in normalizedAccounts)
        {
            bool changed = await EnsureCookieFileEncryptedAsync(account);
            if (changed)
                metadataChanged = true;

            if (account.UpdatedAt == default)
            {
                account.UpdatedAt = account.LastLoginTime == default ? DateTime.Now : account.LastLoginTime;
                metadataChanged = true;
            }
        }

        await EncryptOrphanCookieFilesAsync();

        bool accountListNeedsSave = normalizedAccounts.Count != _accountList.Accounts.Count || metadataChanged;
        _accountList.Accounts = normalizedAccounts;

        if (accountListLoaded && accountListNeedsSave)
        {
            try
            {
                await SaveAccountListAsync();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ReportStorageWarning(AccountsFilePath, ex);
                DebugLog.Warn("AccountManager",
                    $"[AccountManager] Unable to save account list: {ex.Message}");
            }
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
        _sessionGameRoles.Clear();
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
        await File.WriteAllTextAsync(AccountsFilePath, json);
    }
}