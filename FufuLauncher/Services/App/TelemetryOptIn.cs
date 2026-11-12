/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services
{
    public static class TelemetryOptIn
    {
        public const string ConfirmedKey = LocalSettingsService.IsTelemetryOptInConfirmedKey;

        public static async Task<bool> ResolveEnabledAsync(ILocalSettingsService settings)
        {
            try
            {
                if (!IsOn(await settings.ReadSettingAsync(LocalSettingsService.IsTelemetryEnabledKey)))
                {
                    return false;
                }

                if (IsOn(await settings.ReadSettingAsync(ConfirmedKey)))
                {
                    return true;
                }

                await settings.TrySaveSettingAsync(LocalSettingsService.IsTelemetryEnabledKey, false);
                DebugLog.Info("TelemetryOptIn",
                    "[Telemetry] Enabled without a manual opt-in, disabled silently");
                return false;
            }
            catch (Exception ex)
            {
                DebugLog.Info("TelemetryOptIn", $"[Telemetry] Opt-in guard failed: {ex.Message}");
                return false;
            }
        }

        public static async Task SaveUserChoiceAsync(ILocalSettingsService settings, bool enabled)
        {
            try
            {
                await settings.TrySaveSettingAsync(LocalSettingsService.IsTelemetryEnabledKey, enabled);

                if (!enabled) return;

                await settings.TrySaveSettingAsync(ConfirmedKey, true);
            }
            catch (Exception ex)
            {
                DebugLog.Info("TelemetryOptIn", $"[Telemetry] Failed to save telemetry choice: {ex.Message}");
            }
        }

        private static bool IsOn(object? stored)
        {
            try
            {
                return stored != null && Convert.ToBoolean(stored);
            }
            catch
            {
                return false;
            }
        }
    }
}
