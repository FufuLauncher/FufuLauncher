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
    public string Id { get; set; }
    public string Name { get; set; }
    
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
    public ObservableCollection<PresetWrapper> AllPresets { get; } = new();
    private readonly ILocalSettingsService _localSettingsService;

    public PresetManagerWindow()
    {
        InitializeComponent();
        _localSettingsService = App.GetService<ILocalSettingsService>();
        Title = "预设管理";
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        
        try { SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop(); } catch { }
        
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
            try { pinnedIds = JsonSerializer.Deserialize<List<string>>(pinnedIdsJson.ToString()); } catch { }
        }

        string presetsDir = AppPaths.PluginPresetsDir;
        if (Directory.Exists(presetsDir))
        {
            foreach (var file in Directory.GetFiles(presetsDir, "*.json"))
            {
                if (file.EndsWith("active_state.json")) continue;
                try
                {
                    var content = File.ReadAllText(file);
                    var preset = JsonSerializer.Deserialize<PresetModel>(content);
                    if (preset != null)
                    {
                        AllPresets.Add(new PresetWrapper
                        {
                            Id = preset.Id,
                            Name = preset.Name,
                            IsPinned = pinnedIds.Contains(preset.Id)
                        });
                    }
                }
                catch { }
            }
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
                    Title = "提示",
                    Content = "最多只能固定 5 个预设",
                    CloseButtonText = "确定",
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