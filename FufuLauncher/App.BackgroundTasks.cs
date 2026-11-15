/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services.UID;
using FufuLauncher.Views;
using Windows.Media.Core;
using Windows.Media.Playback;
using FufuLauncher.Activation;

namespace FufuLauncher;

public partial class App
{
    #region Background Tasks

    private const int BanCheckExitCode = 4;

    private const uint MB_YESNO = 0x00000004;
    private const int IDYES = 6;


    private async Task<IReadOnlyList<string>> LoadUidLookupAsync()
    {
        try
        {
            var uidService = GetService<IUidLookupService>();
            return await uidService.LoadAndWriteUidsAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"[UidLookup] Write failed: {ex.Message}");
            return Array.Empty<string>();
        }
    }


    private async Task<bool> EnforceBanListAsync(IReadOnlyList<string>? knownUids = null)
    {
        BanCheckResult result;
        try
        {
            result = await GetService<Services.UID.BanCheckService>().CheckAsync(knownUids);
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"[BanCheck] Check failed, allowing through - {ex.Message}");
            return false;
        }

        if (!result.ShouldTerminate) return false;

        DebugLog.Info("App.BackgroundTasks",
            $"[BanCheck] !!! TERMINATING - {result.Outcome} (UID: {result.Uid ?? "n/a"})");

        var message = string.Format(
            "BanCheck_KilledMessage".GetLocalized(),
            Environment.NewLine,
            result.Uid ?? "-",
            result.Reason,
            result.AppealUrl);


        var choice = MessageBox(
            IntPtr.Zero,
            message,
            "BanCheck_KilledTitle".GetLocalized(),
            MB_YESNO | MB_ICONERROR);

        if (choice == IDYES)
        {
            OpenAppealUrl(result.AppealUrl);
        }

        await RequestShutdownAsync();
        return true;
    }


    private async Task RequestShutdownAsync()
    {
        try
        {
            var window = MainWindow;
            if (window != null)
            {
                await _mainDispatcherQueue.EnqueueAsync(() =>
                {
                    try
                    {
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("App.BackgroundTasks", $"[BanCheck] Failed to close main window - {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"[BanCheck] Failed to schedule close - {ex.Message}");
        }


        await Task.Delay(150);

        Environment.Exit(BanCheckExitCode);
    }


    private static void OpenAppealUrl(string? url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                DebugLog.Info("App.BackgroundTasks", $"[BanCheck] Refused to open non-http(s) appeal link: {url}");
                return;
            }

            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"[BanCheck] Failed to open appeal link - {ex.Message}");
        }
    }

    private async Task PlayStartupSoundDelayedAsync()
    {
        try
        {
            await Task.Delay(800);
            await PlayStartupSoundAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"Failed to play startup sound: {ex.Message}");
        }
    }

    private async Task CheckForAnnouncementAsync()
    {
        try
        {
            await Task.Delay(1500);

            var announcementService = GetService<IAnnouncementService>();
            var announcementUrl = await announcementService.CheckForNewAnnouncementAsync();

            if (!string.IsNullOrEmpty(announcementUrl))
            {
                await _mainDispatcherQueue.EnqueueAsync(() =>
                {
                    var announcementWindow = new WebAnnouncementWindow(announcementUrl);
                    announcementWindow.Activate();
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"[Announcement] Announcement check failed: {ex.Message}");
        }
    }

    private async Task RunStartupUpdateCheckAsync()
    {
        try
        {
            DebugLog.Info("App.BackgroundTasks",
                "[Background] Background tasks started, waiting for main window render...");
            await Task.Delay(500);

            DebugLog.Info("App.BackgroundTasks", "[Background] Preparing to dispatch to UI thread...");

            await _mainDispatcherQueue.EnqueueAsync(async () =>
            {
                DebugLog.Info("App.BackgroundTasks", "[Background] Already on UI thread, running update check...");
                await CheckAndShowUpdateWindowAsync();
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"[Background] Background update check failed: {ex.Message}");
            DebugLog.Info("App.BackgroundTasks", $"[Background] Exception type: {ex.GetType().FullName}");
            DebugLog.Info("App.BackgroundTasks", $"[Background] Stack trace: {ex.StackTrace}");
        }
    }

    private async Task CheckStoragePathsAsync()
    {
        try
        {
            if (!AppPaths.HasMissingPaths)
            {
                return;
            }

            var lines = new List<string>();

            if (AppPaths.MissingDataDir != null)
            {
                lines.Add(string.Format(
                    "StoragePath_MissingDir".GetLocalized(),
                    "AgreementPage_UserDataDir".GetLocalized(),
                    AppPaths.MissingDataDir));
            }

            if (AppPaths.MissingCacheDir != null)
            {
                lines.Add(string.Format(
                    "StoragePath_MissingDir".GetLocalized(),
                    "AgreementPage_CacheDir".GetLocalized(),
                    AppPaths.MissingCacheDir));
            }

            lines.Add(string.Format(
                "StoragePath_MissingHint".GetLocalized(),
                "NavSettings".GetLocalized(),
                "SectionStoragePaths".GetLocalized()));

            var notification = new Messages.NotificationMessage(
                "StoragePath_MissingTitle".GetLocalized(),
                string.Join(Environment.NewLine, lines),
                Messages.NotificationType.Warning,
                0);

            DebugLog.Info("App.BackgroundTasks", $"[AppPaths] Missing storage path: {string.Join(" | ", lines)}");

            await Task.Delay(1200);

            await _mainDispatcherQueue.EnqueueAsync(() =>
            {
                WeakReferenceMessenger.Default.Send(notification);
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"[AppPaths] Startup storage path check failed: {ex.Message}");
        }
    }

    internal async Task PlayStartupSoundAsync()
    {
        try
        {
            var localSettingsService = GetService<ILocalSettingsService>();

            var soundEnabled = await localSettingsService.ReadSettingAsync("IsStartupSoundEnabled");
            bool isSoundEnabled = soundEnabled != null && Convert.ToBoolean(soundEnabled);

            if (!isSoundEnabled) return;

            var soundPath = await localSettingsService.ReadSettingAsync("StartupSoundPath");
            if (soundPath == null || string.IsNullOrEmpty(soundPath.ToString())) return;

            string path = soundPath.ToString();
            if (!File.Exists(path))
            {
                DebugLog.Info("App.BackgroundTasks", $"Startup sound file not found: {path}");
                return;
            }

            await _mainDispatcherQueue.EnqueueAsync(() =>
            {
                try
                {
                    var mediaPlayer = new MediaPlayer();
                    MediaPlayerHelper.DisableSystemMediaControls(mediaPlayer);
                    mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(path));
                    mediaPlayer.Volume = 0.7;

                    int disposed = 0;

                    void DisposeOnce()
                    {
                        if (Interlocked.Exchange(ref disposed, 1) == 0)
                        {
                            try
                            {
                                mediaPlayer.Dispose();
                            }
                            catch
                            {
                            }
                        }
                    }

                    mediaPlayer.MediaEnded += (s, e) => DisposeOnce();
                    mediaPlayer.MediaFailed += (s, e) => DisposeOnce();
                    mediaPlayer.Play();

                    var timer = _mainDispatcherQueue.CreateTimer();
                    timer.Interval = TimeSpan.FromSeconds(30);
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        DisposeOnce();
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    DebugLog.Info("App.BackgroundTasks", $"Startup sound playback exception: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"Startup sound processing failed: {ex.Message}");
        }
    }

    private async Task CheckAndShowUpdateWindowAsync()
    {
        try
        {
            var updateService = GetService<IUpdateService>();
            var result = await updateService.CheckUpdateAsync();

            var devBuildService = GetService<IDevBuildDetectionService>();
            WeakReferenceMessenger.Default.Send(
                new Messages.DevBuildDetectionCompletedMessage(devBuildService.IsDevBuild));

            if (result.IsDevBuild && MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowDevBuildBadge();

                WeakReferenceMessenger.Default.Send(new Messages.BackgroundRefreshMessage());
            }

            if (result.ShouldShowUpdate)
            {
                DebugLog.Info("App.BackgroundTasks",
                    $"Preparing to show update window, version: {result.ServerVersion}, preview: {result.IsPreview}");
                DebugLog.Info("App.BackgroundTasks", $"[App] Dynamic update announcement URL: {result.UpdateInfoUrl}");

                MainWindow.Activate();

                var updateWindow = new UpdateNotificationWindow(result.UpdateInfoUrl, result.IsPreview);
                updateWindow.Title = result.IsPreview
                    ? string.Format("App_UpdatePreviewNoticeTitle_Format".GetLocalized(), result.ServerVersion)
                    : string.Format("App_UpdateNoticeTitle_Format".GetLocalized(), result.ServerVersion);
                updateWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.BackgroundTasks", $"Update check failed: {ex.Message}");
            DebugLog.Info("App.BackgroundTasks", $"[App] Exception details: {ex.StackTrace}");
        }
    }

    #endregion
}