/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private ContentDialog? _settingDisableWarningDialog;

    private async void OnSettingBoolToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggleSwitch || toggleSwitch.DataContext is not PluginSettingItem item ||
            toggleSwitch.IsOn == item.BoolValue)
        {
            return;
        }

        var requestedValue = toggleSwitch.IsOn;
        if (!IsLoaded || !ViewModel.IsSettingsInteractable || !IsCurrentSetting(item) ||
            _settingDisableWarningDialog != null)
        {
            toggleSwitch.IsOn = item.BoolValue;
            return;
        }

        if (!requestedValue && RequiresSettingDisableWarning(item))
        {
            toggleSwitch.IsOn = item.BoolValue;
            if (!await ConfirmSettingDisableAsync(new[] { item })) return;
        }

        if (IsLoaded && ViewModel.IsSettingsInteractable && IsCurrentSetting(item))
        {
            item.BoolValue = requestedValue;
        }
        
        if (ReferenceEquals(toggleSwitch.DataContext, item))
        {
            toggleSwitch.IsOn = item.BoolValue;
        }
    }

    private bool IsCurrentSetting(PluginSettingItem item) =>
        ViewModel.Settings.Contains(item) || ViewModel.PinnedSettings.Contains(item);

    private bool IsResinRewardSetting(PluginSettingItem item) =>
        ViewModel.SelectedPluginIndex == 0 &&
        string.Equals(item.Type, "bool", StringComparison.OrdinalIgnoreCase) &&
        item.SectionKey.StartsWith("ResinItem", StringComparison.OrdinalIgnoreCase);

    private bool IsPreventDetectionPopupSetting(PluginSettingItem item) =>
        ViewModel.SelectedPluginIndex == 0 &&
        string.Equals(item.Type, "bool", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(item.SectionKey, "PreventDetectionPopup", StringComparison.OrdinalIgnoreCase);

    private bool RequiresSettingDisableWarning(PluginSettingItem item) =>
        IsResinRewardSetting(item) || IsPreventDetectionPopupSetting(item);

    private object CreateSettingDisableWarningContent(IReadOnlyList<PluginSettingItem> settings)
    {
        if (!settings.Any(IsPreventDetectionPopupSetting))
        {
            return string.Join(Environment.NewLine + Environment.NewLine,
                settings.Select(GetResinRewardDisableMessage));
        }

        var panel = new StackPanel { Spacing = 12 };
        foreach (var item in settings)
        {
            var isDetectionPopup = IsPreventDetectionPopupSetting(item);
            var warning = new TextBlock
            {
                Text = isDetectionPopup
                    ? string.Format("Plugin_PreventDetectionPopupDisable_Content".GetLocalized(), item.DisplayName)
                    : GetResinRewardDisableMessage(item),
                TextWrapping = TextWrapping.Wrap
            };

            if (isDetectionPopup)
            {
                warning.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
            }

            panel.Children.Add(warning);
        }

        return panel;
    }

    private async Task<bool> ConfirmSettingDisableAsync(IReadOnlyList<PluginSettingItem> settings)
    {
        var warningSettings = settings.Where(item => item.BoolValue && RequiresSettingDisableWarning(item)).ToArray();
        if (warningSettings.Length == 0) return true;
        if (!IsLoaded || XamlRoot == null || _settingDisableWarningDialog != null) return false;

        var dialog = new ContentDialog
        {
            Title = warningSettings.Any(IsPreventDetectionPopupSetting)
                ? "Plugin_PreventDetectionPopupDisable_Title".GetLocalized()
                : "Plugin_ResinRewardDisable_Title".GetLocalized(),
            Content = CreateSettingDisableWarningContent(warningSettings),
            PrimaryButtonText = "Plugin_ResinRewardDisable_Confirm".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        _settingDisableWarningDialog = dialog;
        try
        {
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary && IsLoaded &&
                ViewModel.IsSettingsInteractable && settings.All(IsCurrentSetting);
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "ErrorTitle".GetLocalized(),
                string.Format("Err_OperationFailed_Format".GetLocalized(), ex.Message),
                NotificationType.Error,
                6000));
            return false;
        }
        finally
        {
            _settingDisableWarningDialog = null;
        }
    }

    private static string GetResinRewardDisableMessage(PluginSettingItem item)
    {
        const string prefix = "使用";
        const string suffix = "领取奖励";
        var rewardName = item.DisplayName;
        if (rewardName.StartsWith(prefix, StringComparison.Ordinal))
        {
            var suffixIndex = rewardName.IndexOf(suffix, prefix.Length, StringComparison.Ordinal);
            if (suffixIndex > prefix.Length)
            {
                rewardName = rewardName[prefix.Length..suffixIndex].Trim();
            }
        }

        return string.Format("Plugin_ResinRewardDisable_Content".GetLocalized(), rewardName);
    }
}
