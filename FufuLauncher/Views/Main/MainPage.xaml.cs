using System.ComponentModel;
using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace FufuLauncher.Views;

public sealed partial class MainPage : Page
{
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

    private void BackgroundButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateBackgroundToggleOpacity(1.0);
    }

    private void BackgroundButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateBackgroundToggleOpacity(0.0);
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
                
                string gameDir = gamePathSetting as string;
                if (!string.IsNullOrEmpty(gameDir))
                {
                    gameDir = gameDir.Trim('"').Trim();
                }

                if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                {
                    await ShowDialog("错误", "未找到有效的游戏路径，请先在设置页设置游戏位置。");
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
                        await ShowDialog("错误", "无法找到 config.ini 配置文件，无法切换服务器。");
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
                        await ShowDialog("错误", $"缺失核心文件：{sourceSdkPath}\n请确保已将 PCGameSDK.dll 放入软件的 Assets 文件夹。");
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
                
                await ShowDialog("切换成功", $"已成功切换至 {(toBilibili ? "Bilibili 服" : "官方服务器")}。\nSDK已{(toBilibili ? "部署" : "清理")}。");
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
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    private void AnimateBackgroundToggleOpacity(double toOpacity)
    {
        if (BackgroundToggleGrid == null) return;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, BackgroundToggleGrid);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
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
    
    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsGameRunning))
        {
            AnimateLaunchButtonOverlay(ViewModel.IsGameRunning ? 0.0 : 1.0);
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