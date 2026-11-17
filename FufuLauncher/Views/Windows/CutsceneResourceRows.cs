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
    private CutsceneName? _displayMetadata;
    private CutsceneChapter? _displayChapterOverride;
    private IReadOnlyList<string>? _displayLinkedVideoNames;

    public CutsceneResourceRow(CutsceneAsset video, Action onSelectionChanged)
    {
        Video = video;
        _displayMetadata = video.Metadata;
        _isKept = video.IsInstalled;
        _onSelectionChanged = onSelectionChanged;
    }

    public CutsceneResourceRow(CutsceneAudioAsset audio, Action onSelectionChanged)
    {
        Audio = audio;
        _isKept = audio.IsInstalled;
        _onSelectionChanged = onSelectionChanged;
    }

    public CutsceneAsset? Video { get; }
    public CutsceneAudioAsset? Audio { get; }
    public bool IsVideo => Video is not null;
    public bool IsAudio => Audio is not null;
    public CutsceneChapter Chapter => IsVideo
        ? (_displayMetadata?.Chapter ?? Video!.Chapter)
        : (_displayChapterOverride ?? Audio!.Chapter);
    public string Name => Video?.Name ?? Audio!.RelativePath;
    public string? VoiceGroupId => Video?.VoiceGroupId ?? Audio?.VoiceGroupId;
    public string? AudioLanguageFolder => Audio?.LanguageFolder;
    public bool IsInstalled => Video?.IsInstalled ?? Audio!.IsInstalled;
    public bool IsStubbedLocked => Video?.IsStubbedLocked ?? Audio!.IsStubbedLocked;
    public long AssetSize => Video?.Asset.AssetSize ?? Audio!.Asset.AssetSize;
    public long DiskBytes => Video?.DiskBytes ?? Audio!.DiskBytes;
    public long DownloadBytes => Video?.DownloadBytes ?? Audio!.DownloadBytes;
    public bool CanRemove => Video?.CanRemove ?? Audio!.CanRemove;
    public bool CanToggle => CanRemove || !IsInstalled || RemoveUnusedTraveler;
    public bool RemoveUnusedTraveler { get; private set; }
    public bool IsRemovalPending => IsInstalled && !IsKept;
    public bool IsDownloadPending => !IsInstalled && IsKept;
    public Visibility LocalActionsVisibility => IsVideo && IsInstalled ? Visibility.Visible : Visibility.Collapsed;

    public string KindTagText => IsVideo
        ? "Cutscene_KindVideo".GetLocalized()
        : Audio!.LanguageKey.GetLocalized();

    public string DisplayName
    {
        get
        {
            if (IsVideo)
            {
                if (_displayMetadata is null)
                    return Name;
                string title = CutsceneNames.Localize(_displayMetadata.Titles);
                return Video!.Traveler is CutsceneTraveler traveler ? $"{title} · {traveler.DisplayName()}" : title;
            }

            string lang = Audio!.LanguageKey.GetLocalized();
            return string.Format("Cutscene_AudioItemTitle".GetLocalized(), lang, Audio.VoiceGroupId, Audio.Name);
        }
    }

    public string SizeLabel => CutsceneResourcesWindow.FormatSize(AssetSize);

    public string SearchText
    {
        get
        {
            string linked = _displayLinkedVideoNames is { Count: > 0 }
                ? string.Join(" ", _displayLinkedVideoNames)
                : Audio is not null ? string.Join(" ", Audio.LinkedVideoNames) : string.Empty;
            return $"{Name} {DisplayName} {Chapter.Title} {Chapter.Label} {VoiceGroupId} {linked}";
        }
    }

    public string StateLabel
    {
        get
        {
            string baseState = (IsRemovalPending ? "Cutscene_PendingRemoval"
                : IsDownloadPending ? "Cutscene_PendingDownload"
                : IsStubbedLocked ? "Cutscene_StubbedLocked"
                : !IsInstalled ? "Cutscene_NotInstalled"
                : CanRemove ? "Cutscene_Installed" : "Cutscene_Protected").GetLocalized();

            if (IsVideo)
            {
                if (Video!.LinkedAudioPackages is { Count: > 0 } && !string.IsNullOrEmpty(Video.VoiceGroupId))
                    return $"{baseState} · {string.Format("Cutscene_VoiceGroupBadge".GetLocalized(), Video.VoiceGroupId)}";
                return baseState;
            }

            var parts = new List<string> { baseState };
            if (Audio!.IsSharedAcrossChapters)
                parts.Add(string.Format("Cutscene_SharedChaptersBadge".GetLocalized(), Audio.LinkedChapters.Count));

            if (_displayLinkedVideoNames is { Count: > 0 })
            {
                string preview = string.Join("、", _displayLinkedVideoNames.Take(2));
                if (_displayLinkedVideoNames.Count > 2)
                    preview += $" (+{_displayLinkedVideoNames.Count - 2})";
                parts.Add(string.Format("Cutscene_TraceChainVideos".GetLocalized(), preview));
            }
            else if (Audio.LinkedVideoNames.Count > 0)
            {
                string preview = string.Join("、", Audio.LinkedVideoNames.Take(2));
                if (Audio.LinkedVideoNames.Count > 2)
                    preview += $" (+{Audio.LinkedVideoNames.Count - 2})";
                parts.Add(string.Format("Cutscene_TraceChainVideos".GetLocalized(), preview));
            }
            else if (Audio.LinkedChapters.Count > 0)
            {
                parts.Add(string.Format("Cutscene_TraceChainChapter".GetLocalized(), Audio.LinkedChapters[0].Title));
            }

            return string.Join(" · ", parts);
        }
    }

    public void UseDisplayCatalog(CutsceneNameCatalog catalog)
    {
        if (IsVideo)
        {
            _displayMetadata = catalog.Find(Name, Video!.Traveler) ?? Video.Metadata;
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(StateLabel));
            return;
        }

        CutsceneAudioAsset audio = Audio!;
        CutsceneChapter? updatedChapter = null;
        var resolvedNames = new List<string>();
        foreach (string vidName in audio.LinkedVideoNames)
        {
            CutsceneName? scene = catalog.FindByFilename(vidName);
            if (scene is not null)
            {
                updatedChapter ??= scene.Chapter;
                resolvedNames.Add(CutsceneNames.Localize(scene.Titles));
            }
            else
            {
                resolvedNames.Add(Path.GetFileNameWithoutExtension(vidName));
            }
        }

        if (updatedChapter is null && audio.Chapter.QuestChapterId > 0)
            updatedChapter = catalog.FindChapterByQuestChapterId(audio.Chapter.QuestChapterId);

        _displayChapterOverride = updatedChapter;
        _displayLinkedVideoNames = resolvedNames.Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(StateLabel));
    }

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

    public CutsceneChapter Chapter { get; } = chapter;
    public IReadOnlyList<CutsceneResourceRow> Rows { get; } = rows;
    public string Title => Chapter.Title;
    public string TypeLabel => Chapter.Label;
    public string ReleaseLabel => Chapter.ReleaseVersion is Version release ? $"v{release.ToString(2)}" : string.Empty;
    public Visibility ReleaseVisibility => _releaseVisibility;
    public Visibility CountVisibility => _countVisibility;
    public Visibility ExportChapterVisibility => Rows.Any(row => row.IsVideo && row.IsInstalled)
        ? Visibility.Visible : Visibility.Collapsed;
    public string SizeLabel => CutsceneResourcesWindow.FormatSize(Rows.Sum(row => row.AssetSize));
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

    public static bool? GetKeepState(IReadOnlyList<CutsceneResourceRow> items)
    {
        int kept = items.Count(item => item.IsKept);
        return kept == 0 ? false : kept == items.Count ? true : null;
    }
}
