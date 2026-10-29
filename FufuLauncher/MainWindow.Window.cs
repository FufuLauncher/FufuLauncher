/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    #region Window Management

    private void ShowWindow()
    {
        RestoreFromSuspension();

        this.Show();
        BringToFront();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange)
        {
            var presenter = sender.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                if (presenter.State == OverlappedPresenterState.Minimized)
                {
                    if (!_memoryOptimizationTimer.IsEnabled)
                    {
                        _memoryOptimizationTimer.Start();
                    }
                }
                else if (presenter.State != OverlappedPresenterState.Minimized)
                {
                    RestoreFromSuspension();
                }
            }
        }
    }

    private void DisposeTrayIcon()
    {
        try
        {
            _trayIconController?.Dispose();
        }
        catch
        {
        }

        TrayIcon.Dispose();
    }

    private void CleanupWindowResources()
    {
        _memoryOptimizationTimer?.Stop();
        _periodicMemoryTimer?.Stop();
        _gameMemoryTelemetryTimer?.Stop();
        _gameMemoryReleaseTimer?.Stop();
        _messageDismissTimer?.Stop();
        _announcementCheckTimer?.Stop();
        _slideshowTimer?.Stop();
        _networkMonitorService.Stop();


        try
        {
            App.GetService<MainViewModel>()?.Cleanup();
        }
        catch
        {
        }

        try
        {
            App.GetService<ControlPanelViewModel>()?.Cleanup();
        }
        catch
        {
        }

        DisposeGlobalBackgroundPlayer();
        StopBackgroundImageAnimation();
        Interlocked.Increment(ref _backgroundLoadVersion);
        GlobalBackgroundImage.Source = null;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        settings.ColorValuesChanged -= Settings_ColorValuesChanged;
    }

    private async void ExitApplication()
    {
        try
        {
            await App.GetService<FufuLauncher.Services.Mcp.McpHostService>().StopAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Window", $"[MainWindow] Failed to stop MCP service: {ex.Message}");
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await SaveWindowSizeAsync().WaitAsync(cts.Token);
        }
        catch
        {
        }

        _isExit = true;
        CleanupWindowResources();
        DisposeTrayIcon();
        Close();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExit) return;
        args.Cancel = true;

        if (!_isMainUiLoaded)
        {
            _isExit = true;
            CleanupWindowResources();
            DisposeTrayIcon();
            Close();
            return;
        }

        if (_minimizeToTray)
        {
            this.Hide();
            _memoryOptimizationTimer.Start();
        }
        else
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await SaveWindowSizeAsync().WaitAsync(cts.Token);
            }
            catch
            {
            }

            _isExit = true;
            CleanupWindowResources();
            DisposeTrayIcon();
            Close();
        }
    }

    private async Task SaveWindowSizeAsync()
    {
        try
        {
            var localSettings = App.GetService<ILocalSettingsService>();
            var saveEnabledObj = await localSettings.ReadSettingAsync("IsSaveWindowSizeEnabled");
            if (saveEnabledObj != null && Convert.ToBoolean(saveEnabledObj))
            {
                var presenter = AppWindow.Presenter as OverlappedPresenter;
                if (presenter != null && presenter.State != OverlappedPresenterState.Minimized)
                {
                    if (Width > 0 && Height > 0)
                    {
                        await localSettings.SaveSettingAsync("SavedWindowWidth", Width);
                        await localSettings.SaveSettingAsync("SavedWindowHeight", Height);
                    }
                }
            }
        }
        catch
        {
        }
    }

    public async Task InitializeWindowSizeAsync()
    {
        try
        {
            var localSettings = App.GetService<ILocalSettingsService>();

            var accepted = await localSettings.ReadSettingAsync("UserAgreementAccepted");
            if (accepted == null || !Convert.ToBoolean(accepted))
            {
                IsAgreementShowing = true;
                Width = 850;
                Height = 640;
                WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
                AgreementFrame.Visibility = Visibility.Visible;
                AgreementFrame.Navigate(typeof(Views.LanguageSelectionPage));
                SyncPageTheme();
                return;
            }

            await ApplyMainWindowSizeAsync(localSettings);
        }
        catch
        {
            Width = 850;
            Height = 560;
            WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
        }
    }

    private async Task ApplyMainWindowSizeAsync(ILocalSettingsService? localSettings = null)
    {
        try
        {
            localSettings ??= App.GetService<ILocalSettingsService>();
            var saveEnabledObj = await localSettings.ReadSettingAsync("IsSaveWindowSizeEnabled");

            if (saveEnabledObj != null && Convert.ToBoolean(saveEnabledObj))
            {
                var widthObj = await localSettings.ReadSettingAsync("SavedWindowWidth");
                var heightObj = await localSettings.ReadSettingAsync("SavedWindowHeight");

                if (widthObj != null && heightObj != null &&
                    double.TryParse(widthObj.ToString(), out var w) &&
                    double.TryParse(heightObj.ToString(), out var h))
                {
                    Width = w;
                    Height = h;
                    if (!_isOverlayShown) OverlayTranslate.Y = h + 100;
                    WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
                    return;
                }
            }

            Width = 1360;
            Height = 768;
            if (!_isOverlayShown) OverlayTranslate.Y = Height + 100;
            WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
        }
        catch
        {
            Width = 1360;
            Height = 768;
            WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        try
        {
            SetTitleBar(AppTitleBar);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
                TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
            UpdateTitleBarWithAdminStatus();
        }
        catch
        {
        }

        Activated -= OnWindowActivated;
    }

    private void UpdateTitleBarWithAdminStatus()
    {
        try
        {
            var isAdmin = SystemEnvironmentHelper.IsRunningAsAdministrator();
            TitleBarText.Text = isAdmin ? "AppDisplayNameAdmin".GetLocalized() : "AppDisplayName".GetLocalized();

            if (AppVersionHelper.IsPreviewBuild)
            {
                TitleBarVersionText.Text = string.Format("PreviewVersionBadgeFormat".GetLocalized(),
                    AppVersionHelper.NumericVersion);
                TitleBarVersionText.Visibility = Visibility.Visible;
            }
        }
        catch
        {
        }
    }

    public void ShowDevBuildBadge()
    {
        try
        {
            TitleBarVersionText.Text =
                string.Format("DevVersionBadgeFormat".GetLocalized(), AppVersionHelper.NumericVersion);
            TitleBarVersionText.Visibility = Visibility.Visible;
        }
        catch
        {
        }
    }

    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        dispatcherQueue.TryEnqueue(() =>
        {
            TitleBarHelper.ApplySystemThemeToCaptionButtons();
        });
    }

    private void ApplyBackdrop(WindowBackdropType type)
    {
        try
        {
            SystemBackdrop = null;
            switch (type)
            {
                case WindowBackdropType.Mica:
                    SystemBackdrop = new MicaBackdrop();
                    break;
                case WindowBackdropType.Acrylic:
                    SystemBackdrop = new DesktopAcrylicBackdrop();
                    break;
            }
        }
        catch
        {
        }
    }

    private async Task LoadAndApplyAcrylicSettingAsync()
    {
        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var backdropJson = await localSettingsService.ReadSettingAsync("WindowBackdrop");
            WindowBackdropType backdropType;

            if (backdropJson != null)
                backdropType = (WindowBackdropType)Convert.ToInt32(backdropJson);
            else
            {
                var acrylicEnabled = await localSettingsService.ReadSettingAsync("IsAcrylicEnabled");
                var isEnabled = acrylicEnabled != null && Convert.ToBoolean(acrylicEnabled);
                backdropType = isEnabled ? WindowBackdropType.Acrylic : WindowBackdropType.Acrylic;
            }

            ApplyBackdrop(backdropType);
        }
        catch
        {
            ApplyBackdrop(WindowBackdropType.Acrylic);
        }
    }

    private async Task LoadMinimizeToTraySettingAsync()
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync("MinimizeToTray");
            _minimizeToTray = value != null && Convert.ToBoolean(value);
        }
        catch
        {
            _minimizeToTray = false;
        }
    }

    private async Task LoadMinWindowSizeLimitSettingAsync()
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync("IsMinWindowSizeLimitEnabled");
            var enabled = value == null || Convert.ToBoolean(value);
            ApplyMinWindowSizeLimit(enabled);
        }
        catch
        {
            ApplyMinWindowSizeLimit(true);
        }
    }

    private void ApplyMinWindowSizeLimit(bool enabled)
    {
        if (enabled)
        {
            MinWidth = 1360;
            MinHeight = 768;
        }
        else
        {
            MinWidth = 0;
            MinHeight = 0;
        }
    }

    #endregion
}