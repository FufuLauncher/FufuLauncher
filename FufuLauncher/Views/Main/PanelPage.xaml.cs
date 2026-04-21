using System.Diagnostics;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PanelPage : Page
{
    public ControlPanelModel ViewModel { get; }
    public MainViewModel MainViewModel { get; }

    public PanelPage()
    {
        ViewModel = App.GetService<ControlPanelModel>();
        MainViewModel = App.GetService<MainViewModel>();
        DataContext = ViewModel;

        Loaded += PanelPage_Loaded;
        InitializeComponent();
    }

    private void OnOpenGachaAnalysisClick(object sender, RoutedEventArgs e)
    {
        var window = new GachaAnalysisWindow();
        window.Activate();
    }

    private void OnOpenAchievementsClick(object sender, RoutedEventArgs e)
    {
        var window = new AchievementWindow();
        window.Activate();
    }

    private void OnOpenInventoryClick(object sender, RoutedEventArgs e)
    {
        var window = new InventoryWindow();
        window.Activate();
    }

    private void OnOpenPlayerRolesClick(object sender, RoutedEventArgs e)
    {
        var window = new PlayerInfoWindow();
        window.Activate();
    }

    private void OnOpenDailyNoteClick(object sender, RoutedEventArgs e)
    {
        var window = new DailyNoteWindow();
        window.Activate();
    }

    private async void BBSButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog riskDialog = new()
        {
            Title = "安全提示",
            Content = "进入战绩信息页面可能会导致您的账户被标注为风险账户，进而导致部分功能（如某些自动化工具或特定网页访问）无法正常使用，是否确认继续？",
            PrimaryButtonText = "确认继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        
        ContentDialogResult result = await riskDialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary)
        {
            var bbsWindow = new BBSWindow();
            bbsWindow.Activate();
        }
    }

    private void OnOpenVideoResourcesClick(object sender, RoutedEventArgs e)
    {
        var window = new VideoResourcesWindow();
        window.Activate();
    }

    private async void PanelPage_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
        await Task.Delay(600);
        
        ViewModel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.IsGameRunning))
            {
                GameRunningIndicator.Visibility = ViewModel.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        
        GameRunningIndicator.Visibility = ViewModel.IsGameRunning ? Visibility.Visible : Visibility.Collapsed;
    }
}