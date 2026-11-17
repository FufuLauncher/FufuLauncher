/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services;
using FufuLauncher.Services.GameServer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class CutsceneResourcesWindow
{
    private sealed record MediaTool(string FileName, string Setting, string TitleKey, string HintKey,
        string DownloadKey, string Website);

    private static readonly MediaTool Charlotte = new(CutsceneMediaService.DecoderFileName, "CutsceneDecoderPath",
        "Cutscene_ChooseDecoder", "Cutscene_DecoderHint", "Cutscene_DecoderDownloads",
        "https://github.com/The-Steambird/charlotte/releases");
    private static readonly MediaTool Ffmpeg = new(CutsceneMediaService.FfmpegFileName, "CutsceneFfmpegPath",
        "Cutscene_ChooseFfmpeg", "Cutscene_FfmpegHint", "Cutscene_FfmpegDownloads",
        "https://ffmpeg.org/download.html#build-windows");
    private CutsceneNameCatalog? _displayCatalog;
    private bool _usingCachedCatalog;

    private async void OnUpdateNameCatalogClick(object sender, RoutedEventArgs args) =>
        await RunResourceActionAsync("Cutscene_CatalogUpdating", "Cutscene_CatalogUpdateFailed", async token =>
        {
            var progress = new Progress<string>(stage =>
            {
                if (!_closed)
                    DispatcherQueue.TryEnqueue(() => WorkStatus.Text = stage);
            });
            var sophonClient = App.GetService<SophonBuildClient>();
            CutsceneCatalogUpdateResult result = await CutsceneNameCatalogStore.UpdateAsync(
                sophonClient, _catalog?.Scheme, progress, token);
            if (_closed)
                return;

            _displayCatalog = result.Catalog;
            _usingCachedCatalog = result.Updated || _usingCachedCatalog;
            RefreshDisplayCatalog();
            string message = result.Updated ? "Cutscene_CatalogUpdatedVia" : "Cutscene_CatalogCurrent";
            ShowNotice(result.Updated ? InfoBarSeverity.Success : InfoBarSeverity.Informational,
                result.Updated
                    ? string.Format(message.GetLocalized(), result.Catalog.VersionLabel, result.SourceName)
                    : string.Format(message.GetLocalized(), result.Catalog.VersionLabel));
        });

    private async void OnImportCatalogClick(object sender, RoutedEventArgs args) =>
        await RunResourceActionAsync(null, "Cutscene_CatalogImportFailed", async token =>
        {
            var filters = new (string Label, string[] Extensions)[] { ("CutsceneNames.json", ["*.json"]) };
            string? path = await FilePickerService.PickOpenFileAsync(this, filters,
                onError: _ => ShowNotice(InfoBarSeverity.Error, "FilePicker_CannotOpen".GetLocalized()));
            if (path is null || _closed)
                return;

            CutsceneCatalogUpdateResult result = await CutsceneNameCatalogStore.ImportFromFileAsync(path, token);
            if (_closed)
                return;

            _displayCatalog = result.Catalog;
            _usingCachedCatalog = true;
            RefreshDisplayCatalog();
            ShowNotice(InfoBarSeverity.Success,
                string.Format("Cutscene_CatalogUpdatedVia".GetLocalized(), result.Catalog.VersionLabel, result.SourceName));
        });

    private async void OnVoicePacksClick(object sender, RoutedEventArgs args)
    {
        await RunResourceActionAsync("Cutscene_VoiceScanning", "Cutscene_VoiceScanFailed", async token =>
        {
            string gameDir = await GetGameDirectoryAsync()
                ?? throw new InvalidOperationException("Err_GamePathNotFound".GetLocalized());
            var packs = await Task.Run(() => _service.ScanLocalVoicePacks(gameDir, token), token);
            if (_closed)
                return;
            if (packs.Count == 0)
            {
                ShowNotice(InfoBarSeverity.Informational, "Cutscene_NoVoicePacks".GetLocalized());
                return;
            }

            var languages = new ComboBox
            {
                ItemsSource = packs.Select(pack =>
                {
                    long totalBytes = pack.Packages.Sum(p => p.Bytes + p.PersistentBytes);
                    int tracedCount = pack.Packages.Count(p => !string.IsNullOrEmpty(p.VoiceGroupId));
                    int lockedCount = pack.Packages.Count(p => p.IsStubbedLocked);
                    return string.Format("Cutscene_VoicePackDetailedSummary".GetLocalized(),
                        pack.LanguageKey.GetLocalized(), pack.Packages.Count, tracedCount, lockedCount, FormatSize(totalBytes));
                }).ToArray(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var packages = new ListView
            {
                SelectionMode = ListViewSelectionMode.None,
                Height = Math.Clamp(RootGrid.ActualHeight - 300, 140, 360)
            };
            languages.SelectionChanged += (_, _) =>
            {
                if (languages.SelectedIndex < 0)
                    return;

                packages.ItemsSource = packs[languages.SelectedIndex].Packages
                    .Select(package =>
                    {
                        long totalPkgBytes = package.Bytes + package.PersistentBytes;
                        string sizeText = package.IsStubbedLocked
                            ? "Cutscene_StubbedLockedShort".GetLocalized()
                            : FormatSize(totalPkgBytes);
                        string traceText = package.LinkedSummary is not null
                            ? $"{package.Name} · {sizeText} · [{package.VoiceGroupId}] {package.LinkedSummary}"
                            : $"{package.Name} · {sizeText} · {"Cutscene_VoiceCoreProtected".GetLocalized()}";
                        return traceText;
                    })
                    .ToArray();
            };
            languages.SelectedIndex = 0;
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = "Cutscene_VoicePackTracedHint".GetLocalized(),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(languages);
            content.Children.Add(packages);
            SetBusy(true);
            await new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Cutscene_VoicePacks".GetLocalized(),
                Content = content,
                CloseButtonText = "CloseBtn".GetLocalized()
            }.ShowAsync();
        });
    }

    private async void OnLockCleanedClick(object sender, RoutedEventArgs args)
    {
        if (_catalog is not { } catalog)
            return;

        await RunResourceActionAsync("Cutscene_LockingCleaned", "Cutscene_ApplyFailed", async token =>
        {
            var (lockedCount, _) = await Task.Run(
                () => _service.SyncCleanedProtectionLocksAsync(catalog, lockCleaned: true, token), token);
            if (_closed)
                return;

            CutsceneCatalog refreshed = await Task.Run(() => _service.RefreshCutscenes(catalog), token);
            if (!_closed)
            {
                BindCatalog(refreshed);
                ShowNotice(InfoBarSeverity.Success, string.Format("Cutscene_LockCleanedDone".GetLocalized(), lockedCount));
            }
        });
    }

    private async void OnUnlockCleanedClick(object sender, RoutedEventArgs args)
    {
        if (_catalog is not { } catalog)
            return;

        await RunResourceActionAsync("Cutscene_UnlockingCleaned", "Cutscene_ApplyFailed", async token =>
        {
            var (_, unlockedCount) = await Task.Run(
                () => _service.SyncCleanedProtectionLocksAsync(catalog, lockCleaned: false, token), token);
            if (_closed)
                return;

            CutsceneCatalog refreshed = await Task.Run(() => _service.RefreshCutscenes(catalog), token);
            if (!_closed)
            {
                BindCatalog(refreshed);
                ShowNotice(InfoBarSeverity.Success, string.Format("Cutscene_UnlockCleanedDone".GetLocalized(), unlockedCount));
            }
        });
    }

    private async void OnPersistentBypassClick(object sender, RoutedEventArgs args)
    {
        await RunResourceActionAsync(null, "Cutscene_BypassFailed", async token =>
        {
            string gameDir = await GetGameDirectoryAsync()
                ?? throw new InvalidOperationException("Err_GamePathNotFound".GetLocalized());
            var configService = App.GetService<GameServerConfigurationService>();
            GameServerScheme scheme = configService.TryDetectCurrentScheme(gameDir)
                ?? throw new InvalidOperationException("Err_GamePathNotFound".GetLocalized());

            CutscenePersistentStatus status = await Task.Run(
                () => CutsceneProtectionService.InspectPersistent(gameDir, scheme.IsOversea), token);
            if (_closed)
                return;

            var statusLines = new StringBuilder();
            statusLines.AppendLine(string.Format("Cutscene_BypassDirStatus".GetLocalized(),
                status.Exists ? status.PersistentDirectory : "Cutscene_BypassDirMissing".GetLocalized()));
            if (status.Exists)
            {
                statusLines.AppendLine(string.Format("Cutscene_BypassFilesStatus".GetLocalized(),
                    FormatCheckMark(status.HasResRemote),
                    FormatCheckMark(status.HasDataRemote),
                    FormatCheckMark(status.HasSilenceRemote),
                    status.HasBaseRevision ? "1" : "0",
                    status.CacheVersionsCount));
            }
            if (!string.IsNullOrEmpty(status.DetectedLogVersionTag))
            {
                statusLines.AppendLine(string.Format("Cutscene_BypassDetectedLogTag".GetLocalized(),
                    status.DetectedLogVersionTag));
            }

            var tagPasteBox = new TextBox
            {
                Header = "Cutscene_BypassPasteTagHeader".GetLocalized(),
                PlaceholderText = "CNRELWin7.2.0_R12345678_S12345678_D12345678",
                Text = status.DetectedLogVersionTag ?? string.Empty
            };
            var resBox = new TextBox
            {
                Header = "Res Version (R)",
                PlaceholderText = "res_revision",
                Text = status.DetectedLogRes ?? status.CurrentResRevision ?? string.Empty
            };
            var silenceBox = new TextBox
            {
                Header = "Silence Version (S)",
                PlaceholderText = "silence_revision",
                Text = status.DetectedLogSilence ?? status.CurrentSilenceRevision ?? string.Empty
            };
            var dataBox = new TextBox
            {
                Header = "Data Version (D)",
                PlaceholderText = "data_revision",
                Text = status.DetectedLogData ?? status.CurrentDataRevision ?? string.Empty
            };

            tagPasteBox.TextChanged += (_, _) =>
            {
                if (CutsceneProtectionService.TryParseVersionString(tagPasteBox.Text, out string r, out string s, out string d))
                {
                    resBox.Text = r;
                    silenceBox.Text = s;
                    dataBox.Text = d;
                }
            };

            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Message = "Cutscene_BypassRiskWarning".GetLocalized()
            });
            content.Children.Add(new TextBlock
            {
                Text = statusLines.ToString().TrimEnd(),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(tagPasteBox);

            var rsdGrid = new Grid { ColumnSpacing = 8 };
            rsdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rsdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rsdGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(resBox, 0);
            Grid.SetColumn(silenceBox, 1);
            Grid.SetColumn(dataBox, 2);
            rsdGrid.Children.Add(resBox);
            rsdGrid.Children.Add(silenceBox);
            rsdGrid.Children.Add(dataBox);
            content.Children.Add(rsdGrid);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Cutscene_BypassCheck".GetLocalized(),
                Content = content,
                PrimaryButtonText = "Cutscene_BypassApplyBtn".GetLocalized(),
                SecondaryButtonText = status.HasBackup ? "Cutscene_BypassRestoreBtn".GetLocalized() : string.Empty,
                IsSecondaryButtonEnabled = status.HasBackup,
                CloseButtonText = "CancelBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = status.Exists
            };

            SetBusy(true);
            ContentDialogResult choice = await dialog.ShowAsync();
            if (_closed || choice == ContentDialogResult.None)
                return;

            if (choice == ContentDialogResult.Secondary)
            {
                SetBusy(true, "Cutscene_BypassRestoring".GetLocalized());
                int restored = await Task.Run(
                    () => CutsceneProtectionService.RestorePersistentBackupAsync(gameDir, scheme.IsOversea, token),
                    token);
                if (!_closed)
                    ShowNotice(InfoBarSeverity.Success, string.Format("Cutscene_BypassRestored".GetLocalized(), restored));
                return;
            }

            SetBusy(true, "Cutscene_BypassApplying".GetLocalized());
            CutscenePersistentBypassReport report = await Task.Run(() =>
                CutsceneProtectionService.ApplyPersistentBypassAsync(
                    gameDir,
                    scheme.IsOversea,
                    resBox.Text,
                    silenceBox.Text,
                    dataBox.Text,
                    token), token);

            if (!_closed)
            {
                string summary = string.Format("Cutscene_BypassApplied".GetLocalized(),
                    report.DeletedFiles.Count,
                    report.PromotedFiles.Count,
                    string.Join(", ", report.WrittenRevisions));
                if (report.Notes.Count > 0)
                    summary += " · " + string.Join("；", report.Notes);
                ShowNotice(InfoBarSeverity.Success, summary);
            }
        });

        static string FormatCheckMark(bool ok) => ok ? "✓" : "—";
    }

    private async void OnAutoSetupToolsClick(object sender, RoutedEventArgs args)
    {
        await RunResourceActionAsync("Cutscene_ToolDownloading", "Cutscene_ToolDownloadFailed", async token =>
        {
            var progress = new Progress<string>(msg =>
            {
                if (!_closed && _busy)
                    WorkStatus.Text = msg;
            });
            var (decoderPath, ffmpegPath) = await Task.Run(
                () => CutsceneMediaService.DownloadAndInstallCharlotteAsync(progress, token), token);
            if (_closed)
                return;

            var settings = App.GetService<ILocalSettingsService>();
            await settings.SaveSettingAsync(Charlotte.Setting, decoderPath);
            if (ffmpegPath is not null)
                await settings.SaveSettingAsync(Ffmpeg.Setting, ffmpegPath);

            ShowNotice(InfoBarSeverity.Success,
                ffmpegPath is not null
                    ? "Cutscene_ToolsAutoConfiguredAll".GetLocalized()
                    : "Cutscene_DecoderSelected".GetLocalized());
        });
    }

    private async void OnChooseDecoderClick(object sender, RoutedEventArgs args) =>
        await ConfigureMediaToolAsync(Charlotte);

    private async void OnChooseFfmpegClick(object sender, RoutedEventArgs args) =>
        await ConfigureMediaToolAsync(Ffmpeg);

    private void OnClearPreviewCacheClick(object sender, RoutedEventArgs args)
    {
        long freed = CutsceneMediaService.ClearPreviewCache();
        ShowNotice(InfoBarSeverity.Success,
            string.Format("Cutscene_PreviewCacheCleared".GetLocalized(), FormatSize(freed)));
    }

    private async Task ConfigureMediaToolAsync(MediaTool tool) =>
        await RunResourceActionAsync(null, "Cutscene_DecoderSetupFailed", async token =>
        {
            if (await ChooseMediaToolAsync(tool, token) is not null && !_closed)
                ShowNotice(InfoBarSeverity.Success, "Cutscene_DecoderSelected".GetLocalized());
        });

    private async void OnPlayLocalCutsceneClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: CutsceneResourceRow row } && row.IsVideo)
            await PlayLocalCutsceneAsync(row);
    }

    private async void OnExportLocalCutsceneClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: CutsceneResourceRow row } && row.IsVideo)
            await ExportSingleCutsceneAsync(row);
    }

    private async void OnExportChapterClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: CutsceneChapterRow chapter })
            await ExportChapterAsync(chapter);
    }

    private async Task PlayLocalCutsceneAsync(CutsceneResourceRow row)
    {
        if (_catalog is not { } catalog || row.Video is not { IsInstalled: true } video)
            return;

        await RunResourceActionAsync("Cutscene_PreparingMedia", "Cutscene_PlayFailed", async token =>
        {
            string? decoder = await ResolveOrSetupDecoderAsync(token);
            if (decoder is null || _closed)
                return;

            string? ffmpeg = await ResolveMediaToolAsync(Ffmpeg, decoder);
            string cacheDir = CutsceneMediaService.GetPreviewCacheDirectory(video.Asset.AssetHashMd5);
            string cachedMkv = Path.Combine(cacheDir, Path.GetFileNameWithoutExtension(video.Name) + ".mkv");

            if (!File.Exists(cachedMkv) || new FileInfo(cachedMkv).Length == 0)
            {
                string inputCopy = Path.Combine(cacheDir, video.Name);
                if (!File.Exists(inputCopy) || new FileInfo(inputCopy).Length != video.Asset.AssetSize)
                {
                    if (File.Exists(inputCopy)) File.Delete(inputCopy);
                    inputCopy = await _service.CopyLocalCutsceneAsync(catalog, video, cacheDir, token);
                }

                cachedMkv = await CutsceneMediaService.DecodeAsync(decoder, inputCopy, cacheDir, token);
                try
                {
                    if (File.Exists(inputCopy)) File.Delete(inputCopy);
                }
                catch
                {
                }
            }

            if (_closed)
                return;

            CutsceneMediaTracks mediaTracks = await Task.Run(() => CutsceneAudioTracks.ReadMediaTracks(cachedMkv), token);
            IReadOnlyList<CutsceneExtractedSubtitleTrack> subtitleTracks = await Task.Run(
                () => CutsceneMediaService.ExtractSubtitlesAsync(ffmpeg, cachedMkv, mediaTracks.SubtitleTracks, cacheDir, token),
                token);

            if (_closed)
                return;

            var playerWindow = new CutscenePlayerWindow(
                row.DisplayName,
                $"{row.Chapter.Title} · {video.Name}",
                cachedMkv,
                cacheDir,
                ffmpeg,
                mediaTracks.AudioTracks,
                subtitleTracks,
                onExportRequested: () =>
                {
                    if (!_closed && !_busy)
                        DispatcherQueue.TryEnqueue(() => _ = ExportSingleCutsceneAsync(row));
                    return Task.CompletedTask;
                });
            playerWindow.Activate();
        });
    }

    private async Task ExportSingleCutsceneAsync(CutsceneResourceRow row)
    {
        if (_catalog is not { } catalog || row.Video is not { IsInstalled: true } video)
            return;

        await RunResourceActionAsync("Cutscene_PreparingMedia", "Cutscene_ExportFailed", async token =>
        {
            var formats = new (string Label, string[] Extensions)[]
            {
                ("MP4 (*.mp4)", ["*.mp4"]),
                ("Matroska (*.mkv)", ["*.mkv"]),
                ("Cutscene USM (*.usm)", ["*.usm"])
            };
            string? destination = await FilePickerService.PickSaveFileAsync(this, formats,
                Path.GetFileNameWithoutExtension(row.Name),
                onError: _ => ShowNotice(InfoBarSeverity.Error, "FilePicker_CannotOpen".GetLocalized()));
            if (destination is null || _closed)
                return;

            EnsureDestinationOutsideGame(catalog.GameDirectory, destination);

            string ext = Path.GetExtension(destination).ToLowerInvariant();
            if (ext == ".usm")
            {
                SetBusy(true, "Cutscene_Exporting".GetLocalized());
                DirectoryInfo rawWorkspace = Directory.CreateTempSubdirectory("FufuLauncher-CutsceneUsm-");
                try
                {
                    string copy = await _service.CopyLocalCutsceneAsync(catalog, video, rawWorkspace.FullName, token);
                    await CutsceneMediaService.ExportRawUsmAsync(copy, destination, token);
                }
                finally
                {
                    try { rawWorkspace.Delete(recursive: true); } catch { }
                }

                if (!_closed)
                    ShowNotice(InfoBarSeverity.Success, "Cutscene_Exported".GetLocalized());
                return;
            }

            string? decoder = await ResolveOrSetupDecoderAsync(token);
            if (decoder is null || _closed)
                return;

            DirectoryInfo workspace = Directory.CreateTempSubdirectory("FufuLauncher-Cutscene-");
            try
            {
                string input = await _service.CopyLocalCutsceneAsync(catalog, video, workspace.FullName, token);
                string decodedMkv = await CutsceneMediaService.DecodeAsync(decoder, input, workspace.FullName, token);
                if (_closed)
                    return;

                CutsceneMediaTracks tracks = await Task.Run(() => CutsceneAudioTracks.ReadMediaTracks(decodedMkv), token);
                if (_closed)
                    return;

                var exportOptions = await ChooseExportOptionsAsync(tracks, isMp4: ext == ".mp4");
                if (exportOptions is null || _closed)
                    return;

                IReadOnlyList<int>? audioTracks = exportOptions.Value.AudioTracks.Count == tracks.AudioTracks.Count
                    ? null : exportOptions.Value.AudioTracks;
                string? ffmpeg = await ResolveMediaToolAsync(Ffmpeg, decoder);
                if (audioTracks is not null || ext == ".mp4")
                {
                    ffmpeg ??= await ChooseMediaToolAsync(Ffmpeg, token);
                    if (ffmpeg is null || _closed)
                        return;
                }

                SetBusy(true, "Cutscene_Exporting".GetLocalized());
                await CutsceneMediaService.ExportAsync(
                    decodedMkv,
                    destination,
                    ffmpeg,
                    audioTracks,
                    exportOptions.Value.SubtitleMode,
                    exportOptions.Value.SubtitleIndex,
                    token);

                if (!_closed)
                    ShowNotice(InfoBarSeverity.Success, "Cutscene_Exported".GetLocalized());
            }
            finally
            {
                try { workspace.Delete(recursive: true); } catch { }
            }
        });
    }

    private async Task ExportChapterAsync(CutsceneChapterRow chapter)
    {
        if (_catalog is not { } catalog)
            return;

        CutsceneResourceRow[] videos = chapter.Rows.Where(r => r.IsVideo && r.IsInstalled).ToArray();
        if (videos.Length == 0)
            return;

        await RunResourceActionAsync("Cutscene_PreparingMedia", "Cutscene_ExportFailed", async token =>
        {
            var formatCombo = new ComboBox
            {
                Header = "Cutscene_BatchExportFormat".GetLocalized(),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 0
            };
            formatCombo.Items.Add(new ComboBoxItem { Content = "MP4 (*.mp4)", Tag = ".mp4" });
            formatCombo.Items.Add(new ComboBoxItem { Content = "Matroska (*.mkv)", Tag = ".mkv" });
            formatCombo.Items.Add(new ComboBoxItem { Content = "Cutscene USM (*.usm)", Tag = ".usm" });

            var subCheck = new CheckBox
            {
                Content = "Cutscene_BatchExportBurnSub".GetLocalized(),
                IsChecked = true
            };
            formatCombo.SelectionChanged += (_, _) =>
            {
                string fmt = (formatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? ".mp4";
                subCheck.IsEnabled = fmt == ".mp4";
            };

            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = string.Format("Cutscene_BatchExportHint".GetLocalized(), chapter.Title, videos.Length),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(formatCombo);
            content.Children.Add(subCheck);

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Cutscene_ExportChapter".GetLocalized(),
                Content = content,
                PrimaryButtonText = "Cutscene_ExportPickFolder".GetLocalized(),
                CloseButtonText = "CancelBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary
            };

            SetBusy(true);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed)
                return;

            string ext = (formatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? ".mp4";
            bool burnSub = subCheck.IsChecked == true;

            string? folder = await FilePickerService.PickFolderAsync(this,
                onError: _ => ShowNotice(InfoBarSeverity.Error, "FilePicker_CannotOpen".GetLocalized()));
            if (folder is null || _closed)
                return;

            EnsureDestinationOutsideGame(catalog.GameDirectory, Path.Combine(folder, "probe.tmp"));

            string? decoder = null;
            string? ffmpeg = null;
            if (ext != ".usm")
            {
                decoder = await ResolveOrSetupDecoderAsync(token);
                if (decoder is null || _closed)
                    return;

                ffmpeg = await ResolveMediaToolAsync(Ffmpeg, decoder);
                if (ext == ".mp4")
                {
                    ffmpeg ??= await ChooseMediaToolAsync(Ffmpeg, token);
                    if (ffmpeg is null || _closed)
                        return;
                }
            }

            SetBusy(true, "Cutscene_Exporting".GetLocalized());
            int exportedCount = 0;
            for (int i = 0; i < videos.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                CutsceneResourceRow item = videos[i];
                CutsceneAsset video = item.Video!;
                WorkStatus.Text = string.Format("Cutscene_BatchExportProgress".GetLocalized(),
                    i + 1, videos.Length, item.DisplayName);
                WorkProgress.IsIndeterminate = false;
                WorkProgress.Maximum = videos.Length;
                WorkProgress.Value = i;

                string safeStem = SanitizeFileName(item.DisplayName) + "_" + Path.GetFileNameWithoutExtension(video.Name);
                string destPath = Path.Combine(folder, safeStem + ext);

                DirectoryInfo workspace = Directory.CreateTempSubdirectory("FufuLauncher-CutsceneBatch-");
                try
                {
                    string input = await _service.CopyLocalCutsceneAsync(catalog, video, workspace.FullName, token);
                    if (ext == ".usm")
                    {
                        await CutsceneMediaService.ExportRawUsmAsync(input, destPath, token);
                    }
                    else
                    {
                        string decodedMkv = await CutsceneMediaService.DecodeAsync(decoder!, input, workspace.FullName, token);
                        CutsceneMediaTracks tracks = await Task.Run(() => CutsceneAudioTracks.ReadMediaTracks(decodedMkv), token);
                        int? subIdx = burnSub ? PickDefaultSubtitleIndex(tracks.SubtitleTracks) : null;
                        var subMode = subIdx is not null ? CutsceneSubtitleExportMode.BurnIn : CutsceneSubtitleExportMode.None;
                        await CutsceneMediaService.ExportAsync(decodedMkv, destPath, ffmpeg, audioTracks: null, subMode, subIdx, token);
                    }
                    exportedCount++;
                }
                finally
                {
                    try { workspace.Delete(recursive: true); } catch { }
                }
            }

            if (!_closed)
                ShowNotice(InfoBarSeverity.Success, string.Format("Cutscene_BatchExported".GetLocalized(), exportedCount));
        });
    }

    private static void EnsureDestinationOutsideGame(string gameDirectory, string destination)
    {
        string relative = Path.GetRelativePath(gameDirectory, destination);
        if (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Cutscene_ExportOutsideGame".GetLocalized());
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString().Trim();
    }

    private static async Task<string?> ResolveMediaToolAsync(MediaTool tool, string? companionToolPath = null)
    {
        var settings = App.GetService<ILocalSettingsService>();
        string? saved = (await settings.ReadSettingAsync(tool.Setting))?.ToString();
        string? resolved = CutsceneMediaService.ResolveToolPath(saved, tool.FileName, companionToolPath);
        if (resolved is not null && !string.Equals(saved, resolved, StringComparison.OrdinalIgnoreCase))
            await settings.SaveSettingAsync(tool.Setting, resolved);
        return resolved;
    }

    private async Task<string?> ResolveOrSetupDecoderAsync(CancellationToken token)
    {
        string? existing = await ResolveMediaToolAsync(Charlotte);
        if (existing is not null)
            return existing;

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new TextBlock
        {
            Text = "Cutscene_DecoderAutoHint".GetLocalized(),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = Charlotte.DownloadKey.GetLocalized(),
            NavigateUri = new Uri(Charlotte.Website)
        });

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Charlotte.TitleKey.GetLocalized(),
            Content = content,
            PrimaryButtonText = "Cutscene_AutoInstallBtn".GetLocalized(),
            SecondaryButtonText = "Cutscene_PickDecoder".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary
        };

        SetBusy(true);
        ContentDialogResult result = await dialog.ShowAsync();
        if (_closed || result == ContentDialogResult.None)
            return null;

        if (result == ContentDialogResult.Secondary)
            return await PickAndSaveToolAsync(Charlotte, token);

        SetBusy(true, "Cutscene_ToolDownloading".GetLocalized());
        var progress = new Progress<string>(msg =>
        {
            if (!_closed && _busy)
                WorkStatus.Text = msg;
        });
        var (decoderPath, ffmpegPath) = await Task.Run(
            () => CutsceneMediaService.DownloadAndInstallCharlotteAsync(progress, token), token);
        if (_closed)
            return null;

        var settings = App.GetService<ILocalSettingsService>();
        await settings.SaveSettingAsync(Charlotte.Setting, decoderPath);
        if (ffmpegPath is not null)
            await settings.SaveSettingAsync(Ffmpeg.Setting, ffmpegPath);

        return decoderPath;
    }

    private async Task<string?> ChooseMediaToolAsync(MediaTool tool, CancellationToken token)
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(new TextBlock
        {
            Text = tool.HintKey.GetLocalized(),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new HyperlinkButton
        {
            Content = tool.DownloadKey.GetLocalized(),
            NavigateUri = new Uri(tool.Website)
        });
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = tool.TitleKey.GetLocalized(),
            Content = content,
            PrimaryButtonText = "Cutscene_PickDecoder".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary
        };
        SetBusy(true);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed)
            return null;
        return await PickAndSaveToolAsync(tool, token);
    }

    private async Task<string?> PickAndSaveToolAsync(MediaTool tool, CancellationToken token)
    {
        var filters = new (string Label, string[] Extensions)[] { (tool.FileName, ["*.exe"]) };
        string? path = await FilePickerService.PickOpenFileAsync(this, filters,
            onError: _ => ShowNotice(InfoBarSeverity.Error, "FilePicker_CannotOpen".GetLocalized()));
        if (path is null || _closed)
            return null;
        token.ThrowIfCancellationRequested();
        if (!CutsceneMediaService.IsToolPath(path, tool.FileName))
            throw new InvalidOperationException(string.Format("Cutscene_InvalidDecoder".GetLocalized(), tool.FileName));

        var settings = App.GetService<ILocalSettingsService>();
        await settings.SaveSettingAsync(tool.Setting, path);
        if (tool == Charlotte)
        {
            string? siblingFfmpeg = CutsceneMediaService.ResolveToolPath(null, Ffmpeg.FileName, path);
            if (siblingFfmpeg is not null)
                await settings.SaveSettingAsync(Ffmpeg.Setting, siblingFfmpeg);
        }
        return path;
    }

    private async Task<(IReadOnlyList<int> AudioTracks, CutsceneSubtitleExportMode SubtitleMode, int? SubtitleIndex)?>
        ChooseExportOptionsAsync(CutsceneMediaTracks mediaTracks, bool isMp4)
    {
        IReadOnlyList<CutsceneAudioTrack> tracks = mediaTracks.AudioTracks;
        if (tracks.Count == 0)
            return ([], CutsceneSubtitleExportMode.None, null);

        var checkBoxes = new (int TrackIndex, CheckBox Control)[tracks.Count];
        var choices = new StackPanel { Spacing = 4 };
        for (int i = 0; i < tracks.Count; i++)
        {
            CutsceneAudioTrack track = tracks[i];
            var checkBox = new CheckBox { Content = GetAudioTrackLabel(track), IsChecked = true };
            choices.Children.Add(checkBox);
            checkBoxes[i] = (track.Index, checkBox);
        }

        var selectionHint = new InfoBar
        {
            IsClosable = false,
            Severity = InfoBarSeverity.Informational,
            Message = "Cutscene_ExportAudioRequired".GetLocalized()
        };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new ScrollViewer
        {
            Content = choices,
            MaxHeight = 220,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        content.Children.Add(selectionHint);

        ComboBox? subtitleModeBox = null;
        ComboBox? subtitleLangBox = null;
        if (isMp4 && mediaTracks.SubtitleTracks.Count > 0)
        {
            subtitleModeBox = new ComboBox
            {
                Header = "Cutscene_ExportSubtitleMode".GetLocalized(),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                SelectedIndex = 1
            };
            subtitleModeBox.Items.Add(new ComboBoxItem { Content = "Cutscene_ExportSubNone".GetLocalized(), Tag = CutsceneSubtitleExportMode.None });
            subtitleModeBox.Items.Add(new ComboBoxItem { Content = "Cutscene_ExportSubBurnIn".GetLocalized(), Tag = CutsceneSubtitleExportMode.BurnIn });
            subtitleModeBox.Items.Add(new ComboBoxItem { Content = "Cutscene_ExportSubSoft".GetLocalized(), Tag = CutsceneSubtitleExportMode.SoftText });

            subtitleLangBox = new ComboBox
            {
                Header = "Cutscene_PlayerSubtitleTrack".GetLocalized(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            foreach (CutsceneSubtitleTrack sub in mediaTracks.SubtitleTracks)
                subtitleLangBox.Items.Add(new ComboBoxItem { Content = GetSubtitleTrackLabel(sub), Tag = sub.Index });
            subtitleLangBox.SelectedIndex = PickDefaultSubtitleIndex(mediaTracks.SubtitleTracks) ?? 0;

            subtitleModeBox.SelectionChanged += (_, _) =>
            {
                var mode = (subtitleModeBox.SelectedItem as ComboBoxItem)?.Tag is CutsceneSubtitleExportMode m
                    ? m : CutsceneSubtitleExportMode.None;
                subtitleLangBox.IsEnabled = mode != CutsceneSubtitleExportMode.None;
            };

            content.Children.Add(subtitleModeBox);
            content.Children.Add(subtitleLangBox);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Cutscene_ExportAudioTracks".GetLocalized(),
            Content = content,
            PrimaryButtonText = "Cutscene_ExportLocal".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary
        };
        foreach (var (_, checkBox) in checkBoxes)
        {
            checkBox.Checked += UpdateSelection;
            checkBox.Unchecked += UpdateSelection;
        }

        SetBusy(true);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        int[] selectedAudio = checkBoxes.Where(entry => entry.Control.IsChecked == true)
            .Select(entry => entry.TrackIndex).ToArray();
        var selectedSubMode = (subtitleModeBox?.SelectedItem as ComboBoxItem)?.Tag is CutsceneSubtitleExportMode subMode
            ? subMode : CutsceneSubtitleExportMode.None;
        int? selectedSubIdx = (subtitleLangBox?.SelectedItem as ComboBoxItem)?.Tag is int idx ? idx : null;
        return (selectedAudio, selectedSubMode, selectedSubIdx);

        void UpdateSelection(object sender, RoutedEventArgs args)
        {
            bool hasSelection = checkBoxes.Any(entry => entry.Control.IsChecked == true);
            selectionHint.IsOpen = !hasSelection;
            dialog.IsPrimaryButtonEnabled = hasSelection;
        }
    }

    internal static string GetAudioTrackLabel(CutsceneAudioTrack track)
    {
        string? labelKey = track.Language?.Split('-')[0].ToLowerInvariant() switch
        {
            "zh" or "zho" or "chi" => "Cutscene_VoiceChinese",
            "en" or "eng" => "Cutscene_VoiceEnglish",
            "ja" or "jpn" => "Cutscene_VoiceJapanese",
            "ko" or "kor" => "Cutscene_VoiceKorean",
            _ => null
        };
        return labelKey?.GetLocalized() ?? track.Name
            ?? string.Format("Cutscene_AudioTrackNumber".GetLocalized(), track.Index + 1);
    }

    private static string GetSubtitleTrackLabel(CutsceneSubtitleTrack track)
    {
        string? code = track.Language?.ToLowerInvariant();
        return code switch
        {
            "chs" or "zh-hans" or "zh-cn" => "简体中文",
            "cht" or "zh-hant" or "zh-tw" => "繁體中文",
            "zh" or "zho" or "chi" => "Cutscene_VoiceChinese".GetLocalized(),
            "en" or "eng" or "en-us" => "Cutscene_VoiceEnglish".GetLocalized(),
            "ja" or "jp" or "jpn" => "Cutscene_VoiceJapanese".GetLocalized(),
            "ko" or "kr" or "kor" => "Cutscene_VoiceKorean".GetLocalized(),
            _ => track.Name ?? track.Language ?? $"字幕 {track.Index + 1}"
        };
    }

    private static int? PickDefaultSubtitleIndex(IReadOnlyList<CutsceneSubtitleTrack> tracks)
    {
        if (tracks.Count == 0)
            return null;
        string culture = ResourceExtensions.CurrentCulture ?? "zh-CN";
        string[] preferred = culture switch
        {
            "zh-TW" => ["cht", "zh-hant", "zh"],
            "zh-CN" => ["chs", "zh-hans", "zh"],
            "ja-JP" => ["jp", "ja", "jpn"],
            "ko-KR" => ["kr", "ko", "kor"],
            _ => ["en", "eng"]
        };
        foreach (string target in preferred)
        {
            for (int i = 0; i < tracks.Count; i++)
            {
                string? code = tracks[i].Language?.ToLowerInvariant();
                if (code is not null && (code == target || code.StartsWith(target, StringComparison.Ordinal)))
                    return i;
            }
        }
        return 0;
    }

    private async Task RunResourceActionAsync(string? status, string failureKey, Func<CancellationToken, Task> action)
    {
        if (_busy)
            return;
        using var work = new CancellationTokenSource();
        _work = work;
        SetBusy(true, status?.GetLocalized());
        Notice.IsOpen = false;
        try
        {
            await action(work.Token);
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneResourcesWindow), $"Resource action failed ({ex.GetType().Name}).");
            if (!_closed)
                ShowNotice(InfoBarSeverity.Error, ex is InvalidOperationException ? ex.Message : failureKey.GetLocalized());
        }
        finally
        {
            _work = null;
            if (!_closed)
                SetBusy(false);
        }
    }
}
