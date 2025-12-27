using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;

public class GameAccountData
{
    public Guid Id
    {
        get; set;
    }
    public string Name { get; set; } = string.Empty;
    public string SdkData { get; set; } = string.Empty;
    public DateTime LastUsed
    {
        get; set;
    }
}

public class GameConfigData
{
    public string GamePath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public string DirectorySize { get; set; } = "0 MB";
}

namespace FufuLauncher.Views
{
    public sealed partial class BlankPage : Page
    {
        private GameConfigData? _currentConfig;
        private readonly string _accountsFilePath;
        private readonly ILocalSettingsService _localSettingsService;

        public BlankPage()
        {
            this.InitializeComponent();
            _localSettingsService = App.GetService<ILocalSettingsService>();

            var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _accountsFilePath = Path.Combine(localFolder, "FufuLauncher", "game_accounts.json");

            this.Loaded += BlankPage_Loaded;
        }

        private void PathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ApplyPathButton != null)
            {
                ApplyPathButton.IsEnabled = !string.IsNullOrWhiteSpace(PathTextBox.Text);
            }
        }

        private async void ApplyPath_Click(object sender, RoutedEventArgs e)
        {
            await ProcessPathInput(PathTextBox.Text.Trim());
        }
        
        private void DownloadGame_Click(object sender, RoutedEventArgs e)
        {

            string targetPath = _currentConfig?.GamePath;


            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Genshin Game");
            }


            if (!Directory.Exists(targetPath))
            {
                try 
                {
                    Directory.CreateDirectory(targetPath);
                }
                catch (Exception ex)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "路径错误",
                        Content = $"无法创建游戏目录: {targetPath}\n错误: {ex.Message}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    _ = dialog.ShowAsync();
                    return;
                }
            }


            var downloadWindow = new FufuLauncher.Views.DownloadWindow(targetPath);
            downloadWindow.Activate();
        }
        private void OpenMap_Click(object sender, RoutedEventArgs e)
        {
            // 1. 创建新窗口
            var newWindow = new Window();
            newWindow.Title = "提瓦特大地图";

            // 2. 设置窗口大小为长方形 (例如 1280x800)
            var hWnd = WindowNative.GetWindowHandle(newWindow);
            var winId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(winId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

            // 3. 创建 Frame 并导航到 MapPage
            // 关键点：将 newWindow 作为参数传递给 Navigate，这样 MapPage 内部才能控制这个窗口
            var rootFrame = new Frame();
            rootFrame.Navigate(typeof(Views.MapPage), newWindow);

            // 4. 设置窗口内容并激活
            newWindow.Content = rootFrame;
            newWindow.Activate();
        }

        private async Task ProcessPathInput(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ShowEmptyState();
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    await LoadGameInfoAsync(path);
                    await _localSettingsService.SaveSettingAsync("GameInstallationPath", path);
                    WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(path));

                    Debug.WriteLine($"[ApplyPath_Click] 手动输入路径成功: {path}");
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        Title = "无效路径",
                        Content = "输入的路径不存在，请检查路径是否正确。",
                        PrimaryButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();

                    if (await _localSettingsService.ReadSettingAsync("GameInstallationPath") is string savedPath)
                    {
                        PathTextBox.Text = savedPath.Trim('"').Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessPathInput] 处理失败: {ex.Message}");
                await ShowError($"路径处理失败: {ex.Message}");
            }
        }

        private async void PathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter && ApplyPathButton.IsEnabled)
            {
                e.Handled = true;
                await ProcessPathInput(PathTextBox.Text.Trim());
            }
        }


        private async void BlankPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {

                if (await _localSettingsService.ReadSettingAsync("GameInstallationPath") is string savedPath)
                {
                    savedPath = savedPath.Trim('"').Trim();
                    PathTextBox.Text = savedPath;
                    await LoadGameInfoAsync(savedPath);
                }
                else
                {

                    var foundPath = GamePathFinder.FindGamePath();
                    if (!string.IsNullOrEmpty(foundPath))
                    {
                        await ShowAutoPathDialog(foundPath);
                    }
                }

                await LoadAccountsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BlankPage_Loaded] 加载失败: {ex.Message}");
            }
        }

        private async Task ShowAutoPathDialog(string foundPath)
        {
            if (string.IsNullOrEmpty(foundPath) || XamlRoot == null) return;

            var dialog = new ContentDialog
            {
                Title = "自动找到游戏路径",
                Content = $"检测到可能的《原神》安装路径：\n\n{foundPath}\n\n是否应用此路径？",
                PrimaryButtonText = "应用",
                CloseButtonText = "手动选择",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                PathTextBox.Text = foundPath;
                await LoadGameInfoAsync(foundPath);
                await _localSettingsService.SaveSettingAsync("GameInstallationPath", foundPath);
                WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(foundPath));
            }
            else
            {
                await PickGameFolderAsync();
            }
        }

        private async void SelectPath_Click(object sender, RoutedEventArgs e)
        {
            await PickGameFolderAsync();
        }

        private async Task<string?> PickGameFolderAsync()
        {
            var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            folderPicker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                var path = folder.Path;
                PathTextBox.Text = path;

                await _localSettingsService.SaveSettingAsync("GameInstallationPath", path);
                WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(path));

                await LoadGameInfoAsync(path);
                return path;
            }
            return null;
        }

        private async Task LoadGameInfoAsync(string gamePath)
        {
            gamePath = gamePath?.Trim('"').Trim();

            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                ShowEmptyState();
                return;
            }

            LoadingRing.IsActive = true;


            try
            {
                var config = new GameConfigData { GamePath = gamePath };

                _currentConfig = config;

                ShowInfo();

                await Task.Run(async () =>
                {
                    var configPath = Path.Combine(gamePath, "config.ini");
                    if (!File.Exists(configPath))
                    {
                        configPath = Directory.GetFiles(gamePath, "config.ini", SearchOption.AllDirectories)
                            .FirstOrDefault();
                    }

                    if (configPath != null && File.Exists(configPath))
                    {
                        var content = await File.ReadAllTextAsync(configPath);
                        var versionLine = content.Split('\n')
                            .FirstOrDefault(line => line.StartsWith("game_version=", StringComparison.OrdinalIgnoreCase));
                        if (versionLine != null)
                        {
                            var parts = versionLine.Split('=', 2);
                            if (parts.Length > 1)
                                config.Version = parts[1].Trim();
                        }
                        config.ServerType = DetectServerType(content);
                    }
                    else
                    {
                        config.Version = "未找到版本信息";
                        config.ServerType = "未知";
                    }

                    config.DirectorySize = CalculateDirectorySize(gamePath);

                    DispatcherQueue.TryEnqueue(() => ShowInfo());
                });

                _ = GetGameBranchesInfoAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoadGameInfoAsync] 异常: {ex.Message}");
                ShowEmptyState();
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private async Task GetGameBranchesInfoAsync()
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var url = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches?launcher_id=jGHBHlcOq1&language=zh-cn&game_ids[]=1Z8W5NHUQb";

                var response = await client.GetStringAsync(url);
                var json = JsonDocument.Parse(response);

                var root = json.RootElement;
                if (root.GetProperty("retcode").GetInt32() == 0)
                {

                    var gameBranch = root.GetProperty("data").GetProperty("game_branches")[0];

                    var mainInfo = gameBranch.GetProperty("main");
                    var latestVersion = mainInfo.GetProperty("tag").GetString();

                    var versionText = latestVersion ?? "获取失败";
                    DispatcherQueue.TryEnqueue(() => LatestVersionText.Text = versionText);

                    if (gameBranch.TryGetProperty("pre_download", out var preDownload) &&
                        preDownload.ValueKind != JsonValueKind.Null)
                    {
                        var preVersion = preDownload.GetProperty("tag").GetString() ?? "未知";
                        DispatcherQueue.TryEnqueue(() => PreDownloadText.Text = $"有 (版本 {preVersion})");
                    }
                    else
                    {
                        DispatcherQueue.TryEnqueue(() => PreDownloadText.Text = "暂无");
                    }
                }
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LatestVersionText.Text = "获取失败";
                    PreDownloadText.Text = "获取失败";
                });
            }
        }

        private async void OpenAnnouncement_Click(object sender, RoutedEventArgs e)
        {
            var announcementWindow = new Window { Title = "游戏公告" };
            var webView = new Microsoft.UI.Xaml.Controls.WebView2();
            announcementWindow.Closed += (s, args) => webView.Close();
            announcementWindow.Content = webView;

            await webView.EnsureCoreWebView2Async();
            webView.Source = new Uri("https://sdk.mihoyo.com/hk4e/announcement/index.html?auth_appid=announcement&authkey_ver=1&bundle_id=hk4e_cn&channel_id=1&game=hk4e&game_biz=hk4e_cn&lang=zh-cn&level=60&platform=pc&region=cn_gf01&sdk_presentation_style=fullscreen&sdk_screen_transparent=true&sign_type=2&uid=100000000");

            announcementWindow.Activate();
        }

        private void ShowInfo()
        {
            if (_currentConfig == null) return;

            VersionText.Text = _currentConfig.Version;
            ServerText.Text = _currentConfig.ServerType;
            SizeText.Text = _currentConfig.DirectorySize;

            InfoPanel.Visibility = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowEmptyState()
        {
            InfoPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
        }

        private string DetectServerType(string configContent)
        {
            if (configContent.Contains("pcadbdpz") || configContent.Contains("channel=1"))
                return "中国大陆服务器（官服）";

            if (configContent.Contains("channel=14") || configContent.Contains("cps=bilibili"))
                return "中国大陆服务器（B服）";

            if (configContent.Contains("os_usa") || configContent.Contains("os_euro") ||
                configContent.Contains("os_asia") || configContent.Contains("channel=0"))
                return "国际服务器";

            return "未知服务器";
        }

        private string CalculateDirectorySize(string path)
        {
            try
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
                long sizeInBytes = files.Sum(file => new FileInfo(file).Length);

                return sizeInBytes switch
                {
                    >= 1073741824 => $"{sizeInBytes / 1073741824.0:F2} GB",
                    >= 1048576 => $"{sizeInBytes / 1048576.0:F2} MB",
                    >= 1024 => $"{sizeInBytes / 1024.0:F2} KB",
                    _ => $"{sizeInBytes} Bytes"
                };
            }
            catch
            {
                return "无法计算";
            }
        }

        private async Task LoadAccountsAsync()
        {
            try
            {
                if (!File.Exists(_accountsFilePath))
                {
                    DispatcherQueue.TryEnqueue(() => AccountsListView.ItemsSource = new List<GameAccountData>());
                    return;
                }

                var json = await File.ReadAllTextAsync(_accountsFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    DispatcherQueue.TryEnqueue(() => AccountsListView.ItemsSource = new List<GameAccountData>());
                    return;
                }

                List<GameAccountData>? accounts = null;
                try
                {
                    accounts = JsonSerializer.Deserialize<List<GameAccountData>>(json);
                }
                catch
                {
                    try { File.Delete(_accountsFilePath); } catch { }
                    DispatcherQueue.TryEnqueue(() => AccountsListView.ItemsSource = new List<GameAccountData>());
                    return;
                }

                DispatcherQueue.TryEnqueue(() => AccountsListView.ItemsSource = accounts ?? new List<GameAccountData>());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LoadAccountsAsync] 失败: {ex.Message}");
                DispatcherQueue.TryEnqueue(() => AccountsListView.ItemsSource = new List<GameAccountData>());
            }
        }

        private async void AddAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\miHoYo\原神");
                if (key == null) { await ShowError("无法访问注册表"); return; }

                var sdkData = key.GetValue("MIHOYOSDK_ADL_PROD_CN_h3123967166") as byte[];
                if (sdkData == null) { await ShowError("注册表数据无效"); return; }

                int nullIndex = Array.IndexOf(sdkData, (byte)0);
                int length = nullIndex >= 0 ? nullIndex : sdkData.Length;
                var sdkString = Encoding.UTF8.GetString(sdkData, 0, length);

                var accounts = await LoadAccountsFromFileAsync();
                if (accounts.Any(a => a.SdkData == sdkString)) { await ShowError("该账号已存在"); return; }

                accounts.Add(new GameAccountData
                {
                    Id = Guid.NewGuid(),
                    Name = $"账号_{DateTime.Now:MMdd_HHmmss}",
                    SdkData = sdkString,
                    LastUsed = DateTime.Now
                });

                await SaveAccountsToFileAsync(accounts);
                await LoadAccountsAsync();

                Debug.WriteLine($"[AddAccount_Click] 成功保存账号，SDK长度: {sdkString.Length}");
            }
            catch (Exception ex)
            {
                await ShowError($"添加失败: {ex.Message}");
            }
        }

        private async void RefreshAccounts_Click(object sender, RoutedEventArgs e) => await LoadAccountsAsync();

        private async void SwitchAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as Button)?.Tag is not GameAccountData account) return;

                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\miHoYo\原神");
                if (key == null) { await ShowError("无法访问注册表"); return; }

                var sdkBytes = Encoding.UTF8.GetBytes(account.SdkData);
                var target = new byte[sdkBytes.Length + 1];
                Array.Copy(sdkBytes, target, sdkBytes.Length);
                target[sdkBytes.Length] = 0;

                key.SetValue("MIHOYOSDK_ADL_PROD_CN_h3123967166", target, Microsoft.Win32.RegistryValueKind.Binary);

                await UpdateAccountLastUsedAsync(account.Id);
                await LoadAccountsAsync();

                var successDialog = new ContentDialog
                {
                    Title = "切换成功",
                    Content = $"已切换到: {account.Name}\n\n必须重启游戏才能生效！",
                    PrimaryButtonText = "我知道了",
                    XamlRoot = this.XamlRoot
                };
                await successDialog.ShowAsync();

                Debug.WriteLine($"[SwitchAccount_Click] 账号切换成功: {account.Name}");
            }
            catch (Exception ex)
            {
                await ShowError($"切换失败: {ex.Message}");
            }
        }

        private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if ((sender as Button)?.Tag is not GameAccountData account) return;

                var dialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"删除账号 '{account.Name}'？",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

                var accounts = await LoadAccountsFromFileAsync();
                accounts.RemoveAll(a => a.Id == account.Id);
                await SaveAccountsToFileAsync(accounts);
                await LoadAccountsAsync();
            }
            catch (Exception ex)
            {
                await ShowError($"删除失败: {ex.Message}");
            }
        }

        private async Task UpdateAccountLastUsedAsync(Guid id)
        {
            try
            {
                var accounts = await LoadAccountsFromFileAsync();
                var account = accounts.FirstOrDefault(a => a.Id == id);
                if (account != null)
                {
                    account.LastUsed = DateTime.Now;
                    await SaveAccountsToFileAsync(accounts);
                }
            }
            catch { }
        }

        private async Task<List<GameAccountData>> LoadAccountsFromFileAsync()
        {
            try
            {
                if (!File.Exists(_accountsFilePath)) return new List<GameAccountData>();
                var json = await File.ReadAllTextAsync(_accountsFilePath, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<GameAccountData>>(json) ?? new List<GameAccountData>();
            }
            catch { return new List<GameAccountData>(); }
        }

        private async Task SaveAccountsToFileAsync(List<GameAccountData> accounts)
        {
            try
            {
                var dir = Path.GetDirectoryName(_accountsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_accountsFilePath, JsonSerializer.Serialize(accounts, options), Encoding.UTF8);
            }
            catch (Exception ex) { Debug.WriteLine($"[SaveAccountsToFileAsync] 失败: {ex.Message}"); }
        }

        private async Task ShowError(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "操作失败",
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}