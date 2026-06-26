/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
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
using MihoyoBBS;
using Windows.ApplicationModel.DataTransfer;

namespace FufuLauncher.ViewModels;

public partial class AccountViewModel : ObservableRecipient
{

    #region 字段
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IUserInfoService _userInfoService;
    private readonly INavigationService _navigationService;
    private readonly INotificationService _notificationService;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private const int MaxAccounts = 4;
    private readonly AccountManager _accountManager;
    private int _loadVersion;
    private string? _lastNotifiedAccountId;
    private volatile bool _isDisposed;
    #endregion

    #region 生命周期
    public void Cleanup()
    {
        _isDisposed = true;
        Interlocked.Increment(ref _loadVersion); 
    }
    #endregion

    #region 属性
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    [NotifyPropertyChangedFor(nameof(IsNotLoggedIn))]
    private AccountInfo? _currentAccount;

    public bool IsLoggedIn => CurrentAccount != null;
    public bool IsNotLoggedIn => CurrentAccount == null;
    public IRelayCommand OpenSecurityCenterCommand
    {
        get;
    }

    [ObservableProperty] private string _loginButtonText = "登录米游社";
    [ObservableProperty] private string _statusMessage = "";

    [ObservableProperty] private GameRolesResponse? _gameRolesInfo;
    [ObservableProperty] private UserFullInfoResponse? _userFullInfo;
    [ObservableProperty] private bool _isLoadingUserInfo;

    [ObservableProperty] private ObservableCollection<AccountInfo> _savedAccounts = new();
    public bool HasSavedAccounts => SavedAccounts.Count > 0;

    public bool HasCommunityData => UserFullInfo?.data?.user_info?.achieve != null;
    public string CommunityLikeCount => UserFullInfo?.data?.user_info?.achieve?["like_num"]?.ToString() ?? "-";
    public string CommunityPostCount => UserFullInfo?.data?.user_info?.achieve?["post_num"]?.ToString() ?? "-";
    public string CommunityReplyCount => UserFullInfo?.data?.user_info?.achieve?["replypost_num"]?.ToString() ?? "-";


    public List<GameRoleInfo>? BoundRoles => GameRolesInfo?.data?.list;

    #endregion

    #region 命令
    public IRelayCommand LockAccountCommand
    {
        get;
    }

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
    public IRelayCommand CopyCookieCommand
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
    #endregion

    #region 构造函数
    public AccountViewModel(
        ILocalSettingsService localSettingsService,
        IUserInfoService userInfoService,
        INavigationService navigationService,
        INotificationService notificationService,
        AccountManager accountManager)
    {
        _localSettingsService = localSettingsService;
        _userInfoService = userInfoService;
        _navigationService = navigationService;
        _notificationService = notificationService;
        _dispatcherQueue = App.MainWindow.DispatcherQueue;
        _accountManager = accountManager;
        LoginCommand = new AsyncRelayCommand(LoginAsync);
        LogoutCommand = new AsyncRelayCommand(LogoutAsync);
        LoadUserInfoCommand = new AsyncRelayCommand(async () => await LoadUserInfoAsync());
        OpenGenshinDataCommand = new AsyncRelayCommand(OpenGenshinDataAsync);
        CopyCookieCommand = new AsyncRelayCommand(CopyCookieAsync);
        AddAccountCommand = new AsyncRelayCommand(AddNewAccountAsync);
        SwitchAccountCommand = new AsyncRelayCommand<AccountInfo>(SwitchToAccountAsync);
        OpenSecurityCenterCommand = new AsyncRelayCommand(OpenSecurityCenterAsync);
        LockAccountCommand = new AsyncRelayCommand(LockAccountAsync);
        _ = LoadAccountInfo();
    }
    #endregion

    #region 公开方法
    public async Task DeleteAccountAsync(AccountInfo account)
    {
        if (account == null) return;
        var accountId = account.AccountId; 
        bool isCurrentAccount = _accountManager.ActiveAccountId == accountId;

        try
        {
            await _accountManager.DeleteAccountAsync(accountId);

            
            RefreshSavedAccountsList();

            if (isCurrentAccount)
            {
                var activeId = _accountManager.ActiveAccountId;
                if (activeId != null)
                {
                    await LoadActiveAccountAsync(activeId);
                }
                else
                {
                    
                    RunOnUIThread(() =>
                    {
                        CurrentAccount = null;
                        GameRolesInfo = null;
                        UserFullInfo = null;
                        LoginButtonText = "登录米游社";
                        StatusMessage = "没有账户了";
                    });
                }
            }
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => StatusMessage = $"删除失败: {ex.Message}");
        }
    }
    public async Task<bool> LoadUserInfoAsync()
    {
        var myVersion = Interlocked.Increment(ref _loadVersion);
        try
        {
            IsLoadingUserInfo = true;
            RunOnUIThread(() => StatusMessage = "正在加载用户信息...");


            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null)
            {
                Debug.WriteLine("[LoadUserInfo] 无活跃账户");
                RunOnUIThread(() => StatusMessage = "请先登录");
                return false;
            }

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            var entry = _accountManager.GetActiveAccountEntry();
            if (cookies == null || entry == null)
            {
                Debug.WriteLine("[LoadUserInfo] 无法读取账户数据");
                RunOnUIThread(() => StatusMessage = "请先登录");
                return false;
            }


            RunOnUIThread(() =>
            {
                GameRolesInfo = null;
                UserFullInfo = null;
            });

            string cookieString = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));

            Debug.WriteLine($"[LoadUserInfo] 正在调用远程API... (账户: {entry.Id})");


            var rolesTask = _userInfoService.GetUserGameRolesAsync(cookieString);
            var userInfoTask = _userInfoService.GetUserFullInfoAsync(cookieString);
            await Task.WhenAll(rolesTask, userInfoTask);

            var newRolesInfo = await rolesTask;
            var newUserFullInfo = await userInfoTask;

            if (myVersion != _loadVersion)
            {
                Debug.WriteLine($"[LoadUserInfo][{entry.Id}] 结果已过期(v{myVersion}→{_loadVersion})，丢弃");
                return false;
            }


            GameRolesInfo = newRolesInfo;
            UserFullInfo = newUserFullInfo;

            var userInfo = UserFullInfo?.data?.user_info;
            var hasBoundRole = GameRolesInfo?.data?.list?.Count > 0;
            var role = GameRolesInfo?.data?.list?.FirstOrDefault();

            var nickname = userInfo?.nickname ?? role?.nickname ?? $"用户 {entry.Stuid}";
            var avatarUrl = userInfo?.avatar_url ?? "ms-appx:///Assets/DefaultAvatar.png";
            var gameUid = role?.game_uid ?? "";
            var isOs = entry.Id.StartsWith("os_");
            var server = isOs ? "国际服" : "国服";
            var level = role?.level.ToString() ?? "";
            var sign = string.IsNullOrEmpty(userInfo?.introduce) ? "这个人很懒，什么都没有写..." : userInfo.introduce;
            var ipRegion = userInfo?.ip_region ?? "未知";
            var gender = userInfo?.gender ?? 0;

            await _accountManager.UpdateAccountMetaAsync(entry.Id, nickname, avatarUrl, gameUid);

            RunOnUIThread(() =>
            {
                if (CurrentAccount == null)
                {
                    CurrentAccount = new AccountInfo { AccountId = entry.Id, Stuid = entry.Stuid };
                }
                CurrentAccount.Nickname = nickname;
                CurrentAccount.AvatarUrl = avatarUrl;
                CurrentAccount.GameUid = gameUid;
                CurrentAccount.Server = server;
                CurrentAccount.Level = level;
                CurrentAccount.Sign = sign;
                CurrentAccount.IpRegion = ipRegion;
                CurrentAccount.Gender = gender;
                CurrentAccount.HasBoundRole = hasBoundRole;

                StatusMessage = hasBoundRole ? "账户已登录" : "账户已登录（未绑定角色）";
            });
            Debug.WriteLine($"[LoadUserInfo] 用户信息已更新，HasBoundRole={hasBoundRole}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadUserInfo] 异常: {ex.Message}");
            RunOnUIThread(() =>
            {
                StatusMessage = $"加载失败: {ex.Message}";
                GameRolesInfo = null;
                UserFullInfo = null;
            });
            return false;
        }
        finally
        {
            IsLoadingUserInfo = false;
            Debug.WriteLine("========== [LoadUserInfo] 加载结束 ==========");
        }
    }

    partial void OnUserFullInfoChanged(UserFullInfoResponse? value)
    {
        OnPropertyChanged(nameof(HasCommunityData));
        OnPropertyChanged(nameof(CommunityLikeCount));
        OnPropertyChanged(nameof(CommunityPostCount));
        OnPropertyChanged(nameof(CommunityReplyCount));
    }

    partial void OnGameRolesInfoChanged(GameRolesResponse? value)
    {
        OnPropertyChanged(nameof(BoundRoles));
    }

    #endregion

    # region 辅助命令实现

    [RelayCommand]
    private void NavigateToGacha() => _navigationService.NavigateTo(typeof(GachaViewModel).FullName!);
    private async Task LockAccountAsync() => await OpenSecurityWindowInternalAsync(ApiEndpoints.AccountLockUrl, "正在打开账号冻结页面...");
    private async Task CopyCookieAsync()
    {
        try
        {
            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null)
            {
                RunOnUIThread(() =>
                {
                    StatusMessage = "未找到登录信息";
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("复制失败", "未找到登录信息", NotificationType.Error));
                });
                return;
            }

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0)
            {
                RunOnUIThread(() =>
                {
                    StatusMessage = "未找到有效的 Cookie";
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("复制失败", "未找到有效的 Cookie", NotificationType.Error));
                });
                return;
            }

            string cookieString = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));

            RunOnUIThread(() =>
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(cookieString);
                Clipboard.SetContent(dataPackage);
                StatusMessage = "Cookie 已复制到剪切板";
                WeakReferenceMessenger.Default.Send(new NotificationMessage("复制成功", "Cookie 已成功复制到剪贴板", NotificationType.Success));
            });
        }
        catch (Exception ex)
        {
            RunOnUIThread(() =>
            {
                StatusMessage = $"复制失败: {ex.Message}";
                WeakReferenceMessenger.Default.Send(new NotificationMessage("复制失败", ex.Message, NotificationType.Error));
            });
        }
    }
    private async Task OpenSecurityCenterAsync() => await OpenSecurityWindowInternalAsync(ApiEndpoints.AccountSecurityUrl, "正在打开账号安全中心...");
    private async Task OpenSecurityWindowInternalAsync(string url, string loadingMsg)
    {
        try
        {
            RunOnUIThread(() => StatusMessage = loadingMsg);
            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null) return;

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0) return;

            string cookieString = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));

            RunOnUIThread(() =>
            {
                var window = new SecurityWebWindow(cookieString, url);
                window.Activate();
                StatusMessage = "窗口已打开";
            });
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => StatusMessage = $"操作失败: {ex.Message}");
        }
    }
    private async Task LoadActiveAccountAsync(string accountId)
    {
        var cookies = await _accountManager.LoadCookiesAsync(accountId);
        var entry = _accountManager.GetActiveAccountEntry();

        if (cookies == null || entry == null)
        {
            RunOnUIThread(() => CurrentAccount = null);
            return;
        }

        var info = new AccountInfo
        {
            AccountId = entry.Id,
            Nickname = entry.Nickname ?? "未命名",
            Stuid = entry.Stuid,
            AvatarUrl = entry.AvatarUrl ?? "ms-appx:///Assets/DefaultAvatar.png",
            GameUid = entry.GameUid ?? "",
            Server = entry.ServerType
        };

        RunOnUIThread(() => CurrentAccount = info);

        var loaded = await LoadUserInfoAsync();
        if (!loaded)
        {
            
        }
    }
    private async Task OpenGenshinDataAsync()
    {
        try
        {
            RunOnUIThread(() =>
            {
                StatusMessage = "正在打开原神数据窗口...";
                var window = App.GetService<GenshinDataWindow>();
                window.Activate();
                StatusMessage = "窗口已打开";
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 打开原神数据窗口失败: {ex.Message}");
            RunOnUIThread(() => StatusMessage = $"打开失败: {ex.Message}");
        }
    }

    private void RefreshSavedAccountsList()
    {
        RunOnUIThread(() =>
        {
            SavedAccounts.Clear();
            var activeId = _accountManager.ActiveAccountId;
            foreach (var entry in _accountManager.GetAllAccounts())
            {
                if (entry.Id == activeId)
                    continue;
                SavedAccounts.Add(new AccountInfo
                {
                    AccountId = entry.Id,
                    Nickname = entry.Nickname ?? "未命名",
                    Stuid = entry.Stuid,
                    AvatarUrl = entry.AvatarUrl ?? "ms-appx:///Assets/DefaultAvatar.png",
                    GameUid = entry.GameUid ?? ""
                });
            }
            OnPropertyChanged(nameof(HasSavedAccounts));
        });
    }

    private void RunOnUIThread(Action action)
    {
        if (_isDisposed || _dispatcherQueue == null) return;
        if (_dispatcherQueue.HasThreadAccess)
        {
            if (!_isDisposed) action();
        }
        else
            _dispatcherQueue.TryEnqueue(() => { if (!_isDisposed) action(); });
    }
    #endregion

    #region 账号数据管理（加载、保存、备份）

    private async Task LoadAccountInfo()
    {
        try
        {
            Debug.WriteLine("========== [LoadAccountInfo] 开始加载账户信息 ==========");

            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null)
            {
                Debug.WriteLine("[LoadAccountInfo] 无活跃账户");
                RunOnUIThread(() =>
                {
                    CurrentAccount = null;
                    StatusMessage = "未找到登录信息";
                    LoginButtonText = "登录";
                });
                return;
            }

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            var entry = _accountManager.GetActiveAccountEntry();
            if (cookies == null || entry == null)
            {
                Debug.WriteLine("[LoadAccountInfo] 无法加载活跃账户数据");
                RunOnUIThread(() =>
                {
                    CurrentAccount = null;
                    StatusMessage = "账户数据读取失败";
                    LoginButtonText = "登录";
                });
                return;
            }

            RunOnUIThread(() =>
            {
                CurrentAccount = new AccountInfo
                {
                    AccountId = entry.Id,
                    Nickname = entry.Nickname ?? "未命名",
                    Stuid = entry.Stuid,
                    AvatarUrl = entry.AvatarUrl ?? "ms-appx:///Assets/DefaultAvatar.png",
                    GameUid = entry.GameUid ?? entry.Stuid,
                    Server = entry.ServerType,
                    HasBoundRole = false
                };
                LoginButtonText = "重新登录";
                StatusMessage = "账户已登录";
            });

            Debug.WriteLine($"[LoadAccountInfo] 已加载账户: {entry.Nickname} ({entry.Id})");


            _ = LoadUserInfoAsync();

            RefreshSavedAccountsList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadAccountInfo] 异常: {ex.Message}");
            RunOnUIThread(() =>
            {
                StatusMessage = $"加载账户信息失败: {ex.Message}";
                CurrentAccount = null;
                LoginButtonText = "登录";
            });
        }
    }

    public async Task RefreshDataAsync()
    {
        await LoadAccountInfo();
        RefreshSavedAccountsList();
    }
    #endregion

    #region 登录/退出/切换/添加账号
    private async Task LoginAsync()
    {
        if (_accountManager.GetAllAccounts().Count >= MaxAccounts)
        {
            StatusMessage = $"最多只能添加 {MaxAccounts} 个账户";
            return;
        }
        try
        {
            RunOnUIThread(() => StatusMessage = "正在打开登录窗口...");
            var loginWindow = new LoginQrWindow();
            var (cookies, serverType) = await loginWindow.ShowAndWaitAsync();

            Debug.WriteLine($"[LoginAsync] 登录成功，Cookie 数量: {cookies.Count}, 服务器: {serverType}");

            var entry = await _accountManager.AddAccountAsync(cookies, serverType, nickname: "新账户");
            await _accountManager.SwitchAccountAsync(entry.Id);

            await LoadActiveAccountAsync(entry.Id);
            RefreshSavedAccountsList();

            RunOnUIThread(() => StatusMessage = "登录成功");
        }
        catch (TaskCanceledException)
        {
            RunOnUIThread(() => StatusMessage = "登录已取消");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoginAsync] 异常: {ex.Message}");
            RunOnUIThread(() => StatusMessage = $"登录出错: {ex.Message}");
        }
    }
    private async Task LogoutAsync()
    {
        try
        {
    
            Interlocked.Increment(ref _loadVersion);
            await _accountManager.LogoutAsync();


            RunOnUIThread(() =>
            {
                CurrentAccount = null;
                GameRolesInfo = null;
                UserFullInfo = null;
                LoginButtonText = "登录米游社";
                StatusMessage = "已退出登录";
            });
            _lastNotifiedAccountId = null;

           
            RefreshSavedAccountsList();
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => StatusMessage = $"退出失败: {ex.Message}");
        }
    }
    private async Task SwitchToAccountAsync(AccountInfo? targetAccount)
    {
        if (targetAccount == null) return;

        // 同步清空，确保 UI 在切换瞬间就不显示旧数据
        RunOnUIThread(() => { GameRolesInfo = null; UserFullInfo = null; });
        await _accountManager.SwitchAccountAsync(targetAccount.AccountId);
        await LoadActiveAccountAsync(targetAccount.AccountId);


        if (_accountManager.ActiveAccountId != targetAccount.AccountId)
            return;

        if (_lastNotifiedAccountId == targetAccount.AccountId)
            return;

        if (UserFullInfo?.data == null && GameRolesInfo?.data == null)
            return;

        _lastNotifiedAccountId = targetAccount.AccountId;
        RefreshSavedAccountsList();
        RunOnUIThread(() => StatusMessage = "账户登录成功");
        _notificationService.Show("账户登录成功", $"已登录到 {targetAccount.Nickname}", NotificationType.Success, 3000);
    }
    private async Task AddNewAccountAsync()
    {
        if (_accountManager.GetAllAccounts().Count >= MaxAccounts)
        {
            StatusMessage = $"最多只能添加 {MaxAccounts} 个账户";
            return;
        }
        try
        {
            var loginWindow = new LoginQrWindow();
            var (cookies, serverType) = await loginWindow.ShowAndWaitAsync();

            Debug.WriteLine($"[AddNewAccount] 登录成功，Cookie 数量: {cookies.Count}, 服务器: {serverType}");

            var entry = await _accountManager.AddAccountAsync(cookies, serverType, nickname: "新账户");
            await _accountManager.SwitchAccountAsync(entry.Id);

            await LoadActiveAccountAsync(entry.Id);
            RefreshSavedAccountsList();
        }
        catch (TaskCanceledException)
        {
            
        }
        catch (Exception ex)
        {
            RunOnUIThread(() => StatusMessage = $"添加账户失败: {ex.Message}");
        }
    }
    #endregion

}

