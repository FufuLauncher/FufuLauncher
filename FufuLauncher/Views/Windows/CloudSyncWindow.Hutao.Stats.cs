using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow
{
    private void InitializeHutaoStatsSelectors()
    {
        if (HutaoStatsPeriodCombo == null || HutaoStatsCategoryCombo == null || HutaoStatsFloorCombo == null)
        {
            return;
        }

        _suppressHutaoStatsFilterSync = true;
        try
        {
            HutaoStatsPeriodCombo.Items.Clear();
            HutaoStatsPeriodCombo.Items.Add(new ComboBoxItem { Tag = "current", Content = "HutaoSync_StatsPeriodCurrent".GetLocalized() });
            HutaoStatsPeriodCombo.Items.Add(new ComboBoxItem { Tag = "last", Content = "HutaoSync_StatsPeriodLast".GetLocalized() });
            HutaoStatsPeriodCombo.SelectedIndex = 0;

            HutaoStatsCategoryCombo.Items.Clear();
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Util", Content = "HutaoSync_StatsCatUtil".GetLocalized() });
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Attend", Content = "HutaoSync_StatsCatAttend".GetLocalized() });
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Hold", Content = "HutaoSync_StatsCatHold".GetLocalized() });
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Team", Content = "HutaoSync_StatsCatTeam".GetLocalized() });
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Theater", Content = "HutaoSync_StatsCatTheater".GetLocalized() });
            HutaoStatsCategoryCombo.SelectedIndex = 0;

            HutaoStatsFloorCombo.Items.Clear();
            foreach (var fl in new[] { 12, 11, 10, 9 })
            {
                HutaoStatsFloorCombo.Items.Add(new ComboBoxItem
                {
                    Tag = fl.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Content = string.Format("HutaoSync_StatsFloorFormat".GetLocalized(), fl)
                });
            }
            HutaoStatsFloorCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressHutaoStatsFilterSync = false;
        }
    }

    private async void OnHutaoStatsRefreshClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        await RefreshHutaoStatisticsAsync(silent: false);
    }

    private async void OnHutaoStatsRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoRoleSync || _suppressHutaoStatsFilterSync) return;
        var opt = HutaoStatsRoleFilterCombo.SelectedItem as AccountBoundRoleOption;
        _selectedRoleUid = opt?.Uid ?? string.Empty;
        SyncHutaoRoleFilterCombos();
        ApplyAllHutaoRoleFilters();

        if (_currentHutaoSubTab == "HutaoStats")
        {
            await RefreshHutaoStatisticsAsync(silent: true);
        }
    }

    private async void OnHutaoStatsPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoStatsFilterSync) return;
        var tag = (HutaoStatsPeriodCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _hutaoStatsLastPeriod = string.Equals(tag, "last", StringComparison.Ordinal);
        await RefreshHutaoStatisticsAsync(silent: true);
    }

    private void OnHutaoStatsCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoStatsFilterSync) return;
        _hutaoStatsCategory = (HutaoStatsCategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Util";
        if (HutaoStatsFloorCombo != null)
        {
            var usesFloor = _hutaoStatsCategory is "Util" or "Attend" or "Team";
            HutaoStatsFloorCombo.Visibility = usesFloor ? Visibility.Visible : Visibility.Collapsed;
        }

        RenderHutaoStatisticsView();
    }

    private void OnHutaoStatsFloorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoStatsFilterSync) return;
        var tag = (HutaoStatsFloorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (int.TryParse(tag, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var floor))
        {
            _hutaoStatsFloor = floor;
        }

        RenderHutaoStatisticsView();
    }

    private long _hutaoStatsRequestVersion;

    private async Task RefreshHutaoStatisticsAsync(bool silent)
    {
        if (_closed) return;
        var version = ++_hutaoStatsRequestVersion;
        _hutaoStatsLoading = true;
        if (HutaoStatsRefreshButton != null)
        {
            HutaoStatsRefreshButton.IsEnabled = false;
        }

        if (_hutaoStatsRows.Count == 0 && HutaoStatsEmptyText != null)
        {
            HutaoStatsEmptyText.Text = "HutaoSync_StatsLoadingHint".GetLocalized();
            HutaoStatsEmptyText.Visibility = Visibility.Visible;
        }

        var roleFilterSnapshot = _selectedRoleUid;
        var targetUids = GetHutaoStatsTargetUids(roleFilterSnapshot);

        try
        {
            var res = await _hutaoCloud.GetHutaoStatisticsBundleAsync(_hutaoStatsLastPeriod, targetUids);
            if (_closed || version != _hutaoStatsRequestVersion) return;

            if (!res.Ok || res.Data == null)
            {
                if (!silent)
                {
                    ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                }

                RenderHutaoStatisticsView();
                return;
            }

            _hutaoStatsLoaded = true;
            _hutaoStatsLoadedForRoleUid = roleFilterSnapshot;
            _hutaoStatsBundle = res.Data;
            RenderHutaoStatisticsView();

            if (!silent)
            {
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                    "HutaoSync_StatsMsgRefreshed".GetLocalized());
            }
        }
        finally
        {
            if (version == _hutaoStatsRequestVersion)
            {
                _hutaoStatsLoading = false;
                if (HutaoStatsRefreshButton != null)
                {
                    HutaoStatsRefreshButton.IsEnabled = true;
                }
            }
        }
    }

    private void RenderHutaoStatisticsView()
    {
        if (HutaoStatsOverviewScheduleText == null || HutaoStatsRowsRepeater == null)
        {
            return;
        }

        var bundle = _hutaoStatsBundle;
        if (bundle == null)
        {
            HutaoStatsOverviewScheduleText.Text = "-";
            HutaoStatsOverviewRecordsText.Text = "-";
            HutaoStatsOverviewFullStarText.Text = "-";
            HutaoStatsOverviewAvgText.Text = "-";
            HutaoStatsOverviewTheaterText.Text = "-";
            HutaoStatsOverviewUpdatedText.Text = "-";
            RenderHutaoUidRankSection(null);
            _hutaoStatsRows.Clear();
            HutaoStatsEmptyText.Text = "HutaoSync_StatsEmptyHint".GetLocalized();
            HutaoStatsEmptyText.Visibility = Visibility.Visible;
            return;
        }

        var ov = bundle.Overview;
        if (ov != null)
        {
            HutaoStatsOverviewScheduleText.Text = ov.ScheduleId > 0
                ? string.Format("HutaoSync_ScheduleFallbackFormat".GetLocalized(), ov.ScheduleId)
                : "-";
            HutaoStatsOverviewRecordsText.Text = ov.RecordTotal.ToString("N0");
            HutaoStatsOverviewFullStarText.Text = string.Format(
                "HutaoSync_StatsFullStarSummaryFormat".GetLocalized(),
                ov.SpiralAbyssFullStar.ToString("N0"),
                ov.SpiralAbyssTotal.ToString("N0"),
                ov.SpiralAbyssPassed.ToString("N0"));
            HutaoStatsOverviewAvgText.Text = ov.SpiralAbyssTotal > 0
                ? string.Format(
                    "HutaoSync_StatsAvgSummaryFormat".GetLocalized(),
                    (double)ov.SpiralAbyssStarTotal / ov.SpiralAbyssTotal,
                    (double)ov.SpiralAbyssBattleTotal / ov.SpiralAbyssTotal)
                : "-";
            HutaoStatsOverviewUpdatedText.Text = ov.Timestamp > 0
                ? (ov.Timestamp > 10_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ov.Timestamp)
                    : DateTimeOffset.FromUnixTimeSeconds(ov.Timestamp)).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "-";
        }
        else
        {
            HutaoStatsOverviewScheduleText.Text = "-";
            HutaoStatsOverviewRecordsText.Text = "-";
            HutaoStatsOverviewFullStarText.Text = "-";
            HutaoStatsOverviewAvgText.Text = "-";
            HutaoStatsOverviewUpdatedText.Text = "-";
        }

        HutaoStatsOverviewTheaterText.Text = bundle.RoleCombat != null
            ? string.Format(
                "HutaoSync_StatsRoleCombatSummaryFormat".GetLocalized(),
                bundle.RoleCombat.ScheduleId,
                bundle.RoleCombat.RecordTotal.ToString("N0"))
            : "-";

        RenderHutaoUidRankSection(bundle);

        IReadOnlyList<HutaoRankDisplayItem> sourceRows = _hutaoStatsCategory switch
        {
            "Attend" => bundle.AttendanceByFloor.GetValueOrDefault(_hutaoStatsFloor) ?? new List<HutaoRankDisplayItem>(),
            "Hold" => bundle.HoldingRates,
            "Team" => bundle.TeamsByFloor.GetValueOrDefault(_hutaoStatsFloor) ?? new List<HutaoRankDisplayItem>(),
            "Theater" => bundle.RoleCombatRates,
            _ => bundle.UtilizationByFloor.GetValueOrDefault(_hutaoStatsFloor) ?? new List<HutaoRankDisplayItem>()
        };

        _hutaoStatsRows.Clear();
        foreach (var row in sourceRows)
        {
            _hutaoStatsRows.Add(row);
        }

        HutaoStatsEmptyText.Text = "HutaoSync_StatsEmptyHint".GetLocalized();
        HutaoStatsEmptyText.Visibility = _hutaoStatsRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool ShouldRefreshHutaoStats() =>
        _currentHutaoSubTab == "HutaoStats" &&
        !_hutaoStatsLoading &&
        (!_hutaoStatsLoaded || !string.Equals(_hutaoStatsLoadedForRoleUid, _selectedRoleUid, StringComparison.Ordinal));

    private List<string> GetHutaoStatsTargetUids(string roleFilterUid)
    {
        if (!string.IsNullOrWhiteSpace(roleFilterUid))
        {
            return [roleFilterUid.Trim()];
        }

        return _accountRoles
            .Where(r => !r.IsAllRolesOption && !string.IsNullOrWhiteSpace(r.Uid))
            .Select(r => r.Uid.Trim())
            .Where(u => !string.IsNullOrEmpty(u))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private void RenderHutaoUidRankSection(HutaoStatisticsBundle? bundle)
    {
        if (HutaoStatsUidRanksRepeater == null || HutaoStatsUidRankEmptyText == null)
        {
            return;
        }

        _hutaoUidRankRows.Clear();

        if (bundle == null)
        {
            HutaoStatsUidRanksRepeater.Visibility = Visibility.Collapsed;
            HutaoStatsUidRankEmptyText.Text = "HutaoSync_StatsUidRankEmptyHint".GetLocalized();
            HutaoStatsUidRankEmptyText.Visibility = Visibility.Visible;
            return;
        }

        if (bundle.IsLastPeriod)
        {
            HutaoStatsUidRanksRepeater.Visibility = Visibility.Collapsed;
            HutaoStatsUidRankEmptyText.Text = "HutaoSync_StatsUidRankLastPeriodHint".GetLocalized();
            HutaoStatsUidRankEmptyText.Visibility = Visibility.Visible;
            return;
        }

        if (bundle.UidRanks.Count == 0)
        {
            HutaoStatsUidRanksRepeater.Visibility = Visibility.Collapsed;
            HutaoStatsUidRankEmptyText.Text = "HutaoSync_StatsUidRankEmptyHint".GetLocalized();
            HutaoStatsUidRankEmptyText.Visibility = Visibility.Visible;
            return;
        }

        var damageLabel = "HutaoSync_StatsUidDamageLabel".GetLocalized();
        var takeDamageLabel = "HutaoSync_StatsUidTakeDamageLabel".GetLocalized();
        var noRecordHint = "HutaoSync_StatsUidNoRecordHint".GetLocalized();

        foreach (var entry in bundle.UidRanks)
        {
            var rank = entry.Rank;
            var hasData = rank != null && (rank.Damage != null || rank.TakeDamage != null);

            _hutaoUidRankRows.Add(new HutaoUidRankDisplayItem
            {
                RoleTitle = FormatUidRankRoleTitle(entry.Uid),
                DamageLabel = damageLabel,
                DamageText = FormatUidRankMetric(rank?.Damage, entry.DamageAvatarName),
                TakeDamageLabel = takeDamageLabel,
                TakeDamageText = FormatUidRankMetric(rank?.TakeDamage, entry.TakeDamageAvatarName),
                HasRankData = hasData,
                NoRecordHintText = noRecordHint
            });
        }

        HutaoStatsUidRanksRepeater.Visibility = Visibility.Visible;
        HutaoStatsUidRankEmptyText.Visibility = Visibility.Collapsed;
    }

    private string FormatUidRankRoleTitle(string uid)
    {
        var matched = _accountRoles.FirstOrDefault(r =>
            !r.IsAllRolesOption && string.Equals(r.Uid, uid, StringComparison.Ordinal));
        return matched != null ? matched.DisplayText : GameRoleDisplay.Archive(uid);
    }

    private static string FormatUidRankMetric(HutaoRankValue? rankValue, string avatarName)
    {
        if (rankValue == null)
        {
            return "-";
        }

        return $"{avatarName} · " + string.Format(
            "HutaoSync_StatsUidRankMetricFormat".GetLocalized(),
            rankValue.Value.ToString("N0"),
            $"{rankValue.Rate * 100:0.0}%",
            $"{rankValue.RateOnAvatar * 100:0.0}%");
    }
}
