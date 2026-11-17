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
internal sealed class CutsceneResourceRow : ObservableObject
{
    private readonly Action _onSelectionChanged;
    private bool _isKept;
    private readonly string[] _linkedVideoTitles = [];
    private bool _showMediaActions;

    public CutsceneResourceRow(CutsceneAsset video, Action onSelectionChanged)
    {
        Video = video;
        Chapters = [video.Chapter];
        _isKept = video.IsInstalled;
        _onSelectionChanged = onSelectionChanged;
    }

    public CutsceneResourceRow(CutsceneAudioAsset audio, CutsceneNameCatalog names, Action onSelectionChanged)
    {
        Audio = audio;
        Chapters = audio.LinkedChapters;
        _linkedVideoTitles = audio.LinkedVideoNames.Select(names.FindByFilename)
            .OfType<CutsceneName>().Select(scene => CutsceneNames.Localize(scene.Titles))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        _isKept = audio.IsInstalled;
        _onSelectionChanged = onSelectionChanged;
    }

    public CutsceneAsset? Video { get; }
    public CutsceneAudioAsset? Audio { get; }
    public bool IsVideo => Video is not null;
    public bool IsAudio => Audio is not null;
    public IReadOnlyList<CutsceneChapter> Chapters { get; }
    public string Name => Video?.Name ?? Audio!.RelativePath;
    public string? AudioLanguageFolder => Audio?.LanguageFolder;
    public bool IsInstalled => Video?.IsInstalled ?? Audio!.IsInstalled;
    public bool IsStubbedLocked => Video?.IsStubbedLocked ?? Audio!.IsStubbedLocked;
    public long AssetSize => Video?.Asset.AssetSize ?? Audio!.Asset.AssetSize;
    public long DiskBytes => Video?.DiskBytes ?? Audio!.DiskBytes;
    public long DownloadBytes => Video?.DownloadBytes ?? Audio!.DownloadBytes;
    public bool CanRemove => Video?.CanRemove ?? Audio!.CanRemove;
    public bool CanToggle => CanRemove || !IsInstalled || RemoveUnusedTraveler;
    public string? ProtectionHint => CanToggle ? null
        : $"Cutscene_Protection_{(Video?.RemovalProtection ?? Audio!.RemovalProtection)}".GetLocalized();
    public bool RemoveUnusedTraveler { get; private set; }
    public bool IsRemovalPending => IsInstalled && !IsKept;
    public bool IsDownloadPending => !IsInstalled && IsKept;
    public Visibility LocalActionsVisibility => _showMediaActions && IsVideo && IsInstalled
        ? Visibility.Visible : Visibility.Collapsed;

    public string KindTagText => IsVideo
        ? "Cutscene_KindVideo".GetLocalized()
        : Audio!.LanguageKey.GetLocalized();

    public string DisplayName
    {
        get
        {
            if (Video is null)
                return (Audio!.IsQuestVoice ? "Cutscene_QuestAudioItemTitle" : "Cutscene_AudioItemTitle").GetLocalized();
            if (Video.Metadata is not { } metadata)
                return Path.GetFileNameWithoutExtension(Video.Name);

            string title = CutsceneNames.Localize(metadata.Titles);
            return Video.Traveler is CutsceneTraveler traveler ? $"{title} · {traveler.DisplayName()}" : title;
        }
    }

    public string SizeLabel => CutsceneResourcesWindow.FormatSize(AssetSize);

    public string SearchText => $"{Name} {DisplayName} {string.Join(" ", _linkedVideoTitles)}";

    public string StateLabel
    {
        get
        {
            string baseState = (IsRemovalPending ? "Cutscene_PendingRemoval"
                : IsDownloadPending ? "Cutscene_PendingDownload"
                : IsStubbedLocked ? "Cutscene_StubbedLocked"
                : !IsInstalled ? "Cutscene_NotInstalled"
                : "Cutscene_Installed").GetLocalized();
            baseState = ProtectionHint ?? baseState;

            if (Audio is not { } audio)
                return baseState;

            var parts = new List<string> { baseState };
            if (audio.IsSharedAcrossChapters)
                parts.Add(string.Format("Cutscene_SharedChaptersBadge".GetLocalized(), audio.LinkedChapters.Count));
            if (_linkedVideoTitles.Length > 0)
            {
                string preview = string.Join(" · ", _linkedVideoTitles.Take(2));
                if (_linkedVideoTitles.Length > 2)
                    preview += $" (+{_linkedVideoTitles.Length - 2})";
                parts.Add(string.Format("Cutscene_TraceChainVideos".GetLocalized(), preview));
            }

            return string.Join(" · ", parts);
        }
    }

    public void SetMediaActionsVisibility(bool show) =>
        SetProperty(ref _showMediaActions, show, nameof(LocalActionsVisibility));

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
                    OnPropertyChanged(nameof(ProtectionHint));
                }
                OnPropertyChanged(nameof(StateLabel));
                _onSelectionChanged();
            }
        }
    }

    public void SetTravelerRemoval(CutsceneTraveler? traveler)
    {
        if (!IsVideo)
            return;

        if (traveler is null || Video!.Traveler != traveler)
        {
            if (RemoveUnusedTraveler)
                IsKept = true;
            return;
        }

        if (!IsInstalled || IsRemovalPending)
            return;

        RemoveUnusedTraveler = true;
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ProtectionHint));
        IsKept = false;
    }

    public void QueueRemovalIfAllowed()
    {
        if (IsInstalled && CanRemove && IsKept)
            IsKept = false;
    }
}

internal sealed class CutsceneChapterRow(CutsceneChapter chapter, CutsceneResourceRow[] rows) : ObservableObject
{
    private bool _isExpanded;
    private Visibility _releaseVisibility = Visibility.Collapsed;
    private Visibility _countVisibility = Visibility.Collapsed;
    private bool _showMediaActions;

    public CutsceneChapter Chapter { get; } = chapter;
    public IReadOnlyList<CutsceneResourceRow> Rows { get; } = rows;
    public string Title => Chapter.Title;
    public string TypeLabel => Chapter.Label;
    public string ReleaseLabel => Chapter.ReleaseVersion is Version release ? $"v{release.ToString(2)}" : string.Empty;
    public Visibility ReleaseVisibility => _releaseVisibility;
    public Visibility CountVisibility => _countVisibility;
    public Visibility ExportChapterVisibility => _showMediaActions && Rows.Any(row => row.IsVideo && row.IsInstalled)
        ? Visibility.Visible : Visibility.Collapsed;
    public string SizeLabel => CutsceneResourcesWindow.FormatSize(Rows.Sum(row => row.AssetSize));
    public string CountText => Rows.Count.ToString();
    public string CountLabel => string.Format((Rows.Count == 1
        ? "Cutscene_ChapterClipCountSingle" : "Cutscene_ChapterClipCount").GetLocalized(), Rows.Count);
    public bool CanToggle => Rows.Any(row => row.CanToggle);
    public string? ProtectionHint => CanToggle ? null
        : string.Join("\n", Rows.Select(row => row.ProtectionHint).Distinct());

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool? IsKept => GetKeepState(Rows);

    public void SetDisplayOptions(bool showRelease, bool showCount, bool showMediaActions)
    {
        SetProperty(ref _releaseVisibility, showRelease && Chapter.ReleaseVersion is not null
            ? Visibility.Visible : Visibility.Collapsed, nameof(ReleaseVisibility));
        SetProperty(ref _countVisibility, showCount ? Visibility.Visible : Visibility.Collapsed, nameof(CountVisibility));
        SetProperty(ref _showMediaActions, showMediaActions, nameof(ExportChapterVisibility));
    }

    public void RefreshSelection()
    {
        OnPropertyChanged(nameof(IsKept));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ProtectionHint));
    }

    public static bool? GetKeepState(IReadOnlyList<CutsceneResourceRow> items)
    {
        int kept = items.Count(item => item.IsKept);
        return kept == 0 ? false : kept == items.Count ? true : null;
    }
}
