using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
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
            var configPath = Path.Combine(AppContext.BaseDirectory, activeFile);

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
            var displayConfig = await _userConfigService.LoadDisplayConfigAsync();
        
            Debug.WriteLine($"[LoadAccountInfo] 读取到的 DisplayConfig: UID={displayConfig.GameUid}, Nickname={displayConfig.Nickname}");

            if (!string.IsNullOrEmpty(displayConfig.GameUid))
            {
                CurrentAccount = new AccountInfo
                {
                    Nickname = displayConfig.Nickname,
                    GameUid = displayConfig.GameUid,
                    Server = displayConfig.Server,
                    AvatarUrl = displayConfig.AvatarUrl,
                    Level = displayConfig.Level,
                    Sign = displayConfig.Sign,
                    IpRegion = displayConfig.IpRegion,
                    Gender = displayConfig.Gender
                };
            
                LoginButtonText = "重新登录";
                StatusMessage = "账户已登录";
                Debug.WriteLine("[LoadAccountInfo] 状态更新: 已登录");
            }
            else
            {
                CurrentAccount = null;
                StatusMessage = "未找到登录信息";
                Debug.WriteLine("[LoadAccountInfo] 状态更新: 未登录 (UID为空)");
            }
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
        var baseDir = AppContext.BaseDirectory;
        
        var filesToTry = Directory.GetFiles(baseDir, "config*.json").ToList();
        
        var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
        string activeFile = activeFileObj?.ToString() ?? "config.json";
        string activeFilePath = Path.Combine(baseDir, activeFile);

        foreach (var file in filesToTry.Distinct())
        {
            if (!File.Exists(file)) continue;
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (string.IsNullOrEmpty(config?.Account?.Cookie)) continue;

                string uid = ExtractUidFromCookie(config.Account.Cookie);
                if (string.IsNullOrEmpty(uid)) continue;

                if (file.Equals(activeFilePath, StringComparison.OrdinalIgnoreCase)) continue;
                if (_accountFileMap.ContainsKey(uid)) continue;

                _accountFileMap[uid] = Path.GetFileName(file);

                var accountInfo = new AccountInfo { GameUid = uid, Nickname = $"用户 {uid}" };
                var displayFile = Path.Combine(baseDir, $"display_{uid}.json");
                
                if (File.Exists(displayFile))
                {
                    try
                    {
                        var displayJson = await File.ReadAllTextAsync(displayFile);
                        var displayConfig = JsonSerializer.Deserialize<UserDisplayConfig>(displayJson);
                        if (displayConfig != null)
                        {
                            accountInfo.Nickname = displayConfig.Nickname;
                            accountInfo.AvatarUrl = displayConfig.AvatarUrl;
                            accountInfo.Server = displayConfig.Server;
                            accountInfo.Level = displayConfig.Level;
                            accountInfo.Sign = displayConfig.Sign;
                            accountInfo.IpRegion = displayConfig.IpRegion;
                            accountInfo.Gender = displayConfig.Gender;
                        }
                    }
                    catch { }
                }
                SavedAccounts.Add(accountInfo);
            }
            catch { }
        }
        
        OnPropertyChanged(nameof(HasSavedAccounts));
    }

    private async Task ArchiveCurrentAccountAsync()
    {
        if (CurrentAccount == null || string.IsNullOrEmpty(CurrentAccount.GameUid)) return;

        var baseDir = AppContext.BaseDirectory;
        var mainConfigPath = Path.Combine(baseDir, "config.json");

        if (File.Exists(mainConfigPath))
        {
            var isOsObj = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
            bool isOs = isOsObj is bool b && b;
            
            string backupName = isOs ? "config.lab.json" : $"config_{CurrentAccount.GameUid}.json";
            File.Copy(mainConfigPath, Path.Combine(baseDir, backupName), true);

            var displayConfig = new UserDisplayConfig
            {
                Nickname = CurrentAccount.Nickname,
                GameUid = CurrentAccount.GameUid,
                Server = CurrentAccount.Server,
                AvatarUrl = CurrentAccount.AvatarUrl,
                Level = CurrentAccount.Level,
                Sign = CurrentAccount.Sign,
                IpRegion = CurrentAccount.IpRegion,
                Gender = CurrentAccount.Gender
            };
            await File.WriteAllTextAsync(Path.Combine(baseDir, $"display_{CurrentAccount.GameUid}.json"), JsonSerializer.Serialize(displayConfig));
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
        
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (File.Exists(configPath)) File.Delete(configPath);

        CurrentAccount = null;
        GameRolesInfo = null;
        UserFullInfo = null;
        await LoginAsync();
    }

    private async Task SwitchToAccountAsync(AccountInfo? targetAccount)
    {
        if (targetAccount == null || !_accountFileMap.ContainsKey(targetAccount.GameUid)) return;

        try
        {
            await ArchiveCurrentAccountAsync();
            
            var baseDir = AppContext.BaseDirectory;
            string sourceFile = _accountFileMap[targetAccount.GameUid];
            string sourcePath = Path.Combine(baseDir, sourceFile);
            string mainConfigPath = Path.Combine(baseDir, "config.json");
            
            if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, mainConfigPath, true);
            }
            
            bool isOs = sourceFile.Contains(".lab");
            await _localSettingsService.SaveSettingAsync("IsInternationalAccount", isOs);
            await _localSettingsService.SaveSettingAsync("ActiveConfigFile", "config.json");
            
            var displayPath = Path.Combine(baseDir, $"display_{targetAccount.GameUid}.json");
            if (File.Exists(displayPath))
            {
                var displayConfig = JsonSerializer.Deserialize<UserDisplayConfig>(await File.ReadAllTextAsync(displayPath));
                if (displayConfig != null)
                {
                    await _userConfigService.SaveDisplayConfigAsync(displayConfig);
                }
            }

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
            var configPath = Path.Combine(AppContext.BaseDirectory, activeFile);
            
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
        var configPath = Path.Combine(AppContext.BaseDirectory, activeFile);

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

        GameRolesInfo = null;
        UserFullInfo = null;

        Debug.WriteLine("[LoadUserInfo] 正在调用远程API...");
        var rolesTask = _userInfoService.GetUserGameRolesAsync(config.Account.Cookie);
        var userInfoTask = _userInfoService.GetUserFullInfoAsync(config.Account.Cookie);

        await Task.WhenAll(rolesTask, userInfoTask);

        GameRolesInfo = await rolesTask;
        UserFullInfo = await userInfoTask;

        Debug.WriteLine($"[LoadUserInfo] API返回状态: RolesRet={GameRolesInfo?.retcode}, UserRet={UserFullInfo?.retcode}");

        if (GameRolesInfo?.data?.list?.FirstOrDefault() is { } role)
        {
            var userInfo = UserFullInfo?.data?.user_info;

            var displayConfig = new UserDisplayConfig
            {
                Nickname = role.nickname,
                GameUid = role.game_uid,
                Server = role.region_name,
                AvatarUrl = userInfo?.avatar_url ?? "ms-appx:///Assets/DefaultAvatar.png",
                Level = role.level.ToString(),
                Sign = string.IsNullOrEmpty(userInfo?.introduce) ? "这个人很懒，什么都没有写..." : userInfo.introduce,
                IpRegion = userInfo?.ip_region ?? "未知",
                Gender = userInfo?.gender ?? 0
            };

            await _userConfigService.SaveDisplayConfigAsync(displayConfig);
            Debug.WriteLine("[LoadUserInfo] 已保存 DisplayConfig，重新触发 LoadAccountInfo");
            await LoadAccountInfo();
        }
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
            var baseDir = AppContext.BaseDirectory;
            
            var isOsObj = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
            bool isOs = isOsObj is bool b && b;

            if (CurrentAccount != null)
            {
                var backupPath = Path.Combine(baseDir, $"config_{CurrentAccount.GameUid}.json");
                var displayPath = Path.Combine(baseDir, $"display_{CurrentAccount.GameUid}.json");
                if (File.Exists(backupPath)) File.Delete(backupPath);
                if (File.Exists(displayPath)) File.Delete(displayPath);
                
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
            StatusMessage = "已退出登录";

            await LoadSavedAccountsListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"退出失败: {ex.Message}";
            Debug.WriteLine($"[Logout] 异常: {ex.Message}");
        }
    }
}