/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FufuLauncher.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }
    
    private async void OnIndependentDeploymentClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "声明",
            Content = "该独立部署版本软件由PR贡献者自行开发提供，FufuLauncher无法对该软件的安全性、稳定性或后续维护提供任何保证，您需要自行辨别使用风险\n\n是否继续访问该项目地址？",
            PrimaryButtonText = "继续访问",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
    
        if (result == ContentDialogResult.Primary)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/Marchen-orz/MiyoQian"));
        }
    }
    
    private void OnIdentifyMonitorsClick(object sender, RoutedEventArgs e)
{
    var displayAreas = DisplayArea.FindAll();
    for (int i = 0; i < displayAreas.Count; i++)
    {
        int index = i + 1;
        var displayArea = displayAreas[i];

        var window = new Window();
        window.ExtendsContentIntoTitleBar = true;

        var grid = new Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black) { Opacity = 0.8 },
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var textBlock = new TextBlock
        {
            Text = index.ToString(),
            FontSize = 140,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };

        grid.Children.Add(textBlock);
        window.Content = grid;

        IntPtr hWnd = WindowNative.GetWindowHandle(window);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsAlwaysOnTop = true;
            }

            var size = new Windows.Graphics.SizeInt32(250, 250);
            appWindow.Resize(size);

            var centeredX = displayArea.WorkArea.X + (displayArea.WorkArea.Width - size.Width) / 2;
            var centeredY = displayArea.WorkArea.Y + (displayArea.WorkArea.Height - size.Height) / 2;
            appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
        }

        window.Activate();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (s, args) =>
        {
            window.Close();
            ((DispatcherTimer)s).Stop();
        };
        timer.Start();
    }
}

    protected async override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (ViewModel != null)
        {
            await ViewModel.ReloadSettingsAsync();
        }
    }

    private Window _easterEggWindow;

    private async void OnCommunityCheckinInfoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "社区签到说明",
            Content = "由于米游社逐步删除了互动获取米游币渠道，下方选项大概并不能让获取米游币变得更多，等待后续官方更新新策略",
            CloseButtonText = "我知道了",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void OnCloudGameCheckinInfoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "云游戏签到说明",
            Content = "开启云游戏签到需要对应账号添加云游戏登录凭证，否则跳过云游戏签到",
            CloseButtonText = "我知道了",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void OnEasterEggClick(object sender, RoutedEventArgs e)
    {
        if (_easterEggWindow != null)
        {
            _easterEggWindow.Activate();
            return;
        }

        var window = new Window();
        var page = new EasterEggPage();
        window.Content = page;

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(page.AppTitleBarElement);

        window.Title = "Philia093";

        IntPtr hWnd = WindowNative.GetWindowHandle(window);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new Windows.Graphics.SizeInt32(1300, 850);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }
        }

        window.Closed += (s, args) =>
        {
            page.Cleanup();
            _easterEggWindow = null;
        };

        _easterEggWindow = window;
        window.Activate();
    }
    
    private void OnOpenAchievementUpdaterClick(object sender, RoutedEventArgs e)
    {
        var updaterWindow = new AchievementUpdaterWindow();
        updaterWindow.ExtendsContentIntoTitleBar = true;

        IntPtr hWnd = WindowNative.GetWindowHandle(updaterWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new Windows.Graphics.SizeInt32(1100, 750);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }
        }

        updaterWindow.Activate();
    }
    
    private void OnOpenDatabaseEditorClick(object sender, RoutedEventArgs e)
    {
        var editorWindow = new DatabaseEditorWindow();

        editorWindow.ExtendsContentIntoTitleBar = true;
    
        IntPtr hWnd = WindowNative.GetWindowHandle(editorWindow);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
            
            var size = new Windows.Graphics.SizeInt32(800, 550);
            appWindow.Resize(size);
            
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }
        }
        
        editorWindow.Activate();
    }
    
    private void OnOpenSponsorWindowClick(object sender, RoutedEventArgs e)
    {
        var sponsorWindow = new SponsorWindow();

        IntPtr hWnd = WindowNative.GetWindowHandle(sponsorWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var size = new Windows.Graphics.SizeInt32(640, 520);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }

            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
            }
        }

        sponsorWindow.Activate();
    }

    private void OnOpenAboutWindowClick(object sender, RoutedEventArgs e)
    {
        var window = new Window();
        var page = new AboutPage();
        window.Content = page;

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(page.AppTitleBar);

        window.Title = "关于 FufuLauncher";

        try { window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop(); } catch { }

        IntPtr hWnd = WindowNative.GetWindowHandle(window);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            appWindow.SetIcon("Assets/WindowIcon.ico");

            var size = new Windows.Graphics.SizeInt32(1350, 850);
            appWindow.Resize(size);

            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            if (displayArea != null)
            {
                var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
            }

            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsResizable = false;
            }
        }

        window.Activate();
    }

    private async void OnOpenSecurityAuthClick(object sender, RoutedEventArgs e)
    {
        string hwid = await Task.Run(() => Helpers.SystemEnvironmentHelper.GetHwid());

        try
        {
            using var checkClient = new HttpClient();
            var checkPayload = JsonSerializer.Serialize(new { hwid });
            var checkContent = new StringContent(checkPayload, Encoding.UTF8, "application/json");
            var checkResponse = await checkClient.PostAsync("https://dev.s1ky3.xyz/api/verify-hwid", checkContent);
            if (checkResponse.IsSuccessStatusCode)
            {
                var checkBody = await checkResponse.Content.ReadAsStringAsync();
                var checkResult = JsonSerializer.Deserialize<JsonElement>(checkBody);
                if (checkResult.TryGetProperty("authorized", out var auth) && auth.GetBoolean())
                {
                    await new ContentDialog
                    {
                        Title = "提示",
                        Content = "您的开发者认证已通过，请勿重复提交申请",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    }.ShowAsync();
                    return;
                }
            }
        }
        catch { }

        var uidBox = new TextBox { PlaceholderText = "游戏 UID", Margin = new Thickness(0, 8, 0, 0) };
        var nameBox = new TextBox { PlaceholderText = "用户名", Margin = new Thickness(0, 8, 0, 0) };
        var githubBox = new TextBox { PlaceholderText = "GitHub 链接（可选）", Margin = new Thickness(0, 8, 0, 0) };
        var hwidBlock = new TextBlock
        {
            Text = $"HWID: {hwid}",
            Opacity = 0.6,
            Margin = new Thickness(0, 12, 0, 0),
            FontSize = 12
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "填写以下信息提交开发者认证申请" });
        panel.Children.Add(uidBox);
        panel.Children.Add(nameBox);
        panel.Children.Add(githubBox);
        panel.Children.Add(hwidBlock);

        var dialog = new ContentDialog
        {
            Title = "开发者认证申请",
            Content = panel,
            PrimaryButtonText = "提交申请",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        string uid = uidBox.Text?.Trim();
        string username = nameBox.Text?.Trim();
        string github = githubBox.Text?.Trim();

        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(username))
        {
            await new ContentDialog
            {
                Title = "错误",
                Content = "UID 和用户名不能为空",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            }.ShowAsync();
            return;
        }

        if (uid.Length < 9 || uid.Length > 10 || !uid.All(char.IsDigit))
        {
            await new ContentDialog
            {
                Title = "错误",
                Content = "UID 必须为9位或10位数字",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            }.ShowAsync();
            return;
        }

        if (!string.IsNullOrEmpty(github) && !github.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            await new ContentDialog
            {
                Title = "错误",
                Content = "请输入正确的GitHub地址",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            }.ShowAsync();
            return;
        }

        try
        {
            using var client = new HttpClient();
            object payload = string.IsNullOrEmpty(github)
                ? new { uid, username, hwid }
                : new { uid, username, hwid, github };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://dev.s1ky3.xyz/api/dev-apply", content);
            var body = await response.Content.ReadAsStringAsync();

            string msg = response.IsSuccessStatusCode
                ? "申请已提交，请等待管理员审批"
                : $"提交失败: {body}";

            await new ContentDialog
            {
                Title = response.IsSuccessStatusCode ? "成功" : "失败",
                Content = msg,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            await new ContentDialog
            {
                Title = "网络错误",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            }.ShowAsync();
        }
    }

    private bool _cpuUsageWarningToggleLoaded;

    private async void OnCpuUsageWarningToggled(object sender, RoutedEventArgs e)
    {
        if (!_cpuUsageWarningToggleLoaded)
        {
            _cpuUsageWarningToggleLoaded = true;
            return;
        }

        if (sender is ToggleSwitch { IsOn: false })
        {
            var dialog = new ContentDialog
            {
                Title = "已关闭 CPU 占用异常警告",
                Content = "关闭后，启动器即使长期高 CPU 占用也不会再主动提示。若遇到卡顿、发热或异常耗电，请自行留意并及时反馈问题。",
                CloseButtonText = "我知道了",
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private bool _isNavigatingFromMenu;
    private DispatcherTimer? _navLockTimer;

    private static readonly string[] _sectionTags =
        { "AppearanceItem", "HomeTextItem", "LanguageItem", "LaunchConfigItem",
          "BackgroundItem", "WindowEffectsItem", "StartupSoundItem",
          "CheckinSettingsItem",
          "AdvancedOptionsItem", "ScreenshotSettingsItem", "UpdateItem", "AboutItem", "SecurityAuthItem" };

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();

        _isNavigatingFromMenu = true;
        if (SettingsNavigationView.SelectedItem == null)
        {
            SettingsNavigationView.SelectedItem = SettingsNavigationView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        }
        _isNavigatingFromMenu = false;
    }

    private void SettingsNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_isNavigatingFromMenu) return;

        if (args.SelectedItem is NavigationViewItem selectedItem &&
            selectedItem.Tag is string tag)
        {
            _isNavigatingFromMenu = true;

            // Safety net: clear lock if ViewChanged never fires
            // (happens when element is already visible but can't scroll to top)
            _navLockTimer?.Stop();
            _navLockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _navLockTimer.Tick += (s, e) =>
            {
                ((DispatcherTimer)s).Stop();
                _isNavigatingFromMenu = false;
            };
            _navLockTimer.Start();

            var element = FindName(tag) as FrameworkElement;
            if (element != null)
            {
                if (element.ActualHeight > 0)
                {
                    BringElementIntoView(element);
                }
                else
                {
                    RoutedEventHandler loadedHandler = null;
                    loadedHandler = (s, e) =>
                    {
                        BringElementIntoView(element);
                        element.Loaded -= loadedHandler;
                    };
                    element.Loaded += loadedHandler;
                }
            }
        }
    }

    private void SettingsScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;

        // Scroll from nav click completed — release lock, skip sync
        if (_isNavigatingFromMenu)
        {
            _navLockTimer?.Stop();
            _isNavigatingFromMenu = false;
            return;
        }

        var scrollViewer = (ScrollViewer)sender;
        double anchor = scrollViewer.Padding.Top + 1;
        var visibleTag = (string?)null;

        foreach (var tag in _sectionTags)
        {
            var element = FindName(tag) as FrameworkElement;
            if (element == null) continue;

            var transform = element.TransformToVisual(scrollViewer);
            var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

            if (position.Y <= anchor)
            {
                visibleTag = tag;
            }
        }

        if (visibleTag != null)
        {
            _isNavigatingFromMenu = true;
            var targetItem = SettingsNavigationView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == visibleTag);
            if (targetItem != null && SettingsNavigationView.SelectedItem != targetItem)
            {
                SettingsNavigationView.SelectedItem = targetItem;
            }
            _isNavigatingFromMenu = false;
        }
    }
    private async void OnOpenHDRSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new GenshinHDRLuminanceSettingDialog();
        dialog.XamlRoot = this.XamlRoot;
        await dialog.ShowAsync();
    }

    private void OnCloudCredentialClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string uid)
        {
            var cloudWindow = new CloudCredentialWindow(uid);
            cloudWindow.ExtendsContentIntoTitleBar = true;

            IntPtr hWnd = WindowNative.GetWindowHandle(cloudWindow);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
                if (File.Exists(iconPath))
                    appWindow.SetIcon(iconPath);

                var size = new Windows.Graphics.SizeInt32(1280, 720);
                appWindow.Resize(size);

                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    var centeredX = (displayArea.WorkArea.Width - size.Width) / 2;
                    var centeredY = (displayArea.WorkArea.Height - size.Height) / 2;
                    appWindow.Move(new Windows.Graphics.PointInt32(centeredX, centeredY));
                }
            }

            cloudWindow.Activate();
        }
    }
    private void BringElementIntoView(FrameworkElement element)
    {
        if (element == null) return;

        var bringIntoViewOptions = new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.0
        };

        element.StartBringIntoView(bringIntoViewOptions);
    }

    public async Task NavigateToUpdateSectionAsync()
    {
        var updateNavItem = SettingsNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "UpdateItem");

        if (updateNavItem != null)
        {
            SettingsNavigationView.SelectedItem = updateNavItem;
        }

        await Task.Delay(120);

        if (UpdateItem != null)
        {
            BringElementIntoView(UpdateItem);
        }

        await Task.Delay(120);

        CheckUpdateButton?.Focus(FocusState.Programmatic);
    }

    public async Task NavigateToCheckinSettingsAsync()
    {
        var checkinNavItem = SettingsNavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "CheckinSettingsItem");

        if (checkinNavItem != null)
        {
            SettingsNavigationView.SelectedItem = checkinNavItem;
        }

        await Task.Delay(120);

        if (CheckinSettingsItem != null)
        {
            BringElementIntoView(CheckinSettingsItem);
        }
    }

    private bool _isRecordingHotkey;

    private void OnScreenshotHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        if (_isRecordingHotkey) return;
        _isRecordingHotkey = true;
        HotkeyRecordingHint.Visibility = Visibility.Visible;
        ScreenshotHotkeyButton.Content = "...";
        ScreenshotHotkeyButton.KeyDown += OnHotkeyRecordKeyDown;
        ScreenshotHotkeyButton.Focus(FocusState.Programmatic);
    }

    private void OnHotkeyRecordKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;

        // 忽略单独的修饰键按下
        if (key == Windows.System.VirtualKey.Control ||
            key == Windows.System.VirtualKey.Shift ||
            key == Windows.System.VirtualKey.Menu ||
            key == Windows.System.VirtualKey.LeftControl ||
            key == Windows.System.VirtualKey.RightControl ||
            key == Windows.System.VirtualKey.LeftShift ||
            key == Windows.System.VirtualKey.RightShift ||
            key == Windows.System.VirtualKey.LeftMenu ||
            key == Windows.System.VirtualKey.RightMenu)
        {
            return;
        }

        var parts = new System.Collections.Generic.List<string>();

        var ctrlState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
        var shiftState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
        var altState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);

        if (ctrlState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            parts.Add("Ctrl");
        if (altState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            parts.Add("Alt");
        if (shiftState.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
            parts.Add("Shift");

        parts.Add(key.ToString());

        var hotkeyStr = string.Join("+", parts);
        ViewModel.ScreenshotHotkey = hotkeyStr;
        ScreenshotHotkeyButton.Content = hotkeyStr;

        _isRecordingHotkey = false;
        HotkeyRecordingHint.Visibility = Visibility.Collapsed;
        ScreenshotHotkeyButton.KeyDown -= OnHotkeyRecordKeyDown;
        e.Handled = true;
    }
}
