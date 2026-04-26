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

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
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

            var size = new Windows.Graphics.SizeInt32(1000, 650);
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

    private void SettingsNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem &&
            selectedItem.Tag is string tag)
        {
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
    private async void OnOpenHDRSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new GenshinHDRLuminanceSettingDialog();
        dialog.XamlRoot = this.XamlRoot;
        await dialog.ShowAsync();
    }
    private void BringElementIntoView(FrameworkElement element)
    {
        if (element == null) return;

        var bringIntoViewOptions = new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.0,
            VerticalOffset = -52
        };

        element.StartBringIntoView(bringIntoViewOptions);
    }
}