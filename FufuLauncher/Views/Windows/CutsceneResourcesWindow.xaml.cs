/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services;
using FufuLauncher.Services.GameServer;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace FufuLauncher.Views;

public sealed partial class CutsceneResourcesWindow : Window
{
    private static CutsceneResourcesWindow? _instance;
    private readonly GameUpdateService _service = App.GetService<GameUpdateService>();
    private CutsceneCatalog? _catalog;
    private CutsceneNameCatalog? _nameCatalog;
    private List<CutsceneResourceRow> _items = [];
    private List<CutsceneChapterRow> _chapters = [];
    private CutsceneCategory? _category;
    private CancellationTokenSource? _work;
    private bool _busy;
    private bool _closed;
    private bool _batchSelection;
    private bool _updatingFilters;

    private CutsceneResourcesWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureWindow();
        ApplyThemeAndTitleBar();
        ApplyWindowBackdrop();
        Closed += (_, _) =>
        {
            _closed = true;
            _work?.Cancel();
            _instance = null;
        };
    }

    internal static void Show()
    {
        _instance ??= new CutsceneResourcesWindow();
        if (_instance.AppWindow?.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }
        _instance.Activate();
    }

    private void ConfigureWindow()
    {
        try
        {
            var appWindow = AppWindow;
            if (appWindow == null)
                return;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
            if (File.Exists(iconPath))
                appWindow.SetIcon(iconPath);

            RectInt32 area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            double scale = WindowManagerHelper.GetScaleFactor(this);
            int width = Math.Min((int)Math.Round(1240 * scale), area.Width - 32);
            int height = Math.Min((int)Math.Round(900 * scale), area.Height - 32);
            appWindow.MoveAndResize(new RectInt32(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                width,
                height));

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)Math.Round(960 * scale);
                presenter.PreferredMinimumHeight = (int)Math.Round(600 * scale);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneResourcesWindow), $"ConfigureWindow failed: {ex.Message}");
        }
    }

    private void ApplyThemeAndTitleBar()
    {
        try
        {
            var theme = App.GetService<IThemeSelectorService>().Theme;
            RootGrid.RequestedTheme = theme;

            var isDark = theme == ElementTheme.Dark ||
                         (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            var titleBar = AppWindow?.TitleBar;
            if (titleBar == null)
                return;

            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonInactiveForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x66, 0x00, 0x00, 0x00);
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneResourcesWindow), $"ApplyTheme failed: {ex.Message}");
        }
    }

    private void ApplyWindowBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
                return;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneResourcesWindow), $"MicaBackdrop failed: {ex.Message}");
        }

        try
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneResourcesWindow), $"DesktopAcrylicBackdrop fallback failed: {ex.Message}");
            var isDark = RootGrid.RequestedTheme == ElementTheme.Dark ||
                         (RootGrid.RequestedTheme == ElementTheme.Default &&
                          Application.Current.RequestedTheme == ApplicationTheme.Dark);
            RootGrid.Background = new SolidColorBrush(isDark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243));
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        RootGrid.Loaded -= OnLoaded;
        await ScanAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await ScanAsync();

    private async Task ScanAsync()
    {
        if (_busy)
            return;

        using var work = new CancellationTokenSource();
        _work = work;
        SetBusy(true, "Cutscene_Scanning".GetLocalized());
        Notice.IsOpen = false;
        try
        {
            _nameCatalog = await Task.Run(CutsceneNameCatalogStore.LoadCurrent, work.Token);
            if (_closed)
                return;
            UpdateCatalogAvailability();
            if (_nameCatalog is not { } names)
            {
                BindCatalog(null);
                return;
            }

            string? gameDir = await GetGameDirectoryAsync();
            if (gameDir is null)
            {
                if (!_closed)
                {
                    BindCatalog(null);
                    ShowNotice(InfoBarSeverity.Warning, "Err_GamePathNotFound".GetLocalized());
                }
                return;
            }

            CutsceneCatalog catalog = await Task.Run(
                () => _service.ScanCutscenesAsync(gameDir, names, work.Token), work.Token);
            if (_closed)
                return;

            BindCatalog(catalog);
            if (!catalog.HasHistory)
                ShowNotice(InfoBarSeverity.Informational, "Cutscene_HistoryUnavailable".GetLocalized());
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneResourcesWindow), $"Scan failed ({ex.GetType().Name}).");
            if (!_closed)
            {
                BindCatalog(null);
                ShowNotice(InfoBarSeverity.Error, "Cutscene_ScanFailed".GetLocalized());
            }
        }
        finally
        {
            _work = null;
            if (!_closed)
                SetBusy(false);
        }
    }

    private static async Task<string?> GetGameDirectoryAsync()
    {
        string? path = await App.GetService<IGameConfigService>().GetSavedGamePathAsync();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = Path.GetFullPath(path);
        return File.Exists(path) ? Path.GetDirectoryName(path) : Path.TrimEndingDirectorySeparator(path);
    }

    private void BindCatalog(CutsceneCatalog? catalog)
    {
        _catalog = catalog;
        if (catalog is null || _nameCatalog is not { } names)
        {
            _items = [];
        }
        else
        {
            _items = catalog.Videos
                .Select(video => new CutsceneResourceRow(video, OnSelectionChanged))
                .Concat(catalog.Audios.Select(audio => new CutsceneResourceRow(audio, names, OnSelectionChanged)))
                .ToList();
        }
        PendingOnlyButton.IsChecked = false;
        UpdateCatalogAvailability();
        UpdateGameVersionHeader();
        BuildFilters();
        RefreshChapters();
    }

    private void UpdateCatalogAvailability()
    {
        bool ready = _nameCatalog is not null;
        CatalogSetup.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        ManagementHeader.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        ManagementList.Visibility = ManagementHeader.Visibility;
        ChangeActions.Visibility = _catalog is not null ? Visibility.Visible : Visibility.Collapsed;
        Footer.Visibility = ChangeActions.Visibility == Visibility.Visible || OperationProgress.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateGameVersionHeader()
    {
        string catalogLabel = _nameCatalog is { } names
            ? string.Format("Cutscene_CatalogStatusCached".GetLocalized(), names.GameVersion.ToString())
            : string.Empty;
        string historyText = _catalog?.EarlierVersion is { } earlier
            ? $" · {string.Format("Cutscene_HistoryBaseline".GetLocalized(), earlier)}"
            : string.Empty;
        GameVersionText.Text = _catalog is { } catalog
            ? $"{catalog.Scheme.DisplayName} · {catalog.Version}{historyText} · {catalogLabel}"
            : catalogLabel;
    }

    private string GetSelectedResourceScope() =>
        (ResourceScopeFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

    private string GetSelectedAudioLanguage() =>
        (AudioLanguageFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private IEnumerable<CutsceneResourceRow> GetScopeFilteredItems()
    {
        string scope = GetSelectedResourceScope();
        string lang = GetSelectedAudioLanguage();

        return _items.Where(row =>
        {
            if (scope == "Video" && !row.IsVideo)
                return false;
            if (scope == "Audio" && !row.IsAudio)
                return false;
            if (row.IsAudio && lang.Length > 0
                && !string.Equals(row.AudioLanguageFolder, lang, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        });
    }

    private void BuildFilters()
    {
        _updatingFilters = true;
        try
        {
            Filters.Items.Clear();
            CutsceneResourceRow[] scoped = GetScopeFilteredItems().ToArray();
            var byCategory = scoped.SelectMany(row => row.Chapters.Select(chapter => (row, chapter.Category)))
                .ToLookup(link => link.Category, link => link.row);
            if (_category is { } category && !byCategory.Contains(category))
                _category = null;

            SelectorBarItem? selected = AddFilter(null, "Cutscene_All".GetLocalized(), scoped.Length);
            foreach (var group in byCategory.OrderBy(group => group.Key))
                selected = AddFilter(group.Key, $"Cutscene_Category_{group.Key}".GetLocalized(), group.Distinct().Count()) ?? selected;

            Filters.SelectedItem = selected ?? Filters.Items.FirstOrDefault();
        }
        finally
        {
            _updatingFilters = false;
        }
    }

    private SelectorBarItem? AddFilter(CutsceneCategory? category, string label, int count)
    {
        bool isSelected = _category == category;
        var item = new SelectorBarItem
        {
            Text = $"{label} {count}",
            IsSelected = isSelected,
            IsEnabled = !_busy,
            Tag = category
        };
        Filters.Items.Add(item);
        return isSelected ? item : null;
    }

    private void OnCategoryFilterChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingFilters || sender.SelectedItem is null)
            return;
        CutsceneCategory? next = sender.SelectedItem.Tag is CutsceneCategory c ? c : null;
        if (_category == next)
            return;
        _category = next;
        RefreshChapters();
    }

    private void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_catalog is not null)
            RefreshChapters();
    }

    private void OnSortChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_catalog is not null)
            RefreshChapters();
    }

    private void OnResourceScopeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (AudioLanguageFilter is not null)
            AudioLanguageFilter.IsEnabled = !_busy && _nameCatalog is not null && GetSelectedResourceScope() != "Video";
        if (_catalog is not null)
        {
            BuildFilters();
            RefreshChapters();
        }
    }

    private void OnAudioLanguageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_catalog is not null)
        {
            BuildFilters();
            RefreshChapters();
        }
    }

    private void OnPendingOnlyClick(object sender, RoutedEventArgs args) => RefreshChapters();

    private bool ShowMediaActions => ShowPlaybackExport.IsChecked == true;

    private void OnDisplayOptionsChanged(object sender, RoutedEventArgs args) => UpdateDisplayOptions();

    private void UpdateDisplayOptions()
    {
        bool showRelease = ShowReleaseVersion.IsChecked == true;
        bool showCount = ShowClipCount.IsChecked == true;
        bool showMedia = ShowMediaActions;
        foreach (var row in _items)
            row.SetMediaActionsVisibility(showMedia);
        foreach (var chapter in _chapters)
            chapter.SetDisplayOptions(showRelease, showCount, showMedia);

        Visibility mediaVisibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
        MediaToolsSeparator.Visibility = mediaVisibility;
        AutoSetupToolsItem.Visibility = mediaVisibility;
        ChooseDecoderItem.Visibility = mediaVisibility;
        ChooseFfmpegItem.Visibility = mediaVisibility;
        ClearPreviewCacheItem.Visibility = mediaVisibility;
    }

    private void RefreshChapters()
    {
        string[] terms = SearchBox.Text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var expanded = _chapters.Where(chapter => chapter.IsExpanded).Select(chapter => chapter.Chapter).ToHashSet();
        bool pendingOnly = PendingOnlyButton.IsChecked == true;
        var chapters = GetScopeFilteredItems()
            .Where(row => !pendingOnly || row.IsRemovalPending || row.IsDownloadPending)
            .SelectMany(row => row.Chapters.Select(chapter => (row, chapter)))
            .Where(link => (_category is null || link.chapter.Category == _category)
                && MatchesSearch(link.row, link.chapter, terms))
            .GroupBy(link => link.chapter, link => link.row)
            .Select(group => new CutsceneChapterRow(
                group.Key,
                group.OrderBy(row => row.IsAudio).ThenBy(row => row.Name).ToArray())
            {
                IsExpanded = terms.Length > 0 || expanded.Contains(group.Key)
            });
        _chapters = OrderChaptersByRelease(chapters, descending: SortDirection.SelectedIndex == 1).ToList();
        UpdateDisplayOptions();
        ChapterList.ItemsSource = _chapters;
        EmptyText.Text = (_catalog is null ? "Cutscene_NothingLoaded"
            : pendingOnly ? "Cutscene_PendingEmpty" : "Cutscene_Empty").GetLocalized();
        EmptyText.Visibility = _chapters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateExpandButton();
        UpdateSummary();
    }

    private static bool MatchesSearch(CutsceneResourceRow row, CutsceneChapter chapter, string[] terms)
    {
        if (terms.Length == 0)
            return true;
        string search = $"{row.SearchText} {row.KindTagText} {chapter.Title} {chapter.Label}";
        return terms.All(term => search.Contains(term, StringComparison.CurrentCultureIgnoreCase));
    }

    private static IOrderedEnumerable<CutsceneChapterRow> OrderChaptersByRelease(IEnumerable<CutsceneChapterRow> chapters,
        bool descending)
    {
        var datedFirst = chapters.OrderBy(row => row.Chapter.ReleaseVersion is null);
        var byRelease = descending
            ? datedFirst.ThenByDescending(row => row.Chapter.ReleaseVersion)
            : datedFirst.ThenBy(row => row.Chapter.ReleaseVersion);
        var byCategory = byRelease.ThenBy(row => row.Chapter.Category);
        var byChapter = descending
            ? byCategory.ThenByDescending(row => row.Chapter.QuestChapterId)
            : byCategory.ThenBy(row => row.Chapter.QuestChapterId);
        return byChapter.ThenBy(row => row.Title, StringComparer.CurrentCulture);
    }

    private void OnKeepVisibleClick(object sender, RoutedEventArgs args) =>
        SetKept(_chapters.SelectMany(chapter => chapter.Rows).Distinct(), KeepVisible.IsChecked == true);

    private void OnKeepChapterClick(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox { DataContext: CutsceneChapterRow chapter } checkBox)
            SetKept(chapter.Rows, checkBox.IsChecked == true);
    }

    private void OnChapterBadgeContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        sender.Style = args.NewValue is CutsceneChapterRow row
            ? GetCategoryTextStyle(row.Chapter.Category) : (Style)RootGrid.Resources["CutsceneCategoryTextStyle"];
    }

    private Style GetCategoryTextStyle(CutsceneCategory category) =>
        (Style)RootGrid.Resources[$"Cutscene{category}TextStyle"];

    private void OnToggleChapterExpandClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: CutsceneChapterRow chapter })
        {
            chapter.IsExpanded = !chapter.IsExpanded;
            UpdateExpandButton();
        }
    }

    private void OnRemoveAetherClick(object sender, RoutedEventArgs args) => SetTravelerRemoval(CutsceneTraveler.Aether);

    private void OnRemoveLumineClick(object sender, RoutedEventArgs args) => SetTravelerRemoval(CutsceneTraveler.Lumine);

    private void OnClearTravelerClick(object sender, RoutedEventArgs args) => SetTravelerRemoval(null);

    private void SetTravelerRemoval(CutsceneTraveler? traveler)
    {
        if (_busy)
            return;

        _batchSelection = true;
        foreach (var row in _items)
            row.SetTravelerRemoval(traveler);
        _batchSelection = false;
        OnSelectionChanged();
    }

    private void OnQueueLinkedAudioClick(object sender, RoutedEventArgs args)
    {
        if (_busy)
            return;

        var videoKeptMap = _items
            .Where(row => row.IsVideo)
            .ToDictionary(row => row.Name, row => row.IsKept, StringComparer.OrdinalIgnoreCase);

        int queuedCount = 0;
        _batchSelection = true;
        foreach (var row in _items.Where(r => r.IsAudio && r.IsInstalled && r.CanRemove && r.IsKept))
        {
            CutsceneAudioAsset audio = row.Audio!;
            if (audio.LinkedVideoNames.Count == 0)
                continue;

            // Only queue audio if every linked video in this installation is uninstalled or queued for removal.
            bool allLinkedVideosRemoved = audio.LinkedVideoNames.All(vid =>
                !videoKeptMap.TryGetValue(vid, out bool kept) || !kept);
            if (allLinkedVideosRemoved)
            {
                row.IsKept = false;
                queuedCount++;
            }
        }
        _batchSelection = false;
        OnSelectionChanged();

        ShowNotice(
            queuedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational,
            queuedCount > 0
                ? string.Format("Cutscene_QueueLinkedAudioDone".GetLocalized(), queuedCount)
                : "Cutscene_QueueLinkedAudioNone".GetLocalized());
    }

    private void OnQueueAudioByLanguageClick(object sender, RoutedEventArgs args)
    {
        if (_busy || sender is not MenuFlyoutItem { Tag: string languageFolder })
            return;

        int queuedCount = 0;
        _batchSelection = true;
        foreach (var row in _items.Where(r => r.IsAudio
            && string.Equals(r.AudioLanguageFolder, languageFolder, StringComparison.OrdinalIgnoreCase)
            && r.IsInstalled && r.CanRemove && r.IsKept))
        {
            row.IsKept = false;
            queuedCount++;
        }
        _batchSelection = false;
        OnSelectionChanged();

        ShowNotice(
            queuedCount > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Informational,
            queuedCount > 0
                ? string.Format("Cutscene_QueueAudioByLangDone".GetLocalized(), queuedCount)
                : "Cutscene_QueueAudioByLangNone".GetLocalized());
    }

    private void OnExpandClick(object sender, RoutedEventArgs args)
    {
        bool expand = _chapters.Any(chapter => !chapter.IsExpanded);
        foreach (var chapter in _chapters)
            chapter.IsExpanded = expand;
        UpdateExpandButton();
    }

    private void UpdateExpandButton()
    {
        bool allExpanded = _chapters.Count > 0 && _chapters.All(chapter => chapter.IsExpanded);
        ExpandButton.IsEnabled = _chapters.Count > 0 && !_busy;
        ExpandButtonText.Text = (allExpanded ? "Cutscene_CollapseAll" : "Cutscene_ExpandAll").GetLocalized();
        ExpandButtonIcon.Glyph = allExpanded ? "\uF2B6" : "\uF2A3";
    }

    private void SetKept(IEnumerable<CutsceneResourceRow> rows, bool keep)
    {
        _batchSelection = true;
        foreach (var row in rows)
            row.IsKept = keep;
        _batchSelection = false;
        OnSelectionChanged();
    }

    private void OnSelectionChanged()
    {
        if (_batchSelection)
            return;
        if (PendingOnlyButton.IsChecked == true)
        {
            RefreshChapters();
            return;
        }

        foreach (var chapter in _chapters)
            chapter.RefreshSelection();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var videos = _items.Where(row => row.IsVideo).ToArray();
        var audios = _items.Where(row => row.IsAudio).ToArray();
        long total = _items.Sum(row => row.AssetSize);
        int installedVideos = videos.Count(row => row.IsInstalled);
        int installedAudios = audios.Count(row => row.IsInstalled);
        long local = _items.Sum(row => row.DiskBytes);
        var removal = _items.Where(row => row.IsRemovalPending).ToArray();
        var download = _items.Where(row => row.IsDownloadPending).ToArray();
        long removalBytes = removal.Sum(row => row.DiskBytes);
        long downloadBytes = download.Sum(row => row.DownloadBytes);
        long installDelta = download.Sum(row => row.AssetSize - row.DiskBytes);
        long net = installDelta - removalBytes;

        InstallSummary.Text = audios.Length > 0
            ? string.Format("Cutscene_InstallSummaryWithAudio".GetLocalized(),
                installedVideos, videos.Length, installedAudios, audios.Length, FormatSize(local), FormatSize(total))
            : string.Format("Cutscene_InstallSummary".GetLocalized(),
                installedVideos, videos.Length, FormatSize(local), FormatSize(total));

        NetChangeText.Text = net == 0 ? string.Empty : $"{(net < 0 ? "−" : "+")}{FormatSize(Math.Abs(net))}";
        RetainedColumn.Width = Star(local - removalBytes);
        RemovalColumn.Width = Star(removalBytes);
        DownloadColumn.Width = Star(installDelta);
        AbsentColumn.Width = Star(total == 0 ? 1 : total - local - installDelta);
        RemovalText.Text = string.Format("Cutscene_DeleteSummary".GetLocalized(), FormatSize(removalBytes));
        DownloadText.Text = string.Format("Cutscene_DownloadSummary".GetLocalized(), FormatSize(downloadBytes));
        ApplyButton.IsEnabled = !_busy && (removal.Length > 0 || download.Length > 0);
        int pendingCount = removal.Length + download.Length;
        PendingOnlyButton.Content = string.Format("Cutscene_PendingFilter".GetLocalized(), pendingCount);
        PendingOnlyButton.Visibility = pendingCount > 0 || PendingOnlyButton.IsChecked == true
            ? Visibility.Visible : Visibility.Collapsed;

        RemoveAetherItem.IsEnabled = videos.Any(row => row.IsInstalled && row.Video!.Traveler == CutsceneTraveler.Aether);
        RemoveLumineItem.IsEnabled = videos.Any(row => row.IsInstalled && row.Video!.Traveler == CutsceneTraveler.Lumine);
        ClearTravelerItem.IsEnabled = videos.Any(row => row.RemoveUnusedTraveler);

        bool hasRemovableAudios = audios.Any(row => row.IsInstalled && row.CanRemove);
        QueueLinkedAudioItem.IsEnabled = hasRemovableAudios;
        QueueChineseAudioItem.IsEnabled = audios.Any(r => r.AudioLanguageFolder == "Chinese" && r.IsInstalled && r.CanRemove);
        QueueEnglishAudioItem.IsEnabled = audios.Any(r => r.AudioLanguageFolder == "English(US)" && r.IsInstalled && r.CanRemove);
        QueueJapaneseAudioItem.IsEnabled = audios.Any(r => r.AudioLanguageFolder == "Japanese" && r.IsInstalled && r.CanRemove);
        QueueKoreanAudioItem.IsEnabled = audios.Any(r => r.AudioLanguageFolder == "Korean" && r.IsInstalled && r.CanRemove);
        TravelerButton.IsEnabled = !_busy && (RemoveAetherItem.IsEnabled || RemoveLumineItem.IsEnabled || hasRemovableAudios);

        LockCleanedItem.IsEnabled = !_busy && _items.Any(row => !row.IsInstalled);
        UnlockCleanedItem.IsEnabled = !_busy && _items.Any(row => row.IsStubbedLocked);

        var visible = _chapters.SelectMany(chapter => chapter.Rows).Distinct().ToArray();
        KeepVisible.IsEnabled = !_busy && visible.Any(row => row.CanToggle);
        KeepVisible.IsChecked = CutsceneChapterRow.GetKeepState(visible);
        VisibleSummary.Text = string.Format("Cutscene_VisibleSummary".GetLocalized(),
            visible.Count(row => row.IsKept), visible.Length);
    }

    private async void OnApplyClick(object sender, RoutedEventArgs args)
    {
        CutsceneCatalog? catalog = _catalog;
        if (_busy || catalog is null)
            return;

        CutsceneChange[] videoChanges = _items
            .Where(row => row.IsVideo && (row.IsRemovalPending || row.IsDownloadPending))
            .Select(row => new CutsceneChange(row.Video!, row.IsKept, row.RemoveUnusedTraveler))
            .ToArray();
        CutsceneAudioChange[] audioChanges = _items
            .Where(row => row.IsAudio && (row.IsRemovalPending || row.IsDownloadPending))
            .Select(row => new CutsceneAudioChange(row.Audio!, row.IsKept))
            .ToArray();
        bool useStubLock = StubLockProtectionCheck.IsChecked == true;

        using var work = new CancellationTokenSource();
        _work = work;
        SetBusy(true);
        try
        {
            string message = (videoChanges.Any(c => !c.Keep && !c.RemoveUnusedTraveler) || audioChanges.Any(c => !c.Keep))
                ? string.Format((useStubLock ? "Cutscene_ConfirmStubLock" : "Cutscene_Confirm").GetLocalized(),
                    RemovalText.Text, DownloadText.Text)
                : $"{RemovalText.Text} · {DownloadText.Text}";
            if (videoChanges.FirstOrDefault(change => change.RemoveUnusedTraveler)?.Video.Traveler is CutsceneTraveler traveler)
                message += "\n\n" + string.Format("Cutscene_ConfirmTraveler".GetLocalized(), traveler.DisplayName());
            if (audioChanges.Any(c => !c.Keep && c.Audio.IsSharedAcrossChapters))
                message += "\n\n" + "Cutscene_ConfirmSharedAudio".GetLocalized();

            var dialog = new ContentDialog
            {
                Title = "Cutscene_Apply".GetLocalized(),
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "Cutscene_Apply".GetLocalized(),
                CloseButtonText = "CancelBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || _closed)
                return;

            if (!string.Equals(await GetGameDirectoryAsync(), catalog.GameDirectory, StringComparison.OrdinalIgnoreCase))
            {
                ShowNotice(InfoBarSeverity.Warning, "Cutscene_RescanRequired".GetLocalized());
                return;
            }

            Notice.IsOpen = false;
            SetBusy(true, "Cutscene_Apply".GetLocalized());
            var progress = new Progress<GameServerConversionProgress>(UpdateWorkProgress);
            await Task.Run(() => _service.ApplyResourceChangesAsync(
                catalog, videoChanges, audioChanges, useStubLock, progress, work.Token), work.Token);
            if (!_closed)
                ShowNotice(InfoBarSeverity.Success, "Cutscene_Applied".GetLocalized());
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested)
        {
            if (!_closed)
                ShowNotice(InfoBarSeverity.Informational, "Cutscene_Stopped".GetLocalized());
        }
        catch (Exception ex)
        {
            if (!_closed)
            {
                string message = ex.Message.Contains("://", StringComparison.Ordinal)
                    ? "Cutscene_ApplyFailed".GetLocalized() : ex.Message;
                ShowNotice(InfoBarSeverity.Error, message);
            }
        }
        finally
        {
            _work = null;
            if (!_closed)
            {
                if (OperationProgress.Visibility == Visibility.Visible)
                {
                    try
                    {
                        CutsceneCatalog refreshed = await Task.Run(() => _service.RefreshCutscenes(catalog));
                        if (!_closed)
                            BindCatalog(refreshed);
                    }
                    catch (Exception)
                    {
                        if (!_closed)
                        {
                            BindCatalog(null);
                            ShowNotice(InfoBarSeverity.Warning, "Cutscene_RescanRequired".GetLocalized());
                        }
                    }
                }
                if (!_closed)
                    SetBusy(false);
            }
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs args) => _work?.Cancel();

    private void UpdateWorkProgress(GameServerConversionProgress value)
    {
        if (_closed || !_busy)
            return;
        WorkStatus.Text = value.Stage;
        WorkProgress.IsIndeterminate = value.TotalChunks == 0;
        WorkProgress.Value = value.Percent;
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        ChapterList.IsEnabled = !busy;
        bool canManage = !busy && _nameCatalog is not null;
        ResourceActionsButton.IsEnabled = canManage;
        RefreshButton.IsEnabled = canManage;
        QuickCatalogUpdateLink.IsEnabled = canManage;
        FetchCatalogButton.IsEnabled = !busy;
        ImportCatalogButton.IsEnabled = !busy;
        SearchBox.IsEnabled = canManage;
        ShowReleaseVersion.IsEnabled = canManage;
        ShowClipCount.IsEnabled = canManage;
        ShowPlaybackExport.IsEnabled = canManage;
        ResourceScopeFilter.IsEnabled = !busy;
        AudioLanguageFilter.IsEnabled = !busy && GetSelectedResourceScope() != "Video";
        SortDirection.IsEnabled = !busy;
        PendingOnlyButton.IsEnabled = !busy;
        StubLockProtectionCheck.IsEnabled = !busy;
        Filters.IsEnabled = !busy;
        foreach (SelectorBarItem filter in Filters.Items)
            filter.IsEnabled = !busy;
        OperationProgress.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = OperationProgress.Visibility;
        WorkStatus.Text = status ?? string.Empty;
        WorkProgress.IsIndeterminate = busy;
        UpdateCatalogAvailability();
        UpdateExpandButton();
        UpdateSummary();
    }

    private void ShowNotice(InfoBarSeverity severity, string message)
    {
        Notice.Severity = severity;
        Notice.Message = message;
        Notice.IsOpen = true;
    }

    private static GridLength Star(long bytes) => new(Math.Max(0, bytes), GridUnitType.Star);

    internal static string FormatSize(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.##} GB"
        : $"{bytes / (double)(1L << 20):0.##} MB";
}
