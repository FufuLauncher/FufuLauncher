using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml;
using FufuLauncher.ViewModels;
using System.Diagnostics;
using System.IO;

namespace FufuLauncher.Views;

public sealed partial class PanelPage : Page
{
    public ControlPanelModel ViewModel { get; }

    public PanelPage()
    {
        ViewModel = App.GetService<ControlPanelModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }

    private void RootScrollViewer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        // Move focus to the ScrollViewer to dismiss NumberBox input focus
        RootScrollViewer.Focus(FocusState.Programmatic);
    }

    private void NumberBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Optional: handle when numberbox gets focus
    }

    private void NumberBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Optional: handle when numberbox loses focus
    }

    private void LaunchControlPanelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string exePath = Path.Combine(AppContext.BaseDirectory, "ControlPanel.exe");
            
            if (File.Exists(exePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory
                });
            }
            else
            {
                ShowErrorDialog("未找到 ControlPanel.exe", $"请在以下目录放置 ControlPanel.exe:\n{AppContext.BaseDirectory}");
            }
        }
        catch (Exception ex)
        {
            ShowErrorDialog("启动失败", $"无法启动 ControlPanel.exe:\n{ex.Message}");
        }
    }

    private async void ShowErrorDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}