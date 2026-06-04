using System.Linq;
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

    private void OnEasterEggClick(object sender, RoutedEventArgs e)
    {
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
        };

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

    private void OnOpenSecurityAuthClick(object sender, RoutedEventArgs e)
    {
        var authWindow = new SecurityWebWindow("", "https://fu1.fun/dev-auth");
        
        IntPtr hWnd = WindowNative.GetWindowHandle(authWindow);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

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

        authWindow.Activate();
    }

    private bool _isNavigatingFromMenu;
    private DispatcherTimer? _navLockTimer;

    private static readonly string[] _sectionTags =
        { "AppearanceItem", "HomeTextItem", "LanguageItem", "LaunchConfigItem",
          "BackgroundItem", "WindowEffectsItem", "StartupSoundItem",
          "CheckinSettingsItem",
          "AdvancedOptionsItem", "UpdateItem", "AboutItem", "SecurityAuthItem" };

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
}