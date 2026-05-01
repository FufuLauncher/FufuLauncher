using System.ComponentModel;
using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using FufuLauncher.Services.Background;

namespace FufuLauncher.Views;

public sealed partial class MainPage : Page
{
    private Microsoft.UI.Xaml.Media.Brush _originalInfoCardBrush;
    private Microsoft.UI.Xaml.Media.Brush _originalCheckinCardBrush;
    public MainViewModel ViewModel
    {
        get;
    }
    public XamlUICommand OpenLinkCommand
    {
        get;
    }

    private void Copyright_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateCopyrightOpacity(0.8);
    }

    private void Copyright_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateCopyrightOpacity(0.05);
    }
    
        private async void SwitchToBilibili_Click(object sender, RoutedEventArgs e)
        {
            await PrepareAndSwitchServer(true);
        }

        private async void SwitchToOfficial_Click(object sender, RoutedEventArgs e)
        {
            await PrepareAndSwitchServer(false);
        }

        private async Task PrepareAndSwitchServer(bool toBilibili)
        {
            try
            {
                var localSettingsService = App.GetService<ILocalSettingsService>();
                var gamePathSetting = await localSettingsService.ReadSettingAsync("GameInstallationPath");
                
                var gameDir = gamePathSetting as string;
                if (!string.IsNullOrEmpty(gameDir))
                {
                    gameDir = gameDir.Trim('"').Trim();
                }

                if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                {
                    await ShowDialog("错误", "未找到有效的游戏路径，请先在设置页设置游戏位置");
                    return;
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
                        await ShowDialog("错误", "无法找到 config.ini 配置文件，无法切换服务器");
                        return;
                    }
                }
                
                await PerformServerSwitch(gameDir, configPath, toBilibili);
            }
            catch (Exception ex)
            {
                await ShowDialog("错误", $"准备切换时发生异常: {ex.Message}");
            }
        }
        
        private async void AnnouncementBell_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var announcementService = App.GetService<IAnnouncementService>();
                
                var announcementUrl = await announcementService.GetCurrentAnnouncementUrlAsync();
                
                if (string.IsNullOrEmpty(announcementUrl))
                {
                    var localSettings = App.GetService<ILocalSettingsService>();
                    
                    var lastUrlObj = await localSettings.ReadSettingAsync("LastAnnouncementUrl");
                    if (lastUrlObj is string lastUrl && !string.IsNullOrEmpty(lastUrl))
                    {
                        announcementUrl = lastUrl;
                    }
                }


                if (!string.IsNullOrEmpty(announcementUrl))
                {
                    var announcementWindow = new AnnouncementWindowL(announcementUrl);
                    announcementWindow.Activate();
                }
                else
                {
                    Debug.WriteLine("[Announcement] 手动获取公告失败：未获取到且无本地缓存");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Announcement] 手动触发公告异常: {ex.Message}");
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
                        await ShowDialog("错误", $"缺失核心文件：{sourceSdkPath}\n请重新安装该软件");
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
                
                await ShowDialog("切换成功", $"已成功切换至 {(toBilibili ? "Bilibili 服" : "官方服务器")}\nSDK已{(toBilibili ? "部署" : "清理")}");
            }
            catch (Exception ex)
            {
                await ShowDialog("切换失败", ex.Message);
            }
        }
        
        private async Task ShowDialog(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "确定",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    private void AnimateCopyrightOpacity(double toOpacity)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, CopyrightText);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
    private void ScreenshotButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateBlurOpacity(0);
    }

    private void ScreenshotButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateBlurOpacity(1.0);
    }
    
private async void ChangeUidButton_Click(object sender, RoutedEventArgs e)
{
    var localSettings = App.GetService<ILocalSettingsService>();
    var checkinService = App.GetService<IHoyoverseCheckinService>();

    string currentUid = (await localSettings.ReadSettingAsync("CustomCheckinUid"))?.ToString() ?? "";

    var textBox = new TextBox
    {
        PlaceholderText = "请输入9位数字UID",
        Text = currentUid,
        MaxLength = 9,
        Margin = new Thickness(0, 10, 0, 0)
    };

    var statusText = new TextBlock
    {
        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
        Margin = new Thickness(0, 5, 0, 0),
        Visibility = Visibility.Collapsed
    };

    var panel = new StackPanel();
    panel.Children.Add(new TextBlock { Text = "留空表示为所有绑定账号签到。输入指定UID则仅为该账号签到", TextWrapping = TextWrapping.Wrap });
    panel.Children.Add(textBox);
    panel.Children.Add(statusText);

    var dialog = new ContentDialog
    {
        Title = "配置指定签到UID",
        Content = panel,
        PrimaryButtonText = "保存",
        SecondaryButtonText = "清除限制(全部签到)",
        CloseButtonText = "取消",
        XamlRoot = this.XamlRoot
    };

    dialog.PrimaryButtonClick += async (s, args) =>
    {
        var deferral = args.GetDeferral();
        string input = textBox.Text.Trim();
        
        if (string.IsNullOrEmpty(input))
        {
            await localSettings.SaveSettingAsync("CustomCheckinUid", "");
            deferral.Complete();
            return;
        }

        if (input.Length != 9 || !long.TryParse(input, out _))
        {
            args.Cancel = true;
            statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            statusText.Text = "UID必须为9位纯数字";
            statusText.Visibility = Visibility.Visible;
            deferral.Complete();
            return;
        }

        statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkOrange);
        statusText.Text = "正在从验证可用性";
        statusText.Visibility = Visibility.Visible;

        try
        {
            var uids = await checkinService.GetBoundUidsAsync();
            if (!uids.Contains(input))
            {
                args.Cancel = true;
                statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                statusText.Text = "校验失败：该UID未绑定，请确认Cookie内是否包含该角色。";
            }
            else
            {
                await localSettings.SaveSettingAsync("CustomCheckinUid", input);
                _ = ViewModel.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            statusText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            statusText.Text = $"验证请求异常: {ex.Message}";
        }
        finally
        {
            deferral.Complete();
        }
    };

    dialog.SecondaryButtonClick += async (s, args) =>
    {
        await localSettings.SaveSettingAsync("CustomCheckinUid", "");
        _ = ViewModel.InitializeAsync();
    };

    await dialog.ShowAsync();
}

    private void AnimateBlurOpacity(double toOpacity)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, ScreenshotBlurBorder);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void InfoCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateInfoButtonOpacity(1.0);
    }

    private void InfoCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateInfoButtonOpacity(0.0);
    }
    
    private void BackgroundGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FufuLauncher.Services.Background.BackgroundUrlInfo info)
        {
            // 触发 ViewModel 中的背景切换命令
            ViewModel.SelectSpecificBackgroundCommand.Execute(info);
        
            // 自动关闭 Flyout 弹窗
            BackgroundFlyout.Hide();
        }
    }

    private void AnimateInfoButtonOpacity(double toOpacity)
    {
        if (InfoExpandButton == null) return;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, InfoExpandButton);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private bool _isInitialized;

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        
        _originalInfoCardBrush = InfoCardGrid.Background;
        _originalCheckinCardBrush = CheckinCardGrid.Background;
    
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    
        Loaded += (_, _) => 
        {
            LaunchButtonOverlayBorder.Opacity = ViewModel.IsGameRunning ? 0.0 : 1.0;
        };

        OpenLinkCommand = new XamlUICommand();
        OpenLinkCommand.ExecuteRequested += (sender, args) =>
        {
            if (args.Parameter is string url)
            {
                OpenLink(url);
            }
        };
    }
    
    private void UpdateCardBackgrounds()
    {
        if (ViewModel.IsVideoBackground)
        {
            var semiTransparentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.5 };
            InfoCardGrid.Background = semiTransparentBrush;
            CheckinCardGrid.Background = semiTransparentBrush;
        }
        else
        {
            InfoCardGrid.Background = _originalInfoCardBrush;
            CheckinCardGrid.Background = _originalCheckinCardBrush;
        }
    }
    
    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsGameRunning))
        {
            AnimateLaunchButtonOverlay(ViewModel.IsGameRunning ? 0.0 : 1.0);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsVideoBackground))
        {
            UpdateCardBackgrounds();
        }
    }
    

    private void AnimateLaunchButtonOverlay(double toOpacity)
    {
        if (LaunchButtonOverlayBorder.Opacity == toOpacity) return;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromSeconds(1.5)), 
            
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, LaunchButtonOverlayBorder);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.OnPageReturnedAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();

        if (!_isInitialized)
        {
            await ViewModel.InitializeAsync();
            _isInitialized = true;
        }
    
        UpdateCardBackgrounds();
    }

    private async void OpenLink(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                var uri = new Uri(url);
                await Windows.System.Launcher.LaunchUriAsync(uri);
                Debug.WriteLine($"打开链接: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开链接失败: {ex.Message}");
            }
        }
    }
}