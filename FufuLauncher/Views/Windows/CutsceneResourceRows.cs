/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services.GameServer;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Views;

// Public properties are used by WinUI data templates.
internal sealed class CutsceneVideoRow(CutsceneAsset video, Action onSelectionChanged) : ObservableObject
{
    private bool _isKept = video.IsInstalled;

    public CutsceneAsset Video { get; } = video;
    public string Name => Video.Name;
    public string DisplayName
    {
        get
        {
            if (Video.Metadata is null)
                return Name;
            string title = CutsceneNames.Localize(Video.Metadata.Titles);
            return Video.Traveler is CutsceneTraveler traveler ? $"{title} · {traveler.DisplayName()}" : title;
        }
    }
    public string SizeLabel => CutsceneResourcesWindow.FormatSize(Video.Asset.AssetSize);
    public string SearchText => $"{Name} {DisplayName} {Video.Chapter.Title} {Video.Chapter.Label}";
    public bool CanToggle => Video.CanRemove || !Video.IsInstalled || RemoveUnusedTraveler;
    public bool RemoveUnusedTraveler { get; private set; }
    public bool IsRemovalPending => Video.IsInstalled && !IsKept;
    public bool IsDownloadPending => !Video.IsInstalled && IsKept;

    public bool IsKept
    {
        get => _isKept;
        set
        {
            if (CanToggle && SetProperty(ref _isKept, value))
            {
                if (value && RemoveUnusedTraveler)
                {
                    RemoveUnusedTraveler = false;
                    OnPropertyChanged(nameof(CanToggle));
                }
                OnPropertyChanged(nameof(StateLabel));
                onSelectionChanged();
            }
        }
    }

    public void SetTravelerRemoval(CutsceneTraveler? traveler)
    {
        if (traveler is null || Video.Traveler != traveler)
        {
            if (RemoveUnusedTraveler)
                IsKept = true;
            return;
        }

        if (!Video.IsInstalled || IsRemovalPending)
            return;

        RemoveUnusedTraveler = true;
        OnPropertyChanged(nameof(CanToggle));
        IsKept = false;
    }

    public string StateLabel => (IsRemovalPending ? "Cutscene_PendingRemoval"
        : IsDownloadPending ? "Cutscene_PendingDownload"
        : !Video.IsInstalled ? "Cutscene_NotInstalled"
        : Video.CanRemove ? "Cutscene_Installed" : "Cutscene_Protected").GetLocalized();
}

internal sealed class CutsceneChapterRow(CutsceneChapter chapter, CutsceneVideoRow[] rows) : ObservableObject
{
    private bool _isExpanded;
    private Visibility _releaseVisibility = Visibility.Collapsed;
    private Visibility _countVisibility = Visibility.Collapsed;

    public CutsceneChapter Chapter { get; } = chapter;
    public IReadOnlyList<CutsceneVideoRow> Rows { get; } = rows;
    public string Title => Chapter.Title;
    public string TypeLabel => Chapter.Label;
    public string ReleaseLabel => Chapter.ReleaseVersion is Version release ? $"v{release.ToString(2)}" : string.Empty;
    public Visibility ReleaseVisibility => _releaseVisibility;
    public Visibility CountVisibility => _countVisibility;
    public string SizeLabel => CutsceneResourcesWindow.FormatSize(Rows.Sum(row => row.Video.Asset.AssetSize));
    public string CountText => Rows.Count.ToString();
    public string CountLabel => string.Format((Rows.Count == 1
        ? "Cutscene_ChapterClipCountSingle" : "Cutscene_ChapterClipCount").GetLocalized(), Rows.Count);
    public bool CanToggle => Rows.Any(row => row.CanToggle);

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool? IsKept => GetKeepState(Rows);

    public void SetMetadataVisibility(bool showRelease, bool showCount)
    {
        SetProperty(ref _releaseVisibility, showRelease && Chapter.ReleaseVersion is not null
            ? Visibility.Visible : Visibility.Collapsed, nameof(ReleaseVisibility));
        SetProperty(ref _countVisibility, showCount ? Visibility.Visible : Visibility.Collapsed, nameof(CountVisibility));
    }

    public void RefreshSelection()
    {
        OnPropertyChanged(nameof(IsKept));
        OnPropertyChanged(nameof(CanToggle));
    }

    public static bool? GetKeepState(IReadOnlyList<CutsceneVideoRow> videos)
    {
        int kept = videos.Count(video => video.IsKept);
        return kept == 0 ? false : kept == videos.Count ? true : null;
    }
}
