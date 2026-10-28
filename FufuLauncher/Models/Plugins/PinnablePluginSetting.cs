/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public abstract class PinnablePluginSetting : ObservableObject
{
    private bool _isPinned;

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(PinGlyph));
                OnPropertyChanged(nameof(PinOpacity));
                OnPropertyChanged(nameof(PinTooltip));
            }
        }
    }

    public string PinGlyph => IsPinned ? "\uE735" : "\uE734";
    public double PinOpacity => IsPinned ? 0.95 : 0.5;
    public string PinTooltip => IsPinned ? "UnpinSettingTooltip".GetLocalized() : "PinSettingTooltip".GetLocalized();
}
