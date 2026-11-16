using System.Globalization;
using System.Net;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class HutaoCloudService
{
    public async Task<HutaoOperationResult<HutaoStatisticsBundle>> GetHutaoStatisticsBundleAsync(
        bool lastPeriod,
        IReadOnlyList<string>? uids,
        CancellationToken cancellationToken = default)
    {
        var suffix = lastPeriod ? "?last=true" : "?last=false";

        var overviewTask = SendRawRequestAsync<HutaoAbyssOverview>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsOverviewPath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var utilTask = SendRawRequestAsync<List<HutaoAbyssAvatarRateFloor>>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsUtilizationRatePath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var attendTask = SendRawRequestAsync<List<HutaoAbyssAvatarRateFloor>>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsAttendanceRatePath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var holdTask = SendRawRequestAsync<List<HutaoAbyssAvatarHolding>>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsHoldingRatePath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var teamTask = SendRawRequestAsync<List<HutaoAbyssTeamFloor>>(
            HttpMethod.Get,
            $"{ApiEndpoints.HutaoStatisticsTeamCombinationPath}{suffix}",
            null,
            requiresAuth: false,
            cancellationToken);

        var theaterTask = SendRawRequestAsync<HutaoRoleCombatStatistics>(
            HttpMethod.Get,
            ApiEndpoints.HutaoRoleCombatStatisticsPath,
            null,
            requiresAuth: false,
            cancellationToken);

        var catalogTask = ResolveItemCatalogAsync(cancellationToken);

        var normalizedUids = (uids ?? Array.Empty<string>())
            .Select(u => (u ?? string.Empty).Trim())
            .Where(u => !string.IsNullOrEmpty(u))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var rankTasks = !lastPeriod && normalizedUids.Count > 0
            ? normalizedUids
                .Select(targetUid => SendRawRequestAsync<HutaoRecordRankData>(
                    HttpMethod.Get,
                    $"{ApiEndpoints.HutaoRecordRankPath}?Uid={Uri.EscapeDataString(targetUid)}",
                    null,
                    requiresAuth: false,
                    cancellationToken))
                .ToList()
            : null;

        await Task.WhenAll(
            overviewTask,
            utilTask,
            attendTask,
            holdTask,
            teamTask,
            theaterTask,
            catalogTask);

        if (rankTasks is { Count: > 0 })
        {
            await Task.WhenAll(rankTasks);
        }

        var overviewRes = overviewTask.Result;
        var utilRes = utilTask.Result;
        var attendRes = attendTask.Result;
        var holdRes = holdTask.Result;
        var teamRes = teamTask.Result;
        var theaterRes = theaterTask.Result;
        var catalog = catalogTask.Result;

        if (overviewRes.RetCode != 0 &&
            utilRes.RetCode != 0 &&
            attendRes.RetCode != 0 &&
            holdRes.RetCode != 0 &&
            teamRes.RetCode != 0 &&
            theaterRes.RetCode != 0)
        {
            return Fail<HutaoStatisticsBundle>(
                FormatFriendlyError(overviewRes.RetCode, overviewRes.Message, overviewRes.L10nKey,
                    overviewRes.HttpStatus));
        }

        var byId = catalog.ById;
        var utilByFloor = BuildFloorRateRows(utilRes.Data, byId);
        var attendByFloor = BuildFloorRateRows(attendRes.Data, byId);
        var holdingRates = BuildHoldingRows(holdRes.Data, byId);
        var teamsByFloor = BuildTeamRows(teamRes.Data, byId);
        var roleCombatRates = BuildRoleCombatRows(theaterRes.Data, byId);

        var uidRanks = new List<HutaoUidRankItem>(normalizedUids.Count);
        if (rankTasks != null)
        {
            for (var i = 0; i < normalizedUids.Count; i++)
            {
                var rankRes = rankTasks[i].Result;
                var uidRankData = rankRes.RetCode == 0 ? rankRes.Data : null;
                var dmgAvatarName = uidRankData?.Damage is { Item: > 0 }
                    ? ResolveAvatarName(uidRankData.Damage.Item, byId)
                    : string.Empty;
                var takeAvatarName = uidRankData?.TakeDamage is { Item: > 0 }
                    ? ResolveAvatarName(uidRankData.TakeDamage.Item, byId)
                    : string.Empty;

                uidRanks.Add(new HutaoUidRankItem
                {
                    Uid = normalizedUids[i],
                    Rank = uidRankData,
                    DamageAvatarName = dmgAvatarName,
                    TakeDamageAvatarName = takeAvatarName
                });
            }
        }

        return new HutaoOperationResult<HutaoStatisticsBundle>
        {
            Ok = true,
            Data = new HutaoStatisticsBundle
            {
                IsLastPeriod = lastPeriod,
                QueriedUids = normalizedUids,
                Overview = overviewRes.RetCode == 0 ? overviewRes.Data : null,
                RoleCombat = theaterRes.RetCode == 0 ? theaterRes.Data : null,
                UidRanks = uidRanks,
                UtilizationByFloor = utilByFloor,
                AttendanceByFloor = attendByFloor,
                HoldingRates = holdingRates,
                TeamsByFloor = teamsByFloor,
                RoleCombatRates = roleCombatRates
            }
        };
    }

    private static bool HasRequiredHutaoAvatars(IReadOnlyList<HutaoSimpleAvatar>? avatars)
    {
        if (avatars == null || avatars.Count <= 8) return false;
        var ids = avatars.Select(a => a.AvatarId).ToHashSet();
        var hasTraveler = ids.Contains(10000005) || ids.Contains(10000007);
        return hasTraveler && ids.Contains(10000021) && ids.Contains(10000015) && ids.Contains(10000006);
    }

    private static string ExtractChallengeFailureReason(int retCode, string? message, string? l10nKey,
        HttpStatusCode httpStatus)
    {
        if (retCode == 500020 ||
            (!string.IsNullOrWhiteSpace(message) && message.Contains("无效", StringComparison.OrdinalIgnoreCase)))
        {
            return "HutaoSync_ErrChallengeInvalidData".GetLocalized();
        }

        var friendly = FormatFriendlyError(retCode, message, l10nKey, httpStatus);
        var generic = "HutaoSync_ErrGeneric".GetLocalized();
        if (!string.Equals(friendly, generic, StringComparison.Ordinal))
        {
            return friendly;
        }

        return !string.IsNullOrWhiteSpace(message) ? TruncateString(message, 48) : generic;
    }

    private static string ResolveAvatarName(
        int avatarId,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        if (avatarId == 10000005) return "HutaoSync_TravelerMaleName".GetLocalized();
        if (avatarId == 10000007) return "HutaoSync_TravelerFemaleName".GetLocalized();

        if (byId.TryGetValue(avatarId, out var info) && !string.IsNullOrWhiteSpace(info.Name))
        {
            return info.Name;
        }

        return $"#{avatarId}";
    }

    private static Dictionary<int, List<HutaoRankDisplayItem>> BuildFloorRateRows(
        List<HutaoAbyssAvatarRateFloor>? floors,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        var map = new Dictionary<int, List<HutaoRankDisplayItem>>();
        if (floors == null) return map;

        foreach (var floor in floors)
        {
            var sorted = (floor.Ranks ?? new List<HutaoItemRateIntDouble>())
                .Where(r => r.Item > 0)
                .OrderByDescending(r => r.Rate)
                .ToList();

            var list = new List<HutaoRankDisplayItem>(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                var entry = sorted[i];
                list.Add(new HutaoRankDisplayItem
                {
                    RankNumberText = (i + 1).ToString(CultureInfo.InvariantCulture),
                    PrimaryText = ResolveAvatarName(entry.Item, byId),
                    SecondaryText = string.Format("HutaoSync_StatsFloorFormat".GetLocalized(), floor.Floor),
                    MetricText = $"{entry.Rate * 100:0.0}%"
                });
            }

            map[floor.Floor] = list;
        }

        return map;
    }

    private static List<HutaoRankDisplayItem> BuildHoldingRows(
        List<HutaoAbyssAvatarHolding>? holdings,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        if (holdings == null) return new List<HutaoRankDisplayItem>();

        var sorted = holdings
            .Where(h => h.AvatarId > 0)
            .OrderByDescending(h => h.HoldingRate)
            .ToList();

        var list = new List<HutaoRankDisplayItem>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i];
            var constParts = (item.Constellations ?? new List<HutaoItemRateIntDouble>())
                .OrderBy(c => c.Item)
                .Select(c => string.Format("HutaoSync_StatsConstellationFormat".GetLocalized(), c.Item,
                    $"{c.Rate * 100:0.0}%"))
                .ToList();

            list.Add(new HutaoRankDisplayItem
            {
                RankNumberText = (i + 1).ToString(CultureInfo.InvariantCulture),
                PrimaryText = ResolveAvatarName(item.AvatarId, byId),
                SecondaryText = constParts.Count > 0 ? string.Join(" · ", constParts) : string.Empty,
                MetricText = $"{item.HoldingRate * 100:0.0}%"
            });
        }

        return list;
    }

    private static Dictionary<int, List<HutaoRankDisplayItem>> BuildTeamRows(
        List<HutaoAbyssTeamFloor>? floors,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        var map = new Dictionary<int, List<HutaoRankDisplayItem>>();
        if (floors == null) return map;

        var upperLabel = "HutaoSync_StatsTeamUpperLabel".GetLocalized();
        var lowerLabel = "HutaoSync_StatsTeamLowerLabel".GetLocalized();

        foreach (var floor in floors)
        {
            var combined = new List<(string SideLabel, string ItemIds, int Rate)>();
            if (floor.Up != null)
            {
                combined.AddRange(floor.Up.Select(t => (upperLabel, t.Item, t.Rate)));
            }

            if (floor.Down != null)
            {
                combined.AddRange(floor.Down.Select(t => (lowerLabel, t.Item, t.Rate)));
            }

            combined.Sort((a, b) => b.Rate.CompareTo(a.Rate));

            var list = new List<HutaoRankDisplayItem>(combined.Count);
            for (var i = 0; i < combined.Count; i++)
            {
                var entry = combined[i];
                var memberNames = (entry.ItemIds ?? string.Empty)
                    .Split(new[] { ',', '-' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(rawId => int.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                        ? ResolveAvatarName(id, byId)
                        : rawId)
                    .ToList();

                list.Add(new HutaoRankDisplayItem
                {
                    RankNumberText = (i + 1).ToString(CultureInfo.InvariantCulture),
                    PrimaryText = memberNames.Count > 0 ? string.Join(" / ", memberNames) : entry.ItemIds,
                    SecondaryText =
                        $"{entry.SideLabel} · {string.Format("HutaoSync_StatsFloorFormat".GetLocalized(), floor.Floor)}",
                    MetricText = string.Format("HutaoSync_StatsTeamCountFormat".GetLocalized(), entry.Rate)
                });
            }

            map[floor.Floor] = list;
        }

        return map;
    }

    private static List<HutaoRankDisplayItem> BuildRoleCombatRows(
        HutaoRoleCombatStatistics? stats,
        Dictionary<int, (string Name, string ItemType, string RankType)> byId)
    {
        if (stats?.BackupAvatarRates == null) return new List<HutaoRankDisplayItem>();

        var sorted = stats.BackupAvatarRates
            .Where(r => r.Item > 0)
            .OrderByDescending(r => r.Rate)
            .ToList();

        var list = new List<HutaoRankDisplayItem>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var entry = sorted[i];
            list.Add(new HutaoRankDisplayItem
            {
                RankNumberText = (i + 1).ToString(CultureInfo.InvariantCulture),
                PrimaryText = ResolveAvatarName(entry.Item, byId),
                SecondaryText = string.Format("HutaoSync_StatsRoleCombatRecordCountFormat".GetLocalized(),
                    stats.RecordTotal),
                MetricText = $"{entry.Rate * 100:0.0}%"
            });
        }

        return list;
    }
}