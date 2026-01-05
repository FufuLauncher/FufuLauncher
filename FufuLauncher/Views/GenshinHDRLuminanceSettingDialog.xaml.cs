using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Services;
using FufuLauncher.Contracts.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

using DisplayInformation = Microsoft.Graphics.Display.DisplayInformation;
using DisplayAdvancedColorKind = Microsoft.Graphics.Display.DisplayAdvancedColorKind;

namespace FufuLauncher.Views;

[INotifyPropertyChanged]
public sealed partial class GenshinHDRLuminanceSettingDialog : ContentDialog
{
    private DisplayInformation? _displayInformation;

    public GenshinHDRLuminanceSettingDialog()
    {
        InitializeComponent();
        this.Loaded += GenshinHDRLuminanceSettingDialog_Loaded;
        this.Unloaded += GenshinHDRLuminanceSettingDialog_Unloaded;
    }

    private async void GenshinHDRLuminanceSettingDialog_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            this.XamlRoot.Changed -= XamlRoot_Changed;
            this.XamlRoot.Changed += XamlRoot_Changed;
            
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
            
            _displayInformation = DisplayInformation.CreateForWindowId(windowId);
            
            if (_displayInformation != null)
            {
                _displayInformation.AdvancedColorInfoChanged += _displayInformation_AdvancedColorInfoChanged;
                UpdateDisplayInfomation(_displayInformation);
            }
            
            (MaxLuminance, SceneLuminance, UILuminance) = GameSettingService.GetGenshinHDRLuminance();
            var localSettings = App.GetService<ILocalSettingsService>();
            if (localSettings != null)
            {
                var val = await localSettings.ReadSettingAsync(GameLauncherService.GenshinHDRConfigKey);
                if (val != null)
                {
                    IsGenshinHDRForceEnabled = Convert.ToBoolean(val);
                }
                else
                {
                    IsGenshinHDRForceEnabled = GameSettingService.GetGenshinHDRState();
                }
            }
            else
            {
                IsGenshinHDRForceEnabled = GameSettingService.GetGenshinHDRState();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error in Loaded: {ex}");
        }
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_displayInformation != null)
        {
            UpdateDisplayInfomation(_displayInformation);
        }
    }

    private void GenshinHDRLuminanceSettingDialog_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_displayInformation != null)
            {
                _displayInformation.AdvancedColorInfoChanged -= _displayInformation_AdvancedColorInfoChanged;
            }
            this.XamlRoot.Changed -= XamlRoot_Changed;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    [ObservableProperty] private bool hDRNotSupported;
    [ObservableProperty] private bool hDRNotEnabled;
    [ObservableProperty] private bool hDREnabled;
    [ObservableProperty] private string displayInfomation = "正在获取显示器信息...";
    [ObservableProperty] private int maxLuminance = 1000;
    [ObservableProperty] private int sceneLuminance = 300;
    [ObservableProperty] private int uILuminance = 350;
    [ObservableProperty] private bool isGenshinHDRForceEnabled;

    private void _displayInformation_AdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        this.DispatcherQueue.TryEnqueue(() => UpdateDisplayInfomation(sender));
    }

    private void UpdateDisplayInfomation(DisplayInformation display)
    {
        if (display == null) return;
        try 
        {
            var info = display.GetAdvancedColorInfo();
            if (info == null) return;
            
            HDRNotSupported = !info.IsAdvancedColorKindAvailable(DisplayAdvancedColorKind.HighDynamicRange);
            HDRNotEnabled = info.CurrentAdvancedColorKind is not DisplayAdvancedColorKind.HighDynamicRange;
            HDREnabled = !HDRNotEnabled;
            
            string kind = info.CurrentAdvancedColorKind switch
            {
                DisplayAdvancedColorKind.StandardDynamicRange => "标准动态范围 (SDR)",
                DisplayAdvancedColorKind.WideColorGamut => "广色域 (WCG)",
                DisplayAdvancedColorKind.HighDynamicRange => "高动态范围 (HDR)",
                _ => "未知",
            };

            DisplayInfomation = $"""
                色彩空间: {kind}
                峰值亮度: {(double)info.MaxLuminanceInNits:F0} nits
                最大全屏亮度: {(double)info.MaxAverageFullFrameLuminanceInNits:F0} nits
                SDR白色亮度: {(double)info.SdrWhiteLevelInNits:F0} nits
                """;
        }
        catch 
        { 
            DisplayInfomation = "无法读取显示器信息"; 
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        GameSettingService.SetGenshinHDRLuminance(MaxLuminance, SceneLuminance, UILuminance);
        GameSettingService.SetGenshinHDRState(IsGenshinHDRForceEnabled);
        
        var localSettings = App.GetService<ILocalSettingsService>();
        if (localSettings != null)
        {
            await localSettings.SaveSettingAsync(GameLauncherService.GenshinHDRConfigKey, IsGenshinHDRForceEnabled);
        }
    }

    [RelayCommand]
    private void Reset()
    {
        MaxLuminance = 1000;
        SceneLuminance = 300;
        UILuminance = 350;
        IsGenshinHDRForceEnabled = false;
    }

    [RelayCommand]
    private void AutoAdjust()
    {
        if (_displayInformation == null) return;
        try
        {
            var info = _displayInformation.GetAdvancedColorInfo();
            if (info != null)
            {
                double maxLum = info.MaxLuminanceInNits > 0 ? info.MaxLuminanceInNits : 1000;
                double sdrWhite = info.SdrWhiteLevelInNits > 0 ? info.SdrWhiteLevelInNits : 200;

                MaxLuminance = (int)Math.Clamp(maxLum, 300, 2000);
                SceneLuminance = (int)Math.Clamp(sdrWhite + 20, 100, 500);
                UILuminance = (int)Math.Clamp(SceneLuminance + 50, 150, 550);
            }
        }
        catch { }
    }

    [RelayCommand]
    private void Close()
    {
        
    }
}