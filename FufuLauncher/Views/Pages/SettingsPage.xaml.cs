/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage : Page
{
    #region 字段

    private Window _easterEggWindow;

    private bool _cpuUsageWarningToggleLoaded;

    private bool _isNavigatingFromMenu;
    private DispatcherTimer? _navLockTimer;

    private static readonly string[] _sectionTags =
    {
        SettingsSectionIds.Appearance, SettingsSectionIds.HomeCards, SettingsSectionIds.GameAnnouncement,
        SettingsSectionIds.Widgets, SettingsSectionIds.Notes, SettingsSectionIds.HomeText,
        SettingsSectionIds.ImageSource, SettingsSectionIds.Background, SettingsSectionIds.WindowEffects,
        SettingsSectionIds.LaunchConfig, SettingsSectionIds.Screenshots, SettingsSectionIds.Checkin,
        SettingsSectionIds.Language, SettingsSectionIds.WindowBehavior, SettingsSectionIds.Startup,
        SettingsSectionIds.StartupSound, SettingsSectionIds.AdvancedOptions, SettingsSectionIds.StoragePaths,
        SettingsSectionIds.Updates, SettingsSectionIds.About, SettingsSectionIds.SecurityAuth,
        SettingsSectionIds.Mcp
    };

    private readonly List<SettingsSearchResult> _searchIndex = new();
    private FrameworkElement? _highlightedRow;
    private Microsoft.UI.Xaml.Media.Brush? _highlightedRowOriginalBackground;
    private DispatcherTimer? _highlightTimer;

    private bool _isRecordingHotkey;

    #endregion

    #region 初始化与页面生命周期

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        ViewModel.SectionLoadCompleted += OnSettingsSectionLoadCompletedAsync;
    }

    protected async override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        DebugLog.Info("SettingsPage", "SettingsPage.OnNavigatedTo.Start");

        if (ViewModel != null)
        {
            await ViewModel.ReloadSettingsAsync();
        }

        DebugLog.Info("SettingsPage", "SettingsPage.OnNavigatedTo.End");
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        DebugLog.Info("SettingsPage", "SettingsPage.PageLoaded");
        EntranceStoryboard.Begin();
        UpdateTemporaryDeveloperAuthUi();
        UpdatePluginConfigGuardUi();

        _isNavigatingFromMenu = true;
        if (SettingsNavigationView.SelectedItem == null)
        {
            SettingsNavigationView.SelectedItem =
                SettingsNavigationView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault();
        }

        _isNavigatingFromMenu = false;
    }

    private async Task OnSettingsSectionLoadCompletedAsync(string sectionId)
    {
        try
        {
            switch (sectionId)
            {
                case SettingsSectionIds.AdvancedOptions:
                    await UpdateDeviceInfoRowVisibilityAsync();
                    break;

                case SettingsSectionIds.Updates:
                    await UpdatePredownloadDevVisibilityAsync();
                    break;

                case SettingsSectionIds.SecurityAuth:
                    await RefreshModTrustUiAsync();
                    UpdateTemporaryDeveloperAuthUi();
                    UpdatePluginConfigGuardUi();
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsPage", $"[SettingsPage] Failed to refresh section {sectionId}: {ex.Message}");
        }
    }

    #endregion

    #region 对话框辅助

    private async Task<ContentDialogResult> ShowSafeDialogAsync(ContentDialog dialog)
    {
        try
        {
            return await dialog.ShowAsync();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            await Task.Delay(300);
            try
            {
                return await dialog.ShowAsync();
            }
            catch
            {
                return ContentDialogResult.None;
            }
        }
    }

    #endregion
}