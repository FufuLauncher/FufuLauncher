using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Views;

namespace FufuLauncher.ViewModels;

public partial class AccountViewModel : ObservableRecipient
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IUserInfoService _userInfoService;
    private readonly IUserConfigService _userConfigService;
    private readonly INavigationService _navigationService;
    private const int MaxAccounts = 4;
    private Dictionary<string, string> _accountFileMap = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    [NotifyPropertyChangedFor(nameof(IsNotLoggedIn))]
    private AccountInfo? _currentAccount;

    public bool IsLoggedIn => CurrentAccount != null;
    public bool IsNotLoggedIn => CurrentAccount == null;
    public IRelayCommand OpenSecurityCenterCommand { get; }

    [ObservableProperty] private string _loginButtonText = "登录米游社";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private GameRolesResponse? _gameRolesInfo;
    [ObservableProperty] private UserFullInfoResponse? _userFullInfo;
    [ObservableProperty] private bool _isLoadingUserInfo;

    [ObservableProperty] private ObservableCollection<AccountInfo> _savedAccounts = new();
    public bool HasSavedAccounts => SavedAccounts.Count > 0;
    public IRelayCommand LockAccountCommand { get; }

    public IRelayCommand LoginCommand { get; }
    public IRelayCommand LogoutCommand { get; }
    public IRelayCommand LoadUserInfoCommand { get; }
    public IRelayCommand OpenGenshinDataCommand { get; }
    public IRelayCommand CopyCookieCommand { get; }
    public IRelayCommand AddAccountCommand { get; }
    public IRelayCommand<AccountInfo> SwitchAccountCommand { get; }

    public AccountViewModel(
        ILocalSettingsService localSettingsService,
        IUserInfoService userInfoService,
        IUserConfigService userConfigService,
        INavigationService navigationService)
    {
        _localSettingsService = localSettingsService;
        _userInfoService = userInfoService;
        _userConfigService = userConfigService;
        _navigationService = navigationService;

        LoginCommand = new AsyncRelayCommand(LoginAsync);
        LogoutCommand = new RelayCommand(Logout);
        LoadUserInfoCommand = new AsyncRelayCommand(LoadUserInfoAsync);
        OpenGenshinDataCommand = new AsyncRelayCommand(OpenGenshinDataAsync);
        CopyCookieCommand = new AsyncRelayCommand(CopyCookieAsync);
        AddAccountCommand = new AsyncRelayCommand(AddNewAccountAsync);
        SwitchAccountCommand = new AsyncRelayCommand<AccountInfo>(SwitchToAccountAsync);
        OpenSecurityCenterCommand = new AsyncRelayCommand(OpenSecurityCenterAsync);
        LockAccountCommand = new AsyncRelayCommand(LockAccountAsync);
        _ = LoadAccountInfo();
    }
    
    private async Task LockAccountAsync()
    {
        await OpenSecurityWindowInternalAsync(ApiEndpoints.AccountLockUrl, "正在打开账号冻结页面...");
    }

    private async Task CopyCookieAsync()
    {
        try
        {
            var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
            string activeFile = activeFileObj?.ToString() ?? "config.json";
            var configPath = Path.Combine(Helpers.AppPaths.DataDir, activeFile);

            if (File.Exists(configPath))
            {
                var json = await File.ReadAllTextAsync(configPath);
                var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (!string.IsNullOrEmpty(config?.Account?.Cookie))
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(config.Account.Cookie);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                    StatusMessage = "Cookie 已复制到剪切板";
                    
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("复制成功", "Cookie 已成功复制到剪贴板", NotificationType.Success));
                    return;
                }
            }
            StatusMessage = "未找到有效的 Cookie";
            
            WeakReferenceMessenger.Default.Send(new NotificationMessage("复制失败", "未找到有效的 Cookie", NotificationType.Error));
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制失败: {ex.Message}";
            
            WeakReferenceMessenger.Default.Send(new NotificationMessage("复制失败", ex.Message, NotificationType.Error));
        }
    }
    
    private async Task OpenSecurityCenterAsync()
    {
        await OpenSecurityWindowInternalAsync(ApiEndpoints.AccountSecurityUrl, "正在打开账号安全中心...");
    }
    
    private async Task OpenSecurityWindowInternalAsync(string url, string loadingMsg)
    {
        try
        {
            StatusMessage = loadingMsg;
            var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
            string activeFile = activeFileObj?.ToString() ?? "config.json";
            var configPath = Path.Combine(Helpers.AppPaths.DataDir, activeFile);

            if (!File.Exists(configPath)) return;

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (!string.IsNullOrEmpty(config?.Account?.Cookie))
            {
                var window = new SecurityWebWindow(config.Account.Cookie, url);
                window.Activate();
                StatusMessage = "窗口已打开";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"操作失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NavigateToGacha()
    {
        _navigationService.NavigateTo(typeof(GachaViewModel).FullName!);
    }

    private async Task OpenGenshinDataAsync()
    {
        try
        {
            StatusMessage = "正在打开原神数据窗口...";
            var window = App.GetService<GenshinDataWindow>();
            if (window.Visible)
            {
                window.Activate();
                return;
            }
            window.Activate();
            StatusMessage = "窗口已打开";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 打开原神数据窗口失败: {ex.Message}");
            StatusMessage = $"打开失败: {ex.Message}";
        }
    }

    private async Task LoadAccountInfo()
    {
        try
        {
            Debug.WriteLine("========== [LoadAccountInfo] 开始加载账户信息 ==========");

            var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
            string activeFile = activeFileObj?.ToString() ?? "config.json";
            var configPath = Path.Combine(Helpers.AppPaths.DataDir, activeFile);

            if (!File.Exists(configPath))
            {
                Debug.WriteLine("[LoadAccountInfo] 配置文件不存在");
                CurrentAccount = null;
                StatusMessage = "未找到登录信息";
                return;
            }

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (config?.Display == null || string.IsNullOrEmpty(config.Account?.Cookie))
            {
                Debug.WriteLine("[LoadAccountInfo] 状态更新: 未登录");
                CurrentAccount = null;
                StatusMessage = "未找到登录信息";
                return;
            }

            var display = config.Display;
            Debug.WriteLine($"[LoadAccountInfo] 读取到的 Display: UID={display.GameUid}, Nickname={display.Nickname}, HasBoundRole={display.HasBoundRole}");

            if (CurrentAccount == null || CurrentAccount.GameUid != display.GameUid)
            {
                CurrentAccount = new AccountInfo
                {
                    Nickname = display.Nickname,
                    Stuid = config.Account.Stuid,
                    GameUid = display.GameUid,
                    Server = display.Server,
                    AvatarUrl = display.AvatarUrl,
                    Level = display.HasBoundRole ? display.Level : "",
                    Sign = display.Sign,
                    IpRegion = display.IpRegion,
                    Gender = display.Gender,
                    HasBoundRole = display.HasBoundRole
                };
            }
            else
            {
                CurrentAccount.Nickname = display.Nickname;
                CurrentAccount.Stuid = config.Account.Stuid;
                CurrentAccount.Server = display.Server;
                CurrentAccount.AvatarUrl = display.AvatarUrl;
                CurrentAccount.Level = display.HasBoundRole ? display.Level : "";
                CurrentAccount.Sign = display.Sign;
                CurrentAccount.IpRegion = display.IpRegion;
                CurrentAccount.Gender = display.Gender;
                CurrentAccount.HasBoundRole = display.HasBoundRole;
            }

            LoginButtonText = "重新登录";
            StatusMessage = display.HasBoundRole ? "账户已登录" : "账户已登录（未绑定角色）";
            Debug.WriteLine($"[LoadAccountInfo] 状态更新: 已登录, HasBoundRole={display.HasBoundRole}");

            await LoadSavedAccountsListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadAccountInfo] 异常: {ex.Message}");
            StatusMessage = $"加载账户信息失败: {ex.Message}";
            CurrentAccount = null;
        }
    }

    private string ExtractUidFromCookie(string cookie)
    {
        var match = Regex.Match(cookie, @"(?:account_id_v2|ltuid_v2|ltuid|account_id|stuid)=(\d+)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private async Task LoadSavedAccountsListAsync()
    {
        SavedAccounts.Clear();
        _accountFileMap.Clear();
        var baseDir = Helpers.AppPaths.DataDir;

        var filesToTry = Directory.GetFiles(baseDir, "config*.json").ToList();

        var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
        string activeFile = activeFileObj?.ToString() ?? "config.json";
        string activeFilePath = Path.Combine(baseDir, activeFile);

        string currentStuid = CurrentAccount?.Stuid ?? "";

        foreach (var file in filesToTry.Distinct())
        {
            if (!File.Exists(file)) continue;
            try
            {
                var fileName = Path.GetFileName(file);
                if (fileName.Equals("config.json", StringComparison.OrdinalIgnoreCase)) continue;

                var json = await File.ReadAllTextAsync(file);
                var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config?.Account == null || string.IsNullOrEmpty(config.Account.Stuid)) continue;

                string stuid = config.Account.Stuid;

                if (stuid == currentStuid) continue;
                if (_accountFileMap.ContainsKey(stuid)) continue;

                _accountFileMap[stuid] = fileName;

                var accountInfo = new AccountInfo { Stuid = stuid, Nickname = $"用户 {stuid}" };

                if (config.Display != null && !string.IsNullOrEmpty(config.Display.Nickname))
                {
                    accountInfo.Nickname = config.Display.Nickname;
                    accountInfo.AvatarUrl = config.Display.AvatarUrl;
                    accountInfo.Server = config.Display.Server;
                    accountInfo.Level = config.Display.Level;
                    accountInfo.Sign = config.Display.Sign;
                    accountInfo.IpRegion = config.Display.IpRegion;
                    accountInfo.Gender = config.Display.Gender;
                    accountInfo.HasBoundRole = config.Display.HasBoundRole;
                }

                SavedAccounts.Add(accountInfo);
            }
            catch { }
        }

        OnPropertyChanged(nameof(HasSavedAccounts));
    }

    private async Task ArchiveCurrentAccountAsync()
    {
        if (CurrentAccount == null) return;

        var baseDir = Helpers.AppPaths.DataDir;
        var mainConfigPath = Path.Combine(baseDir, "config.json");

        if (File.Exists(mainConfigPath))
        {
            var json = await File.ReadAllTextAsync(mainConfigPath);
            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config?.Account == null || string.IsNullOrEmpty(config.Account.Stuid)) return;

            var isOsObj = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
            bool isOs = isOsObj is bool b && b;

            string backupName = isOs ? $"config.lab_{config.Account.Stuid}.json" : $"config_{config.Account.Stuid}.json";
            File.Copy(mainConfigPath, Path.Combine(baseDir, backupName), true);

            Debug.WriteLine($"[ArchiveCurrentAccount] 已备份配置到: {backupName}");
        }
    }

    private async Task AddNewAccountAsync()
    {
        if (SavedAccounts.Count + (CurrentAccount != null ? 1 : 0) >= MaxAccounts)
        {
            StatusMessage = $"已达到最大账户数量限制 ({MaxAccounts}个)";
            return;
        }
        
        await ArchiveCurrentAccountAsync();
        
        await _localSettingsService.SaveSettingAsync("ActiveConfigFile", "config.json");
        await _localSettingsService.SaveSettingAsync("IsInternationalAccount", false);
        
        var configPath = Path.Combine(Helpers.AppPaths.DataDir, "config.json");
        if (File.Exists(configPath)) File.Delete(configPath);

        CurrentAccount = null;
        GameRolesInfo = null;
        UserFullInfo = null;
        await LoginAsync();
    }

    private async Task SwitchToAccountAsync(AccountInfo? targetAccount)
    {
        if (targetAccount == null || !_accountFileMap.ContainsKey(targetAccount.Stuid)) return;

        try
        {
            await ArchiveCurrentAccountAsync();

            var baseDir = Helpers.AppPaths.DataDir;
            string sourceFile = _accountFileMap[targetAccount.Stuid];
            string sourcePath = Path.Combine(baseDir, sourceFile);
            string mainConfigPath = Path.Combine(baseDir, "config.json");

            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, mainConfigPath, true);
                Debug.WriteLine($"[SwitchToAccount] 已复制 {sourceFile} 到 config.json");
            }

            bool isOs = sourceFile.Contains(".lab");
            await _localSettingsService.SaveSettingAsync("IsInternationalAccount", isOs);
            await _localSettingsService.SaveSettingAsync("ActiveConfigFile", "config.json");

            await LoadAccountInfo();
            await LoadUserInfoAsync();
            StatusMessage = "账户切换成功";
        }
        catch (Exception ex)
        {
            StatusMessage = $"切换失败: {ex.Message}";
        }
    }

    private async Task LoginAsync()
{
    try
    {
        Debug.WriteLine("========== [LoginAsync] 启动登录流程 ==========");
        StatusMessage = "正在打开登录窗口...";
        
        var loginWindow = new LoginQrWindow();
        loginWindow.Activate();
        
        var tcs = new TaskCompletionSource<bool>();
        loginWindow.Closed += (s, e) => tcs.SetResult(loginWindow.DidLoginSucceed());
        var success = await tcs.Task;

        Debug.WriteLine($"[LoginAsync] 登录窗口关闭，成功状态: {success}");

        if (success)
        {
            StatusMessage = "登录成功，正在加载信息...";
            await Task.Delay(500);

            var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
            string activeFile = activeFileObj?.ToString() ?? "config.json";
            var configPath = Path.Combine(Helpers.AppPaths.DataDir, activeFile);
            
            Debug.WriteLine($"[LoginAsync] 准备保存数据，当前 ActiveConfigFile: {activeFile}, 路径: {configPath}, 文件是否存在: {File.Exists(configPath)}");

            if (File.Exists(configPath))
            {
                var json = await File.ReadAllTextAsync(configPath);
                var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                Debug.WriteLine($"[LoginAsync] 配置文件内容读取成功，Cookie是否为空: {string.IsNullOrEmpty(config?.Account?.Cookie)}");

                if (config?.Account != null && !string.IsNullOrEmpty(config.Account.Cookie))
                {
                    await _userInfoService.SaveUserDataAsync(config.Account.Cookie, config.Account.Stuid);
                    Debug.WriteLine("[LoginAsync] SaveUserDataAsync 执行完毕");
                }
            }

            await LoadAccountInfo();
            await LoadUserInfoAsync();
            await LoadSavedAccountsListAsync();
            StatusMessage = "登录成功";
        }
        else
        {
            StatusMessage = "登录已取消";
            Debug.WriteLine("[LoginAsync] 登录流程被用户取消或失败");
            await LoadAccountInfo();
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[LoginAsync] 严重异常: {ex.Message}");
        StatusMessage = $"登录出错: {ex.Message}";
    }
}

    public async Task LoadUserInfoAsync()
    {
        if (IsLoadingUserInfo) return;

        try
        {
            IsLoadingUserInfo = true;
            StatusMessage = "正在加载用户信息...";

            var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
            string activeFile = activeFileObj?.ToString() ?? "config.json";
            var configPath = Path.Combine(Helpers.AppPaths.DataDir, activeFile);

            Debug.WriteLine($"========== [LoadUserInfo] 开始加载 ==========");
            Debug.WriteLine($"[LoadUserInfo] 目标配置文件: {activeFile}");
            Debug.WriteLine($"[LoadUserInfo] 完整路径: {configPath}");
            Debug.WriteLine($"[LoadUserInfo] 文件是否存在: {File.Exists(configPath)}");

            if (!File.Exists(configPath))
            {
                Debug.WriteLine("[LoadUserInfo] 失败原因: 配置文件不存在");
                StatusMessage = "请先登录";
                return;
            }

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Debug.WriteLine($"[LoadUserInfo] Cookie读取状态: {(string.IsNullOrEmpty(config?.Account?.Cookie) ? "空" : "正常")}");

            if (string.IsNullOrEmpty(config?.Account?.Cookie))
            {
                StatusMessage = "请先登录";
                return;
            }

            Debug.WriteLine("[LoadUserInfo] 正在调用远程API...");
            var rolesTask = _userInfoService.GetUserGameRolesAsync(config.Account.Cookie);
            var userInfoTask = _userInfoService.GetUserFullInfoAsync(config.Account.Cookie);

            await Task.WhenAll(rolesTask, userInfoTask);

            var newRolesInfo = await rolesTask;
            var newUserFullInfo = await userInfoTask;

            var oldRolesJson = JsonSerializer.Serialize(GameRolesInfo);
            var newRolesJson = JsonSerializer.Serialize(newRolesInfo);
            if (oldRolesJson != newRolesJson)
            {
                GameRolesInfo = newRolesInfo;
            }

            var oldInfoJson = JsonSerializer.Serialize(UserFullInfo);
            var newInfoJson = JsonSerializer.Serialize(newUserFullInfo);
            if (oldInfoJson != newInfoJson)
            {
                UserFullInfo = newUserFullInfo;
            }

            Debug.WriteLine($"[LoadUserInfo] API返回状态: RolesRet={GameRolesInfo?.retcode}, UserRet={UserFullInfo?.retcode}");
            Debug.WriteLine($"[LoadUserInfo] GameRolesInfo.data.list.Count: {GameRolesInfo?.data?.list?.Count ?? -1}");

            var userInfo = UserFullInfo?.data?.user_info;
            var hasBoundRole = GameRolesInfo?.data?.list?.Count > 0;
            var role = GameRolesInfo?.data?.list?.FirstOrDefault();

            config.Display = new DisplayConfig
            {
                Nickname = userInfo?.nickname ?? role?.nickname ?? $"用户 {config.Account.Stuid}",
                GameUid = role?.game_uid ?? "",
                Server = role?.region_name ?? "",
                AvatarUrl = userInfo?.avatar_url ?? "ms-appx:///Assets/DefaultAvatar.png",
                Level = role?.level.ToString() ?? "",
                Sign = string.IsNullOrEmpty(userInfo?.introduce) ? "这个人很懒，什么都没有写..." : userInfo.introduce,
                IpRegion = userInfo?.ip_region ?? "未知",
                Gender = userInfo?.gender ?? 0,
                HasBoundRole = hasBoundRole
            };

            if (string.IsNullOrEmpty(config.Account.Stoken) || string.IsNullOrEmpty(config.Account.Mid))
            {
                var cookie = config.Account.Cookie;
                if (!string.IsNullOrEmpty(cookie))
                {
                    if (string.IsNullOrEmpty(config.Account.Stoken))
                    {
                        var stokenMatch = System.Text.RegularExpressions.Regex.Match(cookie, @"stoken=([^;]+)");
                        if (stokenMatch.Success) config.Account.Stoken = stokenMatch.Groups[1].Value;
                    }
                    if (string.IsNullOrEmpty(config.Account.Mid))
                    {
                        var midMatch = System.Text.RegularExpressions.Regex.Match(cookie, @"mid=([^;]+)");
                        if (midMatch.Success) config.Account.Mid = midMatch.Groups[1].Value;
                    }
                }
            }

            var newJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, newJson);

            Debug.WriteLine($"[LoadUserInfo] 已保存 Display 到 config.json，HasBoundRole={hasBoundRole}");
            await LoadAccountInfo();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadUserInfo] 异常: {ex.Message}");
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoadingUserInfo = false;
            Debug.WriteLine("========== [LoadUserInfo] 加载结束 ==========");
        }
    }

    private async void Logout()
    {
        try
        {
            var baseDir = Helpers.AppPaths.DataDir;

            var isOsObj = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
            bool isOs = isOsObj is bool b && b;

            if (CurrentAccount != null)
            {
                var backupPath = Path.Combine(baseDir, $"config_{CurrentAccount.GameUid}.json");
                if (File.Exists(backupPath)) File.Delete(backupPath);

                if (isOs)
                {
                    var labPath = Path.Combine(baseDir, "config.lab.json");
                    if (File.Exists(labPath))
                    {
                        File.Delete(labPath);
                        Debug.WriteLine("[Logout] 已清除国际服配置文件 config.lab.json");
                    }
                }
            }

            var configPath = Path.Combine(baseDir, "config.json");
            if (File.Exists(configPath))
            {
                var json = await File.ReadAllTextAsync(configPath);
                var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (config?.Account != null)
                {
                    config.Account.Cookie = "";
                    config.Account.Stuid = "";
                    config.Account.Stoken = "";
                    config.Account.Mid = "";

                    var newJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(configPath, newJson);
                }
            }

            await _localSettingsService.SaveSettingAsync("IsInternationalAccount", false);

            CurrentAccount = null;
            GameRolesInfo = null;
            UserFullInfo = null;
            LoginButtonText = "登录米游社";
            StatusMessage = "已退出登录";

            await LoadSavedAccountsListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"退出失败: {ex.Message}";
            Debug.WriteLine($"[Logout] 异常: {ex.Message}");
        }
    }

    public IAsyncRelayCommand DeleteAccountCommand => new AsyncRelayCommand(DeleteAccountAsync);

    private async Task DeleteAccountAsync()
    {
        try
        {
            if (CurrentAccount == null)
            {
                StatusMessage = "没有可删除的账号";
                return;
            }

            var stuid = CurrentAccount.Stuid;
            await DeleteAccountByUidAsync(stuid);
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败: {ex.Message}";
            Debug.WriteLine($"[DeleteAccount] 异常: {ex.Message}");
        }
    }

    public IAsyncRelayCommand<AccountInfo> DeleteSavedAccountCommand => new AsyncRelayCommand<AccountInfo>(DeleteSavedAccountAsync);

    private async Task DeleteSavedAccountAsync(AccountInfo? account)
    {
        if (account == null) return;
        await DeleteAccountByUidAsync(account.Stuid);
    }

    private async Task DeleteAccountByUidAsync(string uid)
    {
        var baseDir = Helpers.AppPaths.DataDir;

        var backupPath = Path.Combine(baseDir, $"config_{uid}.json");
        var labBackupPath = Path.Combine(baseDir, $"config.lab_{uid}.json");
        if (File.Exists(backupPath)) File.Delete(backupPath);
        if (File.Exists(labBackupPath)) File.Delete(labBackupPath);

        var mainConfigPath = Path.Combine(baseDir, "config.json");
        if (File.Exists(mainConfigPath))
        {
            var json = await File.ReadAllTextAsync(mainConfigPath);
            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (config?.Account?.Stuid == uid)
            {
                File.Delete(mainConfigPath);
            }
        }

        var cloudCredPath = Path.Combine(baseDir, "cloud_credentials.json");
        if (File.Exists(cloudCredPath))
        {
            try
            {
                var credJson = await File.ReadAllTextAsync(cloudCredPath);
                var credDict = JsonSerializer.Deserialize<Dictionary<string, string>>(credJson);
                if (credDict != null && credDict.ContainsKey(uid))
                {
                    credDict.Remove(uid);
                    await File.WriteAllTextAsync(cloudCredPath, JsonSerializer.Serialize(credDict, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch { }
        }

        await LoadSavedAccountsListAsync();

        if (CurrentAccount != null && CurrentAccount.Stuid == uid)
        {
            if (SavedAccounts.Count > 0)
            {
                await SwitchToAccountAsync(SavedAccounts[0]);
            }
            else
            {
                await _userConfigService.SaveDisplayConfigAsync(new UserDisplayConfig());

                var configPath = Path.Combine(baseDir, "config.json");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (config?.Account != null)
                    {
                        config.Account.Cookie = "";
                        config.Account.Stuid = "";
                        config.Account.Stoken = "";
                        config.Account.Mid = "";
                        var newJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(configPath, newJson);
                    }
                }

                await _localSettingsService.SaveSettingAsync("IsInternationalAccount", false);
                CurrentAccount = null;
                GameRolesInfo = null;
                UserFullInfo = null;
                LoginButtonText = "登录米游社";
            }
        }

        StatusMessage = $"账号 {uid} 已删除";
    }
}