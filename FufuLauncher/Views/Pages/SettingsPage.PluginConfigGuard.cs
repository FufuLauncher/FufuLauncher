/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    private bool _isSyncingPluginConfigGuard;
    private DispatcherTimer? _pluginConfigGuardTimer;

    private void UpdatePluginConfigGuardUi()
    {
        if (PluginConfigGuardToggle == null)
        {
            return;
        }

        _isSyncingPluginConfigGuard = true;
        try
        {
            PluginConfigGuardToggle.IsEnabled = !PluginConfigGuard.IsExemptBuild;
            PluginConfigGuardToggle.IsOn = PluginConfigGuard.IsValidationActive;
        }
        finally
        {
            _isSyncingPluginConfigGuard = false;
        }

        UpdatePluginConfigGuardCaption();
        SyncPluginConfigGuardTimer();
    }

    private void UpdatePluginConfigGuardCaption()
    {
        if (PluginConfigGuardCaption == null)
        {
            return;
        }

        if (PluginConfigGuard.IsExemptBuild)
        {
            PluginConfigGuardCaption.Text = "PluginConfigGuard_CaptionExempt".GetLocalized();
            return;
        }

        if (PluginConfigGuard.IsTemporaryBypassActive)
        {
            var remaining = PluginConfigGuard.TemporaryBypassRemaining;
            var countdown = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
            PluginConfigGuardCaption.Text =
                string.Format("PluginConfigGuard_CaptionBypassed".GetLocalized(), countdown);
            return;
        }

        PluginConfigGuardCaption.Text = "PluginConfigGuard_Caption".GetLocalized();
    }

    private void SyncPluginConfigGuardTimer()
    {
        if (!PluginConfigGuard.IsTemporaryBypassActive)
        {
            _pluginConfigGuardTimer?.Stop();
            _pluginConfigGuardTimer = null;
            return;
        }

        if (_pluginConfigGuardTimer != null)
        {
            return;
        }

        _pluginConfigGuardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _pluginConfigGuardTimer.Tick += OnPluginConfigGuardTimerTick;
        _pluginConfigGuardTimer.Start();
    }

    private void OnPluginConfigGuardTimerTick(object? sender, object e)
    {
        if (!PluginConfigGuard.IsTemporaryBypassActive)
        {
            _pluginConfigGuardTimer?.Stop();
            _pluginConfigGuardTimer = null;
            UpdatePluginConfigGuardUi();
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "PluginConfigGuard_Title".GetLocalized(),
                "PluginConfigGuard_BypassEnded".GetLocalized(),
                NotificationType.Information,
                5000));
            return;
        }

        UpdatePluginConfigGuardCaption();
    }

    private async void OnPluginConfigGuardToggled(object sender, RoutedEventArgs e)
    {
        if (_isSyncingPluginConfigGuard || sender is not ToggleSwitch toggle)
        {
            return;
        }

        if (PluginConfigGuard.IsExemptBuild)
        {
            UpdatePluginConfigGuardUi();
            return;
        }

        if (toggle.IsOn == PluginConfigGuard.IsValidationActive)
        {
            return;
        }

        if (toggle.IsOn)
        {
            PluginConfigGuard.StopTemporaryBypass();
            UpdatePluginConfigGuardUi();
            return;
        }

        if (!await IsDeveloperAuthorizedAsync())
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "PluginConfigGuard_Title".GetLocalized(),
                "PluginConfigGuard_NeedDevAuth".GetLocalized(),
                NotificationType.Warning,
                5000));
            UpdatePluginConfigGuardUi();
            return;
        }

        PluginConfigGuard.StartTemporaryBypass();
        UpdatePluginConfigGuardUi();
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "PluginConfigGuard_Title".GetLocalized(),
            "PluginConfigGuard_BypassStarted".GetLocalized(),
            NotificationType.Information,
            5000));
    }

    private static async Task<bool> IsDeveloperAuthorizedAsync()
    {
        var authorizationService = App.GetService<DeveloperAuthorizationService>();
        return authorizationService != null &&
               (authorizationService.IsTemporaryAuthorized || await authorizationService.IsAuthorizedAsync());
    }
}
