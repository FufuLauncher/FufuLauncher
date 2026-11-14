using System.Text.Encodings.Web;
using System.Text.Json;
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
}
