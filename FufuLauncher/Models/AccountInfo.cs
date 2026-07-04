/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public partial class AccountInfo : ObservableObject
{
    [ObservableProperty]
    private string _accountId = "";
    [ObservableProperty] private string _nickname = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GameUidDisplay))]
    private string _stuid = "";
    [ObservableProperty] private string _gameUid = "";
    [ObservableProperty] private string _server = "";
    [ObservableProperty] private string _avatarUrl = "ms-appx:///Assets/DefaultAvatar.png";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelDisplay))]
    private string _level = "";
    [ObservableProperty] private string _sign = "User_DefaultSignature".GetLocalized();
    [ObservableProperty] private string _ipRegion = "Status_Unknown".GetLocalized();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LevelDisplay))]
    [NotifyPropertyChangedFor(nameof(GameUidDisplay))]
    private bool _hasBoundRole = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GenderIcon))]
    [NotifyPropertyChangedFor(nameof(GenderText))]
    [NotifyPropertyChangedFor(nameof(GameUidDisplay))]
    private int _gender = 0;
    public string GenderIcon => _gender switch
    {
        1 => "\uE13D",
        2 => "\uE13C",
        _ => "\uE77B"
    };

    public string GenderText => _gender switch
    {
        1 => "User_GenderMale".GetLocalized(),
        2 => "User_GenderFemale".GetLocalized(),
        _ => "User_GenderPrivate".GetLocalized()
    };

    public string LevelDisplay => HasBoundRole && !string.IsNullOrEmpty(Level) ? Level : "Status_None".GetLocalized();

    public string GameUidDisplay => string.IsNullOrEmpty(Stuid) ? "Status_None".GetLocalized() : Stuid;
}
