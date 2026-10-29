/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Views;
using Microsoft.UI.Dispatching;

namespace FufuLauncher.ViewModels;

public enum PluginInstallOutcome
{
    Running,
    Succeeded,
    Cancelled,
    Failed
}

public partial class PluginStoreViewModel
{
    #region Operation Window

    private PluginInstallWindow? _operationWindow;
    private PluginInstallOutcome _operationOutcome = PluginInstallOutcome.Running;

    private void BeginOperation(PluginStoreItem item, bool isUninstall)
    {
        _operationOutcome = PluginInstallOutcome.Running;
        IsStoreBusy = true;

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? App.MainWindow?.DispatcherQueue ?? _dispatcher;
        if (dispatcherQueue == null)
        {
            DebugLog.Info("PluginStoreViewModel.OperationWindow",
                "[PluginStoreVM] No dispatcher available to open the operation window");
            return;
        }

        if (dispatcherQueue.HasThreadAccess)
        {
            CreateOperationWindow(item, isUninstall);
        }
        else
        {
            dispatcherQueue.TryEnqueue(() => CreateOperationWindow(item, isUninstall));
        }
    }

    private void CreateOperationWindow(PluginStoreItem item, bool isUninstall)
    {
        if (!IsStoreBusy) return;

        CloseStaleOperationWindow();

        try
        {
            var window = new PluginInstallWindow(item, _luaInstaller, isUninstall, () => CancelInstall(item));
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_operationWindow, window))
                {
                    _operationWindow = null;
                }
            };

            _operationWindow = window;
            window.Activate();

            DebugLog.Info("PluginStoreViewModel.OperationWindow",
                $"[PluginStoreVM] Operation window opened for '{item.Id}' (uninstall: {isUninstall})");
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreViewModel.OperationWindow",
                $"[PluginStoreVM] Failed to open the operation window: {ex}");
        }
    }

    private void CloseStaleOperationWindow()
    {
        var staleWindow = _operationWindow;
        _operationWindow = null;

        if (staleWindow == null) return;

        try
        {
            staleWindow.Close();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreViewModel.OperationWindow",
                $"[PluginStoreVM] Failed to close the previous operation window: {ex.Message}");
        }
    }

    private void FinishOperation(PluginStoreItem item)
    {
        var outcome = _operationOutcome;
        _operationOutcome = PluginInstallOutcome.Running;

        IsStoreBusy = false;

        var window = _operationWindow;
        if (window == null) return;

        try
        {
            window.NotifyFinished(outcome, item.InstallStatusText, StatusMessage);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreViewModel.OperationWindow",
                $"[PluginStoreVM] Failed to notify the operation window: {ex.Message}");
        }
    }

    #endregion
}
