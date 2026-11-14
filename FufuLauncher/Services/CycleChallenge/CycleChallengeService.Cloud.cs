using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.CycleChallenge;

namespace FufuLauncher.Services.CycleChallenge;

public sealed partial class CycleChallengeService
{
    private const int MaxCloudRecords = 5000;
    private const int MaxPayloadUtf16Length = 1_048_576;
    private const int MaxSummaryUtf16Length = 16_384;
    private const int MaxIdentifierLength = 64;

    private static readonly JsonSerializerOptions CloudJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public event EventHandler? ChallengesChanged;

    public async Task<CycleChallengeLocalSummary> GetCloudSummaryAsync(
        string? uid = null,
        CancellationToken cancellationToken = default)
    {
        var counts = await Task.Run(() => _repository.GetUidModeCounts(), cancellationToken).ConfigureAwait(false);
        var normalizedFilterUid = string.IsNullOrWhiteSpace(uid) ? null : uid.Trim();

        var groupedByUid = counts
            .GroupBy(x => x.Uid, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var abyss = g.Where(x => x.Mode == (int)ChallengeMode.SpiralAbyss).Sum(x => x.Count);
                var theater = g.Where(x => x.Mode == (int)ChallengeMode.Theater).Sum(x => x.Count);
                var stygian = g.Where(x => x.Mode == (int)ChallengeMode.Stygian).Sum(x => x.Count);
                return new CycleChallengeLocalUidSummary
                {
                    Uid = g.Key,
                    TotalCount = abyss + theater + stygian,
                    AbyssCount = abyss,
                    TheaterCount = theater,
                    StygianCount = stygian
                };
            })
            .ToList();

        if (normalizedFilterUid is not null)
        {
            var matched = groupedByUid.FirstOrDefault(x => string.Equals(x.Uid, normalizedFilterUid, StringComparison.Ordinal));
            return new CycleChallengeLocalSummary
            {
                UidCount = matched is not null ? 1 : 0,
                TotalCount = matched?.TotalCount ?? 0,
                AbyssCount = matched?.AbyssCount ?? 0,
                TheaterCount = matched?.TheaterCount ?? 0,
                StygianCount = matched?.StygianCount ?? 0,
                Uids = groupedByUid
            };
        }

        return new CycleChallengeLocalSummary
        {
            UidCount = groupedByUid.Count,
            TotalCount = groupedByUid.Sum(x => x.TotalCount),
            AbyssCount = groupedByUid.Sum(x => x.AbyssCount),
            TheaterCount = groupedByUid.Sum(x => x.TheaterCount),
            StygianCount = groupedByUid.Sum(x => x.StygianCount),
            Uids = groupedByUid
        };
    }

    public async Task<string> BuildCloudChallengeJsonAsync(
        IReadOnlyList<string>? targetUids,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var entities = await Task.Run(() => _repository.GetRecords(targetUids), cancellationToken).ConfigureAwait(false);

        var exportedRecords = new List<CycleChallengeCloudRecord>(Math.Min(entities.Count, MaxCloudRecords));
        var orderedEntities = entities.Count > MaxCloudRecords
            ? entities
                .OrderByDescending(x => ParseScheduleSortKey(x.ScheduleId))
                .ThenBy(x => x.Uid, StringComparer.Ordinal)
                .ThenBy(x => x.Mode)
                .Take(MaxCloudRecords)
            : entities;

        foreach (var entity in orderedEntities)
        {
            var uid = ClampOptionalString(entity.Uid, MaxIdentifierLength);
            var scheduleId = ClampOptionalString(entity.ScheduleId, MaxIdentifierLength);
            if (string.IsNullOrEmpty(uid) ||
                string.IsNullOrEmpty(scheduleId) ||
                entity.Mode < (int)ChallengeMode.SpiralAbyss ||
                entity.Mode > (int)ChallengeMode.Stygian ||
                string.IsNullOrWhiteSpace(entity.Payload) ||
                entity.Payload.Length > MaxPayloadUtf16Length ||
                !IsValidPayloadJson(entity.Payload))
            {
                continue;
            }

            var summary = NormalizeOptionalSummaryJson(entity.Summary);
            exportedRecords.Add(new CycleChallengeCloudRecord
            {
                Uid = uid,
                Mode = entity.Mode,
                ScheduleId = scheduleId,
                AccountId = ClampOptionalString(entity.AccountId, MaxIdentifierLength),
                StartUtc = ClampOptionalString(entity.StartUtc, MaxIdentifierLength),
                EndUtc = ClampOptionalString(entity.EndUtc, MaxIdentifierLength),
                Summary = summary,
                Payload = entity.Payload,
                FetchedAt = ClampOptionalString(entity.FetchedAt, MaxIdentifierLength),
                ApiVersion = ClampOptionalString(entity.ApiVersion, MaxIdentifierLength)
            });
        }

        var doc = new CycleChallengeCloudDocument
        {
            Info = new CycleChallengeCloudInfo
            {
                ExportApp = "FufuLauncher",
                ExportAppVersion = string.IsNullOrWhiteSpace(appVersion) ? "1.0.0" : ClampOptionalString(appVersion, MaxIdentifierLength),
                ChallengeVersion = "v1.0",
                ExportTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            },
            Records = exportedRecords
        };

        return JsonSerializer.Serialize(doc, CloudJsonOptions);
    }

    public static string? TryConvertChallengeJsonToCloudJson(string rawJson, string appVersion)
    {
        if (!TryParseCloudChallengeDocument(rawJson, out var doc, out _))
        {
            return null;
        }

        var normalized = new CycleChallengeCloudDocument
        {
            Info = new CycleChallengeCloudInfo
            {
                ExportApp = string.IsNullOrWhiteSpace(doc!.Info.ExportApp)
                    ? "FufuLauncher"
                    : ClampOptionalString(doc.Info.ExportApp, 128) ?? "FufuLauncher",
                ExportAppVersion = string.IsNullOrWhiteSpace(doc.Info.ExportAppVersion)
                    ? (string.IsNullOrWhiteSpace(appVersion) ? "1.0.0" : ClampOptionalString(appVersion, MaxIdentifierLength))
                    : ClampOptionalString(doc.Info.ExportAppVersion, MaxIdentifierLength),
                ChallengeVersion = "v1.0",
                ExportTimestamp = doc.Info.ExportTimestamp > 0
                    ? doc.Info.ExportTimestamp
                    : DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            },
            Records = doc.Records
        };

        return JsonSerializer.Serialize(normalized, CloudJsonOptions);
    }

    public async Task<CycleChallengeMergeResult> MergeCloudChallengeJsonAsync(
        string cloudJson,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseCloudChallengeDocument(cloudJson, out var doc, out var parseErrorCode))
        {
            return new CycleChallengeMergeResult
            {
                Ok = false,
                ErrorCode = parseErrorCode ?? "INVALID_CHALLENGE_SCHEMA"
            };
        }

        var result = await Task.Run(() =>
        {
            var localRecords = _repository.GetRecords();
            var beforeTotalCount = localRecords.Count;
            var localMap = new Dictionary<(string Uid, int Mode, string ScheduleId), CycleChallengeRecordEntity>(localRecords.Count);
            foreach (var item in localRecords)
            {
                localMap[(item.Uid, item.Mode, item.ScheduleId)] = item;
            }

            // Deduplicate duplicates within the incoming payload using the same monotonic comparator.
            var deduplicatedIncoming = new Dictionary<(string Uid, int Mode, string ScheduleId), CycleChallengeRecordEntity>(doc!.Records.Count);
            foreach (var rec in doc.Records)
            {
                var modeEnum = (ChallengeMode)rec.Mode;
                var ensuredSummary = EnsureSummaryJson(modeEnum, rec.Payload, rec.Summary);
                var candidate = new CycleChallengeRecordEntity
                {
                    Uid = rec.Uid,
                    Mode = rec.Mode,
                    ScheduleId = rec.ScheduleId,
                    AccountId = rec.AccountId,
                    StartUtc = rec.StartUtc,
                    EndUtc = rec.EndUtc,
                    Summary = ensuredSummary,
                    Payload = rec.Payload,
                    FetchedAt = string.IsNullOrWhiteSpace(rec.FetchedAt)
                        ? DateTimeOffset.UtcNow.ToString("O")
                        : rec.FetchedAt,
                    ApiVersion = rec.ApiVersion
                };

                var key = (candidate.Uid, candidate.Mode, candidate.ScheduleId);
                if (!deduplicatedIncoming.TryGetValue(key, out var existingIncoming) ||
                    IsIncomingRecordBetter(modeEnum, candidate, existingIncoming))
                {
                    deduplicatedIncoming[key] = candidate;
                }
            }

            var toUpsert = new List<CycleChallengeRecordEntity>();
            var addedCount = 0;
            var upgradedCount = 0;
            var keptLocalCount = 0;
            var addedAbyss = 0;
            var addedTheater = 0;
            var addedStygian = 0;
            var upgradedAbyss = 0;
            var upgradedTheater = 0;
            var upgradedStygian = 0;

            foreach (var pair in deduplicatedIncoming)
            {
                var incoming = pair.Value;
                var modeEnum = (ChallengeMode)incoming.Mode;
                if (!localMap.TryGetValue(pair.Key, out var existing))
                {
                    toUpsert.Add(incoming);
                    addedCount++;
                    switch (modeEnum)
                    {
                        case ChallengeMode.SpiralAbyss: addedAbyss++; break;
                        case ChallengeMode.Theater: addedTheater++; break;
                        case ChallengeMode.Stygian: addedStygian++; break;
                    }
                }
                else if (IsIncomingRecordBetter(modeEnum, incoming, existing))
                {
                    incoming.AccountId ??= existing.AccountId;
                    incoming.StartUtc ??= existing.StartUtc;
                    incoming.EndUtc ??= existing.EndUtc;
                    incoming.ApiVersion ??= existing.ApiVersion;
                    toUpsert.Add(incoming);
                    upgradedCount++;
                    switch (modeEnum)
                    {
                        case ChallengeMode.SpiralAbyss: upgradedAbyss++; break;
                        case ChallengeMode.Theater: upgradedTheater++; break;
                        case ChallengeMode.Stygian: upgradedStygian++; break;
                    }
                }
                else
                {
                    keptLocalCount++;
                }
            }

            if (toUpsert.Count > 0)
            {
                _repository.UpsertRange(toUpsert);
            }

            return new CycleChallengeMergeResult
            {
                Ok = true,
                IncomingCount = deduplicatedIncoming.Count,
                AddedCount = addedCount,
                UpgradedCount = upgradedCount,
                KeptLocalCount = keptLocalCount,
                BeforeTotalCount = beforeTotalCount,
                AfterTotalCount = beforeTotalCount + addedCount,
                AddedAbyssCount = addedAbyss,
                AddedTheaterCount = addedTheater,
                AddedStygianCount = addedStygian,
                UpgradedAbyssCount = upgradedAbyss,
                UpgradedTheaterCount = upgradedTheater,
                UpgradedStygianCount = upgradedStygian
            };
        }, cancellationToken).ConfigureAwait(false);

        if (result.Ok && (result.AddedCount > 0 || result.UpgradedCount > 0))
        {
            ChallengesChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    public static string FormatMergeSummary(string slot, CycleChallengeMergeResult merge)
    {
        return string.Format(
            "CloudSync_CycleChallengeMergeSummaryFormat".GetLocalized(),
            slot,
            merge.AddedCount,
            merge.UpgradedCount,
            merge.KeptLocalCount,
            merge.AfterTotalCount);
    }

    public static bool IsIncomingRecordBetter(
        ChallengeMode mode,
        CycleChallengeRecordEntity incoming,
        CycleChallengeRecordEntity existing)
    {
        return mode switch
        {
            ChallengeMode.SpiralAbyss => CompareAbyssProgress(incoming, existing) > 0,
            ChallengeMode.Theater => CompareTheaterProgress(incoming, existing) > 0,
            ChallengeMode.Stygian => CompareStygianProgress(incoming, existing) > 0,
            _ => false
        };
    }

    private static int CompareAbyssProgress(CycleChallengeRecordEntity candidate, CycleChallengeRecordEntity baseline)
    {
        var a = ExtractAbyssProgress(candidate);
        var b = ExtractAbyssProgress(baseline);

        var cmp = a.HasData.CompareTo(b.HasData);
        if (cmp != 0) return cmp;

        if (!a.HasData && !b.HasData)
        {
            cmp = a.IsUnlock.CompareTo(b.IsUnlock);
            if (cmp != 0) return cmp;
        }

        cmp = a.TotalStar.CompareTo(b.TotalStar);
        if (cmp != 0) return cmp;

        cmp = a.MaxFloorNum.CompareTo(b.MaxFloorNum);
        if (cmp != 0) return cmp;

        cmp = a.MaxChamberNum.CompareTo(b.MaxChamberNum);
        if (cmp != 0) return cmp;

        cmp = a.FloorsCount.CompareTo(b.FloorsCount);
        if (cmp != 0) return cmp;

        cmp = a.TotalWinTimes.CompareTo(b.TotalWinTimes);
        if (cmp != 0) return cmp;

        return a.TotalBattleTimes.CompareTo(b.TotalBattleTimes);
    }

    private static int CompareTheaterProgress(CycleChallengeRecordEntity candidate, CycleChallengeRecordEntity baseline)
    {
        var a = ExtractTheaterProgress(candidate);
        var b = ExtractTheaterProgress(baseline);

        var cmp = a.HasData.CompareTo(b.HasData);
        if (cmp != 0) return cmp;

        if (!a.HasData && !b.HasData)
        {
            cmp = a.IsUnlock.CompareTo(b.IsUnlock);
            if (cmp != 0) return cmp;
        }

        cmp = a.DifficultyId.CompareTo(b.DifficultyId);
        if (cmp != 0) return cmp;

        cmp = a.MaxRoundId.CompareTo(b.MaxRoundId);
        if (cmp != 0) return cmp;

        cmp = a.MedalNum.CompareTo(b.MedalNum);
        if (cmp != 0) return cmp;

        cmp = a.TarotFinishedCnt.CompareTo(b.TarotFinishedCnt);
        if (cmp != 0) return cmp;

        cmp = a.Heraldry.CompareTo(b.Heraldry);
        if (cmp != 0) return cmp;

        cmp = a.RoundsCount.CompareTo(b.RoundsCount);
        if (cmp != 0) return cmp;

        return a.HasDetailData.CompareTo(b.HasDetailData);
    }

    private static int CompareStygianProgress(CycleChallengeRecordEntity candidate, CycleChallengeRecordEntity baseline)
    {
        var a = ExtractStygianProgress(candidate);
        var b = ExtractStygianProgress(baseline);

        var aAnyHasData = a.SingleHasData || a.MpHasData;
        var bAnyHasData = b.SingleHasData || b.MpHasData;
        var cmp = aAnyHasData.CompareTo(bAnyHasData);
        if (cmp != 0) return cmp;

        if (!aAnyHasData && !bAnyHasData)
        {
            cmp = a.IsUnlock.CompareTo(b.IsUnlock);
            if (cmp != 0) return cmp;
        }

        var aSingleDiff = a.SingleHasData ? Math.Max(0, a.SingleDifficulty) : 0;
        var bSingleDiff = b.SingleHasData ? Math.Max(0, b.SingleDifficulty) : 0;
        var aMpDiff = a.MpHasData ? Math.Max(0, a.MpDifficulty) : 0;
        var bMpDiff = b.MpHasData ? Math.Max(0, b.MpDifficulty) : 0;

        var aBestOverall = Math.Max(aSingleDiff, aMpDiff);
        var bBestOverall = Math.Max(bSingleDiff, bMpDiff);
        cmp = aBestOverall.CompareTo(bBestOverall);
        if (cmp != 0) return cmp;

        cmp = aSingleDiff.CompareTo(bSingleDiff);
        if (cmp != 0) return cmp;

        if (aSingleDiff > 0)
        {
            cmp = CompareDurationSeconds(a.SingleSecond, b.SingleSecond);
            if (cmp != 0) return cmp;
        }

        cmp = aMpDiff.CompareTo(bMpDiff);
        if (cmp != 0) return cmp;

        if (aMpDiff > 0)
        {
            cmp = CompareDurationSeconds(a.MpSecond, b.MpSecond);
            if (cmp != 0) return cmp;
        }

        var aChallengeTotal = a.SingleChallengeCount + a.MpChallengeCount;
        var bChallengeTotal = b.SingleChallengeCount + b.MpChallengeCount;
        return aChallengeTotal.CompareTo(bChallengeTotal);
    }

    private static int CompareDurationSeconds(int candidateSec, int baselineSec)
    {
        if (candidateSec <= 0 && baselineSec <= 0) return 0;
        if (candidateSec > 0 && baselineSec <= 0) return 1;
        if (candidateSec <= 0 && baselineSec > 0) return -1;
        return baselineSec.CompareTo(candidateSec);
    }

    private static AbyssProgress ExtractAbyssProgress(CycleChallengeRecordEntity entity)
    {
        var totalStar = 0;
        var maxFloorNum = 0;
        var maxChamberNum = 0;
        var floorsCount = 0;
        var totalWinTimes = 0;
        var totalBattleTimes = 0;
        var hasData = false;
        var isUnlock = false;

        if (TryParseJsonObject(entity.Payload, out var payloadDoc, out var root))
        {
            using (payloadDoc)
            {
                totalStar = GetInt(root, "total_star");
                TryParseMaxFloor(GetString(root, "max_floor"), out maxFloorNum, out maxChamberNum);
                totalWinTimes = GetInt(root, "total_win_times");
                totalBattleTimes = GetInt(root, "total_battle_times");
                isUnlock = GetBool(root, "is_unlock");
                if (root.TryGetProperty("floors", out var floors) && floors.ValueKind is JsonValueKind.Array)
                {
                    floorsCount = floors.GetArrayLength();
                }

                hasData = floorsCount > 0 || GetBool(root, "has_data") || totalStar > 0 || maxFloorNum > 0;
            }
        }

        if (TryParseJsonObject(entity.Summary, out var summaryDoc, out var summary))
        {
            using (summaryDoc)
            {
                if (totalStar == 0) totalStar = GetInt(summary, "total_star");
                if (maxFloorNum == 0) TryParseMaxFloor(GetString(summary, "max_floor"), out maxFloorNum, out maxChamberNum);
                if (totalWinTimes == 0) totalWinTimes = GetInt(summary, "total_win_times");
                if (totalBattleTimes == 0) totalBattleTimes = GetInt(summary, "total_battle_times");
                isUnlock |= GetBool(summary, "is_unlock");
                hasData |= GetBool(summary, "has_data") || totalStar > 0 || maxFloorNum > 0;
            }
        }

        return new AbyssProgress(
            hasData,
            isUnlock,
            totalStar,
            maxFloorNum,
            maxChamberNum,
            floorsCount,
            totalWinTimes,
            totalBattleTimes);
    }

    private static TheaterProgress ExtractTheaterProgress(CycleChallengeRecordEntity entity)
    {
        var difficultyId = 0;
        var maxRoundId = 0;
        var medalNum = 0;
        var tarotFinishedCnt = 0;
        var heraldry = 0;
        var roundsCount = 0;
        var hasDetailData = false;
        var hasData = false;
        var isUnlock = false;

        if (TryParseJsonObject(entity.Payload, out var payloadDoc, out var root))
        {
            using (payloadDoc)
            {
                var stat = root.TryGetProperty("stat", out var statEl) && statEl.ValueKind is JsonValueKind.Object
                    ? statEl
                    : default;

                difficultyId = stat.ValueKind is JsonValueKind.Object && stat.TryGetProperty("difficulty_id", out _)
                    ? SafeInt(stat, "difficulty_id")
                    : SafeInt(root, "difficulty_id");
                maxRoundId = stat.ValueKind is JsonValueKind.Object && stat.TryGetProperty("max_round_id", out _)
                    ? SafeInt(stat, "max_round_id")
                    : SafeInt(root, "max_round_id");
                medalNum = stat.ValueKind is JsonValueKind.Object && stat.TryGetProperty("medal_num", out _)
                    ? SafeInt(stat, "medal_num")
                    : SafeInt(root, "medal_num");
                tarotFinishedCnt = stat.ValueKind is JsonValueKind.Object
                    ? SafeInt(stat, "tarot_finished_cnt")
                    : 0;
                heraldry = stat.ValueKind is JsonValueKind.Object && stat.TryGetProperty("heraldry", out _)
                    ? SafeInt(stat, "heraldry")
                    : SafeInt(root, "heraldry");

                if (root.TryGetProperty("detail", out var detail) &&
                    detail.ValueKind is JsonValueKind.Object &&
                    detail.TryGetProperty("rounds_data", out var rounds) &&
                    rounds.ValueKind is JsonValueKind.Array)
                {
                    roundsCount = rounds.GetArrayLength();
                }

                hasDetailData = GetBool(root, "has_detail_data") || roundsCount > 0;
                isUnlock = GetBool(root, "is_unlock");
                hasData = GetBool(root, "has_data") || roundsCount > 0 || maxRoundId > 0 || medalNum > 0;
            }
        }

        if (TryParseJsonObject(entity.Summary, out var summaryDoc, out var summary))
        {
            using (summaryDoc)
            {
                if (difficultyId == 0) difficultyId = SafeInt(summary, "difficulty_id");
                if (maxRoundId == 0) maxRoundId = SafeInt(summary, "max_round_id");
                if (medalNum == 0) medalNum = SafeInt(summary, "medal_num");
                if (heraldry == 0) heraldry = SafeInt(summary, "heraldry");
                isUnlock |= GetBool(summary, "is_unlock");
                hasData |= GetBool(summary, "has_data") || maxRoundId > 0 || medalNum > 0;
            }
        }

        return new TheaterProgress(
            hasData,
            isUnlock,
            difficultyId,
            maxRoundId,
            medalNum,
            tarotFinishedCnt,
            heraldry,
            roundsCount,
            hasDetailData);
    }

    private static StygianProgress ExtractStygianProgress(CycleChallengeRecordEntity entity)
    {
        var singleHasData = false;
        var singleDifficulty = 0;
        var singleSecond = 0;
        var singleChallengeCount = 0;

        var mpHasData = false;
        var mpDifficulty = 0;
        var mpSecond = 0;
        var mpChallengeCount = 0;

        var isUnlock = false;

        if (TryParseJsonObject(entity.Payload, out var payloadDoc, out var root))
        {
            using (payloadDoc)
            {
                isUnlock = GetBool(root, "is_unlock");

                if (root.TryGetProperty("single", out var single) && single.ValueKind is JsonValueKind.Object)
                {
                    ExtractStygianMode(single, out singleHasData, out singleDifficulty, out singleSecond, out singleChallengeCount);
                }

                if (root.TryGetProperty("mp", out var mp) && mp.ValueKind is JsonValueKind.Object)
                {
                    ExtractStygianMode(mp, out mpHasData, out mpDifficulty, out mpSecond, out mpChallengeCount);
                }
            }
        }

        if (TryParseJsonObject(entity.Summary, out var summaryDoc, out var summary))
        {
            using (summaryDoc)
            {
                isUnlock |= GetBool(summary, "is_unlock");
                var summaryDiff = SafeInt(summary, "difficulty");
                var summarySec = SafeInt(summary, "second");
                var summaryHasData = GetBool(summary, "has_data") || summaryDiff > 0;
                if (!singleHasData && summaryHasData)
                {
                    singleHasData = true;
                    singleDifficulty = summaryDiff;
                    singleSecond = summarySec;
                }
            }
        }

        return new StygianProgress(
            singleHasData,
            singleDifficulty,
            singleSecond,
            singleChallengeCount,
            mpHasData,
            mpDifficulty,
            mpSecond,
            mpChallengeCount,
            isUnlock);
    }

    private static void ExtractStygianMode(
        JsonElement modeObj,
        out bool hasData,
        out int difficulty,
        out int second,
        out int challengeCount)
    {
        difficulty = 0;
        second = 0;
        challengeCount = 0;

        if (modeObj.TryGetProperty("best", out var best) && best.ValueKind is JsonValueKind.Object)
        {
            difficulty = SafeInt(best, "difficulty");
            second = SafeInt(best, "second");
        }

        if (modeObj.TryGetProperty("challenge", out var challenges) && challenges.ValueKind is JsonValueKind.Array)
        {
            challengeCount = challenges.GetArrayLength();
        }

        hasData = GetBool(modeObj, "has_data") || difficulty > 0 || challengeCount > 0;
    }

    private static string EnsureSummaryJson(ChallengeMode mode, string payloadJson, string? existingSummary)
    {
        var normalized = NormalizeOptionalSummaryJson(existingSummary);
        if (!string.IsNullOrEmpty(normalized))
        {
            return normalized;
        }

        if (!TryParseJsonObject(payloadJson, out var doc, out var element))
        {
            return "{}";
        }

        using (doc)
        {
            JsonObject summary;
            switch (mode)
            {
                case ChallengeMode.SpiralAbyss:
                    summary = new JsonObject
                    {
                        ["total_star"] = GetInt(element, "total_star"),
                        ["max_floor"] = GetString(element, "max_floor"),
                        ["total_battle_times"] = GetInt(element, "total_battle_times"),
                        ["total_win_times"] = GetInt(element, "total_win_times"),
                        ["is_unlock"] = GetBool(element, "is_unlock"),
                        ["has_data"] = element.TryGetProperty("floors", out var floors) &&
                                       floors.ValueKind is JsonValueKind.Array &&
                                       floors.GetArrayLength() > 0,
                    };
                    break;

                case ChallengeMode.Theater:
                {
                    var stat = element.TryGetProperty("stat", out var statEl) && statEl.ValueKind is JsonValueKind.Object
                        ? statEl
                        : default;
                    summary = new JsonObject
                    {
                        ["difficulty_id"] = SafeInt(stat, "difficulty_id"),
                        ["max_round_id"] = SafeInt(stat, "max_round_id"),
                        ["heraldry"] = SafeInt(stat, "heraldry"),
                        ["medal_num"] = SafeInt(stat, "medal_num"),
                        ["has_data"] = GetBool(element, "has_data"),
                        ["is_unlock"] = GetBool(element, "is_unlock"),
                    };
                    break;
                }

                default:
                {
                    var scheduleEl = element.TryGetProperty("schedule", out var schedule) &&
                                     schedule.ValueKind is JsonValueKind.Object
                        ? schedule
                        : element;
                    var single = element.TryGetProperty("single", out var singleEl) &&
                                 singleEl.ValueKind is JsonValueKind.Object
                        ? singleEl
                        : default;
                    var best = single.ValueKind is JsonValueKind.Object &&
                               single.TryGetProperty("best", out var bestEl) &&
                               bestEl.ValueKind is JsonValueKind.Object
                        ? bestEl
                        : default;
                    summary = new JsonObject
                    {
                        ["difficulty"] = SafeInt(best, "difficulty"),
                        ["second"] = SafeInt(best, "second"),
                        ["has_data"] = single.ValueKind is JsonValueKind.Object && GetBool(single, "has_data"),
                        ["name"] = GetString(scheduleEl, "name"),
                        ["is_unlock"] = GetBool(element, "is_unlock"),
                    };
                    break;
                }
            }

            return summary.ToJsonString(CloudJsonOptions);
        }
    }

    private static bool TryParseCloudChallengeDocument(
        string rawJson,
        out CycleChallengeCloudDocument? doc,
        out string? errorCode)
    {
        doc = null;
        errorCode = null;
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            errorCode = "INVALID_CHALLENGE_SCHEMA";
            return false;
        }

        try
        {
            using var parsed = JsonDocument.Parse(rawJson);
            var root = parsed.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                errorCode = "INVALID_CHALLENGE_SCHEMA";
                return false;
            }

            if (!root.TryGetProperty("info", out var info) || info.ValueKind is not JsonValueKind.Object)
            {
                errorCode = "INVALID_CHALLENGE_SCHEMA";
                return false;
            }

            if (!info.TryGetProperty("challenge_version", out var verEl) ||
                verEl.ValueKind is not JsonValueKind.String ||
                !string.Equals(verEl.GetString(), "v1.0", StringComparison.Ordinal))
            {
                errorCode = "UNSUPPORTED_CHALLENGE_VERSION";
                return false;
            }

            if (!info.TryGetProperty("export_app", out var appEl) ||
                appEl.ValueKind is not JsonValueKind.String ||
                string.IsNullOrWhiteSpace(appEl.GetString()))
            {
                errorCode = "INVALID_CHALLENGE_SCHEMA";
                return false;
            }

            if (!root.TryGetProperty("records", out var recordsEl) ||
                recordsEl.ValueKind is not JsonValueKind.Array ||
                recordsEl.GetArrayLength() > MaxCloudRecords)
            {
                errorCode = "INVALID_CHALLENGE_SCHEMA";
                return false;
            }

            var records = new List<CycleChallengeCloudRecord>(recordsEl.GetArrayLength());
            foreach (var item in recordsEl.EnumerateArray())
            {
                if (item.ValueKind is not JsonValueKind.Object)
                {
                    errorCode = "INVALID_CHALLENGE_SCHEMA";
                    return false;
                }

                if (!TryReadRequiredBoundedString(item, "uid", MaxIdentifierLength, out var uid) ||
                    !TryReadRequiredBoundedString(item, "schedule_id", MaxIdentifierLength, out var scheduleId) ||
                    !TryReadMode(item, out var mode))
                {
                    errorCode = "INVALID_CHALLENGE_SCHEMA";
                    return false;
                }

                if (!item.TryGetProperty("payload", out var payloadEl) ||
                    payloadEl.ValueKind is not JsonValueKind.String)
                {
                    errorCode = "INVALID_CHALLENGE_SCHEMA";
                    return false;
                }

                var payload = payloadEl.GetString();
                if (string.IsNullOrWhiteSpace(payload) ||
                    payload.Length > MaxPayloadUtf16Length ||
                    !IsValidPayloadJson(payload))
                {
                    errorCode = "INVALID_CHALLENGE_SCHEMA";
                    return false;
                }

                var summary = TryReadNullableString(item, "summary", MaxSummaryUtf16Length, out var rawSummary);
                if (!summary ||
                    (rawSummary is { Length: > 0 } && !IsValidSummaryObjectJson(rawSummary)))
                {
                    errorCode = "INVALID_CHALLENGE_SCHEMA";
                    return false;
                }

                if (!TryReadNullableString(item, "account_id", MaxIdentifierLength, out var accountId) ||
                    !TryReadNullableString(item, "start_utc", MaxIdentifierLength, out var startUtc) ||
                    !TryReadNullableString(item, "end_utc", MaxIdentifierLength, out var endUtc) ||
                    !TryReadNullableString(item, "fetched_at", MaxIdentifierLength, out var fetchedAt) ||
                    !TryReadNullableString(item, "api_version", MaxIdentifierLength, out var apiVersion))
                {
                    errorCode = "INVALID_CHALLENGE_SCHEMA";
                    return false;
                }

                records.Add(new CycleChallengeCloudRecord
                {
                    Uid = uid,
                    Mode = mode,
                    ScheduleId = scheduleId,
                    AccountId = accountId,
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    Summary = string.IsNullOrEmpty(rawSummary) ? null : rawSummary,
                    Payload = payload,
                    FetchedAt = fetchedAt,
                    ApiVersion = apiVersion
                });
            }

            var exportTimestamp = 0L;
            if (info.TryGetProperty("export_timestamp", out var tsEl) &&
                tsEl.ValueKind is JsonValueKind.Number &&
                tsEl.TryGetInt64(out var parsedTs) &&
                parsedTs >= 0)
            {
                exportTimestamp = parsedTs;
            }

            doc = new CycleChallengeCloudDocument
            {
                Info = new CycleChallengeCloudInfo
                {
                    ExportApp = ClampOptionalString(appEl.GetString(), 128) ?? "FufuLauncher",
                    ExportAppVersion = info.TryGetProperty("export_app_version", out var appVerEl) &&
                                       appVerEl.ValueKind is JsonValueKind.String
                        ? ClampOptionalString(appVerEl.GetString(), MaxIdentifierLength)
                        : null,
                    ChallengeVersion = "v1.0",
                    ExportTimestamp = exportTimestamp
                },
                Records = records
            };

            return true;
        }
        catch (JsonException)
        {
            errorCode = "INVALID_CHALLENGE_SCHEMA";
            return false;
        }
    }

    private static bool TryReadRequiredBoundedString(
        JsonElement obj,
        string propertyName,
        int maxLen,
        out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        var str = el.GetString()?.Trim();
        if (string.IsNullOrEmpty(str) || str.Length > maxLen)
        {
            return false;
        }

        value = str;
        return true;
    }

    private static bool TryReadMode(JsonElement obj, out int mode)
    {
        mode = -1;
        if (!obj.TryGetProperty("mode", out var el) || el.ValueKind is not JsonValueKind.Number || !el.TryGetInt32(out var val))
        {
            return false;
        }

        if (val < (int)ChallengeMode.SpiralAbyss || val > (int)ChallengeMode.Stygian)
        {
            return false;
        }

        mode = val;
        return true;
    }

    private static bool TryReadNullableString(
        JsonElement obj,
        string propertyName,
        int maxLen,
        out string? value)
    {
        value = null;
        if (!obj.TryGetProperty(propertyName, out var el) || el.ValueKind is JsonValueKind.Null)
        {
            return true;
        }

        if (el.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        var str = el.GetString();
        if (str is not null && str.Length > maxLen)
        {
            return false;
        }

        value = str;
        return true;
    }

    private static string? ClampOptionalString(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
    }

    private static bool IsValidPayloadJson(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsValidSummaryObjectJson(string summary)
    {
        try
        {
            using var doc = JsonDocument.Parse(summary);
            return doc.RootElement.ValueKind is JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? NormalizeOptionalSummaryJson(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var trimmed = summary.Trim();
        if (trimmed.Length > MaxSummaryUtf16Length)
        {
            return null;
        }

        return IsValidSummaryObjectJson(trimmed) ? trimmed : null;
    }

    private static bool TryParseJsonObject(
        string? json,
        out JsonDocument doc,
        out JsonElement root)
    {
        doc = null!;
        root = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind is not JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }

            doc = parsed;
            root = parsed.RootElement;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void TryParseMaxFloor(string? maxFloor, out int floorNum, out int chamberNum)
    {
        floorNum = 0;
        chamberNum = 0;
        if (string.IsNullOrWhiteSpace(maxFloor))
        {
            return;
        }

        var parts = maxFloor.Trim().Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length >= 1 && int.TryParse(parts[0], out var f) && f > 0)
        {
            floorNum = f;
        }

        if (parts.Length >= 2 && int.TryParse(parts[1], out var c) && c >= 0)
        {
            chamberNum = c;
        }
    }

    private static long ParseScheduleSortKey(string scheduleId) =>
        long.TryParse(scheduleId, out var val) ? val : long.MinValue;

    private readonly record struct AbyssProgress(
        bool HasData,
        bool IsUnlock,
        int TotalStar,
        int MaxFloorNum,
        int MaxChamberNum,
        int FloorsCount,
        int TotalWinTimes,
        int TotalBattleTimes);

    private readonly record struct TheaterProgress(
        bool HasData,
        bool IsUnlock,
        int DifficultyId,
        int MaxRoundId,
        int MedalNum,
        int TarotFinishedCnt,
        int Heraldry,
        int RoundsCount,
        bool HasDetailData);

    private readonly record struct StygianProgress(
        bool SingleHasData,
        int SingleDifficulty,
        int SingleSecond,
        int SingleChallengeCount,
        bool MpHasData,
        int MpDifficulty,
        int MpSecond,
        int MpChallengeCount,
        bool IsUnlock);
}
