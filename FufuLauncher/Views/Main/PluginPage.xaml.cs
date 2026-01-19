using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginPage : Page
{
    public PluginViewModel ViewModel { get; }
    
    public MainViewModel MainViewModel { get; }
    public ControlPanelModel ControlPanelViewModel { get; }

    public PluginPage()
    {
        ViewModel = App.GetService<PluginViewModel>();
        MainViewModel = App.GetService<MainViewModel>();
        ControlPanelViewModel = App.GetService<ControlPanelModel>();
        
        InitializeComponent();
    }
    
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
    }

    private void OnPluginToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && toggleSwitch.Tag is PluginItem item)
        {
            if (toggleSwitch.IsOn != item.IsEnabled)
            {
                if (ViewModel.TogglePluginCommand.CanExecute(item))
                {
                    ViewModel.TogglePluginCommand.Execute(item);
                }
                else
                {
                    toggleSwitch.IsOn = item.IsEnabled;
                }
            }
        }
    }
    
    private void OnConfigClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PluginItem item && item.HasConfig)
        {
            Frame.Navigate(typeof(PluginConfigPage), item);
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is PluginItem pluginItem)
        {
            var currentFolderName = new DirectoryInfo(pluginItem.DirectoryPath).Name;
            
            var dialog = new ContentDialog
            {
                Title = "重命名插件文件夹",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var inputTextBox = new TextBox { Text = currentFolderName, AcceptsReturn = false };
            dialog.Content = inputTextBox;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var newName = inputTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != currentFolderName)
                {
                    ViewModel.PerformRename(pluginItem, newName);
                }
            }
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is PluginItem pluginItem)
        {
            if (ViewModel.DeletePluginCommand.CanExecute(pluginItem))
            {
                ViewModel.DeletePluginCommand.Execute(pluginItem);
            }
        }
    }
}