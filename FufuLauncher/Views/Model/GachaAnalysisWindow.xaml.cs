using FufuLauncher.Contracts.Services;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;


namespace FufuLauncher.Views;

public sealed partial class GachaAnalysisWindow : Window
{
    public GachaAnalysisModel ViewModel { get; }

    public GachaAnalysisWindow()
    {
        ViewModel = App.GetService<GachaAnalysisModel>();
        
        InitializeComponent();
        
        RootGrid.DataContext = this;
        ExtendsContentIntoTitleBar = true;
        LoadingRing.IsActive = true;

        ViewModel.GetWindowHandle = () => WinRT.Interop.WindowNative.GetWindowHandle(this);
        ViewModel.RequestMetadataScrapeAction = async () => await ViewModel.FetchMetadataFromApiAsync();
        this.Activated += OnWindowFirstActivated;
        ViewModel.OnUidMismatchAsync = async (currentUid, incomingUid) =>
        {
            var tcs = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = "检测到不同账号",
                    Content = $"当前数据属于 UID: {currentUid}\n即将导入的数据来自 UID: {incomingUid}\n\n是否为 UID {incomingUid} 创建新的数据存档？",
                    PrimaryButtonText = "创建新存档",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = Content.XamlRoot
                };
                var result = await dialog.ShowAsync();
                tcs.TrySetResult(result == ContentDialogResult.Primary);
            });
            return await tcs.Task;
        };
        ViewModel.OnErrorAction = (msg) =>
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = "获取失败",
                    Content = msg,
                    CloseButtonText = "知道了",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };
                await dialog.ShowAsync();
            });
        };

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.IsDataLoaded) && ViewModel.IsDataLoaded)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LoadingRing.IsActive = false;
                    EmptyStatePanel.Visibility = ViewModel.HasGachaData ? Visibility.Collapsed : Visibility.Visible;
                    if (!string.IsNullOrEmpty(ViewModel.SelectedUid))
                        UidComboBox.SelectedItem = ViewModel.SelectedUid;
                });
            }
            else if (e.PropertyName == nameof(ViewModel.HasGachaData))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (ViewModel.IsDataLoaded)
                        EmptyStatePanel.Visibility = ViewModel.HasGachaData ? Visibility.Collapsed : Visibility.Visible;
                });
            }
        };

        // 确保初始状态显示 loading
        ViewModel.IsDataLoaded = false;
    }

    private void OnGachaCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element) FlyoutBase.ShowAttachedFlyout(element);
    }

    private void OnGridPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(element, InputSystemCursor.Create(InputSystemCursorShape.Hand));
        }
    }

    private void OnGridPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(element, null);
        }
    }

    private async void OnDeleteGachaDataClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(ViewModel.SelectedUid))
        {
            var noDataDialog = new ContentDialog
            {
                Title = "提示",
                Content = "当前没有选中任何账号，无法删除记录。",
                CloseButtonText = "知道了",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            await noDataDialog.ShowAsync();
            return;
        }

        ContentDialog deleteDialog = new()
        {
            Title = "警告",
            Content = $"确定要删除 UID: {ViewModel.SelectedUid} 的所有抽卡记录吗？\n此操作不可逆转！",
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        ContentDialogResult result = await deleteDialog.ShowAsync();
        if (result == ContentDialogResult.Primary) await ViewModel.ClearGachaDataAsync();
    }

    private async void OnMiYouSheLoginClick(object sender, RoutedEventArgs e)
    {
        var localSettingsService = App.GetService<ILocalSettingsService>();
        var isOsObj = await localSettingsService.ReadSettingAsync("IsInternationalAccount");
        bool isInternational = isOsObj is bool isOs && isOs;

        if (isInternational)
        {
            var osDialog = new ContentDialog
            {
                Title = "国际服暂不支持",
                Content = "国际服（HoYoLAB）账号的抽卡记录获取功能正在适配中，敬请期待。\n\n国际服用户可通过「通过URL获取」方式导入抽卡记录。",
                CloseButtonText = "知道了",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            await osDialog.ShowAsync();
            return;
        }

        var userConfigService = App.GetService<IUserConfigService>();
        var displayConfig = await userConfigService.LoadDisplayConfigAsync();
        var isLoggedIn = !string.IsNullOrEmpty(displayConfig.GameUid);

        if (!isLoggedIn)
        {
            var dialog = new ContentDialog
            {
                Title = "未登录米游社",
                Content = "检测到尚未登录米游社账号，即将跳转到账户设置页面进行登录。",
                CloseButtonText = "知道了",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();

            if (App.MainWindow is MainWindow mainWindow)
                await mainWindow.NavigateToAccountPageAsync();
            return;
        }

        ViewModel.FetchFromMiYouSheCommand.Execute(null);
    }

    private async void OnUrlFetchClick(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 120,
            Text = ViewModel.GachaUrl ?? "",
            PlaceholderText = "在此处粘贴抽卡分析链接 (URL)..."
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "粘贴从米游社或其他工具获取的抽卡记录链接，链接应包含 authkey 参数。",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(urlBox);

        var dialog = new ContentDialog
        {
            Title = "通过 URL 获取抽卡记录",
            Content = panel,
            PrimaryButtonText = "开始获取",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.GachaUrl = urlBox.Text;
            ViewModel.FetchGachaDataCommand.Execute(null);
        }
    }

    private bool _firstActivated = true;

    private async void OnWindowFirstActivated(object sender, WindowActivatedEventArgs e)
    {
        if (!_firstActivated) return;
        _firstActivated = false;
        this.Activated -= OnWindowFirstActivated;

        // 延迟加载，确保窗口和 LoadingRing 先完成渲染
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
        {
            await ViewModel.LoadSavedGachaDataAsync();
        });
    }

    private async void OnUidComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.SelectedItem is not string selected) return;

        System.Diagnostics.Debug.WriteLine($"[Gacha] OnUidComboBoxSelectionChanged: selected={selected}, ViewModel.SelectedUid={ViewModel.SelectedUid}");

        if (selected == GachaAnalysisModel.AddNewUserItem)
        {
            // 恢复到当前 UID，避免 ComboBox 显示空白
            var previous = ViewModel.SelectedUid;
            combo.SelectedItem = null;
            await ViewModel.AddNewUserCommand.ExecuteAsync(null);
            // AddNewUser 会设置 SelectedUid = ""，如果需要恢复可以在此处处理
        }
        else
        {
            await ViewModel.SwitchUidCommand.ExecuteAsync(selected);
        }
    }

}