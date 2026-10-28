/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.Services
{
    public class ConstraintService
    {
        public const string ActiveSettingKey = "ConstraintActive";
        public const string ForcedSettingKey = "ConstraintForced";
        public const string PreviousLightweightModeKey = "ConstraintPreviousLightweightMode";
        public const string IgnoreSettingKey = "IgnoreConstraintRestrictions";

        private const int RequestTimeoutSeconds = 15;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds) };

        private readonly ILocalSettingsService _localSettings;
        private readonly LightweightPluginService _lightweightPlugin;
        private readonly IPluginUpdateService _pluginUpdateService;
        private readonly SemaphoreSlim _refreshGate = new(1, 1);
        private readonly object _stateLock = new();

        private bool _policyActive;
        private bool _forced;
        private bool _ignoreRestrictions;
        private string _dialogText = string.Empty;

        public ConstraintService(
            ILocalSettingsService localSettings,
            LightweightPluginService lightweightPlugin,
            IPluginUpdateService pluginUpdateService)
        {
            _localSettings = localSettings;
            _lightweightPlugin = lightweightPlugin;
            _pluginUpdateService = pluginUpdateService;
        }

        public bool IsRestricted
        {
            get
            {
                lock (_stateLock)
                {
                    return _policyActive && !_ignoreRestrictions;
                }
            }
        }

        public bool IgnoreRestrictions
        {
            get
            {
                lock (_stateLock)
                {
                    return _ignoreRestrictions;
                }
            }
        }

        public string DialogText
        {
            get
            {
                lock (_stateLock)
                {
                    return _dialogText;
                }
            }
        }

        public string BlockMessage =>
            string.IsNullOrEmpty(DialogText) ? "Constraint_BlockedDefault".GetLocalized() : DialogText;

        public async Task InitializeAsync()
        {
            bool active = false;
            bool forced = false;
            bool ignore = false;

            try
            {
                var activeStored = await _localSettings.ReadSettingAsync(ActiveSettingKey);
                active = activeStored != null && Convert.ToBoolean(activeStored);

                var forcedStored = await _localSettings.ReadSettingAsync(ForcedSettingKey);
                forced = forcedStored != null && Convert.ToBoolean(forcedStored);

                var ignoreStored = await _localSettings.ReadSettingAsync(IgnoreSettingKey);
                ignore = ignoreStored != null && Convert.ToBoolean(ignoreStored);
            }
            catch (Exception ex)
            {
                DebugLog.Info("ConstraintService",
                    $"[PolicyConstraint] Failed to read restriction state: {ex.Message}");
            }

            lock (_stateLock)
            {
                _policyActive = active;
                _forced = forced;
                _ignoreRestrictions = ignore;
            }

            if (IsRestricted)
            {
                await MarkForcedAsync();
                await ApplyRestrictionAsync(installIfMissing: false);
                return;
            }

            if (forced && await RestoreForcedModeAsync(reinstallMainPlugin: false))
            {
                _ = Task.Run(async () => await _pluginUpdateService.InstallOrUpdateMainPluginAsync());
            }
        }

        public void TriggerBackgroundRefresh()
        {
            _ = Task.Run(RefreshAsync);
        }

        public async Task RefreshAsync()
        {
            if (!await _refreshGate.WaitAsync(0))
            {
                return;
            }

            try
            {
                var status = await FetchStatusAsync();
                if (status == null) return;

                await ApplyAsync(status.Value);
            }
            catch (Exception ex)
            {
                DebugLog.Info("ConstraintService", $"[PolicyConstraint] State refresh failed: {ex.Message}");
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public async Task SetIgnoreRestrictionsAsync(bool value)
        {
            bool wasRestricted = IsRestricted;

            lock (_stateLock)
            {
                if (_ignoreRestrictions == value) return;
                _ignoreRestrictions = value;
            }

            await _localSettings.SaveSettingAsync(IgnoreSettingKey, value);

            if (value)
            {
                if (IsForced)
                {
                    await RestoreForcedModeAsync(reinstallMainPlugin: true);
                }

                if (wasRestricted)
                {
                    NotifyStateChanged();
                }

                return;
            }

            if (IsPolicyActive)
            {
                await MarkForcedAsync();
                await ApplyRestrictionAsync(installIfMissing: true);
                await GetBlockMessageAsync();
                NotifyStateChanged();
            }

            await RefreshAsync();
        }

        public async Task<string> GetBlockMessageAsync()
        {
            if (string.IsNullOrEmpty(DialogText))
            {
                var text = await FetchDialogTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    lock (_stateLock)
                    {
                        _dialogText = text;
                    }
                }
            }

            return BlockMessage;
        }

        private bool IsForced
        {
            get
            {
                lock (_stateLock)
                {
                    return _forced;
                }
            }
        }

        private bool IsPolicyActive
        {
            get
            {
                lock (_stateLock)
                {
                    return _policyActive;
                }
            }
        }

        private async Task ApplyAsync(bool status)
        {
            bool wasRestricted = IsRestricted;

            if (status)
            {
                if (PersistPolicyActive(true))
                {
                    await _localSettings.SaveSettingAsync(ActiveSettingKey, true);
                }

                if (IgnoreRestrictions) return;

                await MarkForcedAsync();
                await ApplyRestrictionAsync(installIfMissing: true);
                await GetBlockMessageAsync();

                if (wasRestricted) return;

                NotifyStateChanged();
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "Constraint_BlockedTitle".GetLocalized(),
                    "Constraint_AutoApplied".GetLocalized(),
                    NotificationType.Warning,
                    6000));
                return;
            }

            if (PersistPolicyActive(false))
            {
                await _localSettings.SaveSettingAsync(ActiveSettingKey, false);
            }

            if (IsForced)
            {
                await RestoreForcedModeAsync(reinstallMainPlugin: true);
            }

            if (wasRestricted)
            {
                NotifyStateChanged();
            }
        }

        private bool PersistPolicyActive(bool value)
        {
            lock (_stateLock)
            {
                if (_policyActive == value) return false;
                _policyActive = value;
                return true;
            }
        }

        private async Task MarkForcedAsync()
        {
            if (IsForced) return;

            await _localSettings.SaveSettingAsync(PreviousLightweightModeKey, _lightweightPlugin.IsLightweightMode);
            await _localSettings.SaveSettingAsync(ForcedSettingKey, true);

            lock (_stateLock)
            {
                _forced = true;
            }
        }

        private async Task<bool> RestoreForcedModeAsync(bool reinstallMainPlugin)
        {
            bool previousWasLightweight = false;

            try
            {
                var stored = await _localSettings.ReadSettingAsync(PreviousLightweightModeKey);
                previousWasLightweight = stored != null && Convert.ToBoolean(stored);
            }
            catch (Exception ex)
            {
                DebugLog.Info("ConstraintService", $"[PolicyConstraint] Failed to read historical mode: {ex.Message}");
            }

            bool restoredStandardMode = false;

            if (!previousWasLightweight)
            {
                restoredStandardMode = true;

                await _lightweightPlugin.SetLightweightModeAsync(false);
                LightweightPluginService.RemoveOrDisableLitePlugin();

                bool installed = reinstallMainPlugin && await _pluginUpdateService.InstallOrUpdateMainPluginAsync();

                if (!installed)
                {
                    LightweightPluginService.TryEnableMainPlugin();
                }
            }

            await _localSettings.SaveSettingAsync(ForcedSettingKey, false);

            lock (_stateLock)
            {
                _forced = false;
            }

            return restoredStandardMode;
        }

        private async Task ApplyRestrictionAsync(bool installIfMissing)
        {
            try
            {
                if (!File.Exists(LightweightPluginService.LitePluginDllPath) &&
                    !LightweightPluginService.TryEnableLitePlugin() &&
                    !LightweightPluginService.IsLitePluginInstalled &&
                    installIfMissing)
                {
                    await _lightweightPlugin.InstallOrUpdateLitePluginAsync();
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("ConstraintService",
                    $"[PolicyConstraint] Failed to auto-deploy lightweight plugin: {ex.Message}");
            }

            LightweightPluginService.DisableMainPluginIfPresent();

            if (!_lightweightPlugin.IsLightweightMode)
            {
                await _lightweightPlugin.SetLightweightModeAsync(true);
            }
        }

        private void NotifyStateChanged()
        {
            WeakReferenceMessenger.Default.Send(new ConstraintStateChangedMessage(IsRestricted));
        }

        private static async Task<bool?> FetchStatusAsync()
        {
            var json = await FetchJsonAsync(ApiEndpoints.ConstraintStatusUrl);
            return string.IsNullOrEmpty(json) ? null : ParseStatus(json);
        }

        private static async Task<string> FetchDialogTextAsync()
        {
            var json = await FetchJsonAsync(ApiEndpoints.ConstraintDialogUrl);
            return string.IsNullOrEmpty(json) ? string.Empty : ParseDialogText(json);
        }

        private static async Task<string> FetchJsonAsync(string url)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("FufuLauncher");
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                using var response = await Http.SendAsync(request);
                if (!response.IsSuccessStatusCode) return string.Empty;

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                DebugLog.Info("ConstraintService", $"[PolicyConstraint] Request failed {url}: {ex.Message}");
                return string.Empty;
            }
        }

        private static bool? ParseStatus(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                if (!root.TryGetProperty("Status", out var status) &&
                    !root.TryGetProperty("status", out status))
                {
                    return null;
                }

                return status.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(status.GetString(), out var parsed) && parsed,
                    JsonValueKind.Number => status.TryGetInt32(out var number) && number != 0,
                    _ => false
                };
            }
            catch (Exception ex)
            {
                DebugLog.Info("ConstraintService", $"[PolicyConstraint] Failed to parse state: {ex.Message}");
                return null;
            }
        }

        private static string ParseDialogText(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("text", out var textElement)) return string.Empty;

                var text = textElement.ValueKind == JsonValueKind.String
                    ? textElement.GetString()?.Trim()
                    : null;

                if (string.IsNullOrEmpty(text) || text.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                return text;
            }
            catch (Exception ex)
            {
                DebugLog.Info("ConstraintService", $"[PolicyConstraint] Failed to parse prompt content: {ex.Message}");
                return string.Empty;
            }
        }
    }
}