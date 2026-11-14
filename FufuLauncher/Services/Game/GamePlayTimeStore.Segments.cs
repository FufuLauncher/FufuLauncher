/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class GamePlayTimeStore
{
    private void ReplaceSegmentsNormalizedLocked(List<PlayTimeSegment> segments)
    {
        var normalized = NormalizeSegments(segments);
        _segments.Clear();
        _segments.AddRange(normalized);
    }

    private void RecalculateDailyTotalsLocked(IReadOnlyDictionary<string, long>? beforeDailyFloor)
    {
        var segDaily = ComputeSegmentDailySeconds(_segments);
        var allKeys = new HashSet<string>(_legacyDailySeconds.Keys, StringComparer.Ordinal);
        allKeys.UnionWith(segDaily.Keys);
        if (beforeDailyFloor != null)
        {
            allKeys.UnionWith(beforeDailyFloor.Keys);
        }

        _dailyPlayTimeSeconds.Clear();
        string? maxDate = _lastPlayDate;

        foreach (var dateKey in allKeys)
        {
            if (!IsValidDateKey(dateKey)) continue;

            var legacySec = Math.Max(0, _legacyDailySeconds.GetValueOrDefault(dateKey));
            var segSec = Math.Max(0, segDaily.GetValueOrDefault(dateKey));
            var computed = legacySec + segSec;

            if (beforeDailyFloor != null && beforeDailyFloor.TryGetValue(dateKey, out var floorSec) &&
                computed < floorSec)
            {
                var deficit = floorSec - computed;
                legacySec += deficit;
                _legacyDailySeconds[dateKey] = legacySec;
                computed = floorSec;
            }

            if (computed > 0)
            {
                _dailyPlayTimeSeconds[dateKey] = computed;
                if (string.IsNullOrEmpty(maxDate) || string.CompareOrdinal(dateKey, maxDate) > 0)
                {
                    maxDate = dateKey;
                }
            }
        }

        _lastPlayDate = maxDate;
    }

    private static List<PlayTimeSegment> NormalizeSegments(IEnumerable<PlayTimeSegment> rawSegments)
    {
        var filtered = new List<PlayTimeSegment>();
        foreach (var seg in rawSegments)
        {
            if (seg != null && seg.Start >= 0 && seg.End > seg.Start)
            {
                filtered.Add(new PlayTimeSegment { Start = seg.Start, End = seg.End });
            }
        }

        if (filtered.Count == 0) return filtered;

        filtered.Sort((a, b) =>
        {
            var cmp = a.Start.CompareTo(b.Start);
            return cmp != 0 ? cmp : a.End.CompareTo(b.End);
        });

        var merged = new List<PlayTimeSegment>(filtered.Count) { filtered[0] };
        for (var i = 1; i < filtered.Count; i++)
        {
            var curr = filtered[i];
            var last = merged[^1];
            if (curr.Start <= last.End)
            {
                if (curr.End > last.End)
                {
                    last.End = curr.End;
                }
            }
            else
            {
                merged.Add(curr);
            }
        }

        return merged;
    }

    private static long SumSegmentSeconds(IReadOnlyList<PlayTimeSegment> segments)
    {
        long sum = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var d = segments[i].End - segments[i].Start;
            if (d > 0) sum += d;
        }

        return sum;
    }

    private static Dictionary<string, long> ComputeSegmentDailySeconds(
        IReadOnlyList<PlayTimeSegment> normalizedSegments)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < normalizedSegments.Count; i++)
        {
            var seg = normalizedSegments[i];
            if (seg.End <= seg.Start) continue;

            var current = DateTimeOffset.FromUnixTimeSeconds(seg.Start).ToLocalTime();
            var endLocal = DateTimeOffset.FromUnixTimeSeconds(seg.End).ToLocalTime();

            while (current < endLocal)
            {
                var nextDay = current.Date.AddDays(1);
                var nextOffset = TimeZoneInfo.Local.GetUtcOffset(nextDay);
                var nextMidnight = new DateTimeOffset(nextDay, nextOffset);
                var sliceEnd = endLocal < nextMidnight ? endLocal : nextMidnight;
                var delta = (long)(sliceEnd - current).TotalSeconds;
                if (delta > 0)
                {
                    var key = current.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    result[key] = result.GetValueOrDefault(key) + delta;
                }

                current = sliceEnd;
            }
        }

        return result;
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime dt)
    {
        if (dt.Kind == DateTimeKind.Utc) return new DateTimeOffset(dt, TimeSpan.Zero);
        return new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
    }

    private static bool IsValidDateKey(string key) =>
        !string.IsNullOrEmpty(key) &&
        key.Length == 10 &&
        DateOnly.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}