/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.ViewModels;

public sealed class DcNamedIcon
{
    public string Name
    {
        get;
        init;
    } = string.Empty;

    public string? Icon
    {
        get;
        init;
    }

    public int Star
    {
        get;
        init;
    } = 5;

    public string StarTag => Star == 5 ? "star5" : "star4";
    public bool HasIcon => !string.IsNullOrEmpty(Icon);
}

public sealed class DcWishBanner
{
    public string? Avatar
    {
        get;
        init;
    }

    public string Version
    {
        get;
        init;
    } = string.Empty;

    public string TimeText
    {
        get;
        init;
    } = string.Empty;

    public string StatusText
    {
        get;
        init;
    } = string.Empty;

    public string StatusTag
    {
        get;
        init;
    } = "flat";

    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    public string RelativeText
    {
        get;
        init;
    } = string.Empty;

    public List<DcNamedIcon> Star5
    {
        get;
        init;
    } = new();

    public List<DcNamedIcon> Star4
    {
        get;
        init;
    } = new();

    public bool HasStar4 => Star4.Count > 0;

    public string SearchKey
    {
        get;
        init;
    } = string.Empty;
}

public sealed class DcRerunCard
{
    public string Name
    {
        get;
        init;
    } = string.Empty;

    public string? Avatar
    {
        get;
        init;
    }

    public int Star
    {
        get;
        init;
    }

    public string StarTag => Star == 5 ? "star5" : "star4";

    public string DaysText
    {
        get;
        init;
    } = string.Empty;

    public string DaysCaption
    {
        get;
        init;
    } = string.Empty;

    public string AvgDaysText
    {
        get;
        init;
    } = string.Empty;

    public string UpTimesText
    {
        get;
        init;
    } = string.Empty;

    public string MaxGapText
    {
        get;
        init;
    } = string.Empty;

    public string MinGapText
    {
        get;
        init;
    } = string.Empty;

    public string UrgencyText
    {
        get;
        init;
    } = string.Empty;

    public string UrgencyTag
    {
        get;
        init;
    } = "flat";

    public string ForecastText
    {
        get;
        init;
    } = string.Empty;

    public double ProgressValue
    {
        get;
        init;
    }

    public List<string> History
    {
        get;
        init;
    } = new();

    public bool HasHistory => History.Count > 0;
    public string HistoryText => string.Join(" · ", History);

    public double SortUrgency
    {
        get;
        init;
    }

    public double SortDays
    {
        get;
        init;
    }

    public double SortInterval
    {
        get;
        init;
    }
}