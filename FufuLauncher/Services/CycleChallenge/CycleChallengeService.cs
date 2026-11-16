/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FufuLauncher.Data.Entities;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;
using FufuLauncher.Models.CycleChallenge;
using FufuLauncher.Services;

namespace FufuLauncher.Services.CycleChallenge;

public sealed partial class CycleChallengeService
{
    private const string ApiVersion = "cn-2.112.0/os-2.54.0";

    private readonly GameRecordApiClient _api;
    private readonly CycleChallengeRepository _repository;

    public CycleChallengeService(GameRecordApiClient api, CycleChallengeRepository repository)
    {
        _api = api;
        _repository = repository;
    }


    public ChallengeLoadResult LoadFromCache(ChallengeMode mode, SelectedGameRole role, string? scheduleId)
    {
        var periods = _repository.GetPeriods(role.Role.game_uid, mode);
        var record = scheduleId is null
            ? PickPreferredRecord(role.Role.game_uid, mode, periods)
            : _repository.Get(role.Role.game_uid, mode, scheduleId);

        if (record is null)
        {
            return new ChallengeLoadResult
            {
                Success = false,
                Kind = periods.Count == 0 ? ChallengeErrorKind.NotParticipated : ChallengeErrorKind.ServerError,
                Message = periods.Count == 0
                    ? "CycleChallenge_NotParticipated".GetLocalized()
                    : "CycleChallenge_NoCachedPeriod".GetLocalized(),
                Periods = periods,
            };
        }

        return new ChallengeLoadResult
        {
            Success = true,
            Kind = ChallengeErrorKind.Success,
            Periods = periods,
            Payload = WrapForPanel(mode, record),
            ActiveScheduleId = record.ScheduleId,
            IsFromCache = true,
            FetchedAt = DateTimeOffset.TryParse(record.FetchedAt, out var fetched) ? fetched : null,
        };
    }


    public async Task<ChallengeLoadResult> RefreshAsync(
        ChallengeMode mode, SelectedGameRole role, CancellationToken token)
    {
        var periodsBefore = _repository.GetPeriods(role.Role.game_uid, mode);
        var timeContext = CycleChallengeTime.CreateContext(role.Role.region);
        var failures = new List<RawChallengeResponse>();
        var saveFailed = false;
        RawChallengeResponse? preferredAbyssResponse = null;
        string? freshPanelPayload = null;
        string? freshScheduleId = null;

        switch (mode)
        {
            case ChallengeMode.SpiralAbyss:
                for (var schedule = 1; schedule <= 2; schedule++)
                {
                    var response = await _api.GetSpiralAbyssAsync(role, schedule, token).ConfigureAwait(false);
                    if (response.Success)
                    {
                        if (preferredAbyssResponse is null
                            || (!HasAbyssData(preferredAbyssResponse.DataJson)
                                && HasAbyssData(response.DataJson)))
                        {
                            preferredAbyssResponse = response;
                        }

                        try
                        {
                            SaveSinglePeriod(mode, role, response.DataJson!, token);
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Info("CycleChallengeService",
                                $"[CycleChallengeService] Failed to write Abyss cache: {ex.Message}");
                            saveFailed = true;
                            freshPanelPayload ??= BuildFreshPanelPayload(mode, response.DataJson, isUnlock: true);
                            freshScheduleId ??= TryGetFreshScheduleId(mode, response.DataJson);
                        }
                    }
                    else if (response.Kind is not ChallengeErrorKind.NotParticipated)
                    {
                        failures.Add(response);
                    }
                }

                break;

            case ChallengeMode.Theater:
            {
                var response = await _api.GetRoleCombatAsync(role, token).ConfigureAwait(false);
                if (response.Success)
                {
                    var isUnlock = ReadIsUnlock(response.RawJson);
                    try
                    {
                        SavePeriodArray(mode, role, response.DataJson!, isUnlock, timeContext);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("CycleChallengeService",
                            $"[CycleChallengeService] Failed to write Theater cache: {ex.Message}");
                        saveFailed = true;
                        freshPanelPayload ??= BuildFreshPanelPayload(mode, response.DataJson, isUnlock);
                        freshScheduleId ??= TryGetFreshScheduleId(mode, response.DataJson);
                    }
                }
                else
                {
                    failures.Add(response);
                }

                break;
            }

            case ChallengeMode.Stygian:
            {
                var response = await _api.GetHardChallengeAsync(role, token).ConfigureAwait(false);
                if (response.Success)
                {
                    var isUnlock = ReadIsUnlock(response.RawJson);
                    try
                    {
                        SavePeriodArray(mode, role, response.DataJson!, isUnlock, timeContext);
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("CycleChallengeService",
                            $"[CycleChallengeService] Failed to write Stygian Onslaught cache: {ex.Message}");
                        saveFailed = true;
                        freshPanelPayload ??= BuildFreshPanelPayload(mode, response.DataJson, isUnlock);
                        freshScheduleId ??= TryGetFreshScheduleId(mode, response.DataJson);
                    }
                }
                else
                {
                    failures.Add(response);
                }

                break;
            }
        }


        if (mode == ChallengeMode.SpiralAbyss
            && saveFailed
            && preferredAbyssResponse is not null)
        {
            freshPanelPayload = BuildFreshPanelPayload(
                mode, preferredAbyssResponse.DataJson, isUnlock: true);
            freshScheduleId = TryGetFreshScheduleId(mode, preferredAbyssResponse.DataJson);
        }

        IReadOnlyList<ChallengePeriod> periods = periodsBefore;
        CycleChallengeRecordEntity? latest = null;
        try
        {
            periods = _repository.GetPeriods(role.Role.game_uid, mode);
            latest = PickPreferredRecord(role.Role.game_uid, mode, periods);
        }
        catch (Exception ex) when (saveFailed && freshPanelPayload is not null)
        {
            DebugLog.Info("CycleChallengeService", $"[CycleChallengeService] Failed to read cache: {ex.Message}");
            periods = periodsBefore;
            latest = null;
        }


        if (saveFailed && freshPanelPayload is not null)
        {
            string activeScheduleId = freshScheduleId ?? latest?.ScheduleId ?? string.Empty;
            periods = EnsureFreshPeriodPresent(periods, role, mode, activeScheduleId);

            return new ChallengeLoadResult
            {
                Success = true,
                Kind = ChallengeErrorKind.Success,
                Periods = periods,
                Payload = freshPanelPayload,
                ActiveScheduleId = activeScheduleId,
                IsFromCache = false,
            };
        }

        if (latest is null)
        {
            var firstFailure = failures.FirstOrDefault();
            return new ChallengeLoadResult
            {
                Success = false,
                Kind = firstFailure?.Kind ?? ChallengeErrorKind.NotParticipated,
                Message = firstFailure?.Message,
                Periods = periods,
            };
        }


        var flagCache = failures.Count > 0 && periodsBefore.Count > 0;
        return new ChallengeLoadResult
        {
            Success = true,
            Kind = flagCache ? failures[0].Kind : ChallengeErrorKind.Success,
            Message = flagCache ? failures[0].Message : null,
            Periods = periods,
            Payload = WrapForPanel(mode, latest),
            ActiveScheduleId = latest.ScheduleId,
            IsFromCache = flagCache,
            FetchedAt = DateTimeOffset.TryParse(latest.FetchedAt, out var fetched) ? fetched : null,
        };
    }

    private void SaveSinglePeriod(ChallengeMode mode, SelectedGameRole role, string dataJson, CancellationToken token)
    {
        using var document = JsonDocument.Parse(dataJson);
        var element = document.RootElement.Clone();
        if (element.ValueKind is not JsonValueKind.Object)
        {
            return;
        }

        var scheduleId = GetString(element, "schedule_id");
        if (string.IsNullOrEmpty(scheduleId))
        {
            return;
        }

        var start = NormalizeTime(element, "start_time");
        var end = NormalizeTime(element, "end_time");
        var summary = new JsonObject
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

        Upsert(mode, role, scheduleId, start, end, summary.ToJsonString(), dataJson);
    }

    private void SavePeriodArray(
        ChallengeMode mode,
        SelectedGameRole role,
        string dataJson,
        bool isUnlock,
        CycleChallengeTimeContext timeContext)
    {
        using var document = JsonDocument.Parse(dataJson);
        var root = document.RootElement;
        JsonElement array;
        bool unlock = isUnlock;

        if (root.ValueKind is JsonValueKind.Array)
        {
            array = root;
        }
        else if (root.ValueKind is JsonValueKind.Object &&
                 root.TryGetProperty("data", out var inner) &&
                 inner.ValueKind is JsonValueKind.Array)
        {
            array = inner;
            if (root.TryGetProperty("is_unlock", out _))
            {
                unlock = GetBool(root, "is_unlock");
            }
        }
        else
        {
            return;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind is not JsonValueKind.Object)
            {
                continue;
            }

            var scheduleElement = element.TryGetProperty("schedule", out var schedule) &&
                                  schedule.ValueKind is JsonValueKind.Object
                ? schedule
                : element;

            var scheduleId = GetString(scheduleElement, "schedule_id");
            if (string.IsNullOrEmpty(scheduleId))
            {
                continue;
            }

            var start = NormalizeScheduleTime(scheduleElement, "start_time", "start_date_time", timeContext);
            var end = NormalizeScheduleTime(scheduleElement, "end_time", "end_date_time", timeContext);

            JsonObject summary;
            if (mode == ChallengeMode.Theater)
            {
                var stat = element.TryGetProperty("stat", out var statElement) &&
                           statElement.ValueKind is JsonValueKind.Object
                    ? statElement
                    : default;
                summary = new JsonObject
                {
                    ["difficulty_id"] = SafeInt(stat, "difficulty_id"),
                    ["max_round_id"] = SafeInt(stat, "max_round_id"),
                    ["heraldry"] = SafeInt(stat, "heraldry"),
                    ["medal_num"] = SafeInt(stat, "medal_num"),
                    ["has_data"] = GetBool(element, "has_data"),
                    ["is_unlock"] = unlock,
                };
            }
            else
            {
                var single = element.TryGetProperty("single", out var singleElement) &&
                             singleElement.ValueKind is JsonValueKind.Object
                    ? singleElement
                    : default;
                var best = single.ValueKind is JsonValueKind.Object &&
                           single.TryGetProperty("best", out var bestElement) &&
                           bestElement.ValueKind is JsonValueKind.Object
                    ? bestElement
                    : default;
                summary = new JsonObject
                {
                    ["difficulty"] = SafeInt(best, "difficulty"),
                    ["second"] = SafeInt(best, "second"),
                    ["has_data"] = single.ValueKind is JsonValueKind.Object && GetBool(single, "has_data"),
                    ["name"] = GetString(scheduleElement, "name"),
                    ["is_unlock"] = unlock,
                };
            }

            Upsert(mode, role, scheduleId, start, end, summary.ToJsonString(), element.GetRawText());
        }
    }

    private void Upsert(
        ChallengeMode mode,
        SelectedGameRole role,
        string scheduleId,
        string? start,
        string? end,
        string summary,
        string payload)
    {
        _repository.Upsert(new CycleChallengeRecordEntity
        {
            Uid = role.Role.game_uid,
            Mode = (int)mode,
            ScheduleId = scheduleId,
            AccountId = role.AccountId,
            StartUtc = start,
            EndUtc = end,
            Summary = summary,
            Payload = payload,
            FetchedAt = DateTimeOffset.UtcNow.ToString("O"),
            ApiVersion = ApiVersion,
        });
    }


    private static string? BuildFreshPanelPayload(ChallengeMode mode, string? dataJson, bool isUnlock)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        if (mode == ChallengeMode.SpiralAbyss)
        {
            return dataJson;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            JsonElement array;
            if (root.ValueKind is JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind is JsonValueKind.Object
                     && root.TryGetProperty("data", out var inner)
                     && inner.ValueKind is JsonValueKind.Array)
            {
                array = inner;
            }
            else
            {
                return null;
            }

            var unlock = isUnlock ? "true" : "false";
            return $"{{\"retcode\":0,\"message\":\"OK\",\"is_unlock\":{unlock},\"data\":{array.GetRawText()}}}";
        }
        catch (JsonException ex)
        {
            DebugLog.Info("CycleChallengeService",
                $"[CycleChallengeService] Failed to build temp payload: {ex.Message}");
            return null;
        }
    }


    private static bool HasAbyssData(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            return root.ValueKind is JsonValueKind.Object
                   && root.TryGetProperty("floors", out var floors)
                   && floors.ValueKind is JsonValueKind.Array
                   && floors.GetArrayLength() > 0;
        }
        catch (JsonException ex)
        {
            DebugLog.Info("CycleChallengeService",
                $"[CycleChallengeService] Failed to determine Abyss floors: {ex.Message}");
            return false;
        }
    }


    private static string? TryGetFreshScheduleId(ChallengeMode mode, string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            var root = document.RootElement;
            if (root.ValueKind is JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                {
                    return null;
                }

                root = root[0];
            }

            if (root.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            if (mode == ChallengeMode.SpiralAbyss)
            {
                return NullIfBlank(GetString(root, "schedule_id"));
            }


            if (root.TryGetProperty("data", out var data) && data.ValueKind is JsonValueKind.Array)
            {
                if (data.GetArrayLength() == 0)
                {
                    return null;
                }

                root = data[0];
            }

            if (root.ValueKind is not JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty("schedule", out var schedule) && schedule.ValueKind is JsonValueKind.Object)
            {
                var id = NullIfBlank(GetString(schedule, "schedule_id"));
                if (id is not null)
                {
                    return id;
                }
            }

            return NullIfBlank(GetString(root, "schedule_id"));
        }
        catch (JsonException ex)
        {
            DebugLog.Info("CycleChallengeService",
                $"[CycleChallengeService] Failed to parse fresh period IDs: {ex.Message}");
            return null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;


    private static IReadOnlyList<ChallengePeriod> EnsureFreshPeriodPresent(
        IReadOnlyList<ChallengePeriod> periods, SelectedGameRole role, ChallengeMode mode, string scheduleId)
    {
        if (string.IsNullOrWhiteSpace(scheduleId)
            || periods.Any(p => string.Equals(p.ScheduleId, scheduleId, StringComparison.Ordinal)))
        {
            return periods;
        }

        var list = new List<ChallengePeriod>(periods.Count + 1)
        {
            new(role.Role.game_uid, mode, scheduleId, null, null, null, DateTimeOffset.UtcNow),
        };
        list.AddRange(periods);
        return list;
    }


    private static string WrapForPanel(ChallengeMode mode, CycleChallengeRecordEntity record)
    {
        if (mode == ChallengeMode.SpiralAbyss)
        {
            return record.Payload;
        }

        var isUnlock = ReadSummaryBool(record.Summary, "is_unlock");
        return
            $"{{\"retcode\":0,\"message\":\"OK\",\"is_unlock\":{isUnlock.ToString().ToLowerInvariant()},\"data\":[{record.Payload}]}}";
    }


    private static bool ReadIsUnlock(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var root = document.RootElement;


            if (root.TryGetProperty("is_unlock", out _))
            {
                return GetBool(root, "is_unlock");
            }

            if (root.TryGetProperty("data", out var data) &&
                data.ValueKind is JsonValueKind.Object &&
                data.TryGetProperty("is_unlock", out _))
            {
                return GetBool(data, "is_unlock");
            }


            return true;
        }
        catch (JsonException)
        {
            return true;
        }
    }


    private CycleChallengeRecordEntity? PickPreferredRecord(
        string uid, ChallengeMode mode, IReadOnlyList<ChallengePeriod> periods)
    {
        if (periods.Count == 0)
        {
            return null;
        }


        foreach (var period in periods)
        {
            if (ReadSummaryBool(period.Summary, "has_data"))
            {
                return _repository.Get(uid, mode, period.ScheduleId);
            }
        }

        return _repository.Get(uid, mode, periods[0].ScheduleId);
    }


    private static bool ReadSummaryBool(string? summaryJson, string property)
    {
        if (string.IsNullOrWhiteSpace(summaryJson))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(summaryJson);
            return !document.RootElement.TryGetProperty(property, out _) ||
                   GetBool(document.RootElement, property);
        }
        catch (JsonException)
        {
            return true;
        }
    }


    private static string? NormalizeTime(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var unix) =>
                CycleChallengeTime.ToUtcStorageString(DateTimeOffset.FromUnixTimeSeconds(unix)),
            JsonValueKind.String => NormalizeTimeString(value.GetString()),
            _ => null,
        };
    }

    private static string? NormalizeScheduleTime(
        JsonElement element, string timeProperty, string dateTimeProperty, CycleChallengeTimeContext context)
    {
        if (element.TryGetProperty(timeProperty, out var timeValue) &&
            timeValue.ValueKind is JsonValueKind.String &&
            long.TryParse(timeValue.GetString(), out var unix) && unix > 0)
        {
            return CycleChallengeTime.ToUtcStorageString(DateTimeOffset.FromUnixTimeSeconds(unix));
        }

        if (element.TryGetProperty(dateTimeProperty, out var dateTime) &&
            dateTime.ValueKind is JsonValueKind.Object)
        {
            var year = GetInt(dateTime, "year");
            var month = GetInt(dateTime, "month");
            var day = GetInt(dateTime, "day");
            var hour = GetInt(dateTime, "hour");
            var minute = GetInt(dateTime, "minute");
            var second = GetInt(dateTime, "second");
            if (year > 0 && month > 0 && day > 0)
            {
                var wallClock = new DateTime(
                    year,
                    Math.Clamp(month, 1, 12),
                    Math.Clamp(day, 1, 31),
                    Math.Clamp(hour, 0, 23),
                    Math.Clamp(minute, 0, 59),
                    Math.Clamp(second, 0, 59),
                    DateTimeKind.Unspecified);
                return CycleChallengeTime.ToUtcStorageString(
                    CycleChallengeTime.WallClockToDisplay(wallClock, context));
            }
        }

        return NormalizeTime(element, timeProperty);
    }

    private static string? NormalizeTimeString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = raw.Trim();
        if (long.TryParse(text, out var unix))
        {
            return CycleChallengeTime.ToUtcStorageString(DateTimeOffset.FromUnixTimeSeconds(unix));
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? CycleChallengeTime.ToUtcStorageString(parsed)
            : null;
    }

    private static string GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText(),
                _ => string.Empty,
            }
            : string.Empty;

    private static int GetInt(JsonElement element, string property) => SafeInt(element, property);

    private static int SafeInt(JsonElement element, string property)
    {
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0,
        };
    }

    private static bool GetBool(JsonElement element, string property)
    {
        if (element.ValueKind is not JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false,
        };
    }
}