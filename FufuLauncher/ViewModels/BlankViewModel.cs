using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FufuLauncher.ViewModels
{
    public partial class GameAccount
    {
        public Guid InnerId { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string MihoyoSDK { get; set; } = string.Empty;
        public string? Mid
        {
            get; set;
        }
        public string? MacAddress
        {
            get; set;
        }
        public bool IsExpired
        {
            get; set;
        }
        public DateTime? LastUsed
        {
            get; set;
        }

        public static GameAccount Create(string name, string sdk, string? mid, string? mac) => new()
        {
            Name = name,
            MihoyoSDK = sdk,
            Mid = mid,
            MacAddress = mac,
            LastUsed = DateTime.Now
        };
    }

    public partial class BlankViewModel : ObservableRecipient
    {
        private readonly IGameConfigService _gameConfigService;
        private readonly ILocalSettingsService _localSettingsService;
        private const string GamePathKey = "GameInstallationPath";
        private const string RegistryKey = @"HKEY_CURRENT_USER\Software\miHoYo\原神";
        private const string RegistryValueName = "MIHOYOSDK_ADL_PROD_CN_h3123967166";

        [ObservableProperty]
        private GameConfig? currentGameConfig;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private ObservableCollection<GameAccount> accounts = new();

        [ObservableProperty]
        private GameAccount? selectedAccount;

        [ObservableProperty]
        private bool canAccessRegistry;

        private string? _lastLoadedPath;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly string _accountsFilePath;

        public ICommand SelectGamePathCommand
        {
            get;
        }
        public IAsyncRelayCommand LoadAccountsCommand
        {
            get;
        }
        public IAsyncRelayCommand AddCurrentAccountCommand
        {
            get;
        }
        public IAsyncRelayCommand<GameAccount> SwitchAccountCommand
        {
            get;
        }
        public IAsyncRelayCommand<GameAccount> DeleteAccountCommand
        {
            get;
        }

        public BlankViewModel(IGameConfigService gameConfigService, ILocalSettingsService localSettingsService)
        {
            _gameConfigService = gameConfigService;
            _localSettingsService = localSettingsService;
            _dispatcherQueue = App.MainWindow.DispatcherQueue;

            var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _accountsFilePath = Path.Combine(localFolder, "FufuLauncher", "game_accounts.json");

            SelectGamePathCommand = new AsyncRelayCommand(SelectGamePathAsync);
            LoadAccountsCommand = new AsyncRelayCommand(LoadAccountsAsync);
            AddCurrentAccountCommand = new AsyncRelayCommand(AddCurrentAccountAsync);
            SwitchAccountCommand = new AsyncRelayCommand<GameAccount>(SwitchAccountAsync);
            DeleteAccountCommand = new AsyncRelayCommand<GameAccount>(DeleteAccountAsync);

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                var savedPath = await _localSettingsService.ReadSettingAsync(GamePathKey) as string;
                if (!string.IsNullOrEmpty(savedPath))
                {
                    savedPath = savedPath.Trim('"').Trim();
                    Debug.WriteLine($"[游戏信息页] 初始化读取路径: '{savedPath}'");
                    _lastLoadedPath = savedPath;
                    await LoadGameInfoAsync(savedPath);
                }
                await LoadAccountsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化失败: {ex.Message}");
            }
        }

        public async Task OnNavigatedToAsync()
        {
            try
            {
                var savedPath = await _localSettingsService.ReadSettingAsync(GamePathKey) as string;
                if (!string.IsNullOrEmpty(savedPath))
                {
                    savedPath = savedPath.Trim('"').Trim();
                    Debug.WriteLine($"[游戏信息页] 导航读取路径: '{savedPath}'");

                    if (CurrentGameConfig == null || savedPath != _lastLoadedPath)
                    {
                        await LoadGameInfoAsync(savedPath);
                        _lastLoadedPath = savedPath;
                    }
                }
                await LoadAccountsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"导航时加载失败: {ex.Message}");
            }
        }

        private async Task SelectGamePathAsync()
        {
            try
            {
                if (!_dispatcherQueue.HasThreadAccess)
                {
                    Debug.WriteLine("[错误] 不在UI线程上执行");
                    return;
                }

                var mainWindow = App.MainWindow;
                if (mainWindow == null)
                {
                    await ShowError("无法获取主窗口句柄");
                    return;
                }

                var hwnd = WindowNative.GetWindowHandle(mainWindow);
                if (hwnd == IntPtr.Zero)
                {
                    await ShowError("窗口句柄无效，请以普通用户模式运行");
                    return;
                }

                var folderPicker = new FolderPicker
                {
                    SuggestedStartLocation = PickerLocationId.ComputerFolder,
                    ViewMode = PickerViewMode.List
                };
                folderPicker.FileTypeFilter.Add("*");

                try
                {
                    InitializeWithWindow.Initialize(folderPicker, hwnd);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[警告] InitializeWithWindow失败: {ex.Message}");
                }

                var folder = await Task.Run(async () => await folderPicker.PickSingleFolderAsync());

                if (folder != null)
                {
                    var path = folder.Path.Trim('"').Trim();
                    Debug.WriteLine($"[游戏信息页] 用户选择路径: '{path}'");

                    await LoadGameInfoAsync(path);
                    await _localSettingsService.SaveSettingAsync(GamePathKey, path);
                    _lastLoadedPath = path;

                    WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(path));
                    Debug.WriteLine("[游戏信息页] 已发送路径变更消息");
                }
            }
            catch (UnauthorizedAccessException)
            {
                await ShowError("权限错误：请以普通用户身份运行程序选择游戏路径");
                Debug.WriteLine("[严重错误] 管理员模式权限问题");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择路径失败: {ex.Message}\n堆栈: {ex.StackTrace}");
                await ShowError($"选择路径失败: {ex.Message}");
            }
        }

        private async Task ShowError(string message)
        {
            try
            {
                await _dispatcherQueue.EnqueueAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "操作失败",
                        Content = message,
                        CloseButtonText = "确定",
                        XamlRoot = App.MainWindow.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"显示错误对话框失败: {ex.Message}");
            }
        }

        private async Task LoadGameInfoAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            IsLoading = true;
            try
            {
                Debug.WriteLine($"[游戏信息页] 加载游戏信息: {path}");
                CurrentGameConfig = await _gameConfigService.LoadGameConfigAsync(path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载失败: {ex.Message}");
                CurrentGameConfig = null;
            }
            finally
            {
                IsLoading = false;
            }
        }

        #region 账号管理
        private async Task LoadAccountsAsync()
        {
            try
            {
                CanAccessRegistry = await TestRegistryAccessAsync();
                if (!CanAccessRegistry)
                {
                    Debug.WriteLine("无法访问注册表");
                    return;
                }

                var accounts = await ReadAccountsFromFileAsync();
                Accounts.Clear();
                foreach (var account in accounts.OrderByDescending(a => a.LastUsed))
                {
                    Accounts.Add(account);
                }

                var current = await GetCurrentAccountFromRegistryAsync();
                SelectedAccount = current;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载账号失败: {ex.Message}");
                CanAccessRegistry = false;
            }
        }

        private async Task AddCurrentAccountAsync()
        {
            try
            {
                IsLoading = true;

                var current = await GetCurrentAccountFromRegistryAsync();
                if (current == null)
                {
                    Debug.WriteLine("注册表中没有找到当前账号信息");
                    return;
                }

                var accounts = await ReadAccountsFromFileAsync();
                if (accounts.Any(a => a.Mid == current.Mid))
                {
                    Debug.WriteLine("该账号已存在");
                    return;
                }

                var newAccount = GameAccount.Create(
                    $"账号_{DateTime.Now:MMdd_HHmm}_{current.Mid ?? "Unknown"}",
                    current.MihoyoSDK,
                    current.Mid,
                    current.MacAddress
                );

                accounts.Add(newAccount);
                await WriteAccountsToFileAsync(accounts);

                Accounts.Add(newAccount);
                Debug.WriteLine($"已添加账号: {newAccount.Name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"添加账号失败: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SwitchAccountAsync(GameAccount account)
        {
            if (account == null) return;

            try
            {
                IsLoading = true;
                await SetCurrentAccountToRegistryAsync(account);
                SelectedAccount = account;
                await LoadAccountsAsync();

                Debug.WriteLine($"已切换到账号: {account.Name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"切换账号失败: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DeleteAccountAsync(GameAccount account)
        {
            if (account == null) return;

            try
            {
                var accounts = await ReadAccountsFromFileAsync();
                accounts.RemoveAll(a => a.InnerId == account.InnerId);
                await WriteAccountsToFileAsync(accounts);

                Accounts.Remove(account);
                Debug.WriteLine($"已删除账号: {account.Name}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"删除账号失败: {ex.Message}");
            }
        }
        #endregion

        #region 注册表和文件操作
        private async Task<bool> TestRegistryAccessAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistryKey.Replace("HKEY_CURRENT_USER\\", ""));
                    return key != null;
                }
                catch
                {
                    return false;
                }
            });
        }

        private async Task<GameAccount?> GetCurrentAccountFromRegistryAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var value = Registry.GetValue(RegistryKey, RegistryValueName, null);
                    if (value is not byte[] bytes || bytes.Length == 0)
                        return null;

                    int nullIndex = Array.IndexOf(bytes, (byte)0);
                    int length = nullIndex >= 0 ? nullIndex : bytes.Length;
                    var sdkString = Encoding.UTF8.GetString(bytes, 0, length);

                    return new GameAccount
                    {
                        MihoyoSDK = sdkString,
                        Mid = "当前账号",
                        Name = "当前登录账号"
                    };
                }
                catch
                {
                    return null;
                }
            });
        }

        private async Task SetCurrentAccountToRegistryAsync(GameAccount account)
        {
            await Task.Run(() =>
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKey.Replace("HKEY_CURRENT_USER\\", ""));
                if (key == null)
                    throw new InvalidOperationException("无法访问注册表");

                var sdkBytes = Encoding.UTF8.GetBytes(account.MihoyoSDK);
                var target = new byte[sdkBytes.Length + 1];
                Array.Copy(sdkBytes, target, sdkBytes.Length);
                target[sdkBytes.Length] = 0;

                key.SetValue(RegistryValueName, target, RegistryValueKind.Binary);
            });
        }

        private async Task<List<GameAccount>> ReadAccountsFromFileAsync()
        {
            return await Task.Run(() =>
            {
                if (!File.Exists(_accountsFilePath))
                    return new List<GameAccount>();

                var json = File.ReadAllText(_accountsFilePath, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<GameAccount>>(json) ?? new List<GameAccount>();
            });
        }

        private async Task WriteAccountsToFileAsync(List<GameAccount> accounts)
        {
            await Task.Run(() =>
            {
                var directory = Path.GetDirectoryName(_accountsFilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                File.WriteAllText(_accountsFilePath, json, Encoding.UTF8);
            });
        }
        #endregion

        public void Cleanup()
        {
            Debug.WriteLine("BlankViewModel Cleanup");
        }
    }
}