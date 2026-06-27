/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using Windows.System;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage : Page
{
    public PluginSettingsViewModel ViewModel { get; }
    public MainViewModel MainVM { get; }
    public ControlPanelModel ControlPanelVM { get; }
    private FeedbackWindow _feedbackWindow;
    private Window _prWindow;
    private Windows.Foundation.Point _cropPointerPosition;
    private bool _isCropDragging = false;
    
    private bool _hasShownFpsWarning = false;
    private bool _isEnforcingFpsDisable = false;
    private bool _isInitializing = true;
    private FileSystemWatcher _mainPluginWatcher;
    private bool _hasShownMainPluginMissingWarning = false;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public PluginSettingsPage()
    {
        ViewModel = new PluginSettingsViewModel();
        MainVM = App.GetService<MainViewModel>();
        ControlPanelVM = App.GetService<ControlPanelModel>();
        InitializeComponent();
    
        Loaded += PluginSettingsPage_Loaded;
        Unloaded += PluginSettingsPage_Unloaded;
        
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }
    private void CropScrollViewer_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(CropScrollViewer);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isCropDragging = true;
            _cropPointerPosition = point.Position;
            CropScrollViewer.CapturePointer(e.Pointer);
        }
    }

    private void CropScrollViewer_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isCropDragging)
        {
            var point = e.GetCurrentPoint(CropScrollViewer);
            var deltaX = point.Position.X - _cropPointerPosition.X;
            var deltaY = point.Position.Y - _cropPointerPosition.Y;
        
            CropScrollViewer.ChangeView(
                CropScrollViewer.HorizontalOffset - deltaX,
                CropScrollViewer.VerticalOffset - deltaY,
                null);
            
            _cropPointerPosition = point.Position;
        }
    }
    
    private async void OnInjectionToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch)
        {
            if (toggleSwitch.IsOn == MainVM.UseInjection) return;

            if (toggleSwitch.IsOn)
            {
                var osArch = RuntimeInformation.OSArchitecture;
                if (osArch == Architecture.Arm || 
                    osArch == Architecture.Arm64)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "架构兼容性警告",
                        Content = "您的电脑可能为ARM架构，注入功能在ARM架构的电脑中不可用，是否确认继续开启？",
                        PrimaryButtonText = "继续开启",
                        CloseButtonText = "取消",
                        XamlRoot = XamlRoot
                    };

                    var result = await dialog.ShowAsync();
                    
                    if (result != ContentDialogResult.Primary)
                    {
                        toggleSwitch.IsOn = false;
                        return; 
                    }
                }
            }
            
            MainVM.UseInjection = toggleSwitch.IsOn;
        }
    }

    private void CropScrollViewer_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isCropDragging)
        {
            _isCropDragging = false;
            CropScrollViewer.ReleasePointerCapture(e.Pointer);
        }
    }

    private void CropScrollViewer_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(CropScrollViewer).Properties.MouseWheelDelta;
        if (delta == 0) return;

        double newZoom = CropScrollViewer.ZoomFactor + (delta > 0 ? 0.2 : -0.2);
        newZoom = Math.Max(CropScrollViewer.MinZoomFactor, Math.Min(newZoom, CropScrollViewer.MaxZoomFactor));
    
        CropScrollViewer.ChangeView(null, null, (float)newZoom);
        e.Handled = true;
    }
    
    private async void PluginSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
        StartMainPluginWatcher();
        ShowMainPluginMissingWarningIfNeeded();
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
        
        await CheckAndShowFpsWarningAsync();
        
        if (ViewModel.SettingsOverlayVisibility == Visibility.Visible)
        {
            SettingsOverlay.Visibility = Visibility.Visible;
            SettingsOverlay.Opacity = 1;
        }
        
        _isInitializing = false;
    }
    
    private void PluginSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _mainPluginWatcher?.Dispose();
        _mainPluginWatcher = null;
    }

    private void StartMainPluginWatcher()
    {
        if (_mainPluginWatcher != null) return;

        string mainPluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FuFuPlugin");
        if (!Directory.Exists(mainPluginDir))
        {
            Directory.CreateDirectory(mainPluginDir);
        }

        _mainPluginWatcher = new FileSystemWatcher(mainPluginDir)
        {
            Filter = "FufuLauncher.UnlockerIsland.*",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };

        _mainPluginWatcher.Created += OnMainPluginFileChanged;
        _mainPluginWatcher.Deleted += OnMainPluginFileChanged;
        _mainPluginWatcher.Renamed += OnMainPluginFileChanged;
        _mainPluginWatcher.Changed += OnMainPluginFileChanged;
    }

    private void OnMainPluginFileChanged(object sender, FileSystemEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.RefreshPluginStates();

            if (!ViewModel.IsMainPluginDllMissing())
            {
                _hasShownMainPluginMissingWarning = false;
                return;
            }

            ShowMainPluginMissingWarningIfNeeded();
        });
    }

    private void ShowMainPluginMissingWarningIfNeeded()
    {
        if (_hasShownMainPluginMissingWarning || !ViewModel.IsMainPluginDllMissing()) return;

        _hasShownMainPluginMissingWarning = true;
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "主插件缺失",
            "未找到主插件 DLL 文件，插件可能未安装、被误删或被杀毒软件拦截。请重新下载插件，或将启动器目录加入杀毒软件白名单后再试。",
            NotificationType.Error,
            6000));
    }
    
    private void OnOpenSponsorWindowClick(object sender, RoutedEventArgs e)
    {
        var sponsorWindow = new SponsorWindow();

        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(sponsorWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new Windows.Graphics.SizeInt32(640, 520);
            appWindow.Resize(size);

            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }

            var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
            }
        }

        sponsorWindow.Activate();
    }
    
    private void HelpImage_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Image img && img.Parent is Grid grid)
        {
            if (grid.FindName("LoadingRing") is ProgressRing loadingRing)
            {
                loadingRing.IsActive = false;
                loadingRing.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void HelpImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Image img && img.Parent is Grid grid)
        {
            img.Visibility = Visibility.Collapsed;
        
            if (grid.FindName("LoadingRing") is ProgressRing loadingRing)
            {
                loadingRing.IsActive = false;
                loadingRing.Visibility = Visibility.Collapsed;
            }
        
            if (grid.FindName("ErrorText") is TextBlock errorText)
            {
                errorText.Visibility = Visibility.Visible;
            }
        }
    }
    
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    
        if (e.Parameter is Models.PluginItem item)
        {
            var folderName = new DirectoryInfo(item.DirectoryPath).Name;
        
            if (folderName.Contains("FPS", StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.SelectedPluginIndex = 1;
            }
            else if (folderName.Contains("Avatar", StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.SelectedPluginIndex = 2;
            }
            else
            {
                ViewModel.SelectedPluginIndex = 0;
            }
        }
    }
    
    private void AvatarPreview_ImageOpened(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Image image)
        {
            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                BeginTime = TimeSpan.FromSeconds(0.5),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase 
                { 
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut 
                }
            };
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, image);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);
            storyboard.Begin();
        }
    }
    
    private async void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (e.PropertyName == nameof(ViewModel.SettingsOverlayVisibility))
        {
            if (ViewModel.SettingsOverlayVisibility == Visibility.Visible)
            {
                SettingsOverlay.Visibility = Visibility.Visible;
                OverlayFadeIn.Begin();
            }
            else
            {
                if (SettingsOverlay.Visibility == Visibility.Visible)
                {
                    OverlayFadeOut.Begin();
                }
            }
        }
        else if (e.PropertyName == nameof(ViewModel.SelectedPluginIndex))
        {
            await CheckAndShowFpsWarningAsync();
        }
        else if (e.PropertyName == nameof(ViewModel.IsFpsPluginEnabled))
        {
            if (!ViewModel.IsFpsPluginEnabled && !_isEnforcingFpsDisable)
            {
                await EnforceFpsPluginDisableAsync();
            }
        }
    }

    private async Task CheckAndShowFpsWarningAsync()
    {
        if (ViewModel.SelectedPluginIndex == 1 && !_hasShownFpsWarning && XamlRoot != null)
        {
            _hasShownFpsWarning = true;
            
            var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
            if (localSettings != null)
            {
                var hasDismissedObj = await localSettings.ReadSettingAsync("HasDismissedFpsWarning");
                bool hasDismissed = hasDismissedObj != null && Convert.ToBoolean(hasDismissedObj);
                
                if (hasDismissed)
                {
                    return;
                }
                
                var dialog = new ContentDialog
                {
                    Title = "兼容性警告",
                    Content = "如果开启了NVIDIA RTX40系及以上显卡的AI插帧，或者使用了类似于RTSS、微星小飞机等帧数显示软件，都可能会导致游戏画面卡死或者游戏无法正常启动",
                    PrimaryButtonText = "我知道了",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };
                
                var checkBox = new CheckBox
                {
                    Content = "不再显示此警告",
                    Margin = new Thickness(0, 16, 0, 0)
                };
                
                var stackPanel = new StackPanel();
                stackPanel.Children.Add(new TextBlock 
                { 
                    Text = dialog.Content.ToString(), 
                    TextWrapping = TextWrapping.Wrap 
                });
                stackPanel.Children.Add(checkBox);
                
                dialog.Content = stackPanel;
                
                await dialog.ShowAsync();
                
                if (checkBox.IsChecked == true)
                {
                    await localSettings.SaveSettingAsync("HasDismissedFpsWarning", true);
                }
            }
        }
    }

    private void OverlayFadeOut_Completed(object sender, object e)
    {
        if (ViewModel.SettingsOverlayVisibility == Visibility.Collapsed)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }
    }
    
private async Task EnforceFpsPluginDisableAsync()
{
    _isEnforcingFpsDisable = true;
    try
    {
        await Task.Delay(500);

        string fpsPluginPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS", "FPS.dll");
        
        if (File.Exists(fpsPluginPath))
        {
            try
            {
                File.Delete(fpsPluginPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"强制移除FPS插件文件失败: {ex.Message}");
            }
            
            await PerformFpsPluginRepairAsync(showUI: false);
            
            ViewModel.IsFpsPluginEnabled = true;
            await Task.Delay(100);
            ViewModel.IsFpsPluginEnabled = false;
        }
    }
    finally
    {
        _isEnforcingFpsDisable = false;
    }
}

private async void OnRepairFpsPluginClick(object sender, RoutedEventArgs e)
{
    await PerformFpsPluginRepairAsync(showUI: true);
}

private async Task PerformFpsPluginRepairAsync(bool showUI)
{
    string zipFilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Launcher" , "FPS.zip");
    string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
    string extractPath = Path.Combine(Path.GetTempPath(), "FPS_Extract_" + Guid.NewGuid());
    string finalDestDir = Path.Combine(pluginsDir, "FPS");

    if (!File.Exists(zipFilePath))
    {
        if (showUI) WeakReferenceMessenger.Default.Send(new NotificationMessage("错误", "未找到文件，请确认启动器文件完整", NotificationType.Error));
        return;
    }

    ContentDialog progressDialog = null;
    if (showUI)
    {
        progressDialog = new ContentDialog
        {
            Title = "正在修复FPS插件",
            Content = new ProgressBar { IsIndeterminate = true, Height = 20, Margin = new Thickness(0, 10, 0, 0) },
            XamlRoot = XamlRoot
        };
        _ = progressDialog.ShowAsync();
    }

    try
    {
        if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        Directory.CreateDirectory(extractPath);
        
        await Task.Run(() => ZipFile.ExtractToDirectory(zipFilePath, extractPath));
        
        var subDirs = Directory.GetDirectories(extractPath);
        string sourceDirToMove = (subDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0) ? subDirs[0] : extractPath;

        if (Directory.Exists(finalDestDir)) Directory.Delete(finalDestDir, true);
        
        await Task.Run(() => MoveDirectorySafe(sourceDirToMove, finalDestDir));

        if (progressDialog != null) progressDialog.Hide();
        ViewModel.LoadConfiguration();
        
        await VerifyFpsPluginHashAsync();

        if (showUI) WeakReferenceMessenger.Default.Send(new NotificationMessage("成功", "FPS显示插件已成功修复并安装", NotificationType.Success));
    }
    catch (Exception ex)
    {
        if (progressDialog != null) progressDialog.Hide();
        if (showUI)
        {
            var failDialog = new ContentDialog
            {
                Title = "修复失败",
                Content = ex.Message,
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };
            await failDialog.ShowAsync();
        }
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

    private async void OnSwitchPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is PresetModel preset)
        {
            if (preset.IsLocked)
            {
                var reason = ViewModel.GetPresetLockReason(preset);
                var dialog = new ContentDialog
                {
                    Title = "配置预设已锁定",
                    Content = $"锁定原因：{reason}\n\n如果继续使用，将忽略该警告并把此预设的 Hash 更新为当前插件 Hash，从而重新解锁该预设。是否继续？",
                    PrimaryButtonText = "继续使用并解锁",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    return;
                }

                ViewModel.ForceUnlockAndSwitchPreset(preset);
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

    private async void OnImportAvatarClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int size))
            {
                _currentEditSize = size;
            }

            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, GetActiveWindow());
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".webp");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string avatarDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar");
                if (!Directory.Exists(avatarDir)) Directory.CreateDirectory(avatarDir);

                string originalPath = ViewModel.GetAvatarOriginalPath(_currentEditSize);
                File.Copy(file.Path, originalPath, true);

                await LoadImageToCropperAsync(originalPath);
            }
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("导入失败", ex.Message, NotificationType.Error));
        }
    }

    private async void OnEditCurrentAvatarClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int size))
        {
            _currentEditSize = size;
            string originalPath = ViewModel.GetAvatarOriginalPath(size);
            string normalPath = ViewModel.GetAvatarPath(size);
            string targetPath = File.Exists(originalPath) ? originalPath : normalPath;

            if (File.Exists(targetPath))
            {
                await LoadImageToCropperAsync(targetPath);
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage("提示", "未找到可供编辑的头像", NotificationType.Warning));
            }
        }
    }

    private uint _originalImageWidth;
    private uint _originalImageHeight;
    private string _editingImagePath;
    private int _currentEditSize = 512;
    
    private void OnClearAvatarClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement fe && int.TryParse(fe.Tag?.ToString(), out int size))
            {
                string normalPath = ViewModel.GetAvatarPath(size);
                string originalPath = ViewModel.GetAvatarOriginalPath(size);

                if (File.Exists(normalPath)) File.Delete(normalPath);
                if (File.Exists(originalPath)) File.Delete(originalPath);

                ViewModel.UpdateAvatarPreview();
                WeakReferenceMessenger.Default.Send(new NotificationMessage("成功", $"尺寸 {size}x{size} 头像已清除", NotificationType.Success));
            }
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("清除失败", ex.Message, NotificationType.Error));
        }
    }
    
    private async Task SaveCroppedImageAsync(int targetSize)
    {
        double viewSize = 300.0;
        double baseScale = Math.Max(viewSize / _originalImageWidth, viewSize / _originalImageHeight);
        double finalScale = baseScale * CropScrollViewer.ZoomFactor;

        double cropX = CropScrollViewer.HorizontalOffset / finalScale;
        double cropY = CropScrollViewer.VerticalOffset / finalScale;
        double cropSize = viewSize / finalScale;
        
        int x = Math.Max(0, (int)Math.Floor(cropX));
        int y = Math.Max(0, (int)Math.Floor(cropY));
        int size = (int)Math.Ceiling(cropSize);
        
        using (var image = await SixLabors.ImageSharp.Image.LoadAsync(_editingImagePath))
        {
            int safeX = Math.Min(x, image.Width - 1);
            int safeY = Math.Min(y, image.Height - 1);
            int safeWidth = Math.Min(size, image.Width - safeX);
            int safeHeight = Math.Min(size, image.Height - safeY);
            int finalCropSize = Math.Max(1, Math.Min(safeWidth, safeHeight));

            image.Mutate(ctx => ctx
                .Crop(new Rectangle(safeX, safeY, finalCropSize, finalCropSize))
                .Resize(targetSize, targetSize, KnownResamplers.Bicubic));

            var outputPath = ViewModel.GetAvatarPath(targetSize);
            var directory = Path.GetDirectoryName(outputPath);
            
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await image.SaveAsPngAsync(outputPath);
        }
    }
    
    private async Task LoadImageToCropperAsync(string filePath)
    {
        try
        {
            _editingImagePath = filePath;
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            using (var stream = await file.OpenReadAsync())
            {
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                _originalImageWidth = decoder.OrientedPixelWidth;
                _originalImageHeight = decoder.OrientedPixelHeight;
            }

            var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            bmp.CreateOptions = Microsoft.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(filePath);
        
            CropTargetImage.Source = bmp;

            double viewSize = 300.0;
            double scale = Math.Max(viewSize / _originalImageWidth, viewSize / _originalImageHeight);
        
            CropTargetImage.Width = _originalImageWidth * scale;
            CropTargetImage.Height = _originalImageHeight * scale;
        
            await Task.Delay(50);
            CropScrollViewer.ChangeView(0, 0, 1.0f, true);
        
            await CropImageDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("加载失败", ex.Message, NotificationType.Error));
        }
    }

    private async void OnCropSaveClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            await SaveCroppedImageAsync(_currentEditSize);
            ViewModel.UpdateAvatarPreview();
            WeakReferenceMessenger.Default.Send(new NotificationMessage("成功", "头像裁切并保存成功", NotificationType.Success));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("保存失败", ex.Message, NotificationType.Error));
        }
        finally
        {
            deferral.Complete();
        }
    }
    
    private async void OnResetAllPresetsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "重置全部预设",
            Content = "确定要移除当前插件的所有预设并重新下载恢复默认吗？此操作无法恢复！",
            PrimaryButtonText = "确认重置",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ClearAllPresets();

            if (ViewModel.SelectedPluginIndex == 0)
            {
                string urlLatest = "http://kr2-proxy.gitwarp.top:9980/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin.zip";
                await DownloadAndInstallPluginAsync(urlLatest);
            }
            else if (ViewModel.SelectedPluginIndex == 1)
            {
                await PerformFpsPluginRepairAsync(true);
            }
        }
    }
    
    private async void OnCropBatchApplyClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            int[] sizes = { 512, 256, 128 };
            foreach (var size in sizes)
            {
                await SaveCroppedImageAsync(size);
            }
            ViewModel.UpdateAvatarPreview();
            WeakReferenceMessenger.Default.Send(new NotificationMessage("成功", "已批量生成并覆盖全部尺寸的头像", NotificationType.Success));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("批量保存失败", ex.Message, NotificationType.Error));
        }
        finally
        {
            deferral.Complete();
        }
    }
}
