/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;

namespace FufuLauncher.Helpers;

public static class AppRestartHelper
{
    private const string RestartArgument = "restart";


    public static bool TryRestart(bool asAdministrator = false)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                DebugLog.Info("AppRestartHelper", "Restart failed: cannot get current process path");
                return false;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = RestartArgument,
                    Verb = asAdministrator ? "runas" : string.Empty,
                    UseShellExecute = true
                }
            };
            process.Start();

            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("AppRestartHelper", $"Restart failed: {ex.Message}");
            return false;
        }
    }
}