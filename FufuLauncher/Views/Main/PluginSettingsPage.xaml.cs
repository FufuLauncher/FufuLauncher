/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.Messages;
using FufuLauncher.ViewModels;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage : Page
{
    public PluginSettingsViewModel ViewModel
    {
        get;
    }

    public MainViewModel MainVM
    {
        get;
    }

    public ControlPanelModel ControlPanelVM
    {
        get;
    }

    private bool _isInitializing = true;

    public PluginSettingsPage()
    {
        ViewModel = new PluginSettingsViewModel(deferConfigurationLoading: true);
        MainVM = App.GetService<MainViewModel>();
        ControlPanelVM = App.GetService<ControlPanelModel>();
        InitializeComponent();
        ViewModel.Settings.CollectionChanged += OnSettingItemsChanged;
        ViewModel.PinnedSettings.CollectionChanged += OnSettingItemsChanged;
        SettingsViewSource.Source = ViewModel.SettingGroups;
        Loaded += PluginSettingsPage_Loaded;
        Unloaded += PluginSettingsPage_Unloaded;

        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        WeakReferenceMessenger.Default.Register<ConstraintStateChangedMessage>(this, (_, _) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!IsLoaded) return;
                ApplyModeChange();
            });
        });
    }

    private async void PluginSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        EntranceStoryboard.Begin();
        ResetSettingEntranceAnimations(true);
        InitializeConfigurationChrome();
        ViewModel.ActivateConfigurationLoading();
        StartMainPluginWatcher();
        ShowMainPluginMissingWarningIfNeeded();
        if (ViewModel.IsPluginCorrupted())
        {
            var dialog = new ContentDialog
            {
                Title = "Plugin_Corrupted_Title".GetLocalized(),
                Content = "Plugin_Corrupted_Content".GetLocalized(),
                PrimaryButtonText = "GotItBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
            if (!IsLoaded) return;
        }

        await VerifyFpsPluginHashAsync();
        if (!IsLoaded) return;

        await CheckAndShowFpsWarningAsync();
        if (!IsLoaded) return;

        if (ViewModel.SettingsOverlayVisibility == Visibility.Visible)
        {
            SettingsOverlay.Visibility = Visibility.Visible;
            SettingsOverlay.Opacity = 1;
        }

        _isInitializing = false;
    }

    private void PluginSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _settingDisableWarningDialog?.Hide();
        StopConfigurationChrome();
        ViewModel.SuspendConfigurationLoading();
        ResetSettingEntranceAnimations(false);
        ResetSettingsSelection();
        _mainPluginWatcher?.Dispose();
        _mainPluginWatcher = null;
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is Models.PluginItem item)
        {
            var folderName = new DirectoryInfo(item.DirectoryPath).Name;

            if (folderName.Contains("FPS", StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.SelectedPluginIndex = 1;
            }
            else
            {
                ViewModel.SelectedPluginIndex = 0;
            }
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        StopConfigurationChrome();
        ViewModel.SuspendConfigurationLoading();
        ResetSettingEntranceAnimations(false);
        base.OnNavigatedFrom(e);
    }

    private async void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsLoadingConfiguration))
        {
            UpdateConfigurationLoadingPresentation();
            if (ViewModel.IsLoadingConfiguration)
            {
                ResetSettingsSelection();
            }
        }

        if (_isInitializing || !IsLoaded) return;

        if (e.PropertyName == nameof(ViewModel.SettingsOverlayVisibility))
        {
            if (ViewModel.SettingsOverlayVisibility == Visibility.Visible)
            {
                SettingsOverlay.Visibility = Visibility.Visible;
                OverlayFadeIn.Begin();
            }
            else
            {
                if (SettingsOverlay.Visibility == Visibility.Visible)
                {
                    OverlayFadeOut.Begin();
                }
            }
        }
        else if (e.PropertyName == nameof(ViewModel.SelectionBarVisibility))
        {
            UpdateBatchBarVisibility();
        }
        else if (e.PropertyName == nameof(ViewModel.SelectedPluginIndex))
        {
            await CheckAndShowFpsWarningAsync();
            if (!IsLoaded) return;
        }
        else if (e.PropertyName == nameof(ViewModel.IsFpsPluginEnabled))
        {
            if (!ViewModel.IsFpsPluginEnabled && !_isEnforcingFpsDisable)
            {
                await EnforceFpsPluginDisableAsync();
            }
        }
    }

    private void OverlayFadeOut_Completed(object sender, object e)
    {
        if (ViewModel.SettingsOverlayVisibility == Visibility.Collapsed)
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        }
    }

    public bool InvertBool(bool value) => !value;

    private void OnRetryConfigurationClick(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            ViewModel.LoadConfiguration();
        }
    }

    private async void OnPinSettingClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginSettingItem item)
        {
            await AnimateSettingPinAsync(new[] { item }, !item.IsPinned);
        }
    }

    private async void OnInstallSignatureClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (App.MainWindow is MainWindow mainWindow)
            {
                await mainWindow.NavigateToSettingsTrustSectionAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginSettings] 跳转签名安装失败: {ex.Message}");
        }
    }

    private void MoveDirectorySafe(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            MoveDirectorySafe(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }

        Directory.Delete(sourceDir, true);
    }
}