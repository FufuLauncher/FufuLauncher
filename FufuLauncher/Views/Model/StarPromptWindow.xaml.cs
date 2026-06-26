/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class StarPromptWindow : WindowEx
{
    private readonly ILocalSettingsService _settingsService;

    public StarPromptWindow(ILocalSettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        
        SystemBackdrop = new MicaBackdrop();
        
        Width = 500;
        Height = 320;
        
        WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
    }

    private async void RemindLater_Click(object sender, RoutedEventArgs e)
    {
        await _settingsService.SaveSettingAsync("NextStarPromptDate", DateTime.Now.AddDays(1).ToString("O"));
        Close();
    }

    private async void GotIt_Click(object sender, RoutedEventArgs e)
    {
        await _settingsService.SaveSettingAsync("StarPromptDismissed", true);
        Close();
    }
}
