using System.Text.Json;
using FufuLauncher.Data.Entities;
using FufuLauncher.Models.CycleChallenge;

namespace FufuLauncher.Services.CycleChallenge;

public sealed partial class CycleChallengeService
{
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
                if (maxFloorNum == 0)
                    TryParseMaxFloor(GetString(summary, "max_floor"), out maxFloorNum, out maxChamberNum);
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
                    ExtractStygianMode(single, out singleHasData, out singleDifficulty, out singleSecond,
                        out singleChallengeCount);
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