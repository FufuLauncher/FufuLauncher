using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using WinRT.Interop;
using File = System.IO.File;

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
    public string? Remark
    {
        get; set;
    }
}
public class RedeemCodeItem
{
    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("time")]
    public string Time { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("codes")]
    public List<string> Codes { get; set; } = new List<string>();

    [System.Text.Json.Serialization.JsonPropertyName("valid")]
    public string Valid { get; set; } = string.Empty;
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

    public class StringToInitialConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string name && !string.IsNullOrEmpty(name))
            {
                return name.Substring(0, 1).ToUpper();
            }
            return "?";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
    public sealed partial class BlankPage : Page
    {
        private GameConfigData? _currentConfig;
        private readonly string _accountsFilePath;
        private readonly ILocalSettingsService _localSettingsService;

        public BlankPage()
        {
            InitializeComponent();
            _localSettingsService = App.GetService<ILocalSettingsService>();

            var localFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _accountsFilePath = Path.Combine(localFolder, "FufuLauncher", "game_accounts.json");

            Loaded += BlankPage_Loaded;
        }

        private void PathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ApplyPathButton != null)
            {
                ApplyPathButton.IsEnabled = !string.IsNullOrWhiteSpace(PathTextBox.Text);
            }
        }
        
private async void CreateShortcut_Click(object sender, RoutedEventArgs e)
{
    try
    {
        // ==========================================
        // 1. 基础检查与路径获取 (保持原逻辑)
        // ==========================================
        var localSettings = App.GetService<ILocalSettingsService>();
        var settingObj = await localSettings.ReadSettingAsync("GameInstallationPath");
        var rawPath = settingObj as string;

        if (string.IsNullOrEmpty(rawPath))
        {
            await ShowError("未设置游戏路径。");
            return;
        }
        
        var finalExePath = rawPath;
        
        // 检查原神主程序路径
        if (Directory.Exists(rawPath))
        {
            var cnPath = Path.Combine(rawPath, "YuanShen.exe");
            var globalPath = Path.Combine(rawPath, "GenshinImpact.exe");

            if (File.Exists(cnPath)) finalExePath = cnPath;
            else if (File.Exists(globalPath)) finalExePath = globalPath;
            else
            {
                await ShowError($"在文件夹中找不到游戏主程序：\n{rawPath}");
                return;
            }
        }
        else if (!File.Exists(rawPath))
        {
             await ShowError($"路径无效，文件或文件夹不存在：\n{rawPath}");
             return;
        }

        // ==========================================
        // 2. 准备关键数据
        // ==========================================
        
        // 获取当前启动器(FufuLauncher)的完整路径
        var appPath = Environment.ProcessPath; 
        
        // 核心参数部分：--elevated-inject "游戏路径"
        string argsOnly = $"--elevated-inject \"{finalExePath}\"";

        // 完整命令行： "启动器路径" --elevated-inject "游戏路径"
        // 注意：给 appPath 也加上引号，防止安装路径带空格导致无法识别
        string fullCommandLine = $"\"{appPath}\" {argsOnly}";

        // ==========================================
        // 3. 弹窗询问用户意图
        // ==========================================
        var choiceDialog = new ContentDialog
        {
            Title = "选择操作",
            Content = "请选择您想要执行的操作：\n\n• 创建桌面快捷方式：直接在桌面生成图标。\n• 复制启动命令：获取完整命令行，可用于 Steam 或 脚本。",
            PrimaryButtonText = "创建桌面快捷方式",
            SecondaryButtonText = "复制启动命令",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        var choiceResult = await choiceDialog.ShowAsync();

        if (choiceResult == ContentDialogResult.None)
        {
            return;
        }
        
        if (choiceResult == ContentDialogResult.Primary)
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktopPath, "快捷启动原神.lnk");

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            
            var shortcut = shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = appPath;
            shortcut.Arguments = argsOnly;
            
            shortcut.WorkingDirectory = AppContext.BaseDirectory;
            shortcut.IconLocation = finalExePath + ",0";
            shortcut.Description = "通过 FufuLauncher 注入启动原神";

            shortcut.Save();
            
            var dialog = new ContentDialog
            {
                Title = "快捷方式已创建",
                Content = "已创建桌面快捷方式，请检查你的电脑桌面",
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
        else if (choiceResult == ContentDialogResult.Secondary)
        {
            var argTextBox = new TextBox
            {
                Text = fullCommandLine,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 100,
                AcceptsReturn = true,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Courier New, Monospace")
            };

            var copyDialog = new ContentDialog
            {
                Title = "启动命令",
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children = 
                    {
                        new TextBlock 
                        { 
                            Text = "以下是启动命令，您可以直接在 CMD、PowerShell 或 Steam 的“非 Steam 游戏”目标中使用：", 
                            TextWrapping = TextWrapping.Wrap
                        },
                        argTextBox
                    }
                },
                PrimaryButtonText = "复制并关闭",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var copyResult = await copyDialog.ShowAsync();

            if (copyResult == ContentDialogResult.Primary)
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(fullCommandLine);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
        }
    }
    catch (Exception ex)
    {
        await ShowError($"操作失败: {ex.Message}");
    }
}

        private async Task LoadRedeemCodesAsync()
        {
            try
            {
                CodesLoadingRing.IsActive = true;
                CodesLoadingRing.Visibility = Visibility.Visible;
                NoCodesText.Visibility = Visibility.Collapsed;
                RedeemCodesList.Visibility = Visibility.Collapsed;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

                var json = await client.GetStringAsync("https://cnb.cool/bettergi/genshin-redeem-code/-/git/raw/main/codes.json");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var codes = JsonSerializer.Deserialize<List<RedeemCodeItem>>(json, options);

                if (codes != null && codes.Count > 0)
                {
                    RedeemCodesList.ItemsSource = codes;
                    RedeemCodesList.Visibility = Visibility.Visible;
                }
                else
                {
                    NoCodesText.Text = "当前没有新的兑换码";
                    NoCodesText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RedeemCodes] 获取失败: {ex.Message}");
                NoCodesText.Text = "获取失败，请检查网络";
                NoCodesText.Visibility = Visibility.Visible;
            }
            finally
            {
                CodesLoadingRing.IsActive = false;
                CodesLoadingRing.Visibility = Visibility.Collapsed;
            }
        }

        private void ToggleCodes_Click(object sender, RoutedEventArgs e)
        {
            if (RedeemContentPanel.Visibility == Visibility.Visible)
            {
                RedeemContentPanel.Visibility = Visibility.Collapsed;
                RedeemChevron.Glyph = "\uE70D"; // ChevronDown
            }
            else
            {
                RedeemContentPanel.Visibility = Visibility.Visible;
                RedeemChevron.Glyph = "\uE70E"; // ChevronUp
            }
        }

        private void CopyCode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string code)
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(code);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

                var originalContent = btn.Content;
                btn.Content = "已复制";
                btn.IsEnabled = false;

                Task.Delay(1000).ContinueWith(_ =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        btn.Content = originalContent;
                        btn.IsEnabled = true;
                    });
                });
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


            var downloadWindow = new DownloadWindow(targetPath);
            downloadWindow.Activate();
        }
        private async void SwitchServer_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfig == null || string.IsNullOrEmpty(_currentConfig.GamePath))
            {
                await ShowError("未找到游戏路径，请先在设置中指定游戏位置。");
                return;
            }

            string gameDir = _currentConfig.GamePath;

            if (File.Exists(gameDir))
            {
                gameDir = Path.GetDirectoryName(gameDir) ?? gameDir;
            }

            string configPath = Path.Combine(gameDir, "config.ini");

            if (!File.Exists(configPath))
            {
                string parentDir = Directory.GetParent(gameDir)?.FullName ?? "";
                string parentConfig = Path.Combine(parentDir, "config.ini");

                if (File.Exists(parentConfig))
                {
                    gameDir = parentDir;
                    configPath = parentConfig;
                }
                else
                {
                    await ShowError($"无法找到 config.ini 配置文件。\n\n尝试寻找的路径是：\n{configPath}\n\n请检查您的“游戏路径”设置是否正确指向了游戏安装目录（包含 YuanShen.exe 的文件夹）。");
                    return;
                }
            }

            var dialog = new ContentDialog
            {
                Title = "切换服务器",
                Content = "请选择你要切换到的服务器：",
                PrimaryButtonText = "切换到 Bilibili 服",
                SecondaryButtonText = "切换到 官方服务器",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                await PerformServerSwitch(gameDir, configPath, true);
            }
            else if (result == ContentDialogResult.Secondary)
            {
                await PerformServerSwitch(gameDir, configPath, false);
            }
        }
        private async Task PerformServerSwitch(string gameDir, string configPath, bool toBilibili)
        {
            try
            {
                // 官服: channel=1, sub_channel=1, cps=mihoyo
                // B服: channel=14, sub_channel=0, cps=bilibili
                string channel = toBilibili ? "14" : "1";
                string subChannel = toBilibili ? "0" : "1";
                string cps = toBilibili ? "bilibili" : "mihoyo";

                string[] lines = await File.ReadAllLinesAsync(configPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("channel=")) lines[i] = $"channel={channel}";
                    else if (lines[i].StartsWith("sub_channel=")) lines[i] = $"sub_channel={subChannel}";
                    else if (lines[i].StartsWith("cps=")) lines[i] = $"cps={cps}";
                }
                await File.WriteAllLinesAsync(configPath, lines);

                string dataDirName = "YuanShen_Data";
                if (!Directory.Exists(Path.Combine(gameDir, dataDirName)))
                {
                    dataDirName = "GenshinImpact_Data";
                }

                string pluginsDir = Path.Combine(gameDir, dataDirName, "Plugins");
                string targetSdkPath = Path.Combine(pluginsDir, "PCGameSDK.dll");

                if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);

                if (toBilibili)
                {
                    string appBaseDir = AppContext.BaseDirectory;
                    string sourceSdkPath = Path.Combine(appBaseDir, "Assets", "PCGameSDK.dll");

                    if (File.Exists(sourceSdkPath))
                    {
                        File.Copy(sourceSdkPath, targetSdkPath, true);
                    }
                    else
                    {
                        await ShowError($"缺失核心文件：{sourceSdkPath}\n请确保已将 PCGameSDK.dll 放入软件的 Assets 文件夹。");
                        return;
                    }
                }
                else
                {
                    if (File.Exists(targetSdkPath))
                    {
                        File.Delete(targetSdkPath);
                    }
                }

                await LoadGameConfig(_currentConfig.GamePath);

                var successDialog = new ContentDialog
                {
                    Title = "切换成功",
                    Content = $"已成功切换至 {(toBilibili ? "Bilibili 服" : "官方服务器")}。\nSDK已{(toBilibili ? "部署" : "清理")}。",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await successDialog.ShowAsync();

            }
            catch (Exception ex)
            {
                await ShowError($"切换失败: {ex.Message}");
            }
        }

        private async Task LoadGameConfig(string gameExePath)
        {
            if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath)) return;

            var gameDir = Path.GetDirectoryName(gameExePath);
            var configPath = Path.Combine(gameDir, "config.ini");
            var serverType = "未知服务器";

            if (File.Exists(configPath))
            {
                try
                {
                    var lines = await File.ReadAllLinesAsync(configPath);
                    var channel = "1";

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("channel="))
                        {
                            channel = line.Split('=')[1].Trim();
                            break;
                        }
                    }

                    if (channel == "14") serverType = "Bilibili 服";
                    else if (channel == "1") serverType = "官方服务器";
                    else serverType = $"自定义/其他 (Channel: {channel})";
                }
                catch
                {
                    serverType = "读取配置文件失败";
                }
            }

            if (_currentConfig != null)
            {
                _currentConfig.ServerType = serverType;
            }
        }


        private void OpenMap_Click(object sender, RoutedEventArgs e)
        {
            var newWindow = new Window();
            newWindow.Title = "提瓦特大地图";
            var hWnd = WindowNative.GetWindowHandle(newWindow);
            var winId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(winId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));

            var rootFrame = new Frame();
            rootFrame.Navigate(typeof(MapPage), newWindow);

            newWindow.Content = rootFrame;
            newWindow.Activate();
        }

        private async Task<bool> ValidateGameExecutableAsync(string path)
        {
            var cnExe = Path.Combine(path, "YuanShen.exe");
            var globalExe = Path.Combine(path, "GenshinImpact.exe");

            if (File.Exists(cnExe))
            {
                return true;
            }

            if (File.Exists(globalExe))
            {
                var dialog = new ContentDialog
                {
                    Title = "国际服客户端",
                    Content = "注意：本启动器的注入功能主要是针对国服设计的。在国际服客户端上，此功能可能无法生效或导致未知的错误。\n\n是否继续使用此路径？",
                    PrimaryButtonText = "继续使用",
                    CloseButtonText = "放弃并清除",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                var result = await dialog.ShowAsync();
                return result == ContentDialogResult.Primary;
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "无效的游戏路径",
                    Content = "在该路径下未找到游戏主程序 (YuanShen.exe 或 GenshinImpact.exe)。\n\n请确认您选择的是包含游戏可执行文件的安装目录。",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
                return false;
            }
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
                    bool isValid = await ValidateGameExecutableAsync(path);

                    if (isValid)
                    {
                        await LoadGameInfoAsync(path);
                        await _localSettingsService.SaveSettingAsync("GameInstallationPath", path);
                        WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(path));

                        Debug.WriteLine($"[ProcessPathInput] 路径设置成功: {path}");
                    }
                    else
                    {
                        PathTextBox.Text = string.Empty;
                        ShowEmptyState();
                    }
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
                    else
                    {
                        PathTextBox.Text = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessPathInput] 处理失败: {ex.Message}");
                await ShowError($"路径处理失败: {ex.Message}");

                PathTextBox.Text = string.Empty;
                ShowEmptyState();
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
    EntranceStoryboard.Begin();
    Debug.WriteLine("========== [Debug] BlankPage_Loaded 开始 ==========");
    try
    {
        var savedPathObj = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
        var savedPath = savedPathObj as string;
        
        Debug.WriteLine($"[Debug] 读取到的本地保存路径: '{savedPath}'");
        Debug.WriteLine($"[Debug] IsNullOrWhiteSpace 判断结果: {string.IsNullOrWhiteSpace(savedPath)}");

        if (!string.IsNullOrWhiteSpace(savedPath))
        {
            Debug.WriteLine("[Debug] 路径非空，跳过自动检测，直接加载已有路径。");
            savedPath = savedPath.Trim('"').Trim();
            PathTextBox.Text = savedPath;
            await LoadGameInfoAsync(savedPath);
        }
        else
        {
            Debug.WriteLine("[Debug] 路径为空，准备调用 GamePathFinder.FindGamePath()...");
            var foundPath = GamePathFinder.FindGamePath();
            Debug.WriteLine($"[Debug] GamePathFinder 返回的路径为: '{foundPath}'");

            if (!string.IsNullOrEmpty(foundPath))
            {
                Debug.WriteLine("[Debug] 进入 DispatcherQueue，准备调用 ShowAutoPathDialog");
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await ShowAutoPathDialog(foundPath);
                });
            }
            else
            {
                Debug.WriteLine("[Debug] 未找到路径，跳过弹窗。");
            }
        }

        await LoadAccountsAsync();
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[Debug] BlankPage_Loaded 发生异常: {ex.Message}\n{ex.StackTrace}");
    }
    await LoadRedeemCodesAsync();
    Debug.WriteLine("========== [Debug] BlankPage_Loaded 结束 ==========");
}

private async Task ShowAutoPathDialog(string foundPath)
{
    Debug.WriteLine($"========== [Debug] ShowAutoPathDialog 开始 ==========");
    Debug.WriteLine($"[Debug] 接收到的 foundPath: {foundPath}");

    if (string.IsNullOrEmpty(foundPath)) 
    {
        Debug.WriteLine("[Debug] foundPath 为空，已 return。");
        return;
    }
    
    if (XamlRoot == null)
    {
        Debug.WriteLine("[Debug] 严重问题: XamlRoot 为 null！弹窗无法显示，已 return。");
        return;
    }

    try
    {
        Debug.WriteLine("[Debug] 正在创建 ContentDialog...");
        var dialog = new ContentDialog
        {
            Title = "自动找到游戏路径",
            Content = $"检测到可能的《原神》安装路径：\n\n{foundPath}\n\n是否应用此路径？",
            PrimaryButtonText = "应用",
            CloseButtonText = "手动选择",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot
        };

        Debug.WriteLine("[Debug] 准备调用 dialog.ShowAsync()...");
        var result = await dialog.ShowAsync();
        Debug.WriteLine($"[Debug] 弹窗被关闭，用户的选择是: {result}");

        if (result == ContentDialogResult.Primary)
        {
            Debug.WriteLine("[Debug] 用户点击了“应用”，正在保存...");
            PathTextBox.Text = foundPath;
            await LoadGameInfoAsync(foundPath);
            await _localSettingsService.SaveSettingAsync("GameInstallationPath", foundPath);
            WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(foundPath));
        }
        else
        {
            Debug.WriteLine("[Debug] 用户点击了“手动选择”，调用 PickGameFolderAsync()");
            await PickGameFolderAsync();
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[Debug] ShowAutoPathDialog 发生异常 (可能是多次弹窗冲突): {ex.Message}\n{ex.StackTrace}");
    }
}

        private async void SelectPath_Click(object sender, RoutedEventArgs e)
        {
            await PickGameFolderAsync();
        }

        private async Task PickGameFolderAsync()
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
                await ProcessPathInput(path);

                return;
            }
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


        private void OpenAnnouncement_Click(object sender, RoutedEventArgs e)
        {
            var announcementWindow = new AnnouncementWindow();
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
                return "中国大陆服务器";

            if (configContent.Contains("channel=14") || configContent.Contains("cps=bilibili"))
                return "中国大陆服务器";

            if (configContent.Contains("os") || configContent.Contains("os") ||
                configContent.Contains("os") || configContent.Contains("channel=0"))
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
                if (sdkData == null) { await ShowError("当前没有登录的账号信息（注册表数据为空）"); return; }

                int nullIndex = Array.IndexOf(sdkData, (byte)0);
                int length = nullIndex >= 0 ? nullIndex : sdkData.Length;
                var sdkString = Encoding.UTF8.GetString(sdkData, 0, length);

                var accounts = await LoadAccountsFromFileAsync();
                if (accounts.Any(a => a.SdkData == sdkString))
                {
                    await ShowError("该账号已经保存过了，无需重复保存。");
                    return;
                }

                var inputTextBox = new TextBox
                {
                    PlaceholderText = "请输入账号名称 (例如: 大号 / 小号)",
                    MaxLength = 20,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var dialog = new ContentDialog
                {
                    Title = "保存新账号",
                    Content = inputTextBox,
                    PrimaryButtonText = "保存",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();

                if (result != ContentDialogResult.Primary) return;

                string accountName = inputTextBox.Text.Trim();
                if (string.IsNullOrEmpty(accountName))
                {
                    accountName = $"账号_{DateTime.Now:MMdd_HHmmss}";
                }

                accounts.Add(new GameAccountData
                {
                    Id = Guid.NewGuid(),
                    Name = accountName,
                    SdkData = sdkString,
                    LastUsed = DateTime.Now
                });

                await SaveAccountsToFileAsync(accounts);
                await LoadAccountsAsync();

                Debug.WriteLine($"[AddAccount_Click] 成功保存账号: {accountName}");
            }
            catch (Exception ex)
            {
                await ShowError($"保存失败: {ex.Message}");
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

        private TextBox? _currentEditBox;
        private TextBlock? _currentTextBlock;
        private StackPanel? _currentStackPanel;
        private GameAccountData? _currentAccount;

        private void AccountName_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (_currentEditBox != null)
            {
                CancelEdit();
            }

            if (sender is TextBlock textBlock &&
                FindParent<StackPanel>(textBlock) is StackPanel stackPanel &&
                textBlock.DataContext is GameAccountData account)
            {
                _currentTextBlock = textBlock;
                _currentStackPanel = stackPanel;
                _currentAccount = account;

                _currentTextBlock.Visibility = Visibility.Collapsed;

                _currentEditBox = new TextBox
                {
                    Text = account.Remark ?? account.Name,
                    MinWidth = 100,
                    MaxLength = 20,
                    VerticalAlignment = VerticalAlignment.Center
                };

                _currentEditBox.KeyDown += EditBox_KeyDown;

                _currentEditBox.LostFocus += (_, _) => CancelEdit();

                int index = stackPanel.Children.IndexOf(textBlock);
                stackPanel.Children.Insert(index, _currentEditBox);

                _currentEditBox.Focus(FocusState.Programmatic);
                _currentEditBox.SelectAll();

                AddHandler(PointerPressedEvent, new PointerEventHandler(Page_PointerPressed), true);
            }
        }
        private void EditBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                CommitEdit();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CancelEdit();
            }
        }
        private async void CommitEdit()
        {
            if (_currentEditBox == null || _currentAccount == null) return;

            string newRemark = _currentEditBox.Text.Trim();

            if (string.IsNullOrEmpty(newRemark) || newRemark == _currentAccount.Name)
            {
                _currentAccount.Remark = null;
            }
            else
            {
                _currentAccount.Remark = newRemark;
            }

            CleanupEditUI();

            try
            {
                var accounts = await LoadAccountsFromFileAsync();

                var accountToUpdate = accounts.FirstOrDefault(a => a.SdkData == _currentAccount.SdkData);
                if (accountToUpdate != null)
                {
                    accountToUpdate.Remark = _currentAccount.Remark;
                    await SaveAccountsToFileAsync(accounts);
                }

                await LoadAccountsAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存备注失败: {ex.Message}");
            }
        }
        private void CleanupEditUI()
        {
            if (_currentEditBox == null || _currentStackPanel == null || _currentTextBlock == null) return;

            try
            {
                this.RemoveHandler(PointerPressedEvent, new PointerEventHandler(Page_PointerPressed));
                _currentStackPanel.Children.Remove(_currentEditBox);
                _currentTextBlock.Visibility = Visibility.Visible;
            }
            finally
            {
                _currentEditBox = null;
                _currentTextBlock = null;
                _currentStackPanel = null;
                _currentAccount = null;
            }
        }
        private void Page_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_currentEditBox != null)
            {
                var ptr = e.GetCurrentPoint(_currentEditBox);
                if (ptr.Properties.IsLeftButtonPressed)
                {
                    if (ptr.Position.X < 0 || ptr.Position.Y < 0 ||
                        ptr.Position.X > _currentEditBox.ActualWidth || ptr.Position.Y > _currentEditBox.ActualHeight)
                    {
                        CancelEdit();
                    }
                }
            }
        }

        private void CancelEdit()
        {
            CleanupEditUI();
        }

        private T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var current = child;
            while (current != null)
            {
                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
                if (current is T typedParent)
                    return typedParent;
            }
            return null;
        }
    }
}