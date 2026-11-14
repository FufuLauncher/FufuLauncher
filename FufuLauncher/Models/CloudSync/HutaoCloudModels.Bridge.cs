using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public sealed partial class HutaoGachaEntrySelectionItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _roleTitle = string.Empty;

    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public int ItemCount
    {
        get;
        init;
    }

    public bool Excluded
    {
        get;
        init;
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(RoleTitle) ? RoleTitle : $"UID {Uid}";

    partial void OnRoleTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    public string CountSummary => string.Format(
        "HutaoSync_GachaEntryCountFormat".GetLocalized(),
        ItemCount);
}

public sealed partial class FufuCloudUigfUidItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _roleTitle = string.Empty;

    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public int RecordCount
    {
        get;
        init;
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(RoleTitle) ? RoleTitle : $"UID {Uid}";

    partial void OnRoleTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    public string DetailText => string.Format(
        "HutaoSync_GachaEntryCountFormat".GetLocalized(),
        RecordCount);
}

public sealed partial class FufuCloudChallengeItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _roleDisplayName = string.Empty;

    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public int Mode
    {
        get;
        init;
    }

    public string ScheduleId
    {
        get;
        init;
    } = string.Empty;

    public bool IsLikelyCurrent
    {
        get;
        init;
    }

    public bool CanUpload
    {
        get;
        init;
    }

    public string ModeLabel
    {
        get;
        init;
    } = string.Empty;

    public string TitleText => !string.IsNullOrWhiteSpace(RoleDisplayName)
        ? $"{ModeLabel} · {RoleDisplayName}"
        : $"{ModeLabel} · UID {Uid}";

    partial void OnRoleDisplayNameChanged(string value) => OnPropertyChanged(nameof(TitleText));

    public string PeriodText
    {
        get;
        init;
    } = string.Empty;

    public string SummaryText
    {
        get;
        init;
    } = string.Empty;

    public string StatusBadgeText
    {
        get;
        init;
    } = string.Empty;

    public string Payload
    {
        get;
        init;
    } = string.Empty;
}

public sealed class HutaoBridgeTransferSummary
{
    public string TargetSlot
    {
        get;
        init;
    } = string.Empty;

    public int UidCount
    {
        get;
        init;
    }

    public int TransferredRecords
    {
        get;
        init;
    }

    public int TotalMergedRecords
    {
        get;
        init;
    }

    public int SkippedRecords
    {
        get;
        init;
    }
}

public sealed class HutaoChallengeUploadSummary
{
    public int UploadedCount
    {
        get;
        init;
    }

    public int GachaExtendedCount
    {
        get;
        init;
    }

    public int SkippedPastCount
    {
        get;
        init;
    }

    public int SkippedIncompleteCount
    {
        get;
        init;
    }

    public int FailedCount
    {
        get;
        init;
    }

    public string DetailMessage
    {
        get;
        init;
    } = string.Empty;
}
