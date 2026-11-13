/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using FufuLauncher.Models;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    private async void OnCheckinAccountMoveUpClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CheckinAccountItem account })
            await ViewModel.MoveCheckinAccountAsync(account, -1);
    }

    private async void OnCheckinAccountMoveDownClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CheckinAccountItem account })
            await ViewModel.MoveCheckinAccountAsync(account, 1);
    }
}