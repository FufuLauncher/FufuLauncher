/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class ControlPanelPage : Page
{
    public ControlPanelViewModel ViewModel
    {
        get;
    }

    public MainViewModel MainViewModel
    {
        get;
    }

    public ControlPanelPage()
    {
        ViewModel = App.GetService<ControlPanelViewModel>();
        MainViewModel = App.GetService<MainViewModel>();
        DataContext = ViewModel;

        Loaded += ControlPanelPage_Loaded;
        InitializeComponent();
    }

    private void OnOpenGachaAnalysisClick(object sender, RoutedEventArgs e)
    {
        var window = new GachaAnalysisWindow();
        window.Activate();
    }

    private void OnOpenDataCenterClick(object sender, RoutedEventArgs e)
    {
        var window = new DataCenterWindow();
        window.Activate();
    }

    private void OnOpenCalculatorClick(object sender, RoutedEventArgs e)
    {
        var window = new CalculatorWindow();
        window.Activate();
    }

    private void OnOpenCycleChallengeClick(object sender, RoutedEventArgs e)
    {
        var window = new CycleChallengeWindow();
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
            Title = "ControlPanel_SecurityTipTitle".GetLocalized(),
            Content = "Home_BBSSecurityMessage".GetLocalized(),
            PrimaryButtonText = "Home_BBSConfirm".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
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

    private void OnOpenCommunityClick(object sender, RoutedEventArgs e)
    {
        var window = new CommunityWindow();
        window.Activate();
    }

    private void OnOpenMiyousheClick(object sender, RoutedEventArgs e)
    {
        MiyousheWindow.Show();
    }

    private async void ControlPanelPage_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
        await Task.Delay(600);
    }
}