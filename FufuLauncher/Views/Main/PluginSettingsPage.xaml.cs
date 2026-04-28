using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using Windows.System;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage : Page
{
    public PluginSettingsViewModel ViewModel { get; }
    public MainViewModel MainVM { get; }
    public ControlPanelModel ControlPanelVM { get; }
    private FeedbackWindow _feedbackWindow;
    private Window _prWindow;

    public PluginSettingsPage()
    {
        ViewModel = new PluginSettingsViewModel();
        MainVM = App.GetService<MainViewModel>();
        ControlPanelVM = App.GetService<ControlPanelModel>();
        InitializeComponent();
        
        Loaded += PluginSettingsPage_Loaded;
    }
    
    private async void PluginSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsPluginCorrupted())
        {
            var dialog = new ContentDialog
            {
                Title = "插件可能已损坏",
                Content = "插件文件异常\n这通常是因为插件被杀毒软件（如 Windows Defender）误判拦截或遭到破坏\n\n建议您：\n1. 将本软件目录加入杀毒软件白名单\n2. 在本页面重新下载并安装插件",
                PrimaryButtonText = "我知道了",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }

        await VerifyFpsPluginHashAsync();
    }
    
    private async void OnRepairFpsPluginClick(object sender, RoutedEventArgs e)
    {
        string zipFilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "FPS.zip");
        string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        string extractPath = Path.Combine(Path.GetTempPath(), "FPS_Extract_" + Guid.NewGuid());
        string finalDestDir = Path.Combine(pluginsDir, "FPS");

        if (!File.Exists(zipFilePath))
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("错误", "未找到文件，请确认启动器文件完整", NotificationType.Error));
            return;
        }

        var progressDialog = new ContentDialog
        {
            Title = "正在修复FPS插件",
            Content = new ProgressBar { IsIndeterminate = true, Height = 20, Margin = new Thickness(0, 10, 0, 0) },
            XamlRoot = XamlRoot
        };

        _ = progressDialog.ShowAsync();

        try
        {
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            Directory.CreateDirectory(extractPath);
            
            await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, extractPath));
            
            var subDirs = Directory.GetDirectories(extractPath);
            string sourceDirToMove = (subDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0) ? subDirs[0] : extractPath;

            if (Directory.Exists(finalDestDir)) Directory.Delete(finalDestDir, true);
            
            await Task.Run(() => MoveDirectorySafe(sourceDirToMove, finalDestDir));

            progressDialog.Hide();
            ViewModel.LoadConfiguration();
            
            await VerifyFpsPluginHashAsync();

            WeakReferenceMessenger.Default.Send(new NotificationMessage("成功", "FPS显示插件已成功修复并安装", NotificationType.Success));
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            var failDialog = new ContentDialog
            {
                Title = "修复失败",
                Content = ex.Message,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };
            await failDialog.ShowAsync();
        }
        finally
        {
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        }
    }
    
    private async Task VerifyFpsPluginHashAsync()
    {
        try
        {
            string hashFilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "hash.txt");
            string fpsPluginPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS", "FPS.dll");

            if (!File.Exists(hashFilePath) || !File.Exists(fpsPluginPath))
            {
                return;
            }

            string expectedHash = string.Empty;
            using (var reader = new StreamReader(hashFilePath))
            {
                expectedHash = (await reader.ReadLineAsync())?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(expectedHash))
            {
                return;
            }

            string actualHash = string.Empty;
            using (var sha512 = SHA512.Create())
            {
                using (var stream = File.OpenRead(fpsPluginPath))
                {
                    byte[] hashBytes = await sha512.ComputeHashAsync(stream);
                    actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "警告", 
                    "帧数显示插件哈希不匹配，可能已被篡改或损坏，请重新安装启动器", 
                    NotificationType.Error));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FPS插件哈希校验异常: {ex.Message}");
        }
    }

    public bool InvertBool(bool value) => !value;
    
    private void OnFeedbackClick(object sender, RoutedEventArgs e)
    {
        if (_feedbackWindow == null)
        {
            _feedbackWindow = new FeedbackWindow();
            _feedbackWindow.Closed += (s, args) => _feedbackWindow = null;
        }
        _feedbackWindow.Activate();
    }

    private void OnSwitchPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is PresetModel preset)
        {
            if (preset.IsLocked)
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage("操作受限", "此预设针对匹配的插件，因不符合指定插件，已被锁定", NotificationType.Warning));
                return;
            }
            ViewModel.SwitchPreset(preset);
        }
    }

    private async void OnCreateNewPresetClick(object sender, RoutedEventArgs e)
    {
        var inputTextBox = new TextBox { PlaceholderText = "请输入新预设名称" };
        var dialog = new ContentDialog
        {
            Title = "创建新预设",
            Content = inputTextBox,
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
        {
            var currentData = ViewModel.CurrentPreset?.ConfigData;
            var currentHash = ViewModel.CurrentPreset?.DllHash ?? "";
            
            if (currentData != null)
            {
                var newPreset = ViewModel.CreateNewPreset(inputTextBox.Text.Trim(), currentData, currentHash);
                ViewModel.SwitchPreset(newPreset);
            }
        }
    }
    
private void OnPullRequestsClick(object sender, RoutedEventArgs e)
{
    if (_prWindow == null)
    {
        _prWindow = new Window();
        _prWindow.Title = "Pull Request";
        _prWindow.Closed += (s, args) => _prWindow = null;

        _prWindow.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        _prWindow.ExtendsContentIntoTitleBar = true;

        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(_prWindow);
        Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(600, 450));
        
        var titleBarGrid = new Grid { Height = 32 };
        var titleText = new TextBlock
        {
            Text = "Pull Request",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            FontSize = 12
        };
        titleBarGrid.Children.Add(titleText);
        
        var contentStackPanel = new StackPanel 
        { 
            Padding = new Thickness(24, 16, 24, 24),
            Spacing = 16 
        };

        var textBlock = new TextBlock
        {
            Text = "本项目目前由个人开发者(CodeCubist)独立维护\n我们坚持并积极落实以玩家痛点为核心的开发方向，开放透明和高效的推动更新\n提交Pull Requests是让拥有代码开发编写能力的用户可以协助推进开发进度、优化结构及分摊项目维护压力的核心途径\n如可提供帮助，请通过下方按钮前往代码仓库提交你的贡献",
            TextWrapping = TextWrapping.Wrap
        };

        var openLinkBtn = new Button 
        { 
            Content = "访问GitHub仓库提交Pull Request", 
            HorizontalAlignment = HorizontalAlignment.Left 
        };

        openLinkBtn.Click += async (s, args) => 
        { 
            await Launcher.LaunchUriAsync(new Uri("https://github.com/FufuLauncher/FufuLauncher/pulls")); 
        };

        contentStackPanel.Children.Add(textBlock);
        contentStackPanel.Children.Add(openLinkBtn);
        
        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(titleBarGrid, 0);
        Grid.SetRow(contentStackPanel, 1);

        rootGrid.Children.Add(titleBarGrid);
        rootGrid.Children.Add(contentStackPanel);

        _prWindow.Content = rootGrid;
        
        _prWindow.SetTitleBar(titleBarGrid);
    }
    _prWindow.Activate();
}

    private async void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is PresetModel preset)
        {
            var dialog = new ContentDialog
            {
                Title = "警告",
                Content = $"确定要删除预设 \"{preset.Name}\" 吗？此操作无法恢复！",
                PrimaryButtonText = "确认删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                ViewModel.DeletePreset(preset);
            }
        }
    }

    private async void OnDownloadPluginClick(object sender, RoutedEventArgs e)
    {
        string urlLatest = "http://kr2-proxy.gitwarp.top:9980/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin.zip";
        await DownloadAndInstallPluginAsync(urlLatest);
    }

    private async void OnDownloadVmpPluginClick(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "选择加密版本",
            Content = "加密版插件与普通版功能完全相同，主要用于降低被杀毒软件误判的概率\n请选择您要下载的加密强度：\n\n" +
                      "1. 标准加密版（推荐）：使用低加密模式，理论上足以防误判，对性能影响较小\n" +
                      "2. 高强度加密版：使用高强度的混淆保护，能最大程度防杀软，但会导致插件运行性能严重下降\n\n" +
                      "总而言之，加密强度越高，游戏和插件的性能就越差，但是防误判就越好，自行决策使用\n\n" +
                      "请确认您的选择：",
            PrimaryButtonText = "下载标准加密版",
            SecondaryButtonText = "下载高强度加密版",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary)
        {
            string urlVmp = "http://kr2-proxy.gitwarp.top:9980/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin.vmp.zip";
            await DownloadAndInstallPluginAsync(urlVmp);
        }
        else if (result == ContentDialogResult.Secondary)
        {
            string urlVmp2 = "http://kr2-proxy.gitwarp.top:9980/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin.vmp2.zip";
            await DownloadAndInstallPluginAsync(urlVmp2);
        }
    }

    private async Task DownloadAndInstallPluginAsync(string proxyUrl)
    {
        var fileName = proxyUrl.Split('/').Last();
        if (fileName.Contains("?")) fileName = fileName.Split('?')[0];
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) 
            fileName = "FuFuPlugin.zip";
        
        var rawGithubUrl = proxyUrl.Replace("http://kr2-proxy.gitwarp.top:9980/", "");
        if (rawGithubUrl.Contains("github.com") && rawGithubUrl.Contains("/blob/") && !rawGithubUrl.Contains("?raw=true"))
        {
            rawGithubUrl += "?raw=true";
        }
        
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        var extractPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(fileName) + "_Extract_" + Guid.NewGuid());
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");

        if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
        
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 20, Margin = new Thickness(0, 10, 0, 0) };
        var statusText = new TextBlock { Text = "正在连接...", HorizontalAlignment = HorizontalAlignment.Center };
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(statusText);
        stackPanel.Children.Add(progressBar);

        var progressDialog = new ContentDialog
        {
            Title = $"正在获取插件",
            Content = stackPanel,
            XamlRoot = XamlRoot
        };

        _ = progressDialog.ShowAsync();

        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                HttpResponseMessage response;
                bool usedFallback = false;
                
                try 
                {
                    response = await client.GetAsync(proxyUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) throw new Exception("主线路失败");
                }
                catch
                {
                    statusText.Text = "连接失败，正在尝试备用线路...";
                    usedFallback = true;
                    response = await client.GetAsync(rawGithubUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) throw new Exception($"下载失败 (HTTP {response.StatusCode})");
                }
                
                using (response)
                {
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var totalRead = 0L;
                    var buffer = new byte[8192];
                    
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (totalBytes != -1)
                            {
                                progressBar.Value = Math.Round((double)totalRead / totalBytes * 100, 0);
                                statusText.Text = $"{(usedFallback ? "备用" : "主")}线路下载中... {progressBar.Value}%";
                            }
                        }
                    }
                }
            }
            
            statusText.Text = "正在解压并安装...";
            progressBar.IsIndeterminate = true;
            
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            Directory.CreateDirectory(extractPath);

            await Task.Run(() => ZipFile.ExtractToDirectory(tempPath, extractPath));
            
            var targetFolderName = "FuFuPlugin"; 
            var finalDestDir = Path.Combine(pluginsDir, targetFolderName);
            
            var subDirs = Directory.GetDirectories(extractPath);
            string sourceDirToMove = (subDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0) ? subDirs[0] : extractPath;

            if (Directory.Exists(finalDestDir)) Directory.Delete(finalDestDir, true);
            
            await Task.Run(() => MoveDirectorySafe(sourceDirToMove, finalDestDir));
            
            progressDialog.Hide();
            ViewModel.LoadConfiguration();
            
            WeakReferenceMessenger.Default.Send(new NotificationMessage("成功", "插件已安装并刷新", NotificationType.Success));
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            var failDialog = new ContentDialog
            {
                Title = "错误",
                Content = $"安装失败：{ex.Message}",
                PrimaryButtonText = "手动下载",
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };
            if (await failDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri(rawGithubUrl));
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        }
    }

    private void MoveDirectorySafe(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            MoveDirectorySafe(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
        Directory.Delete(sourceDir, true);
    }
}