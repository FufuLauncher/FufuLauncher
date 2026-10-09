/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public partial class CheckinGameItem(MiyousheCheckinGame game) : ObservableObject
{
    public string GameBiz { get; } = game.GameBiz;
    public string SettingKey { get; } = game.SettingKey;
    public string DisplayName { get; } = $"Checkin_Game_{game.GameBiz}".GetLocalized();

    [ObservableProperty] private bool _isSelected = true;
}
