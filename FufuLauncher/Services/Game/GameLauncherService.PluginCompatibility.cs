/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    private void DisableIncompatiblePlugins(LaunchFlowLog logBuilder)
    {
        try
        {
            foreach (var enforcement in _pluginGameVersionGuard.EnforceAll())
            {
                logBuilder.AppendLine(
                    "[LaunchFlow] Plugin '" + enforcement.PluginName + "' disabled: game version '" +
                    enforcement.Status.GameVersion + "' not supported (rule '" +
                    enforcement.Status.DeclaredRule + "')");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameLauncherService.PluginCompatibility",
                "[LaunchFlow] Plugin game version check failed: " + ex.Message);
            logBuilder.AppendLine("[LaunchFlow] Plugin game version check failed: " + ex.Message);
        }
    }
}
