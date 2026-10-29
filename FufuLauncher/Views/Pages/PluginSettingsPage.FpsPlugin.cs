/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private const string FpsPluginStoreId = "FPS";
    private const int FpsCaptchaRetryLimit = 3;

    private static string FpsPluginDirectory => Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS");
    private static string FpsPluginDllPath => Path.Combine(FpsPluginDirectory, "FPS.dll");

    private bool _hasShownFpsWarning = false;
    private bool _isEnforcingFpsDisable = false;
    private bool _isFpsRepairRunning = false;

    private async Task CheckAndShowFpsWarningAsync()
    {
        if (ViewModel.SelectedPluginIndex == 1 && !_hasShownFpsWarning && XamlRoot != null)
        {
            _hasShownFpsWarning = true;

            var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
            if (localSettings != null)
            {
                var hasDismissedObj = await localSettings.ReadSettingAsync("HasDismissedFpsWarning");
                bool hasDismissed = hasDismissedObj != null && Convert.ToBoolean(hasDismissedObj);

                if (hasDismissed)
                {
                    return;
                }

                var dialog = new ContentDialog
                {
                    Title = "Fps_CompatWarning_Title".GetLocalized(),
                    Content = "Fps_CompatWarning_Content".GetLocalized(),
                    PrimaryButtonText = "GotItBtn".GetLocalized(),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = XamlRoot
                };

                var checkBox = new CheckBox
                {
                    Content = "Fps_CompatWarning_DontShow".GetLocalized(),
                    Margin = new Thickness(0, 16, 0, 0)
                };

                var stackPanel = new StackPanel();
                stackPanel.Children.Add(new TextBlock
                {
                    Text = dialog.Content.ToString(),
                    TextWrapping = TextWrapping.Wrap
                });
                stackPanel.Children.Add(checkBox);

                dialog.Content = stackPanel;

                await dialog.ShowAsync();

                if (checkBox.IsChecked == true)
                {
                    await localSettings.SaveSettingAsync("HasDismissedFpsWarning", true);
                }
            }
        }
    }

    private async Task EnforceFpsPluginDisableAsync()
    {
        _isEnforcingFpsDisable = true;
        try
        {
            await Task.Delay(500);

            if (File.Exists(FpsPluginDllPath))
            {
                try
                {
                    File.Delete(FpsPluginDllPath);
                }
                catch (Exception ex)
                {
                    DebugLog.Info("PluginSettingsPage.FpsPlugin",
                        $"Failed to force-remove FPS plugin files: {ex.Message}");
                }

                await PerformFpsPluginRepairAsync(showUI: false);

                ViewModel.IsFpsPluginEnabled = true;
                await Task.Delay(100);
                ViewModel.IsFpsPluginEnabled = false;
            }
        }
        finally
        {
            _isEnforcingFpsDisable = false;
        }
    }

    private async void OnRepairFpsPluginClick(object sender, RoutedEventArgs e)
    {
        await PerformFpsPluginRepairAsync(showUI: true);
    }

    private async Task PerformFpsPluginRepairAsync(bool showUI)
    {
        if (_isFpsRepairRunning) return;
        _isFpsRepairRunning = true;

        try
        {
            var preLockedFile = FileLockHelper.FindLockedFileInDirectory(FpsPluginDirectory);
            if (preLockedFile != null)
            {
                if (showUI) await ShowLockedFileDialogAsync(preLockedFile);
                return;
            }

            using var cancellation = new CancellationTokenSource();

            var statusText = new TextBlock
            {
                Text = "Fps_Repair_Fetching".GetLocalized(),
                TextWrapping = TextWrapping.Wrap
            };
            var progressBar = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 20,
                Margin = new Thickness(0, 10, 0, 0)
            };

            ContentDialog? progressDialog = null;
            if (showUI)
            {
                var content = new StackPanel { Spacing = 8 };
                content.Children.Add(statusText);
                content.Children.Add(progressBar);

                progressDialog = new ContentDialog
                {
                    Title = "Fps_Repair_Title".GetLocalized(),
                    Content = content,
                    SecondaryButtonText = "CancelBtn".GetLocalized(),
                    XamlRoot = XamlRoot
                };

                progressDialog.SecondaryButtonClick += (_, args) =>
                {
                    args.Cancel = true;
                    cancellation.Cancel();
                    statusText.Text = "Plugin_StopRequested".GetLocalized();
                };

                _ = progressDialog.ShowAsync();
            }

            try
            {
                IProgress<DownloadProgressInfo>? progress = null;
                Action<string>? reportStatus = null;

                if (showUI)
                {
                    progress = new Progress<DownloadProgressInfo>(info =>
                    {
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = Math.Clamp(info.Percent, 0, 100);
                        if (!string.IsNullOrWhiteSpace(info.StatusText))
                        {
                            statusText.Text = info.StatusText;
                        }
                    });

                    reportStatus = text => statusText.Text = text;
                }

                await InstallFpsPluginFromStoreAsync(progress, reportStatus, showUI, cancellation.Token);

                progressDialog?.Hide();

                ViewModel.LoadConfiguration();
                ViewModel.RefreshPluginStates();

                if (showUI)
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("Success".GetLocalized(),
                        "Fps_Repair_Success".GetLocalized(), NotificationType.Success));
            }
            catch (OperationCanceledException)
            {
                progressDialog?.Hide();

                if (showUI)
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "DownloadWindow_Cancelled".GetLocalized(),
                        "Fps_Repair_Cancelled".GetLocalized(),
                        NotificationType.Warning,
                        4000));
            }
            catch (Exception ex)
            {
                progressDialog?.Hide();

                DebugLog.Info("PluginSettingsPage.FpsPlugin",
                    $"Failed to repair FPS plugin from the plugin store: {ex.Message}");

                if (!showUI) return;

                var lockedFile = FileLockHelper.FindLockedFileInDirectory(FpsPluginDirectory);
                if (lockedFile != null)
                {
                    await ShowLockedFileDialogAsync(lockedFile);
                    return;
                }

                var failDialog = new ContentDialog
                {
                    Title = "Fps_Repair_Fail_Title".GetLocalized(),
                    Content = ex.Message,
                    CloseButtonText = "CloseBtn".GetLocalized(),
                    XamlRoot = XamlRoot
                };
                await failDialog.ShowAsync();
            }
        }
        finally
        {
            _isFpsRepairRunning = false;
        }
    }

    private async Task InstallFpsPluginFromStoreAsync(IProgress<DownloadProgressInfo>? progress,
        Action<string>? reportStatus, bool allowCaptcha, CancellationToken cancellationToken)
    {
        var storeService = App.GetService<PluginStoreService>();
        var detail = await storeService.GetPluginDetailAsync(FpsPluginStoreId);

        if (detail == null || string.IsNullOrWhiteSpace(detail.DownloadUrl))
        {
            throw new InvalidOperationException("Fps_Repair_StoreUnavailable".GetLocalized());
        }

        string packagePath = Path.Combine(Path.GetTempPath(), "FPS_Store_" + Guid.NewGuid().ToString("N") + ".zip");
        string extractPath = Path.Combine(Path.GetTempPath(), "FPS_Store_Extract_" + Guid.NewGuid().ToString("N"));

        try
        {
            await DownloadFpsPackageAsync(storeService, detail, packagePath, progress, reportStatus, allowCaptcha,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            reportStatus?.Invoke("PluginStoreExtracting".GetLocalized());

            Directory.CreateDirectory(extractPath);
            await new PluginArchiveExtractor().ExtractAsync(packagePath, extractPath, null, cancellationToken);

            string sourceDir = FindFpsPackageRoot(extractPath);
            string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");

            if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);

            if (Directory.Exists(FpsPluginDirectory)) Directory.Delete(FpsPluginDirectory, true);

            await Task.Run(() => MoveDirectorySafe(sourceDir, FpsPluginDirectory), cancellationToken);
        }
        finally
        {
            TryDeleteFile(packagePath);
            TryDeleteDirectory(extractPath);
        }
    }

    private static async Task DownloadFpsPackageAsync(PluginStoreService storeService, PluginStoreItem detail,
        string packagePath, IProgress<DownloadProgressInfo>? progress, Action<string>? reportStatus, bool allowCaptcha,
        CancellationToken cancellationToken)
    {
        string? dlToken = null;

        for (var attempt = 0; attempt <= FpsCaptchaRetryLimit; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await storeService.DownloadFileAsync(detail.DownloadUrl, packagePath, progress, detail.FileHash,
                    dlToken, null, cancellationToken);
                return;
            }
            catch (CaptchaRequiredException ex)
            {
                TryDeleteFile(packagePath);

                if (!allowCaptcha)
                {
                    throw new InvalidOperationException("PluginStoreCaptchaRequired".GetLocalized(), ex);
                }

                if (attempt == FpsCaptchaRetryLimit)
                {
                    throw new InvalidOperationException("PluginStoreCaptchaRetryExhausted".GetLocalized(), ex);
                }

                reportStatus?.Invoke("PluginStoreCaptchaRequired".GetLocalized());

                dlToken = await PluginCaptchaDialog.ShowAsync(ex.VerifyUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(dlToken))
                {
                    throw new OperationCanceledException("PluginStoreCaptchaCancelled".GetLocalized());
                }
            }
        }

        throw new InvalidOperationException("PluginStoreCaptchaRetryExhausted".GetLocalized());
    }

    private static string FindFpsPackageRoot(string extractPath)
    {
        string? dllPath = Directory.GetFiles(extractPath, "FPS.dll", SearchOption.AllDirectories).FirstOrDefault();

        if (dllPath == null)
        {
            throw new InvalidOperationException("PluginStoreInstallVerifyFailed".GetLocalized());
        }

        return Path.GetDirectoryName(dllPath)!;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSettingsPage.FpsPlugin", $"Failed to delete temporary file: {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginSettingsPage.FpsPlugin", $"Failed to delete temporary directory: {ex.Message}");
        }
    }
}
