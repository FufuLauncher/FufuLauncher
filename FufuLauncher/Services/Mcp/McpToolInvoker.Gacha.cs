/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services.Mcp;

public sealed partial class McpToolInvoker
{
    private const string GachaLastSelectedUidSettingKey = "GachaLastSelectedUid";
    private const int GachaPrimogemsPerPull = 160;

    private static readonly string[] GachaPoolPeriodTypes = ["301", "400", "302", "500"];

    private static readonly (string Bucket, string PoolType)[] GachaPoolBuckets =
    [
        ("character", "301"),
        ("weapon", "302"),
        ("chronicled", "500"),
        ("novice", "100"),
        ("standard", "200")
    ];

    private sealed record GachaUpItem(int ItemId, string Name, int Rank);

    private sealed record GachaPoolRow(
        string Key,
        string PoolType,
        string Version,
        string PoolName,
        DateTime Start,
        DateTime End,
        string StartText,
        string EndText,
        List<GachaUpItem> UpItems);

    private sealed record GachaLogView(
        GachaLogItem Log,
        DateTime Time,
        long SortId,
        string Bucket,
        int Pity);

    private sealed record GachaData(string? Uid, List<GachaLogView> Records, List<GachaPoolRow> Periods);

    private sealed record GachaPeriodSummary(
        GachaPoolRow Row,
        List<GachaLogView> Records,
        List<GachaLogView> FiveStars,
        int FourStarCount,
        bool GotUpFiveStar);

    private async Task<McpToolResult> GetGachaRecordsAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var uid = await ResolveGachaUidAsync(arguments, cancellationToken).ConfigureAwait(false);
        var poolTypes = ResolveGachaPoolTypes(OptionalString(arguments, "pool"));
        var rank = OptionalRank(arguments);
        var itemKeyword = OptionalString(arguments, "item")?.Trim();
        var from = ParseUserDateTime(OptionalString(arguments, "from"), false);
        var to = ParseUserDateTime(OptionalString(arguments, "to"), true);
        if (from != null && to != null && from > to)
        {
            throw new McpToolException("\"from\" must not be later than \"to\".");
        }

        var descending = !string.Equals(OptionalString(arguments, "order"), "asc", StringComparison.OrdinalIgnoreCase);
        var page = OptionalInt(arguments, "page", 1, 1, 100000);
        var pageSize = OptionalInt(arguments, "pageSize", 20, 1, 100);

        var data = await LoadGachaDataAsync(uid, cancellationToken).ConfigureAwait(false);

        var matched = data.Records
            .Where(view => poolTypes == null || poolTypes.Contains(view.Log.GachaType))
            .Where(view => rank == null || ParseRank(view.Log.RankType) == rank)
            .Where(view => string.IsNullOrWhiteSpace(itemKeyword) ||
                           ContainsText(view.Log.Name, itemKeyword) ||
                           ContainsText(view.Log.ItemId, itemKeyword))
            .Where(view => from == null || view.Time >= from)
            .Where(view => to == null || view.Time <= to)
            .ToList();

        var ordered = descending
            ? matched.OrderByDescending(view => view.Time).ThenByDescending(view => view.SortId)
            : matched.OrderBy(view => view.Time).ThenBy(view => view.SortId);
        var pageItems = SelectGachaPage(ordered, page, pageSize, out var total);

        var byPool = matched
            .GroupBy(view => view.Bucket, StringComparer.Ordinal)
            .Select(group => new { Bucket = group.Key, Count = group.Count() })
            .OrderBy(group => GachaBucketOrder(group.Bucket))
            .Select(group => new
            {
                pool = group.Bucket,
                poolName = DescribeBucketName(group.Bucket),
                count = group.Count
            })
            .ToList();

        if (matched.Count == 0)
        {
            return McpToolResult.Success(new
            {
                uid,
                hasRecords = false,
                totalMatched = 0,
                storedRecords = data.Records.Count,
                page,
                pageSize,
                pageCount = 0,
                hasMore = false,
                message = data.Records.Count == 0
                    ? "No wish records are stored for this player yet. Refresh the gacha analysis window first."
                    : "No wish record matches the given filters.",
                records = Array.Empty<object>()
            });
        }

        var fiveStarCount = matched.Count(view => view.Log.RankType == "5");
        var fourStarCount = matched.Count(view => view.Log.RankType == "4");
        var times = matched.Where(view => view.Time != DateTime.MinValue).Select(view => view.Time).ToList();

        return McpToolResult.Success(new
        {
            uid,
            hasRecords = true,
            totalMatched = total,
            storedRecords = data.Records.Count,
            page,
            pageSize,
            pageCount = PageCount(total, pageSize),
            hasMore = page * pageSize < total,
            summary = new
            {
                total = matched.Count,
                fiveStar = fiveStarCount,
                fourStar = fourStarCount,
                threeStar = matched.Count - fiveStarCount - fourStarCount,
                fiveStarRate = FormatPercent(fiveStarCount, matched.Count),
                earliest = times.Count == 0 ? null : FormatRecordTime(times.Min()),
                latest = times.Count == 0 ? null : FormatRecordTime(times.Max()),
                byPool
            },
            records = pageItems
        });
    }

    private async Task<McpToolResult> GetGachaOverviewAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var uid = await ResolveGachaUidAsync(arguments, cancellationToken).ConfigureAwait(false);
        var recentCount = OptionalInt(arguments, "recentFiveStars", 10, 0, 50);

        var data = await LoadGachaDataAsync(uid, cancellationToken).ConfigureAwait(false);
        if (data.Records.Count == 0)
        {
            return McpToolResult.Success(new
            {
                uid,
                hasRecords = false,
                totalPulls = 0,
                message = "No wish records are stored for this player yet. Refresh the gacha analysis window first."
            });
        }

        var records = data.Records;
        var fiveStars = records.Where(view => view.Log.RankType == "5").ToList();
        var fourStarCount = records.Count(view => view.Log.RankType == "4");
        var times = records.Where(view => view.Time != DateTime.MinValue).Select(view => view.Time).ToList();
        var averagePity = fiveStars.Count == 0 ? 0d : Math.Round(fiveStars.Average(view => view.Pity), 1);
        var best = fiveStars.OrderBy(view => view.Pity).ThenBy(view => view.Time).FirstOrDefault();
        var worst = fiveStars.OrderByDescending(view => view.Pity).ThenBy(view => view.Time).FirstOrDefault();

        var pools = new List<object>();
        foreach (var bucket in GachaPoolBuckets)
        {
            var bucketRecords = records.Where(view => view.Bucket == bucket.Bucket).ToList();
            var bucketFiveStars = bucketRecords.Where(view => view.Log.RankType == "5").ToList();
            var last = bucketRecords.LastOrDefault();
            var currentPity = last == null ? 0 : last.Log.RankType == "5" ? 0 : last.Pity;
            pools.Add(new
            {
                pool = bucket.Bucket,
                poolName = DescribeBucketName(bucket.Bucket),
                pulls = bucketRecords.Count,
                fiveStar = bucketFiveStars.Count,
                fourStar = bucketRecords.Count(view => view.Log.RankType == "4"),
                fiveStarRate = FormatPercent(bucketFiveStars.Count, bucketRecords.Count),
                averageFiveStarPity = bucketFiveStars.Count == 0
                    ? 0d
                    : Math.Round(bucketFiveStars.Average(view => view.Pity), 1),
                currentPity
            });
        }

        var monthly = records
            .Where(view => view.Time != DateTime.MinValue)
            .GroupBy(view => view.Time.ToString("yyyy-MM", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                month = group.Key,
                pulls = group.Count(),
                fiveStar = group.Count(view => view.Log.RankType == "5")
            })
            .ToList();

        var recentFiveStars = fiveStars
            .OrderByDescending(view => view.Time)
            .ThenByDescending(view => view.SortId)
            .Take(recentCount)
            .Select(view => new
            {
                name = view.Log.Name,
                itemId = NullIfEmpty(view.Log.ItemId),
                pool = view.Bucket,
                poolName = DescribeBucketName(view.Bucket),
                pity = view.Pity,
                time = FormatRecordTime(view.Time)
            })
            .ToList();

        return McpToolResult.Success(new
        {
            uid,
            hasRecords = true,
            totalPulls = records.Count,
            primogemsSpent = records.Count * GachaPrimogemsPerPull,
            fiveStarCount = fiveStars.Count,
            fourStarCount,
            threeStarCount = records.Count - fiveStars.Count - fourStarCount,
            fiveStarRate = FormatPercent(fiveStars.Count, records.Count),
            fourStarRate = FormatPercent(fourStarCount, records.Count),
            firstPullTime = times.Count == 0 ? null : FormatRecordTime(times.Min()),
            lastPullTime = times.Count == 0 ? null : FormatRecordTime(times.Max()),
            averageFiveStarPity = averagePity,
            bestFiveStarPity = best == null
                ? null
                : new { name = best.Log.Name, poolName = DescribeBucketName(best.Bucket), pity = best.Pity, time = FormatRecordTime(best.Time) },
            worstFiveStarPity = worst == null
                ? null
                : new { name = worst.Log.Name, poolName = DescribeBucketName(worst.Bucket), pity = worst.Pity, time = FormatRecordTime(worst.Time) },
            pools,
            monthly,
            recentFiveStars,
            dataSource = "launcher local wish history (updated when the gacha analysis window refreshes)"
        });
    }

    private async Task<McpToolResult> SearchGachaPoolsAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var uid = await TryResolveGachaUidAsync(arguments, cancellationToken).ConfigureAwait(false);
        var poolTypes = ResolveGachaPoolTypes(OptionalString(arguments, "pool"));
        var query = OptionalString(arguments, "query")?.Trim();
        var from = ParseUserDateTime(OptionalString(arguments, "from"), false);
        var to = ParseUserDateTime(OptionalString(arguments, "to"), true);
        if (from != null && to != null && from > to)
        {
            throw new McpToolException("\"from\" must not be later than \"to\".");
        }

        var hasPullsOnly = OptionalBool(arguments, "hasPulls", false);
        var page = OptionalInt(arguments, "page", 1, 1, 100000);
        var pageSize = OptionalInt(arguments, "pageSize", 20, 1, 100);

        var data = await LoadGachaDataAsync(uid, cancellationToken).ConfigureAwait(false);
        var summaries = data.Periods
            .Where(row => poolTypes == null || poolTypes.Contains(row.PoolType))
            .Where(row => from == null || row.End >= from)
            .Where(row => to == null || row.Start <= to)
            .Where(row => string.IsNullOrWhiteSpace(query) || MatchesPeriodQuery(row, query))
            .Select(row => BuildPeriodSummary(row, data.Records))
            .Where(summary => !hasPullsOnly || summary.Records.Count > 0)
            .ToList();

        summaries = summaries
            .OrderByDescending(summary => summary.Row.Start)
            .ThenBy(summary => GachaPoolPeriodOrder(summary.Row.PoolType))
            .ThenBy(summary => summary.Row.Version, StringComparer.Ordinal)
            .ToList();

        var ordered = summaries.AsEnumerable();
        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var includePlayer = uid != null;

        return McpToolResult.Success(new
        {
            uid,
            recordsAvailable = includePlayer && data.Records.Count > 0,
            knownPeriods = data.Periods.Count,
            totalMatched = summaries.Count,
            page,
            pageSize,
            pageCount = PageCount(summaries.Count, pageSize),
            hasMore = page * pageSize < summaries.Count,
            periods = pageItems.Select(summary => DescribePeriod(summary, includePlayer)).ToList(),
            message = summaries.Count == 0
                ? "No banner period matches the given filters. The launcher stores periods after the gacha analysis window loaded the pool metadata."
                : null
        });
    }

    private async Task<McpToolResult> GetGachaPoolRecordsAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var uid = await ResolveGachaUidAsync(arguments, cancellationToken).ConfigureAwait(false);
        var key = OptionalString(arguments, "key")?.Trim();
        var version = OptionalString(arguments, "version")?.Trim();
        var poolTypes = ResolveGachaPoolTypes(OptionalString(arguments, "pool"));
        var descending = !string.Equals(OptionalString(arguments, "order"), "asc", StringComparison.OrdinalIgnoreCase);
        var page = OptionalInt(arguments, "page", 1, 1, 100000);
        var pageSize = OptionalInt(arguments, "pageSize", 20, 1, 100);

        if (string.IsNullOrWhiteSpace(key) && string.IsNullOrWhiteSpace(version))
        {
            throw new McpToolException(
                "Provide \"key\" from search_gacha_pools, or \"version\" (optionally with \"pool\").");
        }

        var data = await LoadGachaDataAsync(uid, cancellationToken).ConfigureAwait(false);
        var candidates = !string.IsNullOrWhiteSpace(key)
            ? data.Periods.Where(row => MatchesPeriodKey(row, key!)).ToList()
            : data.Periods
                .Where(row => poolTypes == null || poolTypes.Contains(row.PoolType))
                .Where(row => ContainsText(row.Version, version))
                .ToList();

        if (candidates.Count == 0)
        {
            var target = string.IsNullOrWhiteSpace(key) ? $"version \"{version}\"" : $"key \"{key}\"";
            throw new McpToolException(
                $"No banner period matches {target}. Call search_gacha_pools to list the available periods and their keys.");
        }

        if (candidates.Count > 1)
        {
            return McpToolResult.Success(new
            {
                uid,
                matched = false,
                ambiguous = true,
                message = "More than one banner period matches. Repeat the call with one of the candidate keys.",
                candidates = candidates
                    .Select(row => DescribePeriod(BuildPeriodSummary(row, data.Records), true))
                    .ToList()
            });
        }

        var summary = BuildPeriodSummary(candidates[0], data.Records);
        var ordered = descending
            ? summary.Records.OrderByDescending(view => view.Time).ThenByDescending(view => view.SortId)
            : summary.Records.OrderBy(view => view.Time).ThenBy(view => view.SortId);
        var pageItems = SelectGachaPage(ordered, page, pageSize, out var total);

        return McpToolResult.Success(new
        {
            uid,
            matched = true,
            ambiguous = false,
            period = DescribePeriod(summary, true),
            summary = new
            {
                pulls = summary.Records.Count,
                fiveStarCount = summary.FiveStars.Count,
                fourStarCount = summary.FourStarCount,
                threeStarCount = summary.Records.Count - summary.FiveStars.Count - summary.FourStarCount,
                gotUpFiveStar = summary.GotUpFiveStar,
                obtainedItems = DescribeObtainedItems(summary.Records)
            },
            page,
            pageSize,
            pageCount = PageCount(total, pageSize),
            hasMore = page * pageSize < total,
            records = pageItems
        });
    }

    private static List<object> SelectGachaPage(IEnumerable<GachaLogView> ordered, int page, int pageSize,
        out int total)
    {
        var items = ordered.ToList();
        total = items.Count;
        return items
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(DescribeGachaRecord)
            .ToList();
    }

    private static object DescribeGachaRecord(GachaLogView view) => new
    {
        id = view.Log.Id,
        gachaType = view.Log.GachaType,
        pool = view.Bucket,
        poolName = DescribeBucketName(view.Bucket),
        itemId = NullIfEmpty(view.Log.ItemId),
        name = view.Log.Name,
        itemType = NullIfEmpty(view.Log.ItemType),
        rank = ParseRank(view.Log.RankType),
        count = ParseCount(view.Log.Count),
        time = FormatRecordTime(view.Time),
        pity = view.Pity
    };

    private static object DescribePeriod(GachaPeriodSummary summary, bool includePlayer)
    {
        var row = summary.Row;
        return new
        {
            key = row.Key,
            version = row.Version,
            pool = row.PoolType,
            poolName = string.IsNullOrWhiteSpace(row.PoolName) ? GachaService.GetPoolName(row.PoolType) : row.PoolName,
            start = row.StartText,
            end = row.EndText,
            upItems = row.UpItems
                .Select(item => (object)new
                {
                    itemId = item.ItemId > 0 ? item.ItemId : (int?)null,
                    name = item.Name,
                    rank = item.Rank > 0 ? item.Rank : (int?)null
                })
                .ToList(),
            pulls = includePlayer ? summary.Records.Count : (int?)null,
            fiveStarCount = includePlayer ? summary.FiveStars.Count : (int?)null,
            fourStarCount = includePlayer ? summary.FourStarCount : (int?)null,
            gotUpFiveStar = includePlayer ? summary.GotUpFiveStar : (bool?)null,
            obtainedFiveStars = includePlayer
                ? summary.FiveStars
                    .Select(view => DescribeFiveStar(view, row))
                    .ToList()
                : new List<object>()
        };
    }

    private static object DescribeFiveStar(GachaLogView view, GachaPoolRow row) => new
    {
        name = view.Log.Name,
        itemId = NullIfEmpty(view.Log.ItemId),
        pity = view.Pity,
        time = FormatRecordTime(view.Time),
        isUp = IsUpItem(row, view.Log)
    };

    private static List<object> DescribeObtainedItems(IEnumerable<GachaLogView> records) => records
        .GroupBy(view => string.IsNullOrWhiteSpace(view.Log.ItemId) ? $"name:{view.Log.Name}" : $"id:{view.Log.ItemId}",
            StringComparer.Ordinal)
        .Select(group => new { First = group.First(), Count = group.Count() })
        .OrderByDescending(entry => ParseRank(entry.First.Log.RankType))
        .ThenByDescending(entry => entry.Count)
        .ThenBy(entry => entry.First.Log.Name, StringComparer.Ordinal)
        .Select(entry => (object)new
        {
            name = entry.First.Log.Name,
            itemId = NullIfEmpty(entry.First.Log.ItemId),
            rank = ParseRank(entry.First.Log.RankType),
            count = entry.Count
        })
        .ToList();

    private static GachaPeriodSummary BuildPeriodSummary(GachaPoolRow row, IEnumerable<GachaLogView> records)
    {
        var pulls = records
            .Where(view => string.Equals(view.Log.GachaType, row.PoolType, StringComparison.Ordinal) &&
                           view.Time != DateTime.MinValue &&
                           view.Time >= row.Start &&
                           view.Time <= row.End)
            .ToList();
        var fiveStars = pulls.Where(view => view.Log.RankType == "5").ToList();

        return new GachaPeriodSummary(
            row,
            pulls,
            fiveStars,
            pulls.Count(view => view.Log.RankType == "4"),
            fiveStars.Any(view => IsUpItem(row, view.Log)));
    }

    private static bool IsUpItem(GachaPoolRow row, GachaLogItem log) => row.UpItems.Any(item =>
        (item.ItemId > 0 && log.ItemId == item.ItemId.ToString(CultureInfo.InvariantCulture)) ||
        (!string.IsNullOrWhiteSpace(item.Name) &&
         string.Equals(log.Name, item.Name, StringComparison.Ordinal)));

    private static bool MatchesPeriodKey(GachaPoolRow row, string key) =>
        string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase) ||
        string.Equals($"{row.PoolType}|{row.Version}", key, StringComparison.OrdinalIgnoreCase) ||
        string.Equals($"{row.PoolType}|{row.Version}|{row.Start:yyyy-MM-dd}", key, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPeriodQuery(GachaPoolRow row, string query) =>
        ContainsText(row.Version, query) ||
        ContainsText(row.PoolName, query) ||
        ContainsText(row.StartText, query) ||
        ContainsText(row.EndText, query) ||
        row.UpItems.Any(item => ContainsText(item.Name, query));

    private Task<GachaData> LoadGachaDataAsync(string? uid, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var records = uid == null
                ? new List<GachaLogView>()
                : BuildGachaViews(_metadataRepository.GetGachaLogsByUid(uid), cancellationToken);
            return new GachaData(uid, records, LoadGachaPeriods(cancellationToken));
        }, cancellationToken);

    private List<GachaPoolRow> LoadGachaPeriods(CancellationToken cancellationToken)
    {
        var rankByItemId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var metadata in _metadataRepository.GetAllMetadata())
        {
            if (string.IsNullOrWhiteSpace(metadata.ItemId) || rankByItemId.ContainsKey(metadata.ItemId))
            {
                continue;
            }

            if (int.TryParse(metadata.Rank, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank))
            {
                rankByItemId[metadata.ItemId] = rank;
            }
        }

        var rows = new List<GachaPoolRow>();
        foreach (var entity in _metadataRepository.GetPoolMetadataByType(GachaPoolPeriodTypes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryParseRecordTime(entity.StartTime, out var start) ||
                !TryParseRecordTime(entity.EndTime, out var end))
            {
                continue;
            }

            rows.Add(new GachaPoolRow(
                $"{entity.PoolType}|{entity.Version}|{entity.StartTime}",
                entity.PoolType,
                entity.Version ?? string.Empty,
                entity.PoolName ?? string.Empty,
                start,
                end,
                entity.StartTime ?? string.Empty,
                entity.EndTime ?? string.Empty,
                ParseUpItems(entity, rankByItemId)));
        }

        return rows
            .OrderByDescending(row => row.Start)
            .ThenBy(row => GachaPoolPeriodOrder(row.PoolType))
            .ThenBy(row => row.Version, StringComparer.Ordinal)
            .ToList();
    }

    private static List<GachaUpItem> ParseUpItems(GachaPoolMetadataEntity entity,
        IReadOnlyDictionary<string, int> rankByItemId)
    {
        var ids = ParseJsonIntList(entity.UpItems);
        var names = ParseJsonStringList(entity.UpItemNames);
        var count = Math.Max(ids.Count, names.Count);
        var items = new List<GachaUpItem>(count);

        for (var index = 0; index < count; index++)
        {
            var itemId = index < ids.Count ? ids[index] : 0;
            var name = index < names.Count ? names[index] : string.Empty;
            var rank = itemId > 0 &&
                       rankByItemId.TryGetValue(itemId.ToString(CultureInfo.InvariantCulture), out var resolved)
                ? resolved
                : 0;
            items.Add(new GachaUpItem(itemId, name, rank));
        }

        return items;
    }

    private static List<int> ParseJsonIntList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<string> ParseJsonStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<GachaLogView> BuildGachaViews(IEnumerable<GachaLogEntity> entities,
        CancellationToken cancellationToken)
    {
        var ordered = entities
            .Select(entity => new
            {
                Log = new GachaLogItem
                {
                    Id = entity.Id,
                    Uid = entity.Uid,
                    GachaType = entity.GachaType,
                    ItemId = entity.ItemId ?? string.Empty,
                    Count = entity.Count ?? string.Empty,
                    Time = entity.Time ?? string.Empty,
                    Name = entity.Name ?? string.Empty,
                    Lang = entity.Lang ?? string.Empty,
                    ItemType = entity.ItemType ?? string.Empty,
                    RankType = entity.RankType ?? string.Empty
                },
                Time = TryParseRecordTime(entity.Time, out var parsed) ? parsed : DateTime.MinValue,
                SortId = ParseRecordId(entity.Id)
            })
            .OrderBy(item => item.Time)
            .ThenBy(item => item.SortId)
            .ToList();

        var counters = new Dictionary<string, int>(StringComparer.Ordinal);
        var views = new List<GachaLogView>(ordered.Count);
        foreach (var item in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bucket = NormalizeGachaBucket(item.Log.GachaType);
            counters.TryGetValue(bucket, out var counter);
            counter++;
            var pity = counter;
            if (item.Log.RankType == "5")
            {
                counter = 0;
            }

            counters[bucket] = counter;
            views.Add(new GachaLogView(item.Log, item.Time, item.SortId, bucket, pity));
        }

        return views;
    }

    private async Task<string> ResolveGachaUidAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var explicitUid = OptionalString(arguments, "uid")?.Trim();
        if (!string.IsNullOrEmpty(explicitUid))
        {
            if (explicitUid.Length is < 9 or > 10 || !explicitUid.All(char.IsAsciiDigit))
            {
                throw new McpToolException(
                    $"Invalid uid \"{explicitUid}\". A Genshin Impact UID is a 9 or 10 digit number.");
            }

            return explicitUid;
        }

        var knownUids = await Task.Run(() => _metadataRepository.GetDistinctUids(), cancellationToken)
            .ConfigureAwait(false);

        try
        {
            var stored = await _localSettingsService.ReadSettingAsync(GachaLastSelectedUidSettingKey)
                .ConfigureAwait(false);
            var storedUid = stored?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(storedUid) &&
                (knownUids.Count == 0 || knownUids.Contains(storedUid, StringComparer.Ordinal)))
            {
                return storedUid;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpToolInvoker", $"[Mcp] Failed to read the last gacha UID: {ex.Message}");
        }

        var roleUid = _accountManager.GetActiveAccountEntry() is { } account
            ? GameRoleSelection.Current(account)?.game_uid
            : null;
        if (!string.IsNullOrWhiteSpace(roleUid) &&
            (knownUids.Count == 0 || knownUids.Contains(roleUid, StringComparer.Ordinal)))
        {
            return roleUid;
        }

        if (knownUids.Count == 1)
        {
            return knownUids[0];
        }

        if (knownUids.Count > 1)
        {
            throw new McpToolException(
                $"Wish records exist for several players. Pass \"uid\" explicitly. Known UIDs: {string.Join(", ", knownUids)}.");
        }

        throw new McpToolException(
            "No wish records are stored for this launcher yet. Open the gacha analysis window and refresh the records first.");
    }

    private async Task<string?> TryResolveGachaUidAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(OptionalString(arguments, "uid")))
        {
            return await ResolveGachaUidAsync(arguments, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await ResolveGachaUidAsync(arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (McpToolException)
        {
            return null;
        }
    }

    private static HashSet<string>? ResolveGachaPoolTypes(string? pool)
    {
        if (string.IsNullOrWhiteSpace(pool))
        {
            return null;
        }

        return pool.Trim().ToLowerInvariant() switch
        {
            "character" => new HashSet<string> { "301", "400" },
            "weapon" => new HashSet<string> { "302" },
            "chronicled" => new HashSet<string> { "500" },
            "novice" => new HashSet<string> { "100" },
            "standard" => new HashSet<string> { "200" },
            "301" => new HashSet<string> { "301" },
            "400" => new HashSet<string> { "400" },
            "302" => new HashSet<string> { "302" },
            "500" => new HashSet<string> { "500" },
            "100" => new HashSet<string> { "100" },
            "200" => new HashSet<string> { "200" },
            _ => throw new McpToolException(
                $"Unknown pool \"{pool}\". Use character, weapon, chronicled, novice, standard or a gacha type id (301, 400, 302, 500, 100, 200).")
        };
    }

    private static int? OptionalRank(JsonElement arguments)
    {
        var text = OptionalString(arguments, "rank");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) ||
            rank is < 3 or > 5)
        {
            throw new McpToolException($"Invalid rank \"{text}\". Use 3, 4 or 5.");
        }

        return rank;
    }

    private static string NormalizeGachaBucket(string? gachaType) => gachaType switch
    {
        "301" or "400" => "character",
        "302" => "weapon",
        "500" => "chronicled",
        "100" => "novice",
        _ => "standard"
    };

    private static string DescribeBucketName(string bucket) => bucket switch
    {
        "character" => GachaService.GetPoolName("301"),
        "weapon" => GachaService.GetPoolName("302"),
        "chronicled" => GachaService.GetPoolName("500"),
        "novice" => GachaService.GetPoolName("100"),
        _ => GachaService.GetPoolName("200")
    };

    private static int GachaBucketOrder(string bucket) => bucket switch
    {
        "character" => 0,
        "weapon" => 1,
        "chronicled" => 2,
        "novice" => 3,
        _ => 4
    };

    private static int GachaPoolPeriodOrder(string poolType) => poolType switch
    {
        "301" => 0,
        "400" => 1,
        "302" => 2,
        "500" => 3,
        _ => 4
    };

    private static int ParseRank(string? rankType) =>
        int.TryParse(rankType, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) ? rank : 0;

    private static int ParseCount(string? count) =>
        int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : 1;

    private static long ParseRecordId(string? id) =>
        long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static readonly string[] RecordTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-M-d HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/M/d HH:mm:ss",
        "yyyy.MM.dd HH:mm:ss",
        "yyyy.M.d HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy/MM/dd HH:mm",
        "yyyy.MM.dd HH:mm",
        "yyyy-MM-dd",
        "yyyy-M-d",
        "yyyy/MM/dd",
        "yyyy/M/d",
        "yyyy.MM.dd",
        "yyyy.M.d"
    ];

    private static readonly string[] InputDateFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd",
        "yyyy/M/d HH:mm:ss",
        "yyyy/M/d HH:mm",
        "yyyy/M/d",
        "yyyy.M.d HH:mm:ss",
        "yyyy.M.d"
    ];

    private static bool TryParseRecordTime(string? text, out DateTime value)
    {
        value = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (DateTime.TryParseExact(trimmed, RecordTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out value))
        {
            return true;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out value) ||
            DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out value))
        {
            return true;
        }

        var normalized = NormalizeDateSeparators(trimmed);
        return !string.Equals(normalized, trimmed, StringComparison.Ordinal) &&
               DateTime.TryParseExact(normalized, RecordTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None,
                   out value);
    }

    private static string NormalizeDateSeparators(string text)
    {
        var separator = text.IndexOf(' ');
        var datePart = separator < 0 ? text : text[..separator];
        if (!datePart.Contains('.'))
        {
            return text;
        }

        var rest = separator < 0 ? string.Empty : text[separator..];
        return $"{datePart.Replace('.', '-')}{rest}";
    }

    private static DateTime? ParseUserDateTime(string? text, bool endOfDay)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (!DateTime.TryParseExact(trimmed, InputDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var parsed) &&
            !DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces,
                out parsed) &&
            !DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            throw new McpToolException(
                $"Invalid date \"{text}\". Use yyyy-MM-dd or yyyy-MM-dd HH:mm:ss.");
        }

        if (trimmed.Contains(':'))
        {
            return parsed;
        }

        return endOfDay ? parsed.Date.AddDays(1).AddTicks(-1) : parsed.Date;
    }

    private static string? FormatRecordTime(DateTime value) => value == DateTime.MinValue
        ? null
        : value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool ContainsText(string? source, string? keyword) =>
        !string.IsNullOrWhiteSpace(source) &&
        !string.IsNullOrWhiteSpace(keyword) &&
        source.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int PageCount(int total, int pageSize) =>
        total <= 0 ? 0 : (total + pageSize - 1) / pageSize;

    private static string FormatPercent(int hit, int total) =>
        total <= 0 ? "0%" : $"{(double)hit * 100 / total:0.##}%";
}
