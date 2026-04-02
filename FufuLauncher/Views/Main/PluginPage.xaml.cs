using System.IO.Compression;
using Windows.System;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class PluginPage : Page
{
    public PluginViewModel ViewModel
    {
        get;
    }

    public MainViewModel MainViewModel
    {
        get;
    }
    public ControlPanelModel ControlPanelViewModel
    {
        get;
    }

    public PluginPage()
    {
        ViewModel = App.GetService<PluginViewModel>();
        MainViewModel = App.GetService<MainViewModel>();
        ControlPanelViewModel = App.GetService<ControlPanelModel>();

        InitializeComponent();
        
        ViewModel.DuplicateDetected += ViewModel_DuplicateDetected;
    }
    
    private async void ViewModel_DuplicateDetected(object? sender, string message)
    {
        await Task.Delay(1000);
        
        DispatcherQueue.TryEnqueue(async () => 
        {
            if (XamlRoot == null || !IsLoaded) return;

            await ShowDuplicateDialog(message);
        });
    }
    
    private async void OnFreeCamHelpClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "freecam.png");

            if (File.Exists(imagePath))
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(imagePath);
                await Launcher.LaunchFileAsync(file);
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "文件未找到",
                    Content = "未能在 Assets 文件夹中找到 freecam.png",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"无法打开说明图: {ex.Message}";
        }
    }
    
    private void OnOpenDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        // 创建并显示诊断窗口实例
        var diagnosticsWindow = new FufuLauncher.Views.DiagnosticsWindow();
        diagnosticsWindow.Activate();
    }
    
    private async Task ShowDuplicateDialog(string message)
    {
        if (XamlRoot == null) return;

        var dialog = new ContentDialog
        {
            Title = "插件冲突警告",
            Content = new ScrollViewer 
            { 
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                MaxHeight = 300
            },
            PrimaryButtonText = "打开插件目录",
            CloseButtonText = "忽略",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            ViewModel.OpenFolderCommand.Execute(null);
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
    
        GetPluginsBtnAnimation.Begin();
    
        if (ViewModel.Plugins.Count == 0) 
        {
            ViewModel.LoadPlugins();
        }

        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var hasShownRaw = await localSettingsService.ReadSettingAsync(LocalSettingsService.HasShownSecurityWarningKey);
        
            bool hasShown = hasShownRaw is bool b && b;

            if (!hasShown)
            {
                await ShowSecurityWarningDialog();
                await localSettingsService.SaveSettingAsync(LocalSettingsService.HasShownSecurityWarningKey, true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取或保存安全警告配置失败: {ex.Message}");
        }
    }
    
    private async Task ShowSecurityWarningDialog()
    {
        if (XamlRoot == null) return;
    
        var textBlock = new TextBlock
        {
            Text = "安全软件会阻塞该程序的正常注入运行，如无法使用或者插件消失，请关闭你电脑的安全中心！",
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 16,
            Margin = new Thickness(0, 10, 0, 0)
        };
    
        var dialog = new ContentDialog
        {
            Title = "警告",
            Content = textBlock,
            CloseButtonText = "我知道了",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
    
        await dialog.ShowAsync();
    }
    
    private void MoveDirectorySafe(string sourceDir, string destDir)
    {
        var parentDir = Path.GetDirectoryName(destDir);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        if (Path.GetPathRoot(sourceDir)!.Equals(Path.GetPathRoot(destDir), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(sourceDir, destDir);
            return;
        }
    
        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }
    
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
    
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            MoveDirectorySafe(dir, destSubDir);
        }
    
        Directory.Delete(sourceDir, true);
    }

    private void OnPluginToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && toggleSwitch.Tag is PluginItem item)
        {
            if (toggleSwitch.IsOn != item.IsEnabled)
            {
                if (ViewModel.TogglePluginCommand.CanExecute(item))
                {
                    ViewModel.TogglePluginCommand.Execute(item);
                }
                else
                {
                    toggleSwitch.IsOn = item.IsEnabled;
                }
            }
        }
    }
    
    private async void OnGetPluginsClick(object sender, RoutedEventArgs e)
    {
        string urlLatest = "http://kr2-proxy.gitwarp.top:9980/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin.zip";
        string urlOld = "http://kr2-proxy.gitwarp.top:9980/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin-old.zip";
        string urlHotSwitch = "http://kr2-proxy.gitwarp.top:9980/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/input_hot_switch.zip";
        
        var stackPanel = new StackPanel { Spacing = 10 };
        
        var rbLatest = new RadioButton { Content = "下载/更新插件(国际服通用)", IsChecked = true, GroupName = "PluginSelect", Tag = urlLatest };
        
        var rbCustom = new RadioButton { Content = "自定义插件链接", GroupName = "PluginSelect", Tag = "Custom" };
        var txtCustomUrl = new TextBox 
        { 
            PlaceholderText = "请输入下载直链", 
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(28, 0, 0, 0)
        };
        
        rbCustom.Checked += (_, _) => txtCustomUrl.Visibility = Visibility.Visible;
        rbCustom.Unchecked += (_, _) => txtCustomUrl.Visibility = Visibility.Collapsed;

        var warningText = new TextBlock 
        { 
            Text = "注意：最新体验版插件已内置手柄热切换和已适配国际服，且功能全面和性能可观", 
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 5, 0, 5)
        };
        
        stackPanel.Children.Add(new TextBlock { Text = "请选择要下载并安装的插件包：", Margin = new Thickness(0, 0, 0, 5) });
        stackPanel.Children.Add(rbLatest);
        stackPanel.Children.Add(warningText);
        stackPanel.Children.Add(rbCustom);
        stackPanel.Children.Add(txtCustomUrl);
        
        stackPanel.Children.Add(new TextBlock 
        { 
            Text = "默认使用代理加速，若失败将自动切换至GitHub直连", 
            FontSize = 12, 
            Opacity = 0.7,
            Margin = new Thickness(0, 10, 0, 0)
        });

        var dialog = new ContentDialog
        {
            Title = "获取插件",
            Content = stackPanel,
            PrimaryButtonText = "下载并安装",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var selectedUrl = urlLatest;

            if (rbCustom.IsChecked == true)
            {
                selectedUrl = txtCustomUrl.Text.Trim();
                
                if (string.IsNullOrEmpty(selectedUrl))
                {
                    var errDialog = new ContentDialog
                    {
                        Title = "输入错误",
                        Content = "请输入有效的插件下载链接。",
                        CloseButtonText = "确定",
                        XamlRoot = XamlRoot
                    };
                    await errDialog.ShowAsync();
                    return;
                }
            }

            await DownloadAndInstallPluginAsync(selectedUrl);
        }
    }
    
    private async Task DownloadAndInstallPluginAsync(string proxyUrl)
    {
        var fileName = proxyUrl.Split('/').Last();
        if (fileName.Contains("?")) fileName = fileName.Split('?')[0];
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) 
            fileName = "CustomPlugin.zip";
        
        var rawGithubUrl = proxyUrl.Replace("http://kr2-proxy.gitwarp.top:9980/", "");
        
        if (rawGithubUrl.Contains("github.com") && rawGithubUrl.Contains("/blob/") && !rawGithubUrl.Contains("?raw=true"))
        {
            rawGithubUrl += "?raw=true";
        }
        
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        var extractPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(fileName) + "_Extract_" + Guid.NewGuid());
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
        if (!Directory.Exists(pluginsDir))
        {
            Directory.CreateDirectory(pluginsDir);
        }
        
        var progressBar = new ProgressBar 
        { 
            Minimum = 0, Maximum = 100, Value = 0, Height = 20, Margin = new Thickness(0, 10, 0, 0) 
        };
        var statusText = new TextBlock 
        { 
            Text = "正在连接...", HorizontalAlignment = HorizontalAlignment.Center 
        };
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(statusText);
        stackPanel.Children.Add(progressBar);

        var progressDialog = new ContentDialog
        {
            Title = $"正在获取 {fileName}",
            Content = stackPanel,
            CloseButtonText = null,
            XamlRoot = XamlRoot
        };

        progressDialog.ShowAsync();

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
                    if (!response.IsSuccessStatusCode) throw new Exception("First attempt failed");
                }
                catch
                {
                    statusText.Text = "连接失败，正在尝试备用线路...";
                    usedFallback = true;
                    await Task.Delay(1000); 
                    response = await client.GetAsync(rawGithubUrl, HttpCompletionOption.ResponseHeadersRead);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"下载失败 (HTTP {response.StatusCode})");
                    }
                }
                
                using (response)
                {
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var totalRead = 0L;
                    var buffer = new byte[8192];
                    var isMoreToRead = true;
                    
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        while (isMoreToRead)
                        {
                            var read = await stream.ReadAsync(buffer, 0, buffer.Length);
                            if (read == 0) isMoreToRead = false;
                            else
                            {
                                await fileStream.WriteAsync(buffer, 0, read);
                                totalRead += read;
                                if (totalBytes != -1)
                                {
                                    var percent = Math.Round((double)totalRead / totalBytes * 100, 0);
                                    
                                    progressBar.Value = percent;
                                    var source = usedFallback ? "备用线路" : "主线路";
                                    statusText.Text = $"{source}下载中... {percent}%";
                                }
                            }
                        }
                    }
                }
            }
            
            statusText.Text = "正在解压...";
            progressBar.IsIndeterminate = true;
            await Task.Delay(500); 
            
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            Directory.CreateDirectory(extractPath);

            await Task.Run(() => ZipFile.ExtractToDirectory(tempPath, extractPath));
            
            try { File.Delete(tempPath); }
            catch
            {
                // ignored
            }

            statusText.Text = "正在安装...";
            
            var targetFolderName = Path.GetFileNameWithoutExtension(tempPath); 
            var finalDestDir = Path.Combine(pluginsDir, targetFolderName);
            
            var subDirs = Directory.GetDirectories(extractPath);
            var files = Directory.GetFiles(extractPath);

            string sourceDirToMove;
            
            if (subDirs.Length == 1 && files.Length == 0)
            {
                sourceDirToMove = subDirs[0];
                targetFolderName = new DirectoryInfo(sourceDirToMove).Name;
                finalDestDir = Path.Combine(pluginsDir, targetFolderName);
            }
            else
            {
                sourceDirToMove = extractPath;
            }
            
            if (Directory.Exists(finalDestDir))
            {
                Directory.Delete(finalDestDir, true);
            }
            
            await Task.Run(() => MoveDirectorySafe(sourceDirToMove, finalDestDir));
            
            try 
            {
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            }
            catch
            {
                // ignored
            }

            ViewModel.StatusMessage = $"{targetFolderName} 安装成功！";
            ViewModel.LoadPlugins();
            
            progressDialog.Hide();
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            var failDialog = new ContentDialog
            {
                Title = "下载/安装错误",
                Content = $"自动下载失败：{ex.Message}\n\n建议点击下方按钮打开浏览器手动下载。",
                PrimaryButtonText = "手动下载",
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await failDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try { await Launcher.LaunchUriAsync(new Uri(rawGithubUrl)); }
                catch
                {
                    // ignored
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private void OnConfigClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PluginItem item && item.HasConfig)
        {
            var folderName = new DirectoryInfo(item.DirectoryPath).Name;
            bool isFuFuPlugin = folderName.Contains("FuFuPlugin", StringComparison.OrdinalIgnoreCase);

            if (isFuFuPlugin)
            {
                Frame.Navigate(typeof(PluginSettingsPage), item);
                
                var navView = FindParentNavigationView(this);
                if (navView != null)
                {
                    foreach (var menuItem in navView.MenuItems)
                    {
                        if (menuItem is NavigationViewItem navItem && 
                            navItem.Tag?.ToString() == "FufuLauncher.ViewModels.PluginSettingsViewModel")
                        {
                            navView.SelectedItem = navItem;
                            break;
                        }
                    }
                }
            }
            else
            {
                Frame.Navigate(typeof(PluginConfigPage), item);
            }
        }
    }
    
    private NavigationView FindParentNavigationView(DependencyObject child)
    {
        DependencyObject parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        
        if (parentObject is NavigationView parent) return parent;
        
        return FindParentNavigationView(parentObject);
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is PluginItem pluginItem)
        {
            var currentFolderName = new DirectoryInfo(pluginItem.DirectoryPath).Name;

            var dialog = new ContentDialog
            {
                Title = "重命名插件文件夹",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var inputTextBox = new TextBox { Text = currentFolderName, AcceptsReturn = false };
            dialog.Content = inputTextBox;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var newName = inputTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != currentFolderName)
                {
                    ViewModel.PerformRename(pluginItem, newName);
                }
            }
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is PluginItem pluginItem)
        {
            if (ViewModel.DeletePluginCommand.CanExecute(pluginItem))
            {
                ViewModel.DeletePluginCommand.Execute(pluginItem);
            }
        }
    }
}