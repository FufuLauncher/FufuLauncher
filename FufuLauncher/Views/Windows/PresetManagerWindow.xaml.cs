/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.ViewModels;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FufuLauncher.Views;

public class PresetWrapper : INotifyPropertyChanged
{
    public string Id
    {
        get;
        set;
    }

    public string Name
    {
        get;
        set;
    }

    private bool _isPinned;

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed partial class PresetManagerWindow : Window
{
    public ObservableCollection<PresetWrapper> AllPresets
    {
        get;
    } = new();

    private readonly ILocalSettingsService _localSettingsService;

    public PresetManagerWindow()
    {
        InitializeComponent();
        _localSettingsService = App.GetService<ILocalSettingsService>();
        Title = "PresetManager_Title".GetLocalized();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch
        {
        }

        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        if (appWindow != null)
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            appWindow.Resize(new Windows.Graphics.SizeInt32(400, 500));
        }

        LoadPresetsAsync();
    }

    private async void LoadPresetsAsync()
    {
        var pinnedIdsJson = await _localSettingsService.ReadSettingAsync("PinnedPresetIds");
        List<string> pinnedIds = new();
        if (pinnedIdsJson != null)
        {
            try
            {
                pinnedIds = JsonSerializer.Deserialize<List<string>>(pinnedIdsJson.ToString());
            }
            catch
            {
            }
        }

        var presets = await Task.Run(() => PluginPresetStorage.ReadPresets(AppPaths.PluginPresetsDir));
        var pinned = new HashSet<string>(pinnedIds, StringComparer.OrdinalIgnoreCase);
        AllPresets.Clear();
        foreach (var preset in presets)
        {
            AllPresets.Add(new PresetWrapper
            {
                Id = preset.Id,
                Name = preset.Name,
                IsPinned = pinned.Contains(preset.Id)
            });
        }
    }

    private async void PresetCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is PresetWrapper wrapper)
        {
            var pinnedCount = AllPresets.Count(p => p.IsPinned);

            if (cb.IsChecked == true && pinnedCount > 5)
            {
                cb.IsChecked = false;
                wrapper.IsPinned = false;

                var dialog = new ContentDialog
                {
                    Title = "Common_Notice".GetLocalized(),
                    Content = "PresetManager_MaxPinnedHint".GetLocalized(),
                    CloseButtonText = "Common_Confirm".GetLocalized(),
                    XamlRoot = this.Content.XamlRoot
                };
                await dialog.ShowAsync();
                return;
            }

            wrapper.IsPinned = cb.IsChecked == true;

            var pinnedIds = AllPresets.Where(p => p.IsPinned).Select(p => p.Id).ToList();
            await _localSettingsService.SaveSettingAsync("PinnedPresetIds", JsonSerializer.Serialize(pinnedIds));
        }
    }
}