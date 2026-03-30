using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Views;

namespace FufuLauncher.ViewModels;

public partial class AccountViewModel : ObservableRecipient
{
    private readonly IUserInfoService _userInfoService;
    private readonly IUserConfigService _userConfigService;
    private const int MaxAccounts = 4;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    [NotifyPropertyChangedFor(nameof(IsNotLoggedIn))]
    private AccountInfo? _currentAccount;

    public bool IsLoggedIn => CurrentAccount != null;
    public bool IsNotLoggedIn => CurrentAccount == null;

    [ObservableProperty] private string _loginButtonText = "登录米游社";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private GameRolesResponse? _gameRolesInfo;
    [ObservableProperty] private UserFullInfoResponse? _userFullInfo;
    [ObservableProperty] private bool _isLoadingUserInfo;

    [ObservableProperty] private ObservableCollection<AccountInfo> _savedAccounts = new();

    public IRelayCommand LoginCommand
    {
        get;
    }
    public IRelayCommand LogoutCommand
    {
        get;
    }
    public IRelayCommand LoadUserInfoCommand
    {
        get;
    }
    public IRelayCommand OpenGenshinDataCommand
    {
        get;
    }
    public IRelayCommand AddAccountCommand
    {
        get;
    }
    public IRelayCommand<AccountInfo> SwitchAccountCommand
    {
        get;
    }
    private readonly INavigationService _navigationService;

    public AccountViewModel(
        ILocalSettingsService localSettingsService,
        IUserInfoService userInfoService,
        IUserConfigService userConfigService,
        INavigationService navigationService)
    {
        _userInfoService = userInfoService;
        _userConfigService = userConfigService;

        LoginCommand = new AsyncRelayCommand(LoginAsync);
        LogoutCommand = new RelayCommand(Logout);
        LoadUserInfoCommand = new AsyncRelayCommand(LoadUserInfoAsync);
        OpenGenshinDataCommand = new AsyncRelayCommand(OpenGenshinDataAsync);
        AddAccountCommand = new AsyncRelayCommand(AddNewAccountAsync);
        SwitchAccountCommand = new AsyncRelayCommand<AccountInfo>(SwitchToAccountAsync);
        _navigationService = navigationService;

        _ = LoadAccountInfo();
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
            Debug.WriteLine("========== 加载账户信息 ==========");
            var displayConfig = await _userConfigService.LoadDisplayConfigAsync();
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
            }
            else
            {
                CurrentAccount = null;
                StatusMessage = "未找到登录信息";
            }
            await LoadSavedAccountsListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载账户信息失败: {ex.Message}";
            CurrentAccount = null;
        }
    }

    private async Task LoadSavedAccountsListAsync()
    {
        SavedAccounts.Clear();
        var baseDir = AppContext.BaseDirectory;
        var files = Directory.GetFiles(baseDir, "config_*.json");

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var uid = fileName.Replace("config_", "").Replace(".json", "");

            if (CurrentAccount != null && uid == CurrentAccount.GameUid)
                continue;

            var accountInfo = new AccountInfo { GameUid = uid, Nickname = $"用户 {uid}" };
            var displayFile = Path.Combine(baseDir, $"display_{uid}.json");
            if (File.Exists(displayFile))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(displayFile);
                    var displayConfig = JsonSerializer.Deserialize<UserDisplayConfig>(json);
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
    }

    private async Task ArchiveCurrentAccountAsync()
    {
        if (CurrentAccount == null || string.IsNullOrEmpty(CurrentAccount.GameUid)) return;

        var baseDir = AppContext.BaseDirectory;
        var currentConfigPath = Path.Combine(baseDir, "config.json");

        if (File.Exists(currentConfigPath))
        {
            var targetConfigPath = Path.Combine(baseDir, $"config_{CurrentAccount.GameUid}.json");
            File.Copy(currentConfigPath, targetConfigPath, true);

            var displayConfig = new UserDisplayConfig
            {
                Nickname = CurrentAccount.Nickname,
                GameUid = CurrentAccount.GameUid,
                Server = CurrentAccount.Server,
                AvatarUrl = CurrentAccount.AvatarUrl,
                Level = CurrentAccount.Level
            };
            var displayJson = JsonSerializer.Serialize(displayConfig);
            await File.WriteAllTextAsync(Path.Combine(baseDir, $"display_{CurrentAccount.GameUid}.json"), displayJson);
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
        var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (File.Exists(configPath)) File.Delete(configPath);

        CurrentAccount = null;
        GameRolesInfo = null;
        UserFullInfo = null;
        await LoginAsync();
    }

    private async Task SwitchToAccountAsync(AccountInfo? targetAccount)
    {
        if (targetAccount == null) return;
        StatusMessage = $"正在切换到 {targetAccount.Nickname}...";

        try
        {
            await ArchiveCurrentAccountAsync();

            var baseDir = AppContext.BaseDirectory;
            var targetConfigPath = Path.Combine(baseDir, $"config_{targetAccount.GameUid}.json");
            var mainConfigPath = Path.Combine(baseDir, "config.json");

            if (File.Exists(targetConfigPath))
            {
                File.Copy(targetConfigPath, mainConfigPath, true);
                var targetDisplayPath = Path.Combine(baseDir, $"display_{targetAccount.GameUid}.json");
                if (File.Exists(targetDisplayPath))
                {
                    var json = await File.ReadAllTextAsync(targetDisplayPath);
                    var displayConfig = JsonSerializer.Deserialize<UserDisplayConfig>(json);
                    if (displayConfig != null)
                    {
                        await _userConfigService.SaveDisplayConfigAsync(displayConfig);
                    }
                }
                await LoadAccountInfo();
                await LoadUserInfoAsync();
                StatusMessage = "切换成功";
            }
            else
            {
                StatusMessage = "切换失败：配置文件丢失";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"切换出错: {ex.Message}";
        }
    }

    private async Task LoginAsync()
    {
        try
        {
            StatusMessage = "正在打开登录窗口...";
        
            // 实例化新的二维码登录窗口
            var loginWindow = new LoginQrWindow();
            loginWindow.Activate();

            // 等待窗口关闭并获取登录结果
            var tcs = new TaskCompletionSource<bool>();
            loginWindow.Closed += (s, e) => tcs.SetResult(loginWindow.DidLoginSucceed());
            var success = await tcs.Task;

            if (success)
            {
                StatusMessage = "登录成功，正在加载信息...";
                await Task.Delay(500);

                var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (config?.Account != null && !string.IsNullOrEmpty(config.Account.Cookie))
                    {
                        await _userInfoService.SaveUserDataAsync(config.Account.Cookie, config.Account.Stuid);
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
                await LoadAccountInfo();
            }
        }
        catch (Exception ex)
        {
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

            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                StatusMessage = "请先登录";
                return;
            }

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (string.IsNullOrEmpty(config?.Account?.Cookie))
            {
                StatusMessage = "请先登录";
                return;
            }

            GameRolesInfo = null;
            UserFullInfo = null;

            var rolesTask = _userInfoService.GetUserGameRolesAsync(config.Account.Cookie);
            var userInfoTask = _userInfoService.GetUserFullInfoAsync(config.Account.Cookie);

            await Task.WhenAll(rolesTask, userInfoTask);

            GameRolesInfo = await rolesTask;
            UserFullInfo = await userInfoTask;

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
                await LoadAccountInfo();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoadingUserInfo = false;
        }
    }

    private async void Logout()
    {
        try
        {
            if (CurrentAccount != null)
            {
                var baseDir = AppContext.BaseDirectory;
                var backupPath = Path.Combine(baseDir, $"config_{CurrentAccount.GameUid}.json");
                var displayPath = Path.Combine(baseDir, $"display_{CurrentAccount.GameUid}.json");
                if (File.Exists(backupPath)) File.Delete(backupPath);
                if (File.Exists(displayPath)) File.Delete(displayPath);
            }

            await _userConfigService.SaveDisplayConfigAsync(new UserDisplayConfig());

            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
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
        }
    }
}