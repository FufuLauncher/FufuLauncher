/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using Microsoft.Win32;

namespace FufuLauncher.Helpers;

public static class StartupManager
{
    public const string RegistryValueName = "FufuLauncher";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    public static bool IsRegistered()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return runKey?.GetValue(RegistryValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            DebugLog.Info("StartupManager", $"[StartupManager] Failed to read autostart registry entry: {ex.Message}");
            return false;
        }
    }

    public static bool TryApply(bool enabled, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            using var runKey = OpenRunKey(writable: true);
            if (runKey is null)
            {
                errorMessage = "Startup_ErrOpenRegistryKey".GetLocalized();
                return false;
            }

            if (enabled)
            {
                var command = BuildCommand();
                if (command is null)
                {
                    errorMessage = "Startup_ErrProcessPathUnavailable".GetLocalized();
                    return false;
                }

                runKey.SetValue(RegistryValueName, command, RegistryValueKind.String);
                ClearTaskManagerDisabledFlag();
            }
            else
            {
                runKey.DeleteValue(RegistryValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("StartupManager", $"[StartupManager] Failed to write autostart entry: {ex.Message}");
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool ResolveEnabledState(bool requestedEnabled)
    {
        if (IsDisabledByTaskManager())
        {
            return false;
        }

        if (IsRegistered())
        {
            RefreshExecutablePath();
            return true;
        }

        return requestedEnabled && TryApply(true, out _);
    }

    private static void RefreshExecutablePath()
    {
        try
        {
            var command = BuildCommand();
            if (command is null)
            {
                return;
            }

            using var runKey = OpenRunKey(writable: true);
            if (runKey?.GetValue(RegistryValueName) is not string)
            {
                return;
            }

            runKey.SetValue(RegistryValueName, command, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            DebugLog.Info("StartupManager", $"[StartupManager] Failed to refresh autostart path: {ex.Message}");
        }
    }

    private static RegistryKey? OpenRunKey(bool writable)
    {
        var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable);
        return runKey ?? (writable ? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true) : null);
    }

    private static string? BuildCommand()
    {
        var exePath = Environment.ProcessPath;
        return string.IsNullOrEmpty(exePath) ? null : $"\"{exePath}\"";
    }

    private static bool IsDisabledByTaskManager()
    {
        try
        {
            using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath);
            if (approvedKey?.GetValue(RegistryValueName) is not byte[] data || data.Length == 0)
            {
                return false;
            }

            return (data[0] & 1) != 0;
        }
        catch (Exception ex)
        {
            DebugLog.Info("StartupManager", $"[StartupManager] Failed to read startup approval status: {ex.Message}");
            return false;
        }
    }

    private static void ClearTaskManagerDisabledFlag()
    {
        try
        {
            using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true);
            approvedKey?.DeleteValue(RegistryValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            DebugLog.Info("StartupManager", $"[StartupManager] Failed to clear startup disabled flag: {ex.Message}");
        }
    }
}