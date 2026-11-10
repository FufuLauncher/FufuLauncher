/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class QuickFpsConfigurationViewModel : ObservableObject
{
    private readonly QuickFpsHotkeyService _quickFpsService;
    private bool _isApplyingSettings = true;

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _listenOnlyWhileGameRunning = true;
    [ObservableProperty] private bool _switchPresetsEnabled = true;
    [ObservableProperty] private bool _adjustEnabled = true;
    [ObservableProperty] private double _adjustStep = 10;
    [ObservableProperty] private VirtualKeyOption? _selectedIncreaseKey;
    [ObservableProperty] private VirtualKeyOption? _selectedDecreaseKey;
    [ObservableProperty] private bool _isReady;

    public QuickFpsConfigurationViewModel(QuickFpsHotkeyService quickFpsService)
    {
        _quickFpsService = quickFpsService;

        for (var i = 0; i < QuickFpsSettings.SlotCount; i++)
        {
            var slot = new QuickFpsSlotItem(i + 1, KeyChoices);
            slot.PropertyChanged += OnSlotPropertyChanged;
            Slots.Add(slot);
        }

        WeakReferenceMessenger.Default.Register<QuickFpsSettingsChangedMessage>(this, (_, message) =>
            Dispatch(() => ApplySettings(message.Value)));
    }

    public ObservableCollection<VirtualKeyOption> KeyChoices
    {
        get;
    } = VirtualKeyCatalog.CreateQuickFpsOptions();

    public ObservableCollection<QuickFpsSlotItem> Slots
    {
        get;
    } = new();

    public QuickFpsSlotItem Slot1 => Slots[0];

    public QuickFpsSlotItem Slot2 => Slots[1];

    public QuickFpsSlotItem Slot3 => Slots[2];

    public QuickFpsSlotItem Slot4 => Slots[3];

    public QuickFpsSlotItem Slot5 => Slots[4];

    public void Load() => ApplySettings(_quickFpsService.Settings);

    private void ApplySettings(QuickFpsSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            IsEnabled = settings.Enabled;
            ListenOnlyWhileGameRunning = settings.ListenOnlyWhileGameRunning;
            SwitchPresetsEnabled = settings.SwitchPresetsEnabled;
            AdjustEnabled = settings.AdjustEnabled;
            AdjustStep = settings.AdjustStep;
            SelectedIncreaseKey = FindKeyOption(settings.IncreaseKey);
            SelectedDecreaseKey = FindKeyOption(settings.DecreaseKey);

            for (var i = 0; i < Slots.Count && i < QuickFpsSettings.SlotCount; i++)
            {
                Slots[i].Fps = settings.SlotFps[i];
                Slots[i].SelectedKey = FindKeyOption(settings.SlotKeys[i]);
            }
        }
        finally
        {
            _isApplyingSettings = false;
        }

        IsReady = true;
    }

    private VirtualKeyOption? FindKeyOption(int keyCode) =>
        KeyChoices.FirstOrDefault(option => option.KeyCode == keyCode);

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(QuickFpsSlotItem.Fps) or nameof(QuickFpsSlotItem.SelectedKey)))
        {
            return;
        }

        if (e.PropertyName == nameof(QuickFpsSlotItem.Fps)
            && sender is QuickFpsSlotItem slot
            && !double.IsFinite(slot.Fps))
        {
            RevertToStoredSettings();
            return;
        }

        if (e.PropertyName == nameof(QuickFpsSlotItem.SelectedKey)
            && sender is QuickFpsSlotItem { SelectedKey: null })
        {
            return;
        }

        _ = SaveAsync();
    }

    private void RevertToStoredSettings()
    {
        if (_isApplyingSettings)
        {
            return;
        }

        ApplySettings(_quickFpsService.Settings);
    }

    partial void OnIsEnabledChanged(bool value) => _ = SaveAsync();

    partial void OnListenOnlyWhileGameRunningChanged(bool value) => _ = SaveAsync();

    partial void OnSwitchPresetsEnabledChanged(bool value) => _ = SaveAsync();

    partial void OnAdjustEnabledChanged(bool value) => _ = SaveAsync();

    partial void OnAdjustStepChanged(double value)
    {
        if (!double.IsFinite(value))
        {
            RevertToStoredSettings();
            return;
        }

        _ = SaveAsync();
    }

    partial void OnSelectedIncreaseKeyChanged(VirtualKeyOption? value) => _ = SaveAsync();

    partial void OnSelectedDecreaseKeyChanged(VirtualKeyOption? value) => _ = SaveAsync();

    private async Task SaveAsync()
    {
        if (_isApplyingSettings || !IsReady)
        {
            return;
        }

        var settings = new QuickFpsSettings
        {
            Enabled = IsEnabled,
            ListenOnlyWhileGameRunning = ListenOnlyWhileGameRunning,
            SwitchPresetsEnabled = SwitchPresetsEnabled,
            AdjustEnabled = AdjustEnabled,
            AdjustStep = ToInt(AdjustStep, 10, 1),
            IncreaseKey = ToKey(SelectedIncreaseKey, 0x26),
            DecreaseKey = ToKey(SelectedDecreaseKey, 0x28),
            SlotFps = new int[QuickFpsSettings.SlotCount],
            SlotKeys = new int[QuickFpsSettings.SlotCount]
        };

        for (var i = 0; i < QuickFpsSettings.SlotCount && i < Slots.Count; i++)
        {
            settings.SlotFps[i] = ToInt(Slots[i].Fps, 60, 1);
            settings.SlotKeys[i] = Slots[i].SelectedKey?.KeyCode ?? 0;
        }

        try
        {
            await _quickFpsService.UpdateSettingsAsync(settings);
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsConfigurationViewModel", $"[QuickFps] Failed to save settings: {ex.Message}");
        }
    }

    private static int ToInt(double value, int fallback, int minimum)
    {
        if (!double.IsFinite(value))
        {
            return fallback;
        }

        return (int)Math.Clamp(Math.Round(value), minimum, QuickFpsSettings.MaxFps);
    }

    private static int ToKey(VirtualKeyOption? option, int fallback)
    {
        var key = option?.KeyCode ?? -1;
        return key is >= 0 and < 256 ? key : fallback;
    }

    private static void Dispatch(Action action)
    {
        var dispatcherQueue = App.MainWindow?.DispatcherQueue;
        if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcherQueue.TryEnqueue(() => action());
    }
}
