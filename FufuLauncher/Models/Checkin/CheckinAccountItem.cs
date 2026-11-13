/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.ComponentModel;

namespace FufuLauncher.Models;

public partial class CheckinAccountItem : ObservableObject
{
    public string AccountId
    {
        get;
        init;
    } = string.Empty;

    public CheckinOptions? Options
    {
        get;
        set;
    }

    [ObservableProperty] private string _uid = string.Empty;
    [ObservableProperty] private string _nickname = string.Empty;
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private bool _hasCloudCredential;
    [ObservableProperty] private bool _canMoveUp;
    [ObservableProperty] private bool _canMoveDown;
}