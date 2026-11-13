/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    #region 信息与提示对话框

    private async void OnIndependentDeploymentClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Settings_Disclaimer".GetLocalized(),
            Content = "该独立部署版本软件由PR贡献者自行开发提供，FufuLauncher无法对该软件的安全性、稳定性或后续维护提供任何保证，您需要自行辨别使用风险\n\n是否继续访问该项目地址？",
            PrimaryButtonText = "Settings_ContinueVisit".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(ApiEndpoints.MiyoQianUrl));
        }
    }

    private void OnIdentifyMonitorsClick(object sender, RoutedEventArgs e) => MonitorHelper.IdentifyAll();

    private async void OnCloudGameCheckinInfoClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Settings_CloudCheckinNote".GetLocalized(),
            Content = "开启云游戏签到需要对应账号添加云游戏登录凭证，否则跳过云游戏签到",
            CloseButtonText = "GotItBtn".GetLocalized(),
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

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
                CloseButtonText = "GotItBtn".GetLocalized(),
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    #endregion
}