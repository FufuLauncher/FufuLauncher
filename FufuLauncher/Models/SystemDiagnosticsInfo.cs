/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public partial class SystemDiagnosticsInfo : ObservableObject
{
    [ObservableProperty] private string _osVersion = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _cpuName = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _gpuName = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _totalMemory = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _screenResolution = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _currentRefreshRate = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _maxRefreshRate = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _suggestion = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _networkStatus = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _networkRegion = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _diskSpace = "Status_Unknown".GetLocalized();

    [ObservableProperty] private string _securityCenterStatus = "Status_Unknown".GetLocalized();
}
