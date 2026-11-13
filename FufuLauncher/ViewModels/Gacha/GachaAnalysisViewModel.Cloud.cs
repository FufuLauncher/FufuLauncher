/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.UIGF;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class GachaAnalysisViewModel
{
    private static readonly HashSet<string> CloudSupportedUigfLangs = new(StringComparer.Ordinal)
    {
        "de-de", "en-us", "es-es", "fr-fr", "id-id", "it-it", "ja-jp", "ko-kr",
        "pt-pt", "ru-ru", "th-th", "tr-tr", "vi-vn", "zh-cn", "zh-tw"
    };

    private static readonly HashSet<string> CloudValidHk4eGachaTypes = new(StringComparer.Ordinal)
    {
        "100", "200", "301", "302", "400", "500"
    };

    private static readonly HashSet<string> CloudValidHk4eUigfTypes = new(StringComparer.Ordinal)
    {
        "100", "200", "301", "302", "500"
    };

    [GeneratedRegex("^[0-9]{1,19}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidRecordIdRegex();

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2} \\d{2}:\\d{2}:\\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidRecordTimeRegex();

    public List<string> GetAvailableLocalUids()
    {
        if (!string.IsNullOrEmpty(_currentUid))
        {
            SaveGachaLogsToDb();
        }

        return QueryKnownUidsFromDb()
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public int GetLocalUidRecordCount(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return 0;
        if (string.Equals(uid, _currentUid, StringComparison.Ordinal))
        {
            SaveGachaLogsToDb();
        }

        return _metadataRepo.GetGachaLogsByUid(uid).Count;
    }

    public string BuildStrictUigfV4Json(IReadOnlyList<string> targetUids)
    {
        if (!string.IsNullOrEmpty(_currentUid))
        {
            SaveGachaLogsToDb();
        }

        var uidList = (targetUids ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(100)
            .ToList();

        if (uidList.Count == 0)
        {
            return string.Empty;
        }

        var entries = new List<object>();
        var totalCount = 0;

        foreach (var uid in uidList)
        {
            if (uid.Length > 64) continue;

            var entities = _metadataRepo.GetGachaLogsByUid(uid);
            if (entities.Count == 0) continue;

            var items = entities.Select(e => new GachaLogItem
            {
                Id = e.Id,
                Uid = uid,
                GachaType = e.GachaType,
                ItemId = e.ItemId ?? string.Empty,
                Count = e.Count ?? "1",
                Time = e.Time ?? string.Empty,
                Name = e.Name ?? string.Empty,
                Lang = e.Lang ?? string.Empty,
                ItemType = e.ItemType ?? string.Empty,
                RankType = e.RankType ?? string.Empty
            }).ToList();

            FillMissingFieldsFromMetadata(items);

            var entryLang = items
                .Select(x => (x.Lang ?? string.Empty).Trim().ToLowerInvariant())
                .FirstOrDefault(l => CloudSupportedUigfLangs.Contains(l)) ?? "zh-cn";

            var strictList = new List<object>();
            foreach (var item in items)
            {
                if (totalCount >= 250_000) break;

                var id = (item.Id ?? string.Empty).Trim();
                if (!ValidRecordIdRegex().IsMatch(id)) continue;

                var normalizedTime = NormalizeUigfTime(item.Time);
                if (normalizedTime == null) continue;

                var rawGachaType = (item.GachaType ?? string.Empty).Trim();
                var normalizedGacha = GetNormalizedGachaType(rawGachaType);
                var gachaType = CloudValidHk4eGachaTypes.Contains(rawGachaType)
                    ? rawGachaType
                    : CloudValidHk4eGachaTypes.Contains(normalizedGacha)
                        ? normalizedGacha
                        : null;
                if (gachaType == null) continue;

                var uigfGachaType = GameToUigfGachaType(gachaType);
                if (!CloudValidHk4eUigfTypes.Contains(uigfGachaType))
                {
                    uigfGachaType = gachaType == "400" ? "301" : "200";
                }

                strictList.Add(new
                {
                    uigf_gacha_type = uigfGachaType,
                    gacha_type = gachaType,
                    item_id = TruncateString(item.ItemId, 64),
                    count = string.IsNullOrWhiteSpace(item.Count) ? "1" : TruncateString(item.Count, 16),
                    time = normalizedTime,
                    name = TruncateString(item.Name, 128),
                    item_type = TruncateString(item.ItemType, 64),
                    rank_type = TruncateString(item.RankType, 16),
                    id
                });
                totalCount++;
            }

            if (strictList.Count == 0) continue;

            entries.Add(new
            {
                uid,
                timezone = ResolveDefaultTimezone(uid),
                lang = entryLang,
                list = strictList
            });

            if (totalCount >= 250_000) break;
        }

        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var appVersion = !string.IsNullOrWhiteSpace(AppVersionHelper.NumericVersion)
            ? AppVersionHelper.NumericVersion
            : "1.0.0";
        if (appVersion.Length > 64) appVersion = appVersion[..64];

        var root = new
        {
            info = new
            {
                export_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                export_app = "FufuLauncher",
                export_app_version = appVersion,
                version = "v4.0"
            },
            hk4e = entries
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    public async Task<(bool Success, int ImportedAccounts, int ImportedRecords, string? DisplayUid)>
        ImportUigfJsonFromCloudAsync(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return (false, 0, 0, null);
        }

        UIGFJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<UIGFJson>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("GachaAnalysisViewModel.Cloud", $"[CloudSync] UIGF JSON parse error: {ex.Message}");
            return (false, 0, 0, null);
        }

        if (parsed == null)
        {
            return (false, 0, 0, null);
        }

        var perUidIncoming = new Dictionary<string, List<GachaLogItem>>(StringComparer.Ordinal);

        if (parsed.Hk4e != null && parsed.Hk4e.Count > 0)
        {
            foreach (var entry in parsed.Hk4e)
            {
                var uid = (entry.Uid ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(uid) || entry.List == null || entry.List.Count == 0)
                {
                    continue;
                }

                if (!perUidIncoming.TryGetValue(uid, out var bucket))
                {
                    bucket = new List<GachaLogItem>();
                    perUidIncoming[uid] = bucket;
                }

                var entryLang = string.IsNullOrWhiteSpace(entry.Lang) ? "zh-cn" : entry.Lang;
                foreach (var rec in entry.List)
                {
                    if (string.IsNullOrWhiteSpace(rec.Id)) continue;
                    bucket.Add(ConvertUigfItem(rec, uid, entryLang));
                }
            }
        }
        else if (parsed.List != null && parsed.List.Count > 0)
        {
            var fallbackUid = string.IsNullOrWhiteSpace(parsed.Info?.Uid) ? "unknown" : parsed.Info.Uid.Trim();
            var fallbackLang = string.IsNullOrWhiteSpace(parsed.Info?.Lang) ? "zh-cn" : parsed.Info.Lang;
            var bucket = new List<GachaLogItem>();
            foreach (var rec in parsed.List)
            {
                if (string.IsNullOrWhiteSpace(rec.Id)) continue;
                bucket.Add(ConvertUigfItem(rec, fallbackUid, fallbackLang));
            }

            if (bucket.Count > 0)
            {
                perUidIncoming[fallbackUid] = bucket;
            }
        }

        var validAccounts = perUidIncoming.Where(kv => kv.Value.Count > 0).ToList();
        if (validAccounts.Count == 0)
        {
            return (false, 0, 0, null);
        }

        if (!string.IsNullOrEmpty(_currentUid))
        {
            SaveGachaLogsToDb();
        }

        var totalImportedRecords = 0;
        var importedUids = new List<string>(validAccounts.Count);

        await Task.Run(() =>
        {
            foreach (var (uid, rawIncoming) in validAccounts)
            {
                var incoming = rawIncoming
                    .GroupBy(x => x.Id, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .ToList();

                FillMissingFieldsFromMetadata(incoming);

                var existingEntities = _metadataRepo.GetGachaLogsByUid(uid);
                var existing = existingEntities
                    .Where(e => !string.IsNullOrWhiteSpace(e.Id))
                    .GroupBy(e => e.Id, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .Select(entity => new GachaLogItem
                    {
                        Id = entity.Id,
                        Uid = uid,
                        GachaType = entity.GachaType,
                        ItemId = entity.ItemId ?? string.Empty,
                        Count = entity.Count ?? "1",
                        Time = entity.Time ?? string.Empty,
                        Name = entity.Name ?? string.Empty,
                        Lang = entity.Lang ?? string.Empty,
                        ItemType = entity.ItemType ?? string.Empty,
                        RankType = entity.RankType ?? string.Empty
                    })
                    .ToList();

                var merged = MergeLogs(existing, incoming);
                var entitiesToSave = new List<GachaLogEntity>(merged.Count);
                AddLogItems(entitiesToSave, merged);
                foreach (var entity in entitiesToSave)
                {
                    if (string.IsNullOrWhiteSpace(entity.Uid))
                    {
                        entity.Uid = uid;
                    }
                }

                _metadataRepo.ReplaceGachaLogs(uid, entitiesToSave);
                totalImportedRecords += incoming.Count;
                importedUids.Add(uid);
            }
        });

        var allKnownUids = QueryKnownUidsFromDb();
        var displayUid = !string.IsNullOrWhiteSpace(_currentUid) &&
                         importedUids.Contains(_currentUid, StringComparer.Ordinal)
            ? _currentUid
            : importedUids[0];

        _currentUid = displayUid;
        ++_refreshVersion;
        LoadGachaLogsFromDb(displayUid);
        _ = _localSettingsService.SaveSettingAsync(LastSelectedUidKey, displayUid);

        RefreshKnownUidsUI(allKnownUids);
        SelectedUid = displayUid;

        if (_cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count +
            _cachedNoviceLogs.Count + _cachedStandardLogs.Count > 0)
        {
            RefreshUIFromCache();
            HasGachaData = true;
        }

        return (true, importedUids.Count, totalImportedRecords, displayUid);
    }

    private static GachaLogItem ConvertUigfItem(UIGFItem rec, string uid, string lang)
        => new()
        {
            Id = (rec.Id ?? string.Empty).Trim(),
            Uid = uid,
            GachaType = string.IsNullOrWhiteSpace(rec.GachaType) ? (rec.UigfGachaType ?? "200") : rec.GachaType.Trim(),
            ItemId = rec.ItemId ?? string.Empty,
            Count = string.IsNullOrWhiteSpace(rec.Count) ? "1" : rec.Count,
            Time = rec.Time ?? string.Empty,
            Name = rec.Name ?? string.Empty,
            Lang = lang,
            ItemType = rec.ItemType ?? string.Empty,
            RankType = rec.RankType ?? string.Empty
        };

    private static string? NormalizeUigfTime(string? rawTime)
    {
        if (string.IsNullOrWhiteSpace(rawTime)) return null;
        var trimmed = rawTime.Trim();
        if (ValidRecordTimeRegex().IsMatch(trimmed)) return trimmed;

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt)
            ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : null;
    }

    private static int ResolveDefaultTimezone(string uid)
    {
        if (uid.StartsWith('6')) return -5;
        if (uid.StartsWith('7')) return 1;
        return 8;
    }

    private static string TruncateString(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
    }
}