using System.Globalization;
using System.Net;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.CycleChallenge.Dto;
using FufuLauncher.Services.CycleChallenge;

namespace FufuLauncher.Services;

public sealed partial class HutaoCloudService
{
    public async Task<HutaoOperationResult<List<FufuCloudChallengeItem>>> InspectFufuCloudChallengeAsync(string slot)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<List<FufuCloudChallengeItem>>();
        if (bridgeCheck != null) return bridgeCheck;

        if (string.IsNullOrWhiteSpace(slot))
        {
            return Fail<List<FufuCloudChallengeItem>>("HutaoSync_ErrSelectFufuArchive".GetLocalized());
        }

        var pullRes = await _cloudSync.PullArchiveAsync("challenge", slot.Trim());
        if (!pullRes.Ok)
        {
            return Fail<List<FufuCloudChallengeItem>>(
                _cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
        }

        CycleChallengeCloudDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<CycleChallengeCloudDocument>(pullRes.JsonContent, JsonOptions);
        }
        catch
        {
            return Fail<List<FufuCloudChallengeItem>>("CloudSync_ErrInvalidFormat".GetLocalized());
        }

        if (doc?.Records == null || doc.Records.Count == 0)
        {
            return Fail<List<FufuCloudChallengeItem>>("HutaoSync_ErrArchiveNoValidRecords".GetLocalized());
        }

        var now = DateTimeOffset.UtcNow;
        var items = new List<FufuCloudChallengeItem>();

        foreach (var rec in doc.Records)
        {
            if (string.IsNullOrWhiteSpace(rec.Uid) || string.IsNullOrWhiteSpace(rec.Payload)) continue;
            if (rec.Mode != 0 && rec.Mode != 1) continue;

            if (rec.Mode == 0)
            {
                SpiralAbyssDto? abyss;
                try
                {
                    abyss = SpiralAbyssPayloadParser.Parse(rec.Payload);
                }
                catch
                {
                    continue;
                }

                if (abyss == null || !abyss.IsUnlock) continue;

                var canUpload = TryBuildSpiralAbyssData(abyss, rec.ScheduleId, out _);
                var isCurrent = IsPeriodCurrent(rec.StartUtc, rec.EndUtc, abyss.StartTime, abyss.EndTime, now);
                var period = BuildPeriodText(rec.StartUtc, rec.EndUtc, abyss.StartTime, abyss.EndTime, rec.ScheduleId);
                var floorText = string.IsNullOrWhiteSpace(abyss.MaxFloor) ? "--" : abyss.MaxFloor;
                var summary = string.Format("HutaoSync_AbyssSummaryFormat".GetLocalized(), floorText, abyss.TotalStar);

                items.Add(new FufuCloudChallengeItem
                {
                    Uid = rec.Uid.Trim(),
                    Mode = 0,
                    ScheduleId = !string.IsNullOrWhiteSpace(abyss.ScheduleId) ? abyss.ScheduleId! : rec.ScheduleId,
                    IsLikelyCurrent = isCurrent,
                    CanUpload = canUpload,
                    ModeLabel = "HutaoSync_ModeAbyss".GetLocalized(),
                    PeriodText = period,
                    SummaryText = summary,
                    StatusBadgeText = !canUpload
                        ? "HutaoSync_ChallengeStatusIncomplete".GetLocalized()
                        : isCurrent
                            ? "HutaoSync_ChallengeStatusCurrent".GetLocalized()
                            : "HutaoSync_ChallengeStatusPast".GetLocalized(),
                    IsSelected = canUpload && isCurrent,
                    Payload = rec.Payload
                });
            }
            else
            {
                RoleCombatEntryDto? entry;
                try
                {
                    var envelope = CycleChallengePayloadReader.Read(rec.Payload);
                    entry = envelope.Entry is JsonElement el
                        ? el.Deserialize<RoleCombatEntryDto>(CycleChallengePayloadReader.SerializerOptions)
                        : null;
                }
                catch
                {
                    continue;
                }

                if (entry == null) continue;

                var canUpload = TryBuildRoleCombatRequest(rec.Uid.Trim(), entry, rec.ScheduleId, out var req);
                var isCurrent = IsPeriodCurrent(
                    rec.StartUtc,
                    rec.EndUtc,
                    entry.Schedule?.StartTime ?? entry.StartTime,
                    entry.Schedule?.EndTime ?? entry.EndTime,
                    now);
                var period = BuildPeriodText(
                    rec.StartUtc,
                    rec.EndUtc,
                    entry.Schedule?.StartTime ?? entry.StartTime,
                    entry.Schedule?.EndTime ?? entry.EndTime,
                    rec.ScheduleId);
                var maxRound = entry.Stat?.MaxRoundId ?? entry.MaxRoundId ?? 0;
                var medalNum = entry.Stat?.MedalNum ?? entry.MedalNum ?? 0;
                var avatarCount = req?.BackupAvatars.Count ?? 0;
                var summary = string.Format("HutaoSync_TheaterSummaryFormat".GetLocalized(), maxRound, medalNum,
                    avatarCount);

                items.Add(new FufuCloudChallengeItem
                {
                    Uid = rec.Uid.Trim(),
                    Mode = 1,
                    ScheduleId = !string.IsNullOrWhiteSpace(entry.Schedule?.ScheduleId)
                        ? entry.Schedule!.ScheduleId!
                        : (!string.IsNullOrWhiteSpace(entry.ScheduleId) ? entry.ScheduleId! : rec.ScheduleId),
                    IsLikelyCurrent = isCurrent,
                    CanUpload = canUpload,
                    ModeLabel = "HutaoSync_ModeTheater".GetLocalized(),
                    PeriodText = period,
                    SummaryText = summary,
                    StatusBadgeText = !canUpload
                        ? "HutaoSync_ChallengeStatusIncomplete".GetLocalized()
                        : isCurrent
                            ? "HutaoSync_ChallengeStatusCurrent".GetLocalized()
                            : "HutaoSync_ChallengeStatusPast".GetLocalized(),
                    IsSelected = canUpload && isCurrent,
                    Payload = rec.Payload
                });
            }
        }

        if (items.Count == 0)
        {
            return Fail<List<FufuCloudChallengeItem>>("HutaoSync_ErrNoSupportedChallengeRecords".GetLocalized());
        }

        var ordered = items
            .OrderByDescending(i => i.IsLikelyCurrent)
            .ThenByDescending(i => long.TryParse(i.ScheduleId, out var sid) ? sid : 0L)
            .ThenBy(i => i.Mode)
            .ThenBy(i => i.Uid, StringComparer.Ordinal)
            .ToList();

        return new HutaoOperationResult<List<FufuCloudChallengeItem>>
        {
            Ok = true,
            Data = ordered
        };
    }

    public async Task<HutaoOperationResult<HutaoChallengeUploadSummary>> PushFufuCloudChallengeToHutaoAsync(
        string slot,
        IReadOnlyList<FufuCloudChallengeItem> selectedItems,
        CancellationToken cancellationToken = default)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<HutaoChallengeUploadSummary>();
        if (bridgeCheck != null) return bridgeCheck;

        if (!IsLoggedIn || !await EnsureAuthenticatedAsync())
        {
            return new HutaoOperationResult<HutaoChallengeUploadSummary>
            {
                Ok = false,
                Message = "HutaoSync_ErrAuthExpired".GetLocalized(),
                AuthInvalidated = true
            };
        }

        var targets = (selectedItems ?? Array.Empty<FufuCloudChallengeItem>())
            .Where(x => x.IsSelected)
            .ToList();

        if (targets.Count == 0)
        {
            return Fail<HutaoChallengeUploadSummary>("HutaoSync_ErrSelectChallengeFirst".GetLocalized());
        }

        if (_currentUserInfo == null)
        {
            await RefreshUserInfoAsync();
        }

        var reservedUserName = !string.IsNullOrWhiteSpace(_currentUserInfo?.NormalizedUserName)
            ? _currentUserInfo!.NormalizedUserName!
            : (!string.IsNullOrWhiteSpace(_currentUserInfo?.UserName) ? _currentUserInfo!.UserName! : _email);

        var uploadedCount = 0;
        var extendedCount = 0;
        var skippedPastCount = 0;
        var skippedIncompleteCount = 0;
        var failedCount = 0;
        var failureDetails = new List<string>();
        var avatarCache =
            new Dictionary<string, (bool Ok, string ErrorMessage, List<HutaoSimpleAvatar> Avatars)>(StringComparer
                .Ordinal);

        foreach (var item in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!item.CanUpload)
            {
                skippedIncompleteCount++;
                continue;
            }

            if (item.Mode == 0)
            {
                var abyss = SpiralAbyssPayloadParser.Parse(item.Payload);
                if (abyss == null || !TryBuildSpiralAbyssData(abyss, item.ScheduleId, out var abyssData) ||
                    abyssData == null)
                {
                    skippedIncompleteCount++;
                    continue;
                }

                if (!avatarCache.TryGetValue(item.Uid, out var avatarResult))
                {
                    avatarResult = await _gameRecordApiClient.GetCharactersForUidAsync(item.Uid, cancellationToken);
                    avatarCache[item.Uid] = avatarResult;
                }

                if (!avatarResult.Ok || !HasRequiredHutaoAvatars(avatarResult.Avatars))
                {
                    failedCount++;
                    var avatarReason = !avatarResult.Ok && !string.IsNullOrWhiteSpace(avatarResult.ErrorMessage)
                        ? string.Format("HutaoSync_ErrChallengeFetchAvatarsFormat".GetLocalized(), item.Uid,
                            avatarResult.ErrorMessage)
                        : string.Format("HutaoSync_ErrChallengeMissingAvatarsFormat".GetLocalized(), item.Uid);
                    if (!failureDetails.Contains(avatarReason, StringComparer.Ordinal))
                    {
                        failureDetails.Add(avatarReason);
                    }

                    continue;
                }

                var req = new HutaoRecordUploadRequest
                {
                    Uid = item.Uid,
                    Identity = "FufuLauncher",
                    SpiralAbyss = abyssData,
                    Avatars = avatarResult.Avatars,
                    ReservedUserName = reservedUserName
                };

                var rawRes = await SendRawRequestWithRetryAsync<JsonElement?>(
                    HttpMethod.Post,
                    $"{ApiEndpoints.HutaoRecordUploadPath}?returningRank=false",
                    req,
                    cancellationToken);

                if (rawRes.RetCode == 0)
                {
                    uploadedCount++;
                    var dataText = rawRes.Data?.ValueKind == JsonValueKind.String
                        ? rawRes.Data.Value.GetString() ?? string.Empty
                        : rawRes.Data?.ToString() ?? string.Empty;
                    if (ContainsExtendedRewardHint(rawRes.Message, rawRes.L10nKey, dataText))
                    {
                        extendedCount++;
                    }
                }
                else if (IsNotCurrentPeriodError(rawRes.Message, rawRes.L10nKey))
                {
                    skippedPastCount++;
                }
                else if (rawRes.RetCode == 514002 || rawRes.HttpStatus == HttpStatusCode.Unauthorized)
                {
                    ClearSession();
                    return new HutaoOperationResult<HutaoChallengeUploadSummary>
                    {
                        Ok = false,
                        Message = "HutaoSync_ErrAuthExpired".GetLocalized(),
                        AuthInvalidated = true
                    };
                }
                else
                {
                    failedCount++;
                    var reason = ExtractChallengeFailureReason(rawRes.RetCode, rawRes.Message, rawRes.L10nKey,
                        rawRes.HttpStatus);
                    if (!string.IsNullOrWhiteSpace(reason) && !failureDetails.Contains(reason, StringComparer.Ordinal))
                    {
                        failureDetails.Add(reason);
                    }
                }
            }
            else if (item.Mode == 1)
            {
                var envelope = CycleChallengePayloadReader.Read(item.Payload);
                var entry = envelope.Entry is JsonElement el
                    ? el.Deserialize<RoleCombatEntryDto>(CycleChallengePayloadReader.SerializerOptions)
                    : null;

                if (entry == null || !TryBuildRoleCombatRequest(item.Uid, entry, item.ScheduleId, out var combatReq) ||
                    combatReq == null)
                {
                    skippedIncompleteCount++;
                    continue;
                }

                var rawRes = await SendRawRequestWithRetryAsync<JsonElement?>(
                    HttpMethod.Post,
                    ApiEndpoints.HutaoRoleCombatUploadPath,
                    combatReq,
                    cancellationToken);

                if (rawRes.RetCode == 0)
                {
                    uploadedCount++;
                }
                else if (IsNotCurrentPeriodError(rawRes.Message, rawRes.L10nKey))
                {
                    skippedPastCount++;
                }
                else if (rawRes.RetCode == 514002 || rawRes.HttpStatus == HttpStatusCode.Unauthorized)
                {
                    ClearSession();
                    return new HutaoOperationResult<HutaoChallengeUploadSummary>
                    {
                        Ok = false,
                        Message = "HutaoSync_ErrAuthExpired".GetLocalized(),
                        AuthInvalidated = true
                    };
                }
                else
                {
                    failedCount++;
                    var reason = ExtractChallengeFailureReason(rawRes.RetCode, rawRes.Message, rawRes.L10nKey,
                        rawRes.HttpStatus);
                    if (!string.IsNullOrWhiteSpace(reason) && !failureDetails.Contains(reason, StringComparer.Ordinal))
                    {
                        failureDetails.Add(reason);
                    }
                }
            }
        }

        if (extendedCount > 0)
        {
            await RefreshUserInfoAsync();
        }

        var parts = new List<string>();
        if (uploadedCount > 0)
        {
            parts.Add(string.Format("HutaoSync_ChallengeUploadedPart".GetLocalized(), uploadedCount));
        }

        if (extendedCount > 0)
        {
            parts.Add("HutaoSync_ChallengeExtendedPart".GetLocalized());
        }

        if (skippedPastCount > 0)
        {
            parts.Add(string.Format("HutaoSync_ChallengeSkippedPastPart".GetLocalized(), skippedPastCount));
        }

        if (skippedIncompleteCount > 0)
        {
            parts.Add(string.Format("HutaoSync_ChallengeSkippedIncompletePart".GetLocalized(), skippedIncompleteCount));
        }

        if (failedCount > 0)
        {
            parts.Add(string.Format("HutaoSync_ChallengeFailedPart".GetLocalized(), failedCount));
        }

        var summaryText = parts.Count > 0
            ? string.Join("；", parts)
            : "HutaoSync_MsgChallengeNothingUploaded".GetLocalized();

        if (failedCount > 0 && failureDetails.Count > 0)
        {
            summaryText = $"{summaryText}（{string.Join("；", failureDetails.Take(2))}）";
        }

        var overallOk = uploadedCount > 0 || (failedCount == 0 && skippedPastCount > 0);
        return new HutaoOperationResult<HutaoChallengeUploadSummary>
        {
            Ok = overallOk,
            Message = summaryText,
            Data = new HutaoChallengeUploadSummary
            {
                UploadedCount = uploadedCount,
                GachaExtendedCount = extendedCount,
                SkippedPastCount = skippedPastCount,
                SkippedIncompleteCount = skippedIncompleteCount,
                FailedCount = failedCount,
                DetailMessage = summaryText
            }
        };
    }

    private static bool TryBuildSpiralAbyssData(SpiralAbyssDto dto, string fallbackScheduleId,
        out HutaoSpiralAbyssData? result)
    {
        result = null;

        var schedStr = !string.IsNullOrWhiteSpace(dto.ScheduleId) ? dto.ScheduleId : fallbackScheduleId;
        if (!int.TryParse(schedStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scheduleId) ||
            scheduleId <= 0)
        {
            return false;
        }

        if (dto.TotalBattleTimes <= 0 || dto.TotalWinTimes <= 0)
        {
            return false;
        }

        var dmg = dto.DamageRank?.FirstOrDefault();
        var defeat = dto.DefeatRank?.FirstOrDefault();
        var energy = dto.EnergySkillRank?.FirstOrDefault();
        var normal = dto.NormalSkillRank?.FirstOrDefault();
        var take = dto.TakeDamageRank?.FirstOrDefault();

        if (dmg == null || defeat == null || energy == null || normal == null || take == null ||
            dmg.AvatarId <= 0 || defeat.AvatarId <= 0 || energy.AvatarId <= 0 || normal.AvatarId <= 0 ||
            take.AvatarId <= 0)
        {
            return false;
        }

        var floors = (dto.Floors ?? new List<SpiralAbyssFloorDto>())
            .Where(f => (f.Levels?.Count ?? 0) > 0)
            .Select(f => new HutaoAbyssFloor
            {
                Index = f.Index,
                Star = f.Star,
                Levels = (f.Levels ?? new List<SpiralAbyssLevelDto>())
                    .Where(l => (l.Battles?.Count ?? 0) > 0)
                    .Select(l => new HutaoAbyssLevel
                    {
                        Index = l.Index,
                        Star = l.Star,
                        Battles = (l.Battles ?? new List<SpiralAbyssBattleDto>())
                            .Where(b => (b.Avatars?.Count ?? 0) > 0)
                            .Select(b => new HutaoAbyssBattle
                            {
                                Index = b.Index,
                                Avatars = b.Avatars!
                                    .Select(a => a.Id)
                                    .Where(id => id > 0)
                                    .ToList()
                            })
                            .Where(b => b.Avatars.Count > 0)
                            .ToList()
                    })
                    .Where(l => l.Battles.Count > 0)
                    .ToList()
            })
            .Where(f => f.Levels.Count > 0)
            .ToList();

        if (floors.Count == 0)
        {
            return false;
        }

        result = new HutaoSpiralAbyssData
        {
            ScheduleId = scheduleId,
            TotalBattleTimes = dto.TotalBattleTimes,
            TotalWinTimes = dto.TotalWinTimes,
            Damage = new HutaoAbyssItemRank { AvatarId = dmg.AvatarId, Value = dmg.Value },
            Defeat = new HutaoAbyssItemRank { AvatarId = defeat.AvatarId, Value = defeat.Value },
            EnergySkill = new HutaoAbyssItemRank { AvatarId = energy.AvatarId, Value = energy.Value },
            NormalSkill = new HutaoAbyssItemRank { AvatarId = normal.AvatarId, Value = normal.Value },
            TakeDamage = new HutaoAbyssItemRank { AvatarId = take.AvatarId, Value = take.Value },
            Floors = floors
        };

        return true;
    }

    private static bool TryBuildRoleCombatRequest(
        string uid,
        RoleCombatEntryDto entry,
        string fallbackScheduleId,
        out HutaoRoleCombatUploadRequest? request)
    {
        request = null;

        var schedStr = !string.IsNullOrWhiteSpace(entry.Schedule?.ScheduleId)
            ? entry.Schedule!.ScheduleId
            : (!string.IsNullOrWhiteSpace(entry.ScheduleId) ? entry.ScheduleId : fallbackScheduleId);

        if (!int.TryParse(schedStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scheduleId) ||
            scheduleId <= 0)
        {
            return false;
        }

        var avatars = (entry.Detail?.BackupAvatars ?? new List<RoleCombatAvatarDto>())
            .Select(a => a.AvatarId ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (avatars.Count == 0)
        {
            return false;
        }

        request = new HutaoRoleCombatUploadRequest
        {
            Version = 1,
            Uid = uid,
            Identity = "FufuLauncher",
            BackupAvatars = avatars,
            ScheduleId = scheduleId
        };

        return true;
    }

    private static bool IsPeriodCurrent(
        string? startUtc,
        string? endUtc,
        string? rawStartUnix,
        string? rawEndUnix,
        DateTimeOffset now)
    {
        var end = ParseTimestampOrIso(endUtc) ?? ParseTimestampOrIso(rawEndUnix);
        var start = ParseTimestampOrIso(startUtc) ?? ParseTimestampOrIso(rawStartUnix);
        if (end.HasValue)
        {
            if (now > end.Value) return false;
            if (start.HasValue && now < start.Value.AddDays(-1)) return false;
            return true;
        }

        return false;
    }

    private static string BuildPeriodText(
        string? startUtc,
        string? endUtc,
        string? rawStartUnix,
        string? rawEndUnix,
        string fallbackSchedule)
    {
        var start = ParseTimestampOrIso(startUtc) ?? ParseTimestampOrIso(rawStartUnix);
        var end = ParseTimestampOrIso(endUtc) ?? ParseTimestampOrIso(rawEndUnix);

        if (start.HasValue && end.HasValue)
        {
            return $"{start.Value.ToLocalTime():yyyy-MM-dd} ~ {end.Value.ToLocalTime():yyyy-MM-dd}";
        }

        return string.IsNullOrWhiteSpace(fallbackSchedule)
            ? "-"
            : string.Format("HutaoSync_ScheduleFallbackFormat".GetLocalized(), fallbackSchedule);
    }

    private static DateTimeOffset? ParseTimestampOrIso(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix) && unix > 0)
        {
            return unix < 10_000_000_000L
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : DateTimeOffset.FromUnixTimeMilliseconds(unix);
        }

        return DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;
    }

    private static bool ContainsExtendedRewardHint(string? message, string? l10nKey, string? dataText)
    {
        var combined = $"{message} {l10nKey} {dataText}";
        return combined.Contains("赠送", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("延长", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("天", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("Extend", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("GachaLog", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNotCurrentPeriodError(string? message, string? l10nKey)
    {
        var combined = $"{message} {l10nKey}";
        return combined.Contains("非当前", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("NotCurrent", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("ScheduleId", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("期数", StringComparison.OrdinalIgnoreCase);
    }
}