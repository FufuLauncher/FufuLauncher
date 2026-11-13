/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed class GamePlayTimeStore
{
    public const int CurrentSchemaVersion = 2;
    public const string StorageIdentifier = "fufu.game-play-time";
    public const string CloudPlayTimeVersion = "v1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CompactCloudJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<string, long> _dailyPlayTimeSeconds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _legacyDailySeconds = new(StringComparer.Ordinal);
    private readonly List<PlayTimeSegment> _segments = new();
    private string? _lastPlayDate;
    private bool _hasVerifiedStorageIdentity;
    private bool _isLoaded;

    public event EventHandler? PlayTimeChanged;

    public IReadOnlyDictionary<string, long> GetDailyPlayTimeSnapshot()
    {
        lock (_sync)
        {
            return new Dictionary<string, long>(_dailyPlayTimeSeconds, StringComparer.Ordinal);
        }
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isLoaded) return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            lock (_sync)
            {
                if (_isLoaded) return;
            }

            await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PlayTimeLocalSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        lock (_sync)
        {
            var segSeconds = SumSegmentSeconds(_segments);
            var totalSeconds = _dailyPlayTimeSeconds.Values.Sum();
            var legacySeconds = _legacyDailySeconds.Values.Sum();
            var activeDays = _dailyPlayTimeSeconds.Count(kv => kv.Value > 0);
            return new PlayTimeLocalSummary
            {
                TotalDays = activeDays,
                TotalSeconds = totalSeconds,
                SegmentCount = _segments.Count,
                SegmentedSeconds = segSeconds,
                LegacySeconds = legacySeconds
            };
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        PlayTimeChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _dailyPlayTimeSeconds.Clear();
            _legacyDailySeconds.Clear();
            _segments.Clear();
            _lastPlayDate = null;
            _hasVerifiedStorageIdentity = false;
        }

        var storagePath = AppPaths.GamePlayTimeFile;
        if (File.Exists(storagePath))
        {
            if (await TryLoadAsync(storagePath, cancellationToken))
            {
                lock (_sync)
                {
                    _isLoaded = true;
                }

                if (_hasVerifiedStorageIdentity)
                {
                    await RetireMigratedLegacyAsync(cancellationToken);
                }

                return;
            }

            BackupCorruptStorage(storagePath);
        }

        await MigrateLegacyCoreAsync(cancellationToken);
        lock (_sync)
        {
            _isLoaded = true;
        }
    }

    public void AddPlayTime(DateTime localDate, long secondsToAdd)
    {
        if (secondsToAdd <= 0) return;

        var endUnix = Math.Max(secondsToAdd, ToDateTimeOffset(localDate).ToUnixTimeSeconds());
        var startUnix = Math.Max(0, endUnix - secondsToAdd);
        var dateKey = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        lock (_sync)
        {
            if (_segments.Count > 0)
            {
                var last = _segments[^1];
                if (endUnix >= last.End - 5 && endUnix <= last.End + secondsToAdd + 25)
                {
                    last.End += secondsToAdd;
                }
                else
                {
                    _segments.Add(new PlayTimeSegment { Start = startUnix, End = endUnix });
                    ReplaceSegmentsNormalizedLocked(_segments);
                }
            }
            else
            {
                _segments.Add(new PlayTimeSegment { Start = startUnix, End = endUnix });
            }

            var deltaDateKey = dateKey;
            RecalculateDailyTotalsLocked(beforeDailyFloor: null);

            if (string.IsNullOrEmpty(_lastPlayDate) || string.CompareOrdinal(deltaDateKey, _lastPlayDate) > 0)
            {
                _lastPlayDate = deltaDateKey;
            }
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var document = BuildDocumentSnapshot();
            await SaveDocumentCoreAsync(document, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private GamePlayTimeDocument BuildDocumentSnapshot()
    {
        lock (_sync)
        {
            return new GamePlayTimeDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                StorageKey = StorageIdentifier,
                LastPlayDate = _lastPlayDate,
                UpdatedAt = DateTimeOffset.Now,
                DailyPlayTimeSeconds = new Dictionary<string, long>(_dailyPlayTimeSeconds, StringComparer.Ordinal),
                LegacyDailySeconds = new Dictionary<string, long>(_legacyDailySeconds, StringComparer.Ordinal),
                Segments = _segments.Select(s => new PlayTimeSegment { Start = s.Start, End = s.End }).ToList()
            };
        }
    }

    private static async Task SaveDocumentCoreAsync(GamePlayTimeDocument document, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await WriteAtomicallyAsync(AppPaths.GamePlayTimeFile, json, cancellationToken);
    }

    public async Task<string> BuildCloudPlayTimeJsonAsync(string appVersion,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        PlayTimeCloudDocument cloudDoc;
        lock (_sync)
        {
            var normalizedVersion = string.IsNullOrWhiteSpace(appVersion) ? "1.0.0" : appVersion.Trim();
            if (normalizedVersion.Length > 64) normalizedVersion = normalizedVersion[..64];

            ReplaceSegmentsNormalizedLocked(_segments);
            RecalculateDailyTotalsLocked(beforeDailyFloor: null);

            var cleanDaily = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in _dailyPlayTimeSeconds.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (IsValidDateKey(kv.Key) && kv.Value >= 0)
                {
                    cleanDaily[kv.Key] = kv.Value;
                }
            }

            var cleanLegacy = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in _legacyDailySeconds.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (IsValidDateKey(kv.Key) && kv.Value > 0)
                {
                    cleanLegacy[kv.Key] = kv.Value;
                }
            }

            cloudDoc = new PlayTimeCloudDocument
            {
                Info = new PlayTimeCloudInfo
                {
                    ExportApp = "FufuLauncher",
                    ExportAppVersion = normalizedVersion,
                    PlaytimeVersion = CloudPlayTimeVersion,
                    ExportTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                Segments = _segments.Select(s => new PlayTimeSegment { Start = s.Start, End = s.End }).ToList(),
                DailySeconds = cleanDaily,
                LegacyDailySeconds = cleanLegacy
            };
        }

        return JsonSerializer.Serialize(cloudDoc, CompactCloudJsonOptions);
    }

    public async Task<PlayTimeMergeResult> MergeCloudPlayTimeJsonAsync(string cloudJson,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseCloudDocument(cloudJson, out var incomingDoc) || incomingDoc == null)
        {
            return new PlayTimeMergeResult { Ok = false, ErrorCode = "INVALID_PLAYTIME_SCHEMA" };
        }

        GamePlayTimeDocument snapshotToSave;
        PlayTimeMergeResult result;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsLoadedFlag())
            {
                await LoadCoreAsync(cancellationToken);
            }

            lock (_sync)
            {
                var beforeDaily = new Dictionary<string, long>(_dailyPlayTimeSeconds, StringComparer.Ordinal);
                var beforeTotal = beforeDaily.Values.Sum();
                var beforeSegments = NormalizeSegments(_segments);
                var beforeSegTotal = SumSegmentSeconds(beforeSegments);

                var incomingSegments = NormalizeSegments(incomingDoc.Segments);
                var incomingSegTotal = SumSegmentSeconds(incomingSegments);
                var incomingSegDaily = ComputeSegmentDailySeconds(incomingSegments);

                var incomingLegacy = new Dictionary<string, long>(StringComparer.Ordinal);
                if (incomingDoc.LegacyDailySeconds != null)
                {
                    foreach (var kv in incomingDoc.LegacyDailySeconds)
                    {
                        if (IsValidDateKey(kv.Key) && kv.Value > 0)
                        {
                            incomingLegacy[kv.Key] = kv.Value;
                        }
                    }
                }
                else
                {
                    foreach (var kv in incomingDoc.DailySeconds)
                    {
                        if (!IsValidDateKey(kv.Key) || kv.Value <= 0) continue;
                        var segCovered = incomingSegDaily.GetValueOrDefault(kv.Key);
                        var unseg = incomingSegments.Count == 0
                            ? kv.Value
                            : Math.Max(0, kv.Value - segCovered);
                        if (unseg > 0)
                        {
                            incomingLegacy[kv.Key] = unseg;
                        }
                    }
                }

                var combinedSegments = new List<PlayTimeSegment>(beforeSegments.Count + incomingSegments.Count);
                combinedSegments.AddRange(beforeSegments);
                combinedSegments.AddRange(incomingSegments);
                var unionSegments = NormalizeSegments(combinedSegments);
                var afterSegTotal = SumSegmentSeconds(unionSegments);
                var overlapDedupSeconds = Math.Max(0, (beforeSegTotal + incomingSegTotal) - afterSegTotal);

                ReplaceSegmentsNormalizedLocked(unionSegments);

                foreach (var kv in incomingLegacy)
                {
                    _legacyDailySeconds[kv.Key] = Math.Max(_legacyDailySeconds.GetValueOrDefault(kv.Key), kv.Value);
                }

                RecalculateDailyTotalsLocked(beforeDailyFloor: beforeDaily);

                var afterTotal = _dailyPlayTimeSeconds.Values.Sum();
                var addedSeconds = Math.Max(0, afterTotal - beforeTotal);
                var updatedDaysCount = 0;
                foreach (var kv in _dailyPlayTimeSeconds)
                {
                    if (kv.Value > beforeDaily.GetValueOrDefault(kv.Key))
                    {
                        updatedDaysCount++;
                    }
                }

                result = new PlayTimeMergeResult
                {
                    Ok = true,
                    BeforeTotalSeconds = beforeTotal,
                    AfterTotalSeconds = afterTotal,
                    AddedSeconds = addedSeconds,
                    IncomingSegmentSeconds = incomingSegTotal,
                    OverlapDeduplicatedSeconds = overlapDedupSeconds,
                    ResultingSegmentCount = _segments.Count,
                    UpdatedDaysCount = updatedDaysCount
                };

                snapshotToSave = new GamePlayTimeDocument
                {
                    SchemaVersion = CurrentSchemaVersion,
                    StorageKey = StorageIdentifier,
                    LastPlayDate = _lastPlayDate,
                    UpdatedAt = DateTimeOffset.Now,
                    DailyPlayTimeSeconds = new Dictionary<string, long>(_dailyPlayTimeSeconds, StringComparer.Ordinal),
                    LegacyDailySeconds = new Dictionary<string, long>(_legacyDailySeconds, StringComparer.Ordinal),
                    Segments = _segments.Select(s => new PlayTimeSegment { Start = s.Start, End = s.End }).ToList()
                };
            }

            await SaveDocumentCoreAsync(snapshotToSave, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        PlayTimeChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public static string? TryConvertPlayTimeJsonToCloudJson(string rawJson, string appVersion)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;

        var normalizedVersion = string.IsNullOrWhiteSpace(appVersion) ? "1.0.0" : appVersion.Trim();
        if (normalizedVersion.Length > 64) normalizedVersion = normalizedVersion[..64];

        if (TryParseCloudDocument(rawJson, out var existingCloud) && existingCloud != null)
        {
            var normalizedSegments = NormalizeSegments(existingCloud.Segments);
            var cleanDaily = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in existingCloud.DailySeconds)
            {
                if (IsValidDateKey(kv.Key) && kv.Value >= 0)
                {
                    cleanDaily[kv.Key] = kv.Value;
                }
            }

            var cleanLegacy = existingCloud.LegacyDailySeconds != null
                ? new Dictionary<string, long>(StringComparer.Ordinal)
                : null;
            if (cleanLegacy != null && existingCloud.LegacyDailySeconds != null)
            {
                foreach (var kv in existingCloud.LegacyDailySeconds)
                {
                    if (IsValidDateKey(kv.Key) && kv.Value >= 0)
                    {
                        cleanLegacy[kv.Key] = kv.Value;
                    }
                }
            }

            var doc = new PlayTimeCloudDocument
            {
                Info = new PlayTimeCloudInfo
                {
                    ExportApp = string.IsNullOrWhiteSpace(existingCloud.Info.ExportApp)
                        ? "FufuLauncher"
                        : existingCloud.Info.ExportApp,
                    ExportAppVersion = string.IsNullOrWhiteSpace(existingCloud.Info.ExportAppVersion)
                        ? normalizedVersion
                        : existingCloud.Info.ExportAppVersion,
                    PlaytimeVersion = CloudPlayTimeVersion,
                    ExportTimestamp = existingCloud.Info.ExportTimestamp > 0
                        ? existingCloud.Info.ExportTimestamp
                        : DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                Segments = normalizedSegments,
                DailySeconds = cleanDaily,
                LegacyDailySeconds = cleanLegacy
            };
            return JsonSerializer.Serialize(doc, CompactCloudJsonOptions);
        }

        try
        {
            var localDoc = JsonSerializer.Deserialize<GamePlayTimeDocument>(rawJson, JsonOptions);
            if (localDoc == null || !string.Equals(localDoc.StorageKey, StorageIdentifier, StringComparison.Ordinal))
            {
                return null;
            }

            var segments = NormalizeSegments(localDoc.Segments ?? new List<PlayTimeSegment>());
            var segDaily = ComputeSegmentDailySeconds(segments);
            var legacy = new Dictionary<string, long>(StringComparer.Ordinal);

            if (localDoc.LegacyDailySeconds != null)
            {
                foreach (var kv in localDoc.LegacyDailySeconds)
                {
                    if (IsValidDateKey(kv.Key) && kv.Value > 0)
                    {
                        legacy[kv.Key] = kv.Value;
                    }
                }
            }

            var daily = new Dictionary<string, long>(StringComparer.Ordinal);
            if (localDoc.DailyPlayTimeSeconds != null)
            {
                foreach (var kv in localDoc.DailyPlayTimeSeconds)
                {
                    if (!IsValidDateKey(kv.Key) || kv.Value < 0) continue;
                    daily[kv.Key] = kv.Value;
                    var covered = segDaily.GetValueOrDefault(kv.Key);
                    if (kv.Value > covered && localDoc.LegacyDailySeconds == null)
                    {
                        legacy[kv.Key] = kv.Value - covered;
                    }
                }
            }

            foreach (var kv in segDaily)
            {
                daily[kv.Key] = Math.Max(daily.GetValueOrDefault(kv.Key), legacy.GetValueOrDefault(kv.Key) + kv.Value);
            }

            var cloudDoc = new PlayTimeCloudDocument
            {
                Info = new PlayTimeCloudInfo
                {
                    ExportApp = "FufuLauncher",
                    ExportAppVersion = normalizedVersion,
                    PlaytimeVersion = CloudPlayTimeVersion,
                    ExportTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                Segments = segments,
                DailySeconds = daily,
                LegacyDailySeconds = legacy
            };
            return JsonSerializer.Serialize(cloudDoc, CompactCloudJsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string FormatMergeSummary(string slot, PlayTimeMergeResult merge)
    {
        var totalText = FormatSecondsReadable(merge.AfterTotalSeconds);
        if (merge.AddedSeconds <= 0)
        {
            return string.Format("CloudSync_PlayTimeMergedNoGainFormat".GetLocalized(), slot, totalText);
        }

        var addedText = FormatSecondsReadable(merge.AddedSeconds);
        if (merge.OverlapDeduplicatedSeconds > 0)
        {
            var dedupText = FormatSecondsReadable(merge.OverlapDeduplicatedSeconds);
            return string.Format("CloudSync_PlayTimeMergedOverlapFormat".GetLocalized(), slot, addedText, dedupText,
                totalText);
        }

        return string.Format("CloudSync_PlayTimeMergedAddedFormat".GetLocalized(), slot, addedText, totalText);
    }

    public static string FormatSecondsReadable(long totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return string.Format("CloudSync_DurationSecsFormat".GetLocalized(), 0);
        }

        if (totalSeconds >= 3600)
        {
            var hours = totalSeconds / 3600;
            var mins = (totalSeconds % 3600) / 60;
            return string.Format("CloudSync_DurationHoursMinsFormat".GetLocalized(), hours, mins);
        }

        if (totalSeconds >= 60)
        {
            var mins = totalSeconds / 60;
            var secs = totalSeconds % 60;
            return string.Format("CloudSync_DurationMinsSecsFormat".GetLocalized(), mins, secs);
        }

        return string.Format("CloudSync_DurationSecsFormat".GetLocalized(), totalSeconds);
    }

    private bool IsLoadedFlag()
    {
        lock (_sync)
        {
            return _isLoaded;
        }
    }

    private static bool TryParseCloudDocument(string rawJson, out PlayTimeCloudDocument? doc)
    {
        doc = null;
        if (string.IsNullOrWhiteSpace(rawJson)) return false;

        try
        {
            using var jsonDoc = JsonDocument.Parse(rawJson);
            if (jsonDoc.RootElement.ValueKind != JsonValueKind.Object) return false;
            var root = jsonDoc.RootElement;
            if (!root.TryGetProperty("info", out var infoEl) || infoEl.ValueKind != JsonValueKind.Object) return false;
            if (!infoEl.TryGetProperty("playtime_version", out var verEl) ||
                verEl.ValueKind != JsonValueKind.String ||
                !string.Equals(verEl.GetString(), CloudPlayTimeVersion, StringComparison.Ordinal))
            {
                return false;
            }

            if (!root.TryGetProperty("segments", out var segEl) || segEl.ValueKind != JsonValueKind.Array) return false;
            if (!root.TryGetProperty("daily_seconds", out var dailyEl) ||
                dailyEl.ValueKind != JsonValueKind.Object) return false;

            var parsed = JsonSerializer.Deserialize<PlayTimeCloudDocument>(rawJson, CompactCloudJsonOptions);
            if (parsed?.Info == null || parsed.Segments == null || parsed.DailySeconds == null) return false;

            foreach (var seg in parsed.Segments)
            {
                if (seg == null || seg.Start < 0 || seg.End < seg.Start) return false;
            }

            foreach (var kv in parsed.DailySeconds)
            {
                if (!IsValidDateKey(kv.Key) || kv.Value < 0) return false;
            }

            if (parsed.LegacyDailySeconds != null)
            {
                foreach (var kv in parsed.LegacyDailySeconds)
                {
                    if (!IsValidDateKey(kv.Key) || kv.Value < 0) return false;
                }
            }

            doc = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

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

    private async Task<bool> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var document = JsonSerializer.Deserialize<GamePlayTimeDocument>(json, JsonOptions);
            if (document == null) return false;

            var dailySeconds = document.DailyPlayTimeSeconds;
            lock (_sync)
            {
                _dailyPlayTimeSeconds.Clear();
                _legacyDailySeconds.Clear();
                _segments.Clear();

                if (document.Segments != null)
                {
                    ReplaceSegmentsNormalizedLocked(document.Segments);
                }

                var segDaily = ComputeSegmentDailySeconds(_segments);
                if (document.LegacyDailySeconds != null)
                {
                    foreach (var entry in document.LegacyDailySeconds)
                    {
                        if (!IsValidDateKey(entry.Key) || entry.Value <= 0) continue;
                        _legacyDailySeconds[entry.Key] = entry.Value;
                    }
                }

                var loadedDaily = new Dictionary<string, long>(StringComparer.Ordinal);
                if (dailySeconds != null)
                {
                    foreach (var entry in dailySeconds)
                    {
                        if (!IsValidDateKey(entry.Key) || entry.Value < 0) continue;
                        loadedDaily[entry.Key] = entry.Value;

                        var segCovered = segDaily.GetValueOrDefault(entry.Key);
                        if (entry.Value > segCovered && document.LegacyDailySeconds == null)
                        {
                            _legacyDailySeconds[entry.Key] = Math.Max(
                                _legacyDailySeconds.GetValueOrDefault(entry.Key),
                                entry.Value - segCovered);
                        }
                    }
                }

                _lastPlayDate = document.LastPlayDate;
                RecalculateDailyTotalsLocked(loadedDaily);

                _hasVerifiedStorageIdentity =
                    string.Equals(document.StorageKey, StorageIdentifier, StringComparison.Ordinal) &&
                    document.SchemaVersion is 1 or CurrentSchemaVersion;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to load {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private async Task MigrateLegacyCoreAsync(CancellationToken cancellationToken)
    {
        var legacyPath = AppPaths.LegacyFufuConfigFile;
        if (!File.Exists(legacyPath)) return;

        LegacyGamePlayTimeConfig? legacy;
        try
        {
            var json = await File.ReadAllTextAsync(legacyPath, cancellationToken);
            legacy = JsonSerializer.Deserialize<LegacyGamePlayTimeConfig>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to read legacy {Path.GetFileName(legacyPath)}: {ex.Message}");
            return;
        }

        if (legacy == null) return;

        lock (_sync)
        {
            if (legacy.GamePlayTimeData != null)
            {
                foreach (var entry in legacy.GamePlayTimeData)
                {
                    if (!IsValidDateKey(entry.Key) || entry.Value < 0) continue;
                    _legacyDailySeconds[entry.Key] = entry.Value;
                }
            }

            _lastPlayDate = string.IsNullOrWhiteSpace(legacy.LastPlayDate) ? null : legacy.LastPlayDate;
            RecalculateDailyTotalsLocked(beforeDailyFloor: null);
        }

        try
        {
            var snapshot = BuildDocumentSnapshot();
            await SaveDocumentCoreAsync(snapshot, cancellationToken);
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to write migrated game time data; legacy config retained: {ex.Message}");
            return;
        }

        if (!await VerifyMigratedStorageAsync(cancellationToken))
        {
            DebugLog.Info("GamePlayTimeStore",
                "Migrated game time storage verification failed; legacy config retained");
            return;
        }

        TryDeleteLegacyConfig(legacyPath);
        DebugLog.Info("GamePlayTimeStore",
            $"Migrated legacy game time data to {Path.GetFileName(AppPaths.GamePlayTimeFile)}");
    }

    private async Task RetireMigratedLegacyAsync(CancellationToken cancellationToken)
    {
        var legacyPath = AppPaths.LegacyFufuConfigFile;
        if (!File.Exists(legacyPath)) return;

        LegacyGamePlayTimeConfig? legacy;
        try
        {
            var json = await File.ReadAllTextAsync(legacyPath, cancellationToken);
            legacy = JsonSerializer.Deserialize<LegacyGamePlayTimeConfig>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to read legacy {Path.GetFileName(legacyPath)}; legacy config retained: {ex.Message}");
            return;
        }

        if (legacy == null) return;

        lock (_sync)
        {
            if (legacy.GamePlayTimeData != null)
            {
                foreach (var entry in legacy.GamePlayTimeData)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value <= 0) continue;
                    if (!_dailyPlayTimeSeconds.TryGetValue(entry.Key, out var migratedSeconds) ||
                        migratedSeconds < entry.Value)
                    {
                        DebugLog.Info("GamePlayTimeStore",
                            "Legacy game time config retained because the new storage does not contain all legacy data");
                        return;
                    }
                }
            }
        }

        TryDeleteLegacyConfig(legacyPath);
        DebugLog.Info("GamePlayTimeStore",
            "Retired legacy game time config after confirming the new storage contains its data");
    }

    private async Task<bool> VerifyMigratedStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(AppPaths.GamePlayTimeFile, cancellationToken);
            var document = JsonSerializer.Deserialize<GamePlayTimeDocument>(json, JsonOptions);
            if (document == null) return false;
            if (!string.Equals(document.StorageKey, StorageIdentifier, StringComparison.Ordinal)) return false;
            if (document.SchemaVersion != CurrentSchemaVersion) return false;

            var persistedDailySeconds = document.DailyPlayTimeSeconds;
            if (persistedDailySeconds == null) return false;

            lock (_sync)
            {
                if (!string.Equals(document.LastPlayDate, _lastPlayDate, StringComparison.Ordinal)) return false;
                if (persistedDailySeconds.Count != _dailyPlayTimeSeconds.Count) return false;

                foreach (var entry in _dailyPlayTimeSeconds)
                {
                    if (!persistedDailySeconds.TryGetValue(entry.Key, out var persistedSeconds) ||
                        persistedSeconds != entry.Value)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to verify migrated game time storage: {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteLegacyConfig(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            DebugLog.Info("GamePlayTimeStore",
                $"Deleted legacy game time config after successful migration: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to delete legacy game time config; file retained: {ex.Message}");
        }
    }

    private static async Task WriteAtomicallyAsync(string path, string json,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void BackupCorruptStorage(string path)
    {
        try
        {
            var backupPath = $"{path}.corrupt.{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: true);
            DebugLog.Info("GamePlayTimeStore", $"Backed up corrupt game time storage to {backupPath}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore", $"Failed to back up corrupt game time storage: {ex.Message}");
        }
    }
}

internal sealed class GamePlayTimeDocument
{
    public int SchemaVersion
    {
        get;
        set;
    }

    public string StorageKey
    {
        get;
        set;
    } = string.Empty;

    public string? LastPlayDate
    {
        get;
        set;
    }

    public DateTimeOffset UpdatedAt
    {
        get;
        set;
    }

    public Dictionary<string, long> DailyPlayTimeSeconds
    {
        get;
        set;
    } = new();

    public Dictionary<string, long>? LegacyDailySeconds
    {
        get;
        set;
    }

    public List<PlayTimeSegment>? Segments
    {
        get;
        set;
    }
}

internal sealed class LegacyGamePlayTimeConfig
{
    public Dictionary<string, long>? GamePlayTimeData
    {
        get;
        set;
    }

    public string? LastPlayDate
    {
        get;
        set;
    }
}