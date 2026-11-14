/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.ViewModels;

public sealed class DcCharacterCard
{
    public string Name
    {
        get;
        init;
    } = string.Empty;

    public string Ename
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

    public string StarText
    {
        get;
        init;
    } = string.Empty;

    public double MetaScore
    {
        get;
        init;
    }

    public string MetaScoreText
    {
        get;
        init;
    } = string.Empty;

    public string TierText
    {
        get;
        init;
    } = string.Empty;

    public string TierTag
    {
        get;
        init;
    } = "f";

    public string LevelText
    {
        get;
        init;
    } = string.Empty;

    public string ConstellationText
    {
        get;
        init;
    } = string.Empty;

    public string TalentText
    {
        get;
        init;
    } = string.Empty;

    public string SampleText
    {
        get;
        init;
    } = string.Empty;

    public string DamageText
    {
        get;
        init;
    } = string.Empty;

    public string DamageName
    {
        get;
        init;
    } = string.Empty;

    public string AbyssRateText
    {
        get;
        init;
    } = string.Empty;

    public string StygianRateText
    {
        get;
        init;
    } = string.Empty;

    public string OwnRateText
    {
        get;
        init;
    } = string.Empty;

    public List<DcRateRow> TopWeapons
    {
        get;
        init;
    } = new();

    public List<DcRateRow> TopArtifacts
    {
        get;
        init;
    } = new();

    public string HeadlineText
    {
        get;
        init;
    } = string.Empty;

    public string HeadlineTag
    {
        get;
        init;
    } = "flat";

    public bool HasHeadline => !string.IsNullOrEmpty(HeadlineText);

    public double SortHeat
    {
        get;
        init;
    }

    public double SortOwn
    {
        get;
        init;
    }

    public double SortAbyss
    {
        get;
        init;
    }

    public double SortLevel
    {
        get;
        init;
    }

    public double SortDamage
    {
        get;
        init;
    }

    public double SortValue
    {
        get;
        init;
    }

    public string SearchKey
    {
        get;
        init;
    } = string.Empty;

    public DcCharacterDetail Detail
    {
        get;
        init;
    } = new();
}

public sealed class DcCharacterDetail
{
    public string Name
    {
        get;
        set;
    } = string.Empty;

    public string Ename
    {
        get;
        set;
    } = string.Empty;

    public string? Avatar
    {
        get;
        set;
    }

    public string StarText
    {
        get;
        set;
    } = string.Empty;

    public string StarTag
    {
        get;
        set;
    } = "star5";

    public int Star => string.Equals(StarTag, "star5", StringComparison.Ordinal) ? 5 : 4;

    public string TierText
    {
        get;
        set;
    } = string.Empty;

    public string TierTag
    {
        get;
        set;
    } = "f";

    public string MetaScoreText
    {
        get;
        set;
    } = string.Empty;

    public string SubtitleText
    {
        get;
        set;
    } = string.Empty;

    public List<DcKpiTile> Metrics
    {
        get;
        set;
    } = new();

    public List<DcRateRow> Weapons
    {
        get;
        set;
    } = new();

    public List<DcRateRow> Artifacts
    {
        get;
        set;
    } = new();

    public List<DcBar> Constellations
    {
        get;
        set;
    } = new();

    public List<DcTeamCard> Teams
    {
        get;
        set;
    } = new();

    public List<DcNamedIcon> BannerHistory
    {
        get;
        set;
    } = new();

    public List<DcInsight> Insights
    {
        get;
        set;
    } = new();

    public string WishSummary
    {
        get;
        set;
    } = string.Empty;

    public string RerunSummary
    {
        get;
        set;
    } = string.Empty;

    public bool HasWeapons => Weapons.Count > 0;
    public bool HasArtifacts => Artifacts.Count > 0;
    public bool HasConstellations => Constellations.Count > 0;
    public bool HasTeams => Teams.Count > 0;
    public bool HasBannerHistory => BannerHistory.Count > 0;
    public bool HasInsights => Insights.Count > 0;
    public bool HasWishSummary => !string.IsNullOrEmpty(WishSummary);
    public bool HasRerunSummary => !string.IsNullOrEmpty(RerunSummary);
}