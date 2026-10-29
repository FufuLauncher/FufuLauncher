/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class PluginStoreViewModel
{
    #region Plugin Uninstall & Local State

    private async Task UninstallPluginAsync(PluginStoreItem item)
    {
        if (item == null || item.IsInstallInProgress || _installCts != null || IsStoreBusy) return;

        var outcome = PluginInstallOutcome.Running;

        try
        {
            _installingPluginIds.Add(item.Id);

            item.IsInstallInProgress = true;
            item.State = StorePluginState.Installing;
            item.InstallProgressPercent = 0;
            item.InstallStatusText = "PluginStoreUninstalling".GetLocalized();

            BeginOperation(item, true);

            if (!string.IsNullOrEmpty(item.LuaUninstallUrl))
            {
                var maxCaptchaRetries = 3;
                var attempt = 0;
                var luaSuccess = false;

                while (attempt < maxCaptchaRetries)
                {
                    try
                    {
                        item.InstallStatusText = attempt > 0
                            ? "PluginStoreRetrying".GetLocalized()
                            : "PluginStoreUninstalling".GetLocalized();

                        var uninstallUrl = AppendTokenToUrl(item.LuaUninstallUrl, item.AccessToken);
                        await _luaInstaller.ExecuteInstallScriptAsync(
                            uninstallUrl,
                            expectedLuaHash: null,
                            expectedFileHash: null,
                            cancellationToken: CancellationToken.None,
                            dllFileName: null,
                            pluginId: item.Id,
                            dlToken: item.DlToken,
                            accessToken: item.AccessToken);

                        luaSuccess = true;
                        break;
                    }
                    catch (CaptchaRequiredException captchaEx)
                    {
                        DebugLog.Info("PluginStoreViewModel.Uninstall",
                            $"[PluginStoreVM] Uninstall captcha required: {captchaEx.VerifyUrl}");
                        item.InstallStatusText = "PluginStoreCaptchaRequired".GetLocalized();

                        var dlToken = await ShowGeetestCaptchaAsync(captchaEx.VerifyUrl);

                        if (string.IsNullOrWhiteSpace(dlToken))
                        {
                            DebugLog.Info("PluginStoreViewModel.Uninstall",
                                "[PluginStoreVM] Uninstall captcha cancelled, falling back to directory delete");
                            break;
                        }

                        item.DlToken = dlToken;
                        attempt++;
                        DebugLog.Info("PluginStoreViewModel.Uninstall",
                            $"[PluginStoreVM] Uninstall: got dl_token, retrying (attempt {attempt})...");
                    }
                    catch (PrivatePluginAccessException privEx)
                    {
                        DebugLog.Info("PluginStoreViewModel.Uninstall",
                            $"[PluginStoreVM] Uninstall private access required: {privEx.Message}");
                        item.InstallStatusText = "PluginStorePrivateAccessRequired".GetLocalized();

                        var accessKey = await ShowPrivateAccessDialogAsync(item);
                        if (string.IsNullOrWhiteSpace(accessKey))
                        {
                            DebugLog.Info("PluginStoreViewModel.Uninstall",
                                "[PluginStoreVM] Uninstall private access cancelled, falling back to directory delete");
                            break;
                        }

                        var accessResult = await _storeService.GetPrivateAccessAsync(item.Id, accessKey);
                        item.AccessToken = accessResult.AccessToken;
                        attempt++;
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("download") ||
                                                               ex.Message.Contains("Download"))
                    {
                        DebugLog.Info("PluginStoreViewModel.Uninstall",
                            $"[PluginStoreVM] Uninstall download error (may need captcha): {ex.Message}");
                        attempt++;
                        if (attempt >= maxCaptchaRetries)
                        {
                            DebugLog.Info("PluginStoreViewModel.Uninstall",
                                "[PluginStoreVM] Uninstall captcha retries exhausted, falling back to directory delete");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("PluginStoreViewModel.Uninstall",
                            $"[PluginStoreVM] Lua uninstall error, falling back to directory delete: {ex.Message}");
                        break;
                    }
                }

                if (luaSuccess)
                {
                    DebugLog.Info("PluginStoreViewModel.Uninstall",
                        "[PluginStoreVM] Lua uninstall script completed successfully");
                }
            }

            var pluginDir = Path.Combine(_pluginsDir, item.Id);
            if (Directory.Exists(pluginDir))
            {
                Directory.Delete(pluginDir, true);
                DebugLog.Info("PluginStoreViewModel.Uninstall",
                    $"[PluginStoreVM] Deleted plugin directory: {pluginDir}");
            }

            item.State = StorePluginState.Available;
            item.InstallProgress = 0;
            item.InstallStatusText = "PluginStoreUninstallComplete".GetLocalized();
            StatusMessage = string.Format("PluginStoreUninstallSuccess".GetLocalized(), item.Name);
            outcome = PluginInstallOutcome.Succeeded;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreViewModel.Uninstall", $"[PluginStoreVM] Uninstall error: {ex}");
            item.State = StorePluginState.Installed;
            item.InstallStatusText = "PluginStoreUninstallFailed".GetLocalized();
            StatusMessage = string.Empty;
            outcome = PluginInstallOutcome.Failed;
        }
        finally
        {
            _installingPluginIds.Remove(item.Id);
            item.IsInstallInProgress = false;

            _operationOutcome = outcome;
            FinishOperation(item);
        }
    }

    private void CleanupPluginDir(string pluginId)
    {
        try
        {
            var pluginDir = Path.Combine(_pluginsDir, pluginId);
            if (Directory.Exists(pluginDir))
            {
                Directory.Delete(pluginDir, true);
                DebugLog.Info("PluginStoreViewModel.Uninstall",
                    $"[PluginStoreVM] Cleaned up partial install: {pluginDir}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreViewModel.Uninstall",
                $"[PluginStoreVM] Failed to clean up plugin dir: {ex.Message}");
        }
    }

    private bool IsPluginInstalledOnDisk(string pluginId, out string? localVersion)
    {
        localVersion = null;

        if (string.IsNullOrWhiteSpace(pluginId) || !Directory.Exists(_pluginsDir)) return false;

        var pluginDir = Path.Combine(_pluginsDir, pluginId);
        if (!Directory.Exists(pluginDir)) return false;

        var configPath = Path.Combine(pluginDir, "config.ini");
        if (!File.Exists(configPath)) return false;

        try
        {
            var lines = File.ReadAllLines(configPath);
            string? dllFileName = null;
            var inGeneral = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inGeneral = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inGeneral) continue;

                var parts = trimmed.Split('=', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key.Equals("Version", StringComparison.OrdinalIgnoreCase))
                {
                    localVersion = value;
                }
                else if (key.Equals("File", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                {
                    dllFileName = value;
                }
            }

            if (!string.IsNullOrEmpty(dllFileName))
            {
                var dllPath = Path.Combine(pluginDir, dllFileName);
                if (!File.Exists(dllPath)) return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateLocalState(PluginStoreItem storeItem)
    {
        if (!IsPluginInstalledOnDisk(storeItem.Id, out var localVersion)) return;

        if (!string.IsNullOrEmpty(localVersion))
        {
            storeItem.State = localVersion != storeItem.Version
                ? StorePluginState.UpdateAvailable
                : StorePluginState.Installed;
        }
        else
        {
            storeItem.State = StorePluginState.Installed;
        }
    }

    private static string AppendTokenToUrl(string url, string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) return url;
        var uriBuilder = new UriBuilder(url);
        var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
        query["access_token"] = accessToken;
        uriBuilder.Query = query.ToString();
        return uriBuilder.ToString();
    }

    #endregion
}