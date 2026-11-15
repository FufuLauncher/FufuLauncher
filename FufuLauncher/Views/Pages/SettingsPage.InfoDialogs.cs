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
            Content = "Settings_IndependentDeploymentDesc".GetLocalized(),
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
            Content = "Settings_CloudCheckinNoteDesc".GetLocalized(),
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
                Title = "Settings_CpuWarningDisabledTitle".GetLocalized(),
                Content = "Settings_CpuWarningDisabledDesc".GetLocalized(),
                CloseButtonText = "GotItBtn".GetLocalized(),
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    #endregion
}