using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class UpdateNotificationWindow : WindowEx
{
    public UpdateNotificationWindow(string updateInfoUrl)
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        UpdateWebView.Source = new Uri(updateInfoUrl);

        this.CenterOnScreen();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        IsShownInSwitchers = true;
        
        if (Content is FrameworkElement rootElement)
        {
            rootElement.Loaded += UpdateNotificationWindow_Loaded;
        }
    }

    private async void UpdateNotificationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        string currentVersion = "1.1.0.3";
        
        var dialog = new ContentDialog
        {
            Title = "说明",
            Content = $"当前启动器版本：{currentVersion}\n\n如果您已经完成了更新，此公告用于向您展示更新内容\n如果您尚未更新，此公告则是提醒您有新版本可供升级\n请勿重复更新哦",
            CloseButtonText = "我知道了",
            DefaultButton = ContentDialogButton.Close,
            
            XamlRoot = Content.XamlRoot 
        };

        await dialog.ShowAsync();
    }
    
    private void OnUpdateBtnClicked(object sender, RoutedEventArgs e)
    {
        var updateWindow = new UpdateWindow();
        updateWindow.Activate();
        
        Close();
    }
}