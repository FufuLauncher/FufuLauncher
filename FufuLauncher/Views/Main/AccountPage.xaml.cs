using System.Diagnostics;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FufuLauncher.Views;

public sealed partial class AccountPage : Page
{
    public AccountViewModel ViewModel
    {
        get;
    }

    public AccountPage()
    {
        ViewModel = App.GetService<AccountViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Debug.WriteLine("AccountPage initialized");
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Debug.WriteLine($"=== 页面导航到AccountPage，自动刷新用户信息 ===");

        await ViewModel.LoadUserInfoAsync();
    }
    
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
    }
    
    private void OnSwitchAccountClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is AccountInfo account)
        {
            ViewModel.SwitchAccountCommand.Execute(account);
        }
    }
    
    private async void OnGachaAnalysisClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new GachaDialog();
        dialog.XamlRoot = this.XamlRoot; 
        await dialog.ShowAsync();
    }
}