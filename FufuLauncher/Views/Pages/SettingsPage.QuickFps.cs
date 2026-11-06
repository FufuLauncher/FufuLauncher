/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Globalization;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    private bool _isRefreshingQuickFps;

    public ObservableCollection<VirtualKeyOption> QuickFpsKeyChoices { get; } = CreateQuickFpsKeyChoices();

    private static QuickFpsHotkeyService QuickFpsService => App.GetService<QuickFpsHotkeyService>();

    private NumberBox[] QuickFpsSlotFpsBoxes => new[]
    {
        QuickFpsSlot1FpsBox, QuickFpsSlot2FpsBox, QuickFpsSlot3FpsBox, QuickFpsSlot4FpsBox, QuickFpsSlot5FpsBox
    };

    private ComboBox[] QuickFpsSlotKeyBoxes => new[]
    {
        QuickFpsSlot1KeyBox, QuickFpsSlot2KeyBox, QuickFpsSlot3KeyBox, QuickFpsSlot4KeyBox, QuickFpsSlot5KeyBox
    };

    private TextBlock[] QuickFpsSlotLabels => new[]
    {
        QuickFpsSlot1Label, QuickFpsSlot2Label, QuickFpsSlot3Label, QuickFpsSlot4Label, QuickFpsSlot5Label
    };

    private static ObservableCollection<VirtualKeyOption> CreateQuickFpsKeyChoices()
    {
        var choices = new ObservableCollection<VirtualKeyOption>
        {
            new()
            {
                KeyCode = 0,
                KeyName = "QuickFpsUnbound".GetLocalized()
            }
        };

        foreach (var key in VirtualKeyCatalog.Keys)
        {
            choices.Add(key);
        }

        return choices;
    }

    private void RefreshQuickFpsSettingsUi()
    {
        if (!IsLoaded)
        {
            return;
        }

        var settings = QuickFpsService.Settings;

        _isRefreshingQuickFps = true;
        try
        {
            QuickFpsEnabledToggle.IsOn = settings.Enabled;
            QuickFpsGameOnlyToggle.IsOn = settings.ListenOnlyWhileGameRunning;
            QuickFpsSwitchToggle.IsOn = settings.SwitchPresetsEnabled;
            QuickFpsAdjustToggle.IsOn = settings.AdjustEnabled;
            QuickFpsStepBox.Value = settings.AdjustStep;
            QuickFpsIncreaseKeyBox.SelectedValue = settings.IncreaseKey;
            QuickFpsDecreaseKeyBox.SelectedValue = settings.DecreaseKey;

            var fpsBoxes = QuickFpsSlotFpsBoxes;
            var keyBoxes = QuickFpsSlotKeyBoxes;
            var labels = QuickFpsSlotLabels;
            for (var i = 0; i < QuickFpsSettings.SlotCount; i++)
            {
                labels[i].Text = string.Format(CultureInfo.CurrentCulture,
                    "QuickFpsSlotFormat".GetLocalized(), i + 1);
                fpsBoxes[i].Value = settings.SlotFps[i];
                keyBoxes[i].SelectedValue = settings.SlotKeys[i];
            }
        }
        finally
        {
            _isRefreshingQuickFps = false;
        }

        UpdateQuickFpsControlAvailability(settings.Enabled);
    }

    private void UpdateQuickFpsControlAvailability(bool enabled)
    {
        QuickFpsGameOnlyToggle.IsEnabled = enabled;
        QuickFpsSwitchToggle.IsEnabled = enabled;
        QuickFpsAdjustToggle.IsEnabled = enabled;
        QuickFpsStepBox.IsEnabled = enabled;
        QuickFpsIncreaseKeyBox.IsEnabled = enabled;
        QuickFpsDecreaseKeyBox.IsEnabled = enabled;

        var fpsBoxes = QuickFpsSlotFpsBoxes;
        var keyBoxes = QuickFpsSlotKeyBoxes;
        for (var i = 0; i < QuickFpsSettings.SlotCount; i++)
        {
            fpsBoxes[i].IsEnabled = enabled;
            keyBoxes[i].IsEnabled = enabled;
        }
    }

    private void OnQuickFpsEnabledToggled(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        UpdateQuickFpsControlAvailability(QuickFpsEnabledToggle.IsOn);
        _ = SaveQuickFpsSettingsAsync();
    }

    private void OnQuickFpsGameOnlyToggled(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        _ = SaveQuickFpsSettingsAsync();
    }

    private void OnQuickFpsSwitchToggled(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        _ = SaveQuickFpsSettingsAsync();
    }

    private void OnQuickFpsAdjustToggled(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        _ = SaveQuickFpsSettingsAsync();
    }

    private void OnQuickFpsStepChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        if (!double.IsFinite(args.NewValue))
        {
            RefreshQuickFpsSettingsUi();
            return;
        }

        var value = ToQuickFpsInt(args.NewValue, 10, 1);
        if (Math.Abs(args.NewValue - value) > 0.001)
        {
            _isRefreshingQuickFps = true;
            try
            {
                sender.Value = value;
            }
            finally
            {
                _isRefreshingQuickFps = false;
            }
        }

        _ = SaveQuickFpsSettingsAsync();
    }

    private void OnQuickFpsSlotFpsChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        if (!double.IsFinite(args.NewValue))
        {
            RefreshQuickFpsSettingsUi();
            return;
        }

        var value = ToQuickFpsInt(args.NewValue, 60, 1);
        if (Math.Abs(args.NewValue - value) > 0.001)
        {
            _isRefreshingQuickFps = true;
            try
            {
                sender.Value = value;
            }
            finally
            {
                _isRefreshingQuickFps = false;
            }
        }

        _ = SaveQuickFpsSettingsAsync();
    }

    private void OnQuickFpsIncreaseKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        _ = SaveQuickFpsSettingsAsync();
    }

    private void OnQuickFpsDecreaseKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        _ = SaveQuickFpsSettingsAsync();
    }

    private void OnQuickFpsSlotKeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingQuickFps)
        {
            return;
        }

        _ = SaveQuickFpsSettingsAsync();
    }

    private async Task SaveQuickFpsSettingsAsync()
    {
        if (!IsLoaded)
        {
            return;
        }

        var settings = new QuickFpsSettings
        {
            Enabled = QuickFpsEnabledToggle.IsOn,
            ListenOnlyWhileGameRunning = QuickFpsGameOnlyToggle.IsOn,
            SwitchPresetsEnabled = QuickFpsSwitchToggle.IsOn,
            AdjustEnabled = QuickFpsAdjustToggle.IsOn,
            AdjustStep = ToQuickFpsInt(QuickFpsStepBox.Value, 10, 1),
            IncreaseKey = ToQuickFpsKey(QuickFpsIncreaseKeyBox.SelectedValue, 0x26),
            DecreaseKey = ToQuickFpsKey(QuickFpsDecreaseKeyBox.SelectedValue, 0x28),
            SlotFps = new int[QuickFpsSettings.SlotCount],
            SlotKeys = new int[QuickFpsSettings.SlotCount]
        };

        var fpsBoxes = QuickFpsSlotFpsBoxes;
        var keyBoxes = QuickFpsSlotKeyBoxes;
        for (var i = 0; i < QuickFpsSettings.SlotCount; i++)
        {
            settings.SlotFps[i] = ToQuickFpsInt(fpsBoxes[i].Value, 60, 1);
            settings.SlotKeys[i] = ToQuickFpsKey(keyBoxes[i].SelectedValue, 0);
        }

        try
        {
            await QuickFpsService.UpdateSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsPage.QuickFps",
                $"[QuickFps] Failed to save launcher settings: {ex.Message}");
        }
    }

    private static int ToQuickFpsInt(double value, int fallback, int minimum)
    {
        if (!double.IsFinite(value))
        {
            return fallback;
        }

        return (int)Math.Clamp(Math.Round(value), minimum, QuickFpsSettings.MaxFps);
    }

    private static int ToQuickFpsKey(object? value, int fallback)
    {
        var key = value switch
        {
            int number => number,
            double number when double.IsFinite(number) => (int)Math.Round(number),
            _ => -1
        };

        return key is >= 0 and < 256 ? key : fallback;
    }
}
