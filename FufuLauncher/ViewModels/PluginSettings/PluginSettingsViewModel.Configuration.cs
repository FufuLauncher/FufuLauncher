/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel
{
    #region 配置加载

    public void LoadConfiguration()
    {
        if (_deferConfigurationLoading)
        {
            RequestConfigurationLoad();
            return;
        }

        LoadConfigurationSynchronously();
    }

    private int _contentTranslationVersion;

    private void LoadConfigurationSynchronously()
    {
        Settings.Clear();
        PinnedSettings.Clear();
        _settingOrder.Clear();
        _settingOrderIndexes.Clear();
        NotifySelectionChanged();

        Interlocked.Increment(ref _contentTranslationVersion);

        if (!File.Exists(_iniPath))
        {
            if (SelectedPluginIndex == 0)
            {
                PluginName = IsLightweightMode
                    ? "LightweightMode_LiteNotInstalled".GetLocalized()
                    : "PluginSettings_FuFuPluginNotInstalled".GetLocalized();
            }
            else
            {
                PluginName = "PluginSettings_FpsUnlockNotInstalled".GetLocalized();
            }

            PluginDescription = "PluginSettings_EnsurePluginDirConfigHint".GetLocalized();
            AvailablePresets.Clear();
            CurrentPreset = null;
            return;
        }

        try
        {
            var fileInfo = new FileInfo(_iniPath);
            LastModifiedDate = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

            var configData = _iniFile.ReadAll();
            if (configData.TryGetValue("General", out var generalSection))
            {
                PluginName = generalSection.GetValueOrDefault("Name", "PluginSettings_UnknownPlugin".GetLocalized());
                PluginDescription =
                    generalSection.GetValueOrDefault("Description", "PluginSettings_NoDescription".GetLocalized());
                PluginDeveloper =
                    generalSection.GetValueOrDefault("Developer", "PluginSettings_UnknownDeveloper".GetLocalized());
            }

            ManagePresets(configData);

            var restoredSettingNames = new List<string>();
            bool isDevZone = false;
            foreach (var section in _iniFile.ReadAll())
            {
                if (section.Key.Equals("General", StringComparison.OrdinalIgnoreCase)) continue;

                if (section.Key.Equals("DEV", StringComparison.OrdinalIgnoreCase))
                {
                    isDevZone = true;
                    continue;
                }

                if (isDevZone && !_isDevFeaturesEnabled)
                {
                    continue;
                }

                var dic = section.Value;
                var iniName = dic.GetValueOrDefault("Name", section.Key);
                var name = ResolveLocalizedSettingName(section.Key, iniName);

                var type = dic.GetValueOrDefault("Type", "string");
                var value = dic.GetValueOrDefault("Value", "");
                var help = dic.GetValueOrDefault("help", "");

                var settingItem = new PluginSettingItem(_iniFile, section.Key, name, type, value, help,
                    OnSettingValueChanged, UseKeyListInput);

                if (ApplySettingDisableGuard(settingItem))
                {
                    restoredSettingNames.Add(settingItem.DisplayName);
                }

                _settingOrderIndexes[section.Key] = _settingOrder.Count;
                _settingOrder.Add(section.Key);

                if (IsSettingPinned(section.Key))
                {
                    settingItem.IsPinned = true;
                    PinnedSettings.Add(settingItem);
                }
                else
                {
                    Settings.Add(settingItem);
                }
            }

            NotifySettingDisablesRestored(restoredSettingNames);
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "PluginSettings_ConfigReadFailedTitle".GetLocalized(),
                string.Format("PluginSettings_ReadConfigFailedDetail_Format".GetLocalized(), ex.Message),
                NotificationType.Error,
                6000
            ));
        }
    }

    private string ResolveLocalizedSettingName(string sectionKey, string iniName)
    {
        if (SelectedPluginIndex != 0)
        {
            return iniName;
        }

        var localizationKey = $"Plugin_{sectionKey}";
        var localizedName = localizationKey.GetLocalized();
        return localizedName != localizationKey ? localizedName : iniName;
    }

    private void QueueContentTranslation(IReadOnlyList<SettingTranslationRequest> requests, string? rawDescription)
    {
        if (!PluginContentTranslationService.Instance.ShouldTranslate)
        {
            return;
        }

        if (requests.Count == 0 && string.IsNullOrWhiteSpace(rawDescription))
        {
            return;
        }

        var version = Volatile.Read(ref _contentTranslationVersion);
        _ = RunContentTranslationAsync(version, requests, rawDescription);
    }

    private async Task RunContentTranslationAsync(int version,
        IReadOnlyList<SettingTranslationRequest> requests, string? rawDescription)
    {
        var translator = PluginContentTranslationService.Instance;

        try
        {
            if (!string.IsNullOrWhiteSpace(rawDescription) &&
                !translator.TryGetCachedTranslation(rawDescription, out _))
            {
                var translatedDescription = await translator.TranslateAsync(rawDescription, rawDescription);
                if (version != Volatile.Read(ref _contentTranslationVersion))
                {
                    return;
                }

                PluginContentTranslationService.RunOnUiThread(() => PluginDescription = translatedDescription);
            }

            foreach (var request in requests)
            {
                if (version != Volatile.Read(ref _contentTranslationVersion))
                {
                    return;
                }

                var translated = await translator.TranslateAsync(request.RawName, request.FallbackName);
                if (version != Volatile.Read(ref _contentTranslationVersion))
                {
                    return;
                }

                PluginContentTranslationService.RunOnUiThread(
                    () => request.Item.ApplyTranslatedDisplayName(translated));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSettingsViewModel.Configuration",
                $"[PluginSettings] Content translation failed: {ex.Message}");
        }
    }

    private sealed record SettingTranslationRequest(PluginSettingItem Item, string RawName, string FallbackName);

    #endregion
}