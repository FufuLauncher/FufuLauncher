/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;

namespace FufuLauncher.ViewModels;

public partial class QuickFpsSlotItem : ObservableObject
{
    [ObservableProperty] private double _fps;

    [ObservableProperty] private VirtualKeyOption? _selectedKey;

    public QuickFpsSlotItem(int index, ObservableCollection<VirtualKeyOption> keyChoices)
    {
        Index = index;
        KeyChoices = keyChoices;
        Label = string.Format(CultureInfo.CurrentCulture, "QuickFpsSlotFormat".GetLocalized(), index);
        FpsAutomationName = string.Format(CultureInfo.CurrentCulture, "{0} {1}", Label,
            "QuickFpsFps".GetLocalized());
        KeyAutomationName = string.Format(CultureInfo.CurrentCulture, "{0} {1}", Label,
            "QuickFpsKey".GetLocalized());
    }

    public int Index
    {
        get;
    }

    public string Label
    {
        get;
    }

    public ObservableCollection<VirtualKeyOption> KeyChoices
    {
        get;
    }

    public string FpsAutomationName
    {
        get;
    }

    public string KeyAutomationName
    {
        get;
    }
}