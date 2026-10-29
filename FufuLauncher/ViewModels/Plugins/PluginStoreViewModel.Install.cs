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
    #region Plugin Install

    private bool _installCancelRequested;

    private void CancelInstall(PluginStoreItem item)
    {
        if (item == null || !item.IsInstallInProgress) return;

        DebugLog.Info("PluginStoreViewModel.Install", $"[PluginStoreVM] User cancelled install for plugin: {item.Id}");

        _installCancelRequested = true;
        _installCts?.Cancel();

        item.InstallStatusText = "PluginStoreCancelling".GetLocalized();
        item.DownloadSpeedBytesPerSecond = 0;
    }

    private async Task InstallPluginAsync(PluginStoreItem item)
    {
        if (item == null || item.IsInstallInProgress || _installCts != null || IsStoreBusy) return;

        var preserveExisting = Directory.Exists(Path.Combine(_pluginsDir, item.Id));

        if (!string.IsNullOrWhiteSpace(item.MinAppVersion) &&
            !IsVersionSatisfied(CurrentAppVersion, item.MinAppVersion))
        {
            await ShowMinVersionWarningAsync(item);
            item.State = StorePluginState.Available;
            item.InstallProgress = 0;
            item.InstallStatusText = "PluginStoreVersionTooLow".GetLocalized();
            return;
        }

        var outcome = PluginInstallOutcome.Running;
        _installCancelRequested = false;

        try
        {
            _installCts = new CancellationTokenSource();

            _installingPluginIds.Add(item.Id);

            item.State = StorePluginState.Installing;
            item.IsInstallInProgress = true;
            item.InstallProgress = 0;
            item.InstallProgressPercent = 0;
            item.DownloadedBytes = 0;
            item.TotalDownloadBytes = -1;
            item.DownloadSpeedBytesPerSecond = 0;
            item.InstallStatusText = "PluginStoreVerifying".GetLocalized();

            BeginOperation(item, false);

            if (item.IsPrivate && string.IsNullOrWhiteSpace(item.AccessToken))
            {
                item.InstallStatusText = "PluginStorePrivateAccessRequired".GetLocalized();

                var accessKey = await ShowPrivateAccessDialogAsync(item);
                if (string.IsNullOrWhiteSpace(accessKey))
                {
                    _installingPluginIds.Remove(item.Id);
                    item.State = StorePluginState.Available;
                    item.InstallProgress = 0;
                    item.InstallStatusText = "PluginStoreCancelled".GetLocalized();
                    outcome = PluginInstallOutcome.Cancelled;
                    return;
                }

                try
                {
                    var accessResult = await _storeService.GetPrivateAccessAsync(item.Id, accessKey);
                    item.AccessToken = accessResult.AccessToken;
                    item.ArchivePassword = accessResult.ArchivePassword;

                    if (accessResult.Plugin != null)
                    {
                        item.Version = accessResult.Plugin.Version;
                        item.FileHash = accessResult.Plugin.FileHash;
                        item.LuaHash = accessResult.Plugin.LuaHash;
                        item.LuaInstallUrl = accessResult.Plugin.LuaInstallUrl;
                        item.LuaUninstallUrl = accessResult.Plugin.LuaUninstallUrl;
                        item.DownloadUrl = accessResult.Plugin.DownloadUrl;
                        item.SizeBytes = accessResult.Plugin.SizeBytes;
                        item.DllFileName = accessResult.Plugin.DllFileName;
                        item.ArchiveFileName = accessResult.Plugin.ArchiveFileName;
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Info("PluginStoreViewModel.Install",
                        $"[PluginStoreVM] Private access failed: {ex.Message}");
                    _installingPluginIds.Remove(item.Id);
                    item.State = StorePluginState.Available;
                    item.InstallProgress = 0;
                    item.InstallStatusText = "PluginStorePrivateAccessDenied".GetLocalized();
                    StatusMessage = ex.Message;
                    outcome = PluginInstallOutcome.Failed;
                    return;
                }
            }

            outcome = await DoInstallAsync(item, preserveExisting);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginStoreViewModel.Install", $"[PluginStoreVM] Install error: {ex}");
            outcome = PluginInstallOutcome.Failed;
            _installingPluginIds.Remove(item.Id);
            item.State = StorePluginState.Available;
            item.InstallProgress = 0;
            item.InstallStatusText = "PluginStoreInstallFailedShort".GetLocalized();
            StatusMessage = string.Format("PluginStoreInstallFailed".GetLocalized(), ex.Message);

            if (!preserveExisting) CleanupPluginDir(item.Id);
        }
        finally
        {
            _installingPluginIds.Remove(item.Id);
            item.IsInstallInProgress = false;
            _installCts?.Dispose();
            _installCts = null;
            item.ArchivePassword = null;

            if (preserveExisting && item.State == StorePluginState.Available) UpdateLocalState(item);

            if (outcome == PluginInstallOutcome.Failed && _installCancelRequested)
            {
                outcome = PluginInstallOutcome.Cancelled;
            }

            _installCancelRequested = false;

            _operationOutcome = outcome;
            FinishOperation(item);
        }
    }

    private async Task<PluginInstallOutcome> DoInstallAsync(PluginStoreItem item, bool preserveExisting)
    {
        var maxCaptchaRetries = 3;
        var attempt = 0;

        while (attempt < maxCaptchaRetries)
        {
            try
            {
                item.InstallStatusText = attempt > 0
                    ? "PluginStoreRetrying".GetLocalized()
                    : "PluginStoreDownloadingLua".GetLocalized();

                await _luaInstaller.ExecuteInstallScriptAsync(
                    item.LuaInstallUrl,
                    item.LuaHash,
                    item.FileHash,
                    _installCts?.Token ?? CancellationToken.None,
                    item.DllFileName,
                    item.Id,
                    item.DlToken,
                    item.AccessToken,
                    item.DownloadUrl,
                    item.ArchivePassword,
                    item.ArchiveFileName);

                var pluginDir = Path.Combine(_pluginsDir, item.Id);
                _luaInstaller.EnsureConfigFileEntry(pluginDir, item.DllFileName);

                if (!IsPluginInstalledOnDisk(item.Id, out _))
                {
                    DebugLog.Info("PluginStoreViewModel.Install",
                        $"[PluginStoreVM] Install verification failed: plugin '{item.Id}' not found on disk after install script");
                    item.State = StorePluginState.Available;
                    item.InstallProgress = 0;
                    item.InstallStatusText = "PluginStoreInstallFailedShort".GetLocalized();
                    StatusMessage = string.Format("PluginStoreInstallFailed".GetLocalized(),
                        "PluginStoreInstallVerifyFailed".GetLocalized());
                    if (!preserveExisting) CleanupPluginDir(item.Id);
                    return PluginInstallOutcome.Failed;
                }

                item.InstallProgress = 100;
                item.InstallProgressPercent = 100.0;
                item.InstallStatusText = "PluginStoreInstallComplete".GetLocalized();
                item.DownloadSpeedBytesPerSecond = 0;

                ApplyInstalledState(item);

                StatusMessage = string.Format("PluginStoreInstallSuccess".GetLocalized(), item.Name);
                return PluginInstallOutcome.Succeeded;
            }
            catch (CaptchaRequiredException captchaEx)
            {
                DebugLog.Info("PluginStoreViewModel.Install",
                    $"[PluginStoreVM] Captcha required: {captchaEx.VerifyUrl}");
                item.InstallStatusText = "PluginStoreCaptchaRequired".GetLocalized();

                var dlToken = await ShowGeetestCaptchaAsync(captchaEx.VerifyUrl);

                if (string.IsNullOrWhiteSpace(dlToken))
                {
                    throw new OperationCanceledException("PluginStoreCaptchaCancelled".GetLocalized());
                }

                item.DlToken = dlToken;
                attempt++;
                DebugLog.Info("PluginStoreViewModel.Install",
                    $"[PluginStoreVM] Got dl_token, retrying download (attempt {attempt})...");
            }
            catch (PrivatePluginAccessException privEx)
            {
                DebugLog.Info("PluginStoreViewModel.Install",
                    $"[PluginStoreVM] Private access required: {privEx.Message}");
                item.InstallStatusText = "PluginStorePrivateAccessRequired".GetLocalized();

                var accessKey = await ShowPrivateAccessDialogAsync(item);
                if (string.IsNullOrWhiteSpace(accessKey))
                    throw new OperationCanceledException("PluginStorePrivateAccessCancelled".GetLocalized());

                var accessResult = await _storeService.GetPrivateAccessAsync(item.Id, accessKey);
                item.AccessToken = accessResult.AccessToken;
                item.ArchivePassword = accessResult.ArchivePassword;
                if (accessResult.Plugin != null)
                {
                    item.FileHash = accessResult.Plugin.FileHash;
                    item.LuaHash = accessResult.Plugin.LuaHash;
                    item.DownloadUrl = accessResult.Plugin.DownloadUrl;
                    item.LuaInstallUrl = accessResult.Plugin.LuaInstallUrl;
                    item.DllFileName = accessResult.Plugin.DllFileName;
                    item.ArchiveFileName = accessResult.Plugin.ArchiveFileName;
                }

                attempt++;
            }
            catch (HashMismatchException ex)
            {
                DebugLog.Info("PluginStoreViewModel.Install", $"[PluginStoreVM] Hash mismatch: {ex.Message}");
                item.State = StorePluginState.Available;
                item.InstallProgress = 0;
                item.InstallStatusText = "PluginStoreHashFailed".GetLocalized();
                StatusMessage = string.Format("PluginStoreInstallFailed".GetLocalized(), ex.Message);
                if (!preserveExisting) CleanupPluginDir(item.Id);
                return PluginInstallOutcome.Failed;
            }
            catch (SecurityViolationException ex)
            {
                DebugLog.Info("PluginStoreViewModel.Install", $"[PluginStoreVM] Security violation: {ex.Message}");
                item.State = StorePluginState.Available;
                item.InstallProgress = 0;
                item.InstallStatusText = "PluginStoreSecurityBlockedShort".GetLocalized();
                StatusMessage = string.Format("PluginStoreSecurityBlocked".GetLocalized(), ex.Message);
                if (!preserveExisting) CleanupPluginDir(item.Id);
                return PluginInstallOutcome.Failed;
            }
            catch (OperationCanceledException)
            {
                item.State = StorePluginState.Available;
                item.InstallProgress = 0;
                item.InstallStatusText = "PluginStoreCancelled".GetLocalized();
                if (!preserveExisting) CleanupPluginDir(item.Id);
                return PluginInstallOutcome.Cancelled;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("download") ||
                                                       ex.Message.Contains("Download"))
            {
                DebugLog.Info("PluginStoreViewModel.Install",
                    $"[PluginStoreVM] Download error (may need captcha): {ex.Message}");
                attempt++;
                if (attempt >= maxCaptchaRetries) throw;
            }
        }

        throw new InvalidOperationException("PluginStoreCaptchaRetryExhausted".GetLocalized());
    }

    private void ApplyInstalledState(PluginStoreItem item)
    {
        void Apply()
        {
            item.State = StorePluginState.Installed;
        }

        if (_dispatcher == null || _dispatcher.HasThreadAccess)
        {
            Apply();
        }
        else
        {
            _dispatcher.TryEnqueue(() => Apply());
        }
    }

    private static bool IsVersionSatisfied(string currentVersion, string minVersion)
    {
        if (!AppVersionHelper.TryParseVersion(currentVersion, out var cur) ||
            !AppVersionHelper.TryParseVersion(minVersion, out var min))
        {
            return true;
        }

        return cur >= min;
    }

    private void OnInstallProgress(DownloadProgressInfo info)
    {
        _dispatcher?.TryEnqueue(() =>
        {
            foreach (var id in _installingPluginIds)
            {
                var installing = Plugins.FirstOrDefault(p => p.Id == id);
                if (installing != null && installing.State == StorePluginState.Installing)
                {
                    installing.InstallProgress = (int)Math.Round(info.Percent);
                    installing.InstallProgressPercent = info.Percent;
                    installing.InstallStatusText = info.StatusText;
                    installing.DownloadedBytes = info.BytesDownloaded;
                    installing.TotalDownloadBytes = info.TotalBytes;
                    installing.DownloadSpeedBytesPerSecond = info.SpeedBytesPerSecond;
                }
            }
        });
    }

    #endregion
}
