using System.Text.Json;
using System.Text.Json.Nodes;
using FufuLauncher.Models;
using FufuLauncher.Models.CycleChallenge;

namespace FufuLauncher.Services.CycleChallenge;

public sealed partial class CycleChallengeService
{
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
                    var stat = element.TryGetProperty("stat", out var statEl) &&
                               statEl.ValueKind is JsonValueKind.Object
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
        if (!obj.TryGetProperty("mode", out var el) || el.ValueKind is not JsonValueKind.Number ||
            !el.TryGetInt32(out var val))
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
}