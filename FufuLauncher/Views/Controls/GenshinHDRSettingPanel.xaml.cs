using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using DisplayAdvancedColorKind = Microsoft.Graphics.Display.DisplayAdvancedColorKind;
using DisplayInformation = Microsoft.Graphics.Display.DisplayInformation;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

[INotifyPropertyChanged]
public sealed partial class GenshinHDRSettingPanel : UserControl
{
    private DisplayInformation? _displayInformation;
    private Flyout? _hostFlyout;
    private bool _isLoaded;
    private CancellationTokenSource? _saveDebounceCts;

    public GenshinHDRSettingPanel()
    {
        InitializeComponent();
        Loaded += GenshinHDRSettingPanel_Loaded;
        Unloaded += GenshinHDRSettingPanel_Unloaded;
    }

    public static void ShowAt(FrameworkElement target, FlyoutPlacementMode placement = FlyoutPlacementMode.Bottom)
    {
        var panel = new GenshinHDRSettingPanel();
        var flyout = new Flyout
        {
            Content = panel,
            Placement = placement,
            ShouldConstrainToRootBounds = false,
            AreOpenCloseAnimationsEnabled = true
        };
        flyout.FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(Control.PaddingProperty, new Thickness(16)),
                new Setter(Control.CornerRadiusProperty, new CornerRadius(8)),
                new Setter(FrameworkElement.MaxWidthProperty, 480.0),
                new Setter(FrameworkElement.MaxHeightProperty, 620.0)
            }
        };
        panel._hostFlyout = flyout;
        flyout.ShowAt(target);
    }

    private async void GenshinHDRSettingPanel_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        try
        {
            XamlRoot.Changed -= XamlRoot_Changed;
            XamlRoot.Changed += XamlRoot_Changed;

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
            DebugLog.Info("GenshinHDRSettingPanel", ex);
        }
        finally
        {
            _isLoaded = true;
        }
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_displayInformation != null)
        {
            UpdateDisplayInfomation(_displayInformation);
        }
    }

    private void GenshinHDRSettingPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isLoaded)
            {
                SaveSettings();
            }

            if (_displayInformation != null)
            {
                _displayInformation.AdvancedColorInfoChanged -= _displayInformation_AdvancedColorInfoChanged;
            }

            if (XamlRoot != null)
            {
                XamlRoot.Changed -= XamlRoot_Changed;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GenshinHDRSettingPanel", ex);
        }
    }

    [ObservableProperty]
    public partial bool HDRNotSupported
    {
        get;
        set;
    }

    [ObservableProperty]
    public partial bool HDRNotEnabled
    {
        get;
        set;
    }

    [ObservableProperty]
    public partial bool HDREnabled
    {
        get;
        set;
    }

    [ObservableProperty]
    public partial string SystemHDRStatusText
    {
        get;
        set;
    } = "HDR_Status_Detecting".GetLocalized();

    [ObservableProperty]
    public partial string DisplayColorSpace
    {
        get;
        set;
    } = "-";

    [ObservableProperty]
    public partial string DisplayMaxLuminance
    {
        get;
        set;
    } = "-";

    [ObservableProperty]
    public partial string DisplayFullFrameLuminance
    {
        get;
        set;
    } = "-";

    [ObservableProperty]
    public partial string DisplaySdrWhiteLevel
    {
        get;
        set;
    } = "-";

    [ObservableProperty]
    public partial string DisplayInfomation
    {
        get;
        set;
    } = "";

    [ObservableProperty]
    public partial int MaxLuminance
    {
        get;
        set;
    } = 1000;

    [ObservableProperty]
    public partial int SceneLuminance
    {
        get;
        set;
    } = 300;

    [ObservableProperty]
    public partial int UILuminance
    {
        get;
        set;
    } = 350;

    [ObservableProperty]
    public partial bool IsGenshinHDRForceEnabled
    {
        get;
        set;
    }

    partial void OnMaxLuminanceChanged(int value) => TriggerAutoSave();
    partial void OnSceneLuminanceChanged(int value) => TriggerAutoSave();
    partial void OnUILuminanceChanged(int value) => TriggerAutoSave();
    partial void OnIsGenshinHDRForceEnabledChanged(bool value) => TriggerAutoSave();

    private void TriggerAutoSave()
    {
        if (!_isLoaded) return;
        _saveDebounceCts?.Cancel();
        _saveDebounceCts = new CancellationTokenSource();
        var token = _saveDebounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                if (token.IsCancellationRequested) return;
                SaveSettings();
            }
            catch
            {
            }
        });
    }

    private void SaveSettings()
    {
        GameSettingService.SetGenshinHDRLuminance(MaxLuminance, SceneLuminance, UILuminance);
        GameSettingService.SetGenshinHDRState(IsGenshinHDRForceEnabled);

        var localSettings = App.GetService<ILocalSettingsService>();
        if (localSettings != null)
        {
            _ = localSettings.SaveSettingAsync(GameLauncherService.GenshinHDRConfigKey, IsGenshinHDRForceEnabled);
        }
    }

    private void _displayInformation_AdvancedColorInfoChanged(DisplayInformation sender, object args)
    {
        DispatcherQueue.TryEnqueue(() => UpdateDisplayInfomation(sender));
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

            if (HDREnabled)
            {
                SystemHDRStatusText = "HDR_Status_Enabled".GetLocalized();
            }
            else if (HDRNotSupported)
            {
                SystemHDRStatusText = "HDR_Status_NotSupported".GetLocalized();
            }
            else
            {
                SystemHDRStatusText = "HDR_Status_NotEnabled".GetLocalized();
            }

            DisplayColorSpace = info.CurrentAdvancedColorKind switch
            {
                DisplayAdvancedColorKind.StandardDynamicRange => "HDR_ColorSpace_SDR".GetLocalized(),
                DisplayAdvancedColorKind.WideColorGamut => "HDR_ColorSpace_WCG".GetLocalized(),
                DisplayAdvancedColorKind.HighDynamicRange => "HDR_ColorSpace_HDR".GetLocalized(),
                _ => "HDR_ColorSpace_Unknown".GetLocalized()
            };

            DisplayMaxLuminance = $"{info.MaxLuminanceInNits:F0} nits";
            DisplayFullFrameLuminance = $"{info.MaxAverageFullFrameLuminanceInNits:F0} nits";
            DisplaySdrWhiteLevel = $"{info.SdrWhiteLevelInNits:F0} nits";

            DisplayInfomation = $"""
                                 色彩空间: {DisplayColorSpace}
                                 峰值亮度: {DisplayMaxLuminance}
                                 最大全屏亮度: {DisplayFullFrameLuminance}
                                 SDR白色亮度: {DisplaySdrWhiteLevel}
                                 """;
        }
        catch
        {
            SystemHDRStatusText = "HDR_Status_Unavailable".GetLocalized();
            DisplayColorSpace = "HDR_UnableToRead".GetLocalized();
            DisplayMaxLuminance = "-";
            DisplayFullFrameLuminance = "-";
            DisplaySdrWhiteLevel = "-";
        }
    }

    [RelayCommand]
    private void Reset()
    {
        MaxLuminance = 1000;
        SceneLuminance = 300;
        UILuminance = 350;
        IsGenshinHDRForceEnabled = false;
        SaveSettings();
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
                var maxLum = info.MaxLuminanceInNits > 0 ? info.MaxLuminanceInNits : 1000;
                var sdrWhite = info.SdrWhiteLevelInNits > 0 ? info.SdrWhiteLevelInNits : 200;

                MaxLuminance = (int)Math.Clamp(maxLum, 300, 2000);
                SceneLuminance = (int)Math.Clamp(sdrWhite + 20, 100, 500);
                UILuminance = Math.Clamp(SceneLuminance + 50, 150, 550);
                SaveSettings();
            }
        }
        catch
        {
        }
    }

    public void Close()
    {
        if (_hostFlyout != null)
        {
            _hostFlyout.Hide();
            return;
        }

        DependencyObject current = this;
        while (current != null)
        {
            if (current is Popup popup)
            {
                popup.IsOpen = false;
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }
}