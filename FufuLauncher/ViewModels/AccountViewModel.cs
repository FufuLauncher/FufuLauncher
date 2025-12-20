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
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IUserInfoService _userInfoService;
    private readonly IUserConfigService _userConfigService;

    [ObservableProperty] private AccountInfo? _currentAccount;
    [ObservableProperty] private string _loginButtonText = "登录米游社";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private GameRolesResponse? _gameRolesInfo;
    [ObservableProperty] private UserFullInfoResponse? _userFullInfo;
    [ObservableProperty] private bool _isLoadingUserInfo;


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

    public AccountViewModel(
        ILocalSettingsService localSettingsService,
        IUserInfoService userInfoService,
        IUserConfigService userConfigService)
    {
        _localSettingsService = localSettingsService;
        _userInfoService = userInfoService;
        _userConfigService = userConfigService;

        LoginCommand = new AsyncRelayCommand(LoginAsync);
        LogoutCommand = new RelayCommand(Logout);
        LoadUserInfoCommand = new AsyncRelayCommand(LoadUserInfoAsync);

        OpenGenshinDataCommand = new AsyncRelayCommand(OpenGenshinDataAsync);

        _ = LoadAccountInfo();
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
                Debug.WriteLine($"找到显示配置: {displayConfig.Nickname}, UID: {displayConfig.GameUid}");

                CurrentAccount = new AccountInfo
                {
                    Nickname = displayConfig.Nickname,
                    GameUid = displayConfig.GameUid,
                    Server = displayConfig.Server,
                    AvatarUrl = displayConfig.AvatarUrl,
                    Level = displayConfig.Level
                };
                LoginButtonText = "重新登录";
                StatusMessage = "账户已登录";
            }
            else
            {
                Debug.WriteLine("未找到显示配置");
                CurrentAccount = null;
                StatusMessage = "未找到登录信息";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 加载账户信息失败: {ex.Message}");
            StatusMessage = $"加载账户信息失败: {ex.Message}";
            CurrentAccount = null;
        }
    }

    private async Task LoginAsync()
    {
        try
        {
            Debug.WriteLine("========== 开始登录流程 ==========");
            StatusMessage = "正在打开登录窗口...";

            var loginWindow = new LoginWebViewDialog();
            loginWindow.Activate();

            var tcs = new TaskCompletionSource<bool>();
            loginWindow.Closed += (s, e) => tcs.SetResult(loginWindow.DidLoginSucceed());
            var success = await tcs.Task;

            if (success)
            {
                Debug.WriteLine("登录窗口返回成功");
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
                        Debug.WriteLine("调用 SaveUserDataAsync 保存配置");
                        await _userInfoService.SaveUserDataAsync(config.Account.Cookie, config.Account.Stuid);
                    }
                }

                await LoadAccountInfo();
                await LoadUserInfoAsync();
                StatusMessage = "登录成功";
            }
            else
            {
                Debug.WriteLine("登录窗口返回失败");
                StatusMessage = "登录已取消";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 登录出错: {ex.Message}");
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

            Debug.WriteLine("\n========== 开始加载用户信息 ==========");

            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                Debug.WriteLine("ERROR: config.json 不存在");
                StatusMessage = "请先登录";
                return;
            }

            var json = await File.ReadAllTextAsync(configPath);
            Debug.WriteLine($"读取 config.json: {json.Length} 字符");

            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (string.IsNullOrEmpty(config?.Account?.Cookie))
            {
                Debug.WriteLine("ERROR: Cookie 为空");
                StatusMessage = "请先登录";
                return;
            }

            Debug.WriteLine($"Cookie 长度: {config.Account.Cookie.Length}");
            Debug.WriteLine($"Stuid: {config.Account.Stuid ?? "空"}");

            GameRolesInfo = null;
            UserFullInfo = null;

            StatusMessage = "正在请求角色信息...";
            Debug.WriteLine("调用 GetUserGameRolesAsync...");
            var rolesTask = _userInfoService.GetUserGameRolesAsync(config.Account.Cookie);

            StatusMessage = "正在请求社区信息...";
            Debug.WriteLine("调用 GetUserFullInfoAsync...");
            var userInfoTask = _userInfoService.GetUserFullInfoAsync(config.Account.Cookie);

            await Task.WhenAll(rolesTask, userInfoTask);

            GameRolesInfo = await rolesTask;
            UserFullInfo = await userInfoTask;

            Debug.WriteLine($"角色信息加载: retcode={GameRolesInfo?.retcode}, 数量={GameRolesInfo?.data?.list?.Count ?? 0}");
            Debug.WriteLine($"社区信息加载: retcode={UserFullInfo?.retcode}, 昵称={UserFullInfo?.data?.user_info?.nickname ?? "无"}");

            if (GameRolesInfo?.data?.list?.FirstOrDefault() is { } role)
            {
                var displayConfig = new UserDisplayConfig
                {
                    Nickname = role.nickname,
                    GameUid = role.game_uid,
                    Server = role.region_name,
                    AvatarUrl = UserFullInfo?.data?.user_info?.avatar_url ?? "ms-appx:///Assets/DefaultAvatar.png",
                    Level = role.level.ToString()
                };

                await _userConfigService.SaveDisplayConfigAsync(displayConfig);
                await LoadAccountInfo();
            }

            Debug.WriteLine("========== 用户信息加载完成 ==========\n");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 加载失败: {ex.Message}");
            Debug.WriteLine($"堆栈: {ex.StackTrace}");
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
            Debug.WriteLine("========== 开始退出登录 ==========");

            await _userConfigService.SaveDisplayConfigAsync(new UserDisplayConfig());
            Debug.WriteLine("已清空 user.config.json");

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

                    var newJson = JsonSerializer.Serialize(config, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    await File.WriteAllTextAsync(configPath, newJson);
                    Debug.WriteLine("已清空 config.json 认证信息");
                }
            }

            CurrentAccount = null;
            GameRolesInfo = null;
            UserFullInfo = null;
            LoginButtonText = "登录米游社";
            StatusMessage = "已退出登录";

            Debug.WriteLine("========== 退出登录完成 ==========");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 退出失败: {ex.Message}");
            StatusMessage = $"退出失败: {ex.Message}";
        }
    }
}