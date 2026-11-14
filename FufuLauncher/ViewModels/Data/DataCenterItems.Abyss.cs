/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FufuLauncher.ViewModels;

public sealed class DcTierMember
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

    public string UseRateText
    {
        get;
        init;
    } = string.Empty;

    public string DetailText
    {
        get;
        init;
    } = string.Empty;

    public string StarTag => Star == 5 ? "star5" : "star4";
}

public sealed class DcTierGroup
{
    public string RankName
    {
        get;
        init;
    } = string.Empty;

    public string TierTag
    {
        get;
        init;
    } = "f";

    public string CountText
    {
        get;
        init;
    } = string.Empty;

    public string Description
    {
        get;
        init;
    } = string.Empty;

    public List<DcTierMember> Members
    {
        get;
        init;
    } = new();
}

public sealed class DcRankRow
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

    public double UseRate
    {
        get;
        init;
    }

    public string UseRateText
    {
        get;
        init;
    } = string.Empty;

    public string OwnRateText
    {
        get;
        init;
    } = string.Empty;

    public string FieldShareText
    {
        get;
        init;
    } = string.Empty;

    public string ConstellationText
    {
        get;
        init;
    } = string.Empty;

    public string ClearTimeText
    {
        get;
        init;
    } = string.Empty;

    public bool HasClearTime
    {
        get;
        init;
    }

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

    public bool HasChange
    {
        get;
        init;
    }

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

    public string StarTag => Star == 5 ? "star5" : "star4";
}

public sealed class DcTeamMember
{
    public string? Avatar
    {
        get;
        init;
    }

    public string Name
    {
        get;
        init;
    } = string.Empty;

    public int Star
    {
        get;
        init;
    }

    public string StarTag => Star == 5 ? "star5" : "star4";
}

public sealed class DcTeamCard
{
    public int Position
    {
        get;
        init;
    }

    public string PositionText => "#" + Position;

    public List<DcTeamMember> Members
    {
        get;
        init;
    } = new();

    public string TeamNames
    {
        get;
        init;
    } = string.Empty;

    public double UseRate
    {
        get;
        init;
    }

    public string UseRateText
    {
        get;
        init;
    } = string.Empty;

    public string HasRateText
    {
        get;
        init;
    } = string.Empty;

    public string AttendRateText
    {
        get;
        init;
    } = string.Empty;

    public string UseCountText
    {
        get;
        init;
    } = string.Empty;

    public string ClearTimeText
    {
        get;
        init;
    } = string.Empty;

    public bool HasClearTime
    {
        get;
        init;
    }

    public List<DcBar> HalfSplit
    {
        get;
        init;
    } = new();
}

public sealed class DcAbyssSection : INotifyPropertyChanged
{
    public bool IsStygian
    {
        get;
        init;
    }

    public bool ShowClearTime
    {
        get;
        init;
    }

    public List<DcOption> RankSortOptions
    {
        get;
        init;
    } = new();

    public ObservableCollection<DcKpiTile> Kpis
    {
        get;
    } = new();

    public ObservableCollection<DcTierGroup> Tiers
    {
        get;
    } = new();

    public ObservableCollection<DcRankRow> Ranks
    {
        get;
    } = new();

    public ObservableCollection<DcTeamCard> Teams
    {
        get;
    } = new();

    public ObservableCollection<DcMoverRow> Risers
    {
        get;
    } = new();

    public ObservableCollection<DcMoverRow> Fallers
    {
        get;
    } = new();

    public ObservableCollection<DcBar> RestartDistribution
    {
        get;
    } = new();

    public ObservableCollection<DcOption> Versions
    {
        get;
    } = new();

    public ObservableCollection<DcOption> TeamFilters
    {
        get;
    } = new();

    public List<DcTeamCard> AllTeams
    {
        get;
    } = new();

    public int TeamShown
    {
        get;
        set;
    }

    public string? LoadedVersion
    {
        get;
        set;
    }

    public string? LoadedTeamFilter
    {
        get;
        set;
    }

    private int _selectedVersionIndex = -1;

    public int SelectedVersionIndex
    {
        get => _selectedVersionIndex;
        set
        {
            if (_selectedVersionIndex == value) return;
            _selectedVersionIndex = value;
            Raise();
        }
    }

    private int _selectedTeamFilterIndex = -1;

    public int SelectedTeamFilterIndex
    {
        get => _selectedTeamFilterIndex;
        set
        {
            if (_selectedTeamFilterIndex == value) return;
            _selectedTeamFilterIndex = value;
            Raise();
        }
    }

    private int _subView;

    public bool ShowTier => _subView == 0;
    public bool ShowRank => _subView == 1;
    public bool ShowTeam => _subView == 2;

    public void SetSubView(int subView)
    {
        if (_subView == subView) return;
        _subView = subView;
        Raise(nameof(ShowTier));
        Raise(nameof(ShowRank));
        Raise(nameof(ShowTeam));
    }

    private string _rankSort = "use";

    public string RankSort
    {
        get => _rankSort;
        set
        {
            if (_rankSort == value) return;
            _rankSort = value;
            Raise();
        }
    }

    private string _headline = string.Empty;

    public string Headline
    {
        get => _headline;
        set
        {
            if (_headline == value) return;
            _headline = value;
            Raise();
        }
    }

    private string _tips = string.Empty;

    public string Tips
    {
        get => _tips;
        set
        {
            if (_tips == value) return;
            _tips = value;
            Raise();
            Raise(nameof(HasTips));
        }
    }

    public bool HasTips => !string.IsNullOrEmpty(_tips);

    private string _teamCountText = string.Empty;

    public string TeamCountText
    {
        get => _teamCountText;
        set
        {
            if (_teamCountText == value) return;
            _teamCountText = value;
            Raise();
        }
    }

    private string _teamMoreText = string.Empty;

    public string TeamMoreText
    {
        get => _teamMoreText;
        set
        {
            if (_teamMoreText == value) return;
            _teamMoreText = value;
            Raise();
        }
    }

    private bool _hasMoreTeams;

    public bool HasMoreTeams
    {
        get => _hasMoreTeams;
        set
        {
            if (_hasMoreTeams == value) return;
            _hasMoreTeams = value;
            Raise();
        }
    }

    private bool _showRestartDistribution;

    public bool ShowRestartDistribution
    {
        get => _showRestartDistribution;
        set
        {
            if (_showRestartDistribution == value) return;
            _showRestartDistribution = value;
            Raise();
        }
    }

    private bool _hasMovers;

    public bool HasMovers
    {
        get => _hasMovers;
        set
        {
            if (_hasMovers == value) return;
            _hasMovers = value;
            Raise();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}