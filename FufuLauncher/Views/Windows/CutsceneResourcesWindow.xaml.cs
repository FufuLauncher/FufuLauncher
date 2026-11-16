/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services;
using FufuLauncher.Services.GameServer;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace FufuLauncher.Views;

public sealed partial class CutsceneResourcesWindow : Window
{
    private static CutsceneResourcesWindow? _instance;
    private readonly GameUpdateService _service = App.GetService<GameUpdateService>();
    private CutsceneCatalog? _catalog;
    private List<CutsceneVideoRow> _videos = [];
    private List<CutsceneChapterRow> _chapters = [];
    private CutsceneCategory? _category;
    private CancellationTokenSource? _work;
    private bool _busy;
    private bool _closed;
    private bool _batchSelection;

    private CutsceneResourcesWindow()
    {
        InitializeComponent();
        RootGrid.RequestedTheme = App.GetService<IThemeSelectorService>().Theme;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(920, area.Width - 32);
        int height = Math.Min(920, area.Height - 32);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2,
            width, height));
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
        _instance.Activate();
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

            CutsceneCatalog catalog = await Task.Run(() => _service.ScanCutscenesAsync(gameDir, work.Token), work.Token);
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
        _videos = catalog?.Videos.Select(video => new CutsceneVideoRow(video, OnSelectionChanged)).ToList() ?? [];
        PendingOnlyButton.IsChecked = false;
        if (_category is CutsceneCategory category && !_videos.Any(item => item.Video.Chapter.Category == category))
            _category = null;
        GameVersionText.Text = catalog is null ? string.Empty
            : $"{catalog.Scheme.DisplayName} · {catalog.Version}";
        BuildFilters();
        RefreshChapters();
    }

    private void BuildFilters()
    {
        Filters.Children.Clear();
        AddFilter(null, "Cutscene_All".GetLocalized(), _videos.Count);
        foreach (var group in _videos.GroupBy(video => video.Video.Chapter.Category).OrderBy(group => group.Key))
            AddFilter(group.Key, group.First().Video.Chapter.Label, group.Count());
    }

    private void AddFilter(CutsceneCategory? category, string label, int count)
    {
        var button = new ToggleButton
        {
            IsChecked = _category == category,
            Content = $"{label} {count}",
            CornerRadius = new CornerRadius(14),
            Tag = category
        };
        if (category is CutsceneCategory cutsceneCategory)
        {
            button.Style = GetCategoryBadgeStyle(cutsceneCategory);
            button.Template = (ControlTemplate)RootGrid.Resources["CutsceneFilterTemplate"];
            button.FontSize = 14;
            button.Padding = new Thickness(12, 5, 12, 5);
            button.MinHeight = 32;
            button.UseSystemFocusVisuals = true;
        }
        button.Click += (_, _) =>
        {
            _category = category;
            foreach (ToggleButton filter in Filters.Children.Cast<ToggleButton>())
                filter.IsChecked = Equals(filter.Tag, category);
            RefreshChapters();
        };
        Filters.Children.Add(button);
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

    private void OnPendingOnlyClick(object sender, RoutedEventArgs args) => RefreshChapters();

    private void OnMetadataDisplayChanged(object sender, RoutedEventArgs args) => UpdateMetadataVisibility();

    private void UpdateMetadataVisibility()
    {
        bool showRelease = ShowReleaseVersion.IsChecked == true;
        bool showCount = ShowClipCount.IsChecked == true;
        foreach (var chapter in _chapters)
            chapter.SetMetadataVisibility(showRelease, showCount);
    }

    private void RefreshChapters()
    {
        string[] terms = SearchBox.Text.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var expanded = _chapters.Where(chapter => chapter.IsExpanded).Select(chapter => chapter.Chapter).ToHashSet();
        bool pendingOnly = PendingOnlyButton.IsChecked == true;
        var chapters = _videos
            .Where(row => (_category is null || row.Video.Chapter.Category == _category)
                && (!pendingOnly || row.IsRemovalPending || row.IsDownloadPending)
                && terms.All(term => row.SearchText.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
            .GroupBy(row => row.Video.Chapter)
            .Select(group => new CutsceneChapterRow(group.Key, group.OrderBy(row => row.Name).ToArray())
            {
                IsExpanded = terms.Length > 0 || expanded.Contains(group.Key)
            });
        _chapters = OrderChaptersByRelease(chapters, descending: SortDirection.SelectedIndex == 1).ToList();
        UpdateMetadataVisibility();
        ChapterList.ItemsSource = _chapters;
        EmptyText.Text = (_catalog is null ? "Cutscene_NothingLoaded"
            : pendingOnly ? "Cutscene_PendingEmpty" : "Cutscene_Empty").GetLocalized();
        EmptyText.Visibility = _chapters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateExpandButton();
        UpdateSummary();
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
        SetKept(_chapters.SelectMany(chapter => chapter.Rows), KeepVisible.IsChecked == true);

    private void OnKeepChapterClick(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox { DataContext: CutsceneChapterRow chapter } checkBox)
            SetKept(chapter.Rows, checkBox.IsChecked == true);
    }

    private void OnChapterBadgeContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        sender.Style = args.NewValue is CutsceneChapterRow row
            ? GetCategoryBadgeStyle(row.Chapter.Category) : (Style)RootGrid.Resources["CutsceneBadgeStyle"];
    }

    private Style GetCategoryBadgeStyle(CutsceneCategory category) =>
        (Style)RootGrid.Resources[$"Cutscene{category}BadgeStyle"];

    private void OnRemoveAetherClick(object sender, RoutedEventArgs args) => SetTravelerRemoval(CutsceneTraveler.Aether);

    private void OnRemoveLumineClick(object sender, RoutedEventArgs args) => SetTravelerRemoval(CutsceneTraveler.Lumine);

    private void OnClearTravelerClick(object sender, RoutedEventArgs args) => SetTravelerRemoval(null);

    private void SetTravelerRemoval(CutsceneTraveler? traveler)
    {
        if (_busy)
            return;

        _batchSelection = true;
        foreach (var row in _videos)
            row.SetTravelerRemoval(traveler);
        _batchSelection = false;
        OnSelectionChanged();
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
        ExpandButton.IsEnabled = _chapters.Count > 0 && !_busy;
        ExpandButton.Content = (_chapters.All(chapter => chapter.IsExpanded) && _chapters.Count > 0
            ? "Cutscene_CollapseAll" : "Cutscene_ExpandAll").GetLocalized();
    }

    private void SetKept(IEnumerable<CutsceneVideoRow> rows, bool keep)
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
        long total = _videos.Sum(row => row.Video.Asset.AssetSize);
        int installedCount = _videos.Count(row => row.Video.IsInstalled);
        long local = _videos.Sum(row => row.Video.LocalBytes);
        var removal = _videos.Where(row => row.IsRemovalPending).ToArray();
        var download = _videos.Where(row => row.IsDownloadPending).ToArray();
        long removalBytes = removal.Sum(row => row.Video.LocalBytes);
        long downloadBytes = download.Sum(row => row.Video.DownloadBytes);
        long installDelta = download.Sum(row => row.Video.Asset.AssetSize - row.Video.LocalBytes);
        long net = installDelta - removalBytes;
        InstallSummary.Text = string.Format("Cutscene_InstallSummary".GetLocalized(), installedCount, _videos.Count,
            FormatSize(local), FormatSize(total));
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
        RemoveAetherItem.IsEnabled = _videos.Any(row => row.Video.IsInstalled && row.Video.Traveler == CutsceneTraveler.Aether);
        RemoveLumineItem.IsEnabled = _videos.Any(row => row.Video.IsInstalled && row.Video.Traveler == CutsceneTraveler.Lumine);
        ClearTravelerItem.IsEnabled = _videos.Any(row => row.RemoveUnusedTraveler);
        TravelerButton.IsEnabled = !_busy && (RemoveAetherItem.IsEnabled || RemoveLumineItem.IsEnabled);

        var visible = _chapters.SelectMany(chapter => chapter.Rows).ToArray();
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

        CutsceneChange[] changes = _videos.Where(row => row.IsRemovalPending || row.IsDownloadPending)
            .Select(row => new CutsceneChange(row.Video, row.IsKept, row.RemoveUnusedTraveler)).ToArray();
        using var work = new CancellationTokenSource();
        _work = work;
        SetBusy(true);
        try
        {
            string message = changes.Any(change => !change.Keep && !change.RemoveUnusedTraveler)
                ? string.Format("Cutscene_Confirm".GetLocalized(), RemovalText.Text, DownloadText.Text)
                : $"{RemovalText.Text} · {DownloadText.Text}";
            if (changes.FirstOrDefault(change => change.RemoveUnusedTraveler)?.Video.Traveler is CutsceneTraveler traveler)
                message += "\n\n" + string.Format("Cutscene_ConfirmTraveler".GetLocalized(), traveler.DisplayName());

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
            await Task.Run(() => _service.ApplyCutsceneChangesAsync(catalog, changes, progress, work.Token), work.Token);
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
        RefreshButton.IsEnabled = !busy;
        SearchBox.IsEnabled = !busy;
        SortMode.IsEnabled = !busy;
        SortDirection.IsEnabled = !busy;
        PendingOnlyButton.IsEnabled = !busy;
        foreach (ToggleButton filter in Filters.Children.Cast<ToggleButton>())
            filter.IsEnabled = !busy;
        OperationProgress.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = OperationProgress.Visibility;
        WorkStatus.Text = status ?? string.Empty;
        WorkProgress.IsIndeterminate = busy;
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
