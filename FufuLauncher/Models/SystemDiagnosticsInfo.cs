/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.ComponentModel;

namespace FufuLauncher.Models;

public partial class SystemDiagnosticsInfo : ObservableObject
{
    [ObservableProperty] private string _osVersion = "未知";

    [ObservableProperty] private string _cpuName = "未知";

    [ObservableProperty] private string _gpuName = "未知";

    [ObservableProperty] private string _totalMemory = "未知";

    [ObservableProperty] private string _screenResolution = "未知";

    [ObservableProperty] private string _currentRefreshRate = "未知";

    [ObservableProperty] private string _maxRefreshRate = "未知";

    [ObservableProperty] private string _suggestion = "未知";

    [ObservableProperty] private string _networkStatus = "未知";

    [ObservableProperty] private string _networkRegion = "未知";

    [ObservableProperty] private string _diskSpace = "未知";

    [ObservableProperty] private string _securityCenterStatus = "未知";
}
