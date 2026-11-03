/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services;
using FufuLauncher.Services.GameServer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    #region 预下载

    private const string PredownloadTargetSettingKey = "PredownloadTargetServer";

    private sealed record PredownloadTargetChoice(string Label, bool? IsOversea);

    private List<PredownloadTargetChoice>? _predownloadTargetChoices;
    private bool _isRestoringPredownloadTarget;

    private async void OnStartPredownloadClick(object sender, RoutedEventArgs e)
    {
        await StartGameUpdateAsync(GameUpdateOperationKind.Predownload);
    }

    private async void OnApplyPredownloadClick(object sender, RoutedEventArgs e)
    {
        await StartGameUpdateAsync(GameUpdateOperationKind.ApplyPredownload);
    }

    private async void OnPredownloadTargetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringPredownloadTarget ||
            PredownloadTargetCombo.SelectedItem is not PredownloadTargetChoice choice)
        {
            return;
        }

        var localSettings = App.GetService<ILocalSettingsService>();
        if (localSettings is null)
        {
            return;
        }

        string value = choice.IsOversea switch
        {
            true => "oversea",
            false => "chinese",
            _ => "auto",
        };
        await localSettings.SaveSettingAsync(PredownloadTargetSettingKey, value);
    }

    private async Task StartGameUpdateAsync(GameUpdateOperationKind kind)
    {
        string? gameDir = null;
        var configService = App.GetService<IGameConfigService>();
        if (configService is not null)
        {
            gameDir = await configService.GetSavedGamePathAsync();
        }

        if (string.IsNullOrEmpty(gameDir))
        {
            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ErrorTitle".GetLocalized(),
                Content = "Err_GamePathNotFound".GetLocalized(),
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = XamlRoot
            });
            return;
        }

        if (File.Exists(gameDir))
        {
            gameDir = Path.GetDirectoryName(gameDir) ?? gameDir;
        }

        if (!await IsDeveloperFeatureEnabledAsync())
        {
            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ErrorTitle".GetLocalized(),
                Content = "GameUpdate_DevOnly".GetLocalized(),
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = XamlRoot
            });
            return;
        }

        GameServerScheme? schemeOverride = ResolvePredownloadTargetScheme(gameDir);
        var newWindow = new GameUpdateWindow(gameDir, kind, schemeOverride: schemeOverride);
        newWindow.Activate();
    }

    private GameServerScheme? ResolvePredownloadTargetScheme(string gameDir)
    {
        if (PredownloadTargetCombo.SelectedItem is not PredownloadTargetChoice choice || choice.IsOversea is null)
        {
            return null;
        }

        bool isOversea = choice.IsOversea.Value;
        var configurationService = App.GetService<GameServerConfigurationService>();
        GameServerScheme? detected = configurationService.TryDetectCurrentScheme(gameDir);
        if (detected is not null && detected.IsOversea == isOversea)
        {
            return detected;
        }

        return isOversea ? GameServerScheme.OverseaOfficialDefault : GameServerScheme.ChineseOfficialOfficial;
    }

    private async Task UpdatePredownloadDevVisibilityAsync()
    {
        bool devEnabled = await IsDeveloperFeatureEnabledAsync();
        PredownloadDevPanel.Visibility = devEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!devEnabled)
        {
            return;
        }

        await EnsurePredownloadTargetChoicesAsync();
    }

    private async Task EnsurePredownloadTargetChoicesAsync()
    {
        if (_predownloadTargetChoices is null)
        {
            _predownloadTargetChoices =
            [
                new PredownloadTargetChoice("GameUpdate_PredownloadTargetAuto".GetLocalized(), null),
                new PredownloadTargetChoice("GameUpdate_PredownloadTargetOversea".GetLocalized(), true),
                new PredownloadTargetChoice("GameUpdate_PredownloadTargetChinese".GetLocalized(), false),
            ];
            PredownloadTargetCombo.ItemsSource = _predownloadTargetChoices;
        }

        if (PredownloadTargetCombo.SelectedIndex >= 0)
        {
            return;
        }

        var localSettings = App.GetService<ILocalSettingsService>();
        object? saved = localSettings is null
            ? null
            : await localSettings.ReadSettingAsync(PredownloadTargetSettingKey);
        int index = saved?.ToString() switch
        {
            "oversea" => 1,
            "chinese" => 2,
            _ => 0,
        };

        _isRestoringPredownloadTarget = true;
        try
        {
            PredownloadTargetCombo.SelectedIndex = index;
        }
        finally
        {
            _isRestoringPredownloadTarget = false;
        }
    }

    private async Task<bool> IsDeveloperFeatureEnabledAsync()
    {
        var authorizationService = App.GetService<DeveloperAuthorizationService>();
        if (authorizationService is null)
        {
            return false;
        }

        bool devEnabled = authorizationService.IsTemporaryAuthorized;
        if (!devEnabled)
        {
            var localSettings = App.GetService<ILocalSettingsService>();
            if (localSettings is not null)
            {
                var value = await localSettings.ReadSettingAsync("IsDevFeaturesEnabled");
                devEnabled = value is not null && Convert.ToBoolean(value);
            }
        }

        return devEnabled && await authorizationService.IsAuthorizedAsync();
    }

    #endregion
}
