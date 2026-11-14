/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class GamePlayTimeStore
{
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
}
