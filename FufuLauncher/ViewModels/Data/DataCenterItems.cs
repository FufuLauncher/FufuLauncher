/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.ViewModels;

public sealed class DcKpiTile
{
    public string Glyph
    {
        get;
        init;
    } = "";

    public string Title
    {
        get;
        init;
    } = string.Empty;

    public string Value
    {
        get;
        init;
    } = "-";

    public string Caption
    {
        get;
        init;
    } = string.Empty;

    public string ColorTag
    {
        get;
        init;
    } = "accent";

    public bool HasCaption => !string.IsNullOrEmpty(Caption);
}

public sealed class DcRateRow
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

    public double Rate
    {
        get;
        init;
    }

    public string RateText
    {
        get;
        init;
    } = string.Empty;

    public string ColorTag
    {
        get;
        init;
    } = "accent";

    public bool HasIcon => !string.IsNullOrEmpty(Icon);
}

public sealed class DcBar
{
    public string Label
    {
        get;
        init;
    } = string.Empty;

    public double Value
    {
        get;
        init;
    }

    public string ValueText
    {
        get;
        init;
    } = string.Empty;

    public string ColorTag
    {
        get;
        init;
    } = "accent";

    public bool IsHighlighted
    {
        get;
        init;
    }
}

public sealed class DcMoverRow
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

    public string CurrentText
    {
        get;
        init;
    } = string.Empty;

    public string PreviousText
    {
        get;
        init;
    } = string.Empty;

    public string ChangeText
    {
        get;
        init;
    } = string.Empty;

    public string ChangeGlyph
    {
        get;
        init;
    } = "";

    public string ChangeTag
    {
        get;
        init;
    } = "flat";
}

public sealed class DcCountRow
{
    public int Position
    {
        get;
        init;
    }

    public string PositionText => Position.ToString();

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

    public string CountText
    {
        get;
        init;
    } = string.Empty;

    public string DetailText
    {
        get;
        init;
    } = string.Empty;

    public double Ratio
    {
        get;
        init;
    }

    public bool HasIcon => !string.IsNullOrEmpty(Icon);
}

public sealed class DcInsight
{
    public string Glyph
    {
        get;
        init;
    } = "";

    public string Title
    {
        get;
        init;
    } = string.Empty;

    public string Body
    {
        get;
        init;
    } = string.Empty;

    public string ColorTag
    {
        get;
        init;
    } = "accent";
}

public sealed class DcOption
{
    public string Title
    {
        get;
        init;
    } = string.Empty;

    public string? Value
    {
        get;
        init;
    }

    public override string ToString() => Title;
}

public enum DataCenterView
{
    Overview = 0,
    Characters = 1,
    SpiralAbyss = 2,
    Stygian = 3,
    Wish = 4,
    Rerun = 5,
    Timeline = 6
}
