/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;

namespace FufuLauncher.Models;

public partial class GachaDisplayItem : ObservableObject
{
    public string Name
    {
        get; set;
    }
    public string Type
    {
        get; set;
    }
    public string PoolType
    {
        get; set;
    }
    public int Rank
    {
        get; set;
    }

    public int Count
    {
        get; set;
    }

    public string Time
    {
        get; set;
    }

    public string LastGetTime
    {
        get; set;
    }

    [ObservableProperty] private string _imageUrl;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ElementImageSource))] private string _elementUrl;

    public ImageSource ElementImageSource
    {
        get
        {
            if (string.IsNullOrEmpty(ElementUrl)) return null;
            try
            {
                return new BitmapImage(new Uri(ElementUrl));
            }
            catch
            {
                return null;
            }
        }
    }

    public SolidColorBrush RarityBackground => Rank switch
    {
        5 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 198, 160, 96)),
        4 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 149, 118, 193)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 168, 209))
    };

    public SolidColorBrush RarityColorHex => Rank switch
    {
        5 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 198, 160, 96)),
        4 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 149, 118, 193)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 168, 209))
    };

    [ObservableProperty] private PityStatus _pityStatus;

    public int PityMaximum => Rank == 5 ? (PoolType == "302" ? 80 : 90) : 10;

    // 进度条颜色与抽数文字颜色（CountToColorConverter）一一对应：
    // 欧气十足(≤30 抽)绿色、中段(≤60 抽)橙色、保底区间(>60 抽)红色
    public SolidColorBrush ProgressBarColor => Count switch
    {
        <= 30 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 50, 205, 50)),
        <= 60 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 128, 0)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 0, 0))
    };

    public SolidColorBrush PityStatusBrush => PityStatus switch
    {
        PityStatus.LostPity => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100)),
        PityStatus.Guaranteed => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 180, 100)),
        PityStatus.SmallPity => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 46, 204, 64)),
        PityStatus.Up => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 100, 200, 255)),
        _ => null
    };

    public string PityStatusText => PityStatus switch
    {
        PityStatus.LostPity => "歪",
        PityStatus.Guaranteed => "大保底",
        PityStatus.SmallPity => "小保底",
        PityStatus.Up => "UP",
        _ => ""
    };
}

public class ScrapedMetadata
{
    public string Name
    {
        get; set;
    }
    public string ImgSrc
    {
        get; set;
    }
    public string ElementSrc
    {
        get; set;
    }
    public string Type
    {
        get; set;
    }
    public string Rank
    {
        get; set;
    }
    public string ItemId
    {
        get; set;
    }

    public SolidColorBrush RarityBackground => Rank switch
    {
        "5" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 198, 160, 96)),
        "4" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 149, 118, 193)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 168, 209))
    };
}

public class GachaKpiItem
{
    public string Glyph { get; set; }
    public string Label { get; set; }
    public string Value { get; set; }
    public string Hint { get; set; }
}

public class GachaChartPoint
{
    public string Label { get; set; }
    public string SubLabel { get; set; }
    public double Value { get; set; }
    public double Percentage { get; set; }
    public double BarWidth { get; set; }
    public double BarHeight { get; set; }
    public string DisplayValue { get; set; }
    public int ColorIndex { get; set; }
}

public class GachaPieSlice
{
    public string Label { get; set; }
    public string DisplayValue { get; set; }
    public double Percentage { get; set; }
    public double StartAngle { get; set; }
    public double SweepAngle { get; set; }
    public int ColorIndex { get; set; }
}

public class GachaAnalysisDashboard
{
    public ObservableCollection<GachaKpiItem> KpiItems { get; set; } = new();
    public ObservableCollection<GachaChartPoint> PoolDistribution { get; set; } = new();
    public ObservableCollection<GachaChartPoint> RarityDistribution { get; set; } = new();
    public ObservableCollection<GachaPieSlice> PoolPieSlices { get; set; } = new();
    public ObservableCollection<GachaPieSlice> RarityPieSlices { get; set; } = new();
    public ObservableCollection<GachaChartPoint> RecentFiveStarPities { get; set; } = new();
    public ObservableCollection<GachaChartPoint> FourStarTopItems { get; set; } = new();
    public ObservableCollection<GachaChartPoint> PityBuckets { get; set; } = new();
    public ObservableCollection<GachaChartPoint> MonthlyPulls { get; set; } = new();

    public int TenPullCount { get; set; }
    public int TenPullGoldCount { get; set; }
    public string TenPullGoldRateText { get; set; } = "0%";
    public int SinglePullCount { get; set; }
    public int SinglePullGoldCount { get; set; }
    public string SinglePullGoldRateText { get; set; } = "0%";
    public double AverageFiveStarCharacterPulls { get; set; }
    public string AverageFiveStarCharacterPullsText { get; set; } = "0";
    public int AverageFiveStarCharacterPrimogems { get; set; }
    public string AverageFiveStarCharacterPrimogemsText { get; set; } = "0";
    public string AverageFiveStarPullsText { get; set; } = "0";
    public string CurrentDeepestPityText { get; set; } = "0 抽";
    public string CurrentDeepestPityHint { get; set; } = "暂无五星垫数";
    public string BestFiveStarPityText { get; set; } = "0 抽";
    public string BestFiveStarPityHint { get; set; } = "暂无五星记录";
    public string WorstFiveStarPityText { get; set; } = "0 抽";
    public string WorstFiveStarPityHint { get; set; } = "暂无五星记录";
    public string ActiveMonthCountText { get; set; } = "0";
    public string MonthlyAveragePullsText { get; set; } = "0";
    public string BusiestMonthText { get; set; } = "暂无";
    public string BusiestMonthPullsText { get; set; } = "0 抽";
    public string DateRangeText { get; set; } = "暂无记录";

    public static GachaAnalysisDashboard Empty() => new()
    {
        KpiItems =
        {
            new GachaKpiItem { Glyph = "\uE8EF", Label = "总抽数", Value = "0", Hint = "暂无祈愿记录" },
            new GachaKpiItem { Glyph = "\uE8C7", Label = "原石估算", Value = "0", Hint = "按每抽 160 原石" },
            new GachaKpiItem { Glyph = "\uE735", Label = "五星出货", Value = "0", Hint = "0%" },
            new GachaKpiItem { Glyph = "\uE734", Label = "四星出货", Value = "0", Hint = "0%" },
            new GachaKpiItem { Glyph = "\uE7C1", Label = "五星角色均耗", Value = "0 抽", Hint = "暂无五星角色" },
            new GachaKpiItem { Glyph = "\uE7C1", Label = "五星均抽", Value = "0 抽", Hint = "暂无五星记录" },
            new GachaKpiItem { Glyph = "\uE8A5", Label = "当前最深垫数", Value = "0 抽", Hint = "暂无五星垫数" },
            new GachaKpiItem { Glyph = "\uE74C", Label = "最欧五星", Value = "0 抽", Hint = "暂无五星记录" },
            new GachaKpiItem { Glyph = "\uE7BA", Label = "最非五星", Value = "0 抽", Hint = "暂无五星记录" },
            new GachaKpiItem { Glyph = "\uE787", Label = "活跃月份", Value = "0", Hint = "月均 0 抽" }
        }
    };
}

