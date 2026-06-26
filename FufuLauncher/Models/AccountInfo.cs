/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.ComponentModel;

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
    [ObservableProperty] private string _sign = "这个人很懒，什么都没有写...";
    [ObservableProperty] private string _ipRegion = "未知";
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
        1 => "男",
        2 => "女",
        _ => "保密"
    };

    public string LevelDisplay => HasBoundRole && !string.IsNullOrEmpty(Level) ? Level : "暂无";

    public string GameUidDisplay => string.IsNullOrEmpty(Stuid) ? "暂无" : Stuid;
}
