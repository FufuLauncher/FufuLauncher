using System.Globalization;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.UIGF;

namespace FufuLauncher.Services;

public sealed partial class HutaoCloudService
{
    public async Task<HutaoOperationResult<List<HutaoGachaEntry>>> GetGachaEntriesAsync()
    {
        var res = await SendAuthenticatedAsync<List<HutaoGachaEntry>>(
            HttpMethod.Get,
            ApiEndpoints.HutaoGachaEntriesPath,
            null);

        if (!res.Ok)
        {
            return res;
        }

        return new HutaoOperationResult<List<HutaoGachaEntry>>
        {
            Ok = true,
            Data = res.Data ?? new List<HutaoGachaEntry>()
        };
    }

    public async Task<HutaoOperationResult<int>> DeleteGachaUidAsync(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return Fail<int>("HutaoSync_ErrSelectUidFirst".GetLocalized());
        }

        var path = $"{ApiEndpoints.HutaoGachaDeletePath}?Uid={Uri.EscapeDataString(uid.Trim())}";
        return await SendAuthenticatedAsync<int>(HttpMethod.Get, path, null);
    }

    public async Task<HutaoOperationResult<HutaoBridgeTransferSummary>> PullGachaToFufuCloudAsync(
        IReadOnlyList<string> uids,
        string targetSlot,
        bool mergeExistingFufuArchive,
        bool overwriteExisting = true,
        CancellationToken cancellationToken = default)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<HutaoBridgeTransferSummary>();
        if (bridgeCheck != null) return bridgeCheck;

        var cleanUids = (uids ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (cleanUids.Count == 0)
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrSelectUidFirst".GetLocalized());
        }

        if (!_cloudSync.IsValidSlotName(targetSlot))
        {
            return Fail<HutaoBridgeTransferSummary>("CloudSync_ErrInvalidSlot".GetLocalized());
        }

        var normalizedSlot = targetSlot.Trim();

        var gachaPlanCheck = await EnsureGachaServiceAvailableAsync<HutaoBridgeTransferSummary>();
        if (gachaPlanCheck != null) return gachaPlanCheck;

        var catalog = await ResolveItemCatalogAsync(cancellationToken);

        var uidEntries = new Dictionary<string, UIGFEntry>(StringComparer.Ordinal);
        var downloadedCount = 0;

        foreach (var uid in cleanUids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endIdsRes = await SendAuthenticatedAsync<Dictionary<string, long>>(
                HttpMethod.Get,
                $"{ApiEndpoints.HutaoGachaEndIdsPath}?Uid={Uri.EscapeDataString(uid)}",
                null,
                cancellationToken);

            if (!endIdsRes.Ok)
            {
                return new HutaoOperationResult<HutaoBridgeTransferSummary>
                {
                    Ok = false,
                    Message = endIdsRes.Message,
                    AuthInvalidated = endIdsRes.AuthInvalidated
                };
            }

            var activeQueryTypes = QueryTypes.AsEnumerable();
            if (endIdsRes.Data is { Count: > 0 })
            {
                activeQueryTypes = endIdsRes.Data
                    .Where(kv => kv.Value > 0 && int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qt) && QueryTypes.Contains(qt))
                    .Select(kv => int.Parse(kv.Key, CultureInfo.InvariantCulture))
                    .Distinct()
                    .ToList();
            }

            var tz = ResolveDefaultTimezone(uid);
            var itemMap = new Dictionary<string, UIGFItem>(StringComparer.Ordinal);

            foreach (var queryType in activeQueryTypes)
            {
                long? cursor = null;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var path = cursor.HasValue
                        ? $"{ApiEndpoints.HutaoGachaLimitedRetrievePath}?uid={Uri.EscapeDataString(uid)}&configType={queryType}&endId={cursor.Value}&count=200"
                        : $"{ApiEndpoints.HutaoGachaLimitedRetrievePath}?uid={Uri.EscapeDataString(uid)}&configType={queryType}&count=200";
                    var pageRes = await SendAuthenticatedAsync<List<HutaoGachaItem>>(HttpMethod.Get, path, null, cancellationToken);
                    if (!pageRes.Ok)
                    {
                        return new HutaoOperationResult<HutaoBridgeTransferSummary>
                        {
                            Ok = false,
                            Message = pageRes.Message,
                            AuthInvalidated = pageRes.AuthInvalidated
                        };
                    }

                    var batch = pageRes.Data ?? new List<HutaoGachaItem>();
                    if (batch.Count == 0) break;

                    var minId = long.MaxValue;
                    foreach (var gachaItem in batch)
                    {
                        if (gachaItem.Id <= 0) continue;
                        if (gachaItem.Id < minId) minId = gachaItem.Id;

                        var converted = ConvertHutaoItemToUigf(gachaItem, tz, catalog);
                        if (converted == null) continue;

                        itemMap[converted.Id] = converted;
                    }

                    if (batch.Count < 200 || minId == long.MaxValue || (cursor.HasValue && minId >= cursor.Value))
                    {
                        break;
                    }

                    cursor = minId;
                }
            }

            if (itemMap.Count > 0)
            {
                downloadedCount += itemMap.Count;
                uidEntries[uid] = new UIGFEntry
                {
                    Uid = uid,
                    Timezone = tz,
                    Lang = "zh-cn",
                    List = itemMap.Values
                        .OrderByDescending(x => long.TryParse(x.Id, out var idVal) ? idVal : 0L)
                        .ToList()
                };
            }
        }

        if (uidEntries.Count == 0)
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrNoHutaoGachaData".GetLocalized());
        }

        if (mergeExistingFufuArchive)
        {
            var listRes = await _cloudSync.ListArchivesAsync("uigf");
            if (listRes.Ok && listRes.Data?.Archives.Any(a =>
                    string.Equals(a.DataType, "uigf", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.Slot, normalizedSlot, StringComparison.OrdinalIgnoreCase)) == true)
            {
                var existingPull = await _cloudSync.PullArchiveAsync("uigf", normalizedSlot);
                if (existingPull.Ok && !string.IsNullOrWhiteSpace(existingPull.JsonContent))
                {
                    MergeExistingUigfIntoEntries(existingPull.JsonContent, uidEntries, catalog);
                }
            }
        }

        var strictJson = BuildStrictUigfV4Json(uidEntries);
        if (string.IsNullOrWhiteSpace(strictJson))
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrNoHutaoGachaData".GetLocalized());
        }

        var uploadRes = await _cloudSync.UploadArchiveAsync(
            "uigf",
            normalizedSlot,
            strictJson,
            overwrite: overwriteExisting || mergeExistingFufuArchive);
        if (!uploadRes.Ok)
        {
            return Fail<HutaoBridgeTransferSummary>(
                _cloudSync.FormatFriendlyError(uploadRes.Code, uploadRes.Message, uploadRes.HttpStatusCode));
        }

        var totalRecords = uidEntries.Values.Sum(e => e.List.Count);
        return new HutaoOperationResult<HutaoBridgeTransferSummary>
        {
            Ok = true,
            Message = string.Format(
                "HutaoSync_MsgPullGachaSuccess".GetLocalized(),
                cleanUids.Count,
                downloadedCount,
                normalizedSlot,
                totalRecords),
            Data = new HutaoBridgeTransferSummary
            {
                TargetSlot = normalizedSlot,
                UidCount = uidEntries.Count,
                TransferredRecords = downloadedCount,
                TotalMergedRecords = totalRecords
            }
        };
    }

    public async Task<HutaoOperationResult<List<FufuCloudUigfUidItem>>> InspectFufuCloudUigfAsync(string slot)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<List<FufuCloudUigfUidItem>>();
        if (bridgeCheck != null) return bridgeCheck;

        if (string.IsNullOrWhiteSpace(slot))
        {
            return Fail<List<FufuCloudUigfUidItem>>("HutaoSync_ErrSelectFufuArchive".GetLocalized());
        }

        var pullRes = await _cloudSync.PullArchiveAsync("uigf", slot.Trim());
        if (!pullRes.Ok)
        {
            return Fail<List<FufuCloudUigfUidItem>>(
                _cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
        }

        var entries = ParseUigfEntries(pullRes.JsonContent);
        if (entries.Count == 0)
        {
            return Fail<List<FufuCloudUigfUidItem>>("HutaoSync_ErrArchiveNoValidRecords".GetLocalized());
        }

        var items = entries.Values
            .OrderBy(e => e.Uid, StringComparer.Ordinal)
            .Select(e => new FufuCloudUigfUidItem
            {
                Uid = e.Uid,
                RecordCount = e.List.Count,
                IsSelected = true
            })
            .ToList();

        return new HutaoOperationResult<List<FufuCloudUigfUidItem>>
        {
            Ok = true,
            Data = items
        };
    }

    public async Task<HutaoOperationResult<HutaoBridgeTransferSummary>> PushFufuCloudGachaToHutaoAsync(
        string slot,
        IReadOnlyList<string> selectedUids,
        CancellationToken cancellationToken = default)
    {
        var bridgeCheck = await EnsureFufuCloudReadyAsync<HutaoBridgeTransferSummary>();
        if (bridgeCheck != null) return bridgeCheck;

        if (string.IsNullOrWhiteSpace(slot))
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrSelectFufuArchive".GetLocalized());
        }

        var uidSet = (selectedUids ?? Array.Empty<string>())
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (uidSet.Count == 0)
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrSelectUidFirst".GetLocalized());
        }

        var gachaPlanCheck = await EnsureGachaServiceAvailableAsync<HutaoBridgeTransferSummary>();
        if (gachaPlanCheck != null) return gachaPlanCheck;

        var pullRes = await _cloudSync.PullArchiveAsync("uigf", slot.Trim());
        if (!pullRes.Ok)
        {
            return Fail<HutaoBridgeTransferSummary>(
                _cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
        }

        var entries = ParseUigfEntries(pullRes.JsonContent);
        var targetEntries = entries.Values
            .Where(e => uidSet.Contains(e.Uid) && e.List.Count > 0)
            .ToList();

        if (targetEntries.Count == 0)
        {
            return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrArchiveNoValidRecords".GetLocalized());
        }

        var catalog = await ResolveItemCatalogAsync(cancellationToken);
        var totalUploaded = 0;
        var alreadySyncedCount = 0;
        var skippedInvalidCount = 0;

        foreach (var entry in targetEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var endIdsRes = await SendAuthenticatedAsync<Dictionary<string, long>>(
                HttpMethod.Get,
                $"{ApiEndpoints.HutaoGachaEndIdsPath}?Uid={Uri.EscapeDataString(entry.Uid)}",
                null,
                cancellationToken);

            if (!endIdsRes.Ok)
            {
                DebugLog.Info("HutaoCloudService", $"[Hutao] PushGacha EndIds failed for UID {entry.Uid}: {endIdsRes.Message}");
                return new HutaoOperationResult<HutaoBridgeTransferSummary>
                {
                    Ok = false,
                    Message = endIdsRes.Message,
                    AuthInvalidated = endIdsRes.AuthInvalidated
                };
            }

            var endIds = endIdsRes.Data ?? new Dictionary<string, long>(StringComparer.Ordinal);
            var tz = entry.Timezone is >= -12 and <= 14 ? entry.Timezone : ResolveDefaultTimezone(entry.Uid);
            var seenIds = new HashSet<long>();
            var toUpload = new List<HutaoGachaItem>();

            foreach (var rec in entry.List)
            {
                if (!long.TryParse((rec.Id ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                {
                    skippedInvalidCount++;
                    continue;
                }

                if (!seenIds.Add(id))
                {
                    skippedInvalidCount++;
                    continue;
                }

                var gachaType = ParseGachaTypeInt(rec.GachaType, rec.UigfGachaType);
                if (gachaType == 0)
                {
                    skippedInvalidCount++;
                    continue;
                }

                var queryType = gachaType == 400 ? 301 : ParseGachaTypeInt(rec.UigfGachaType, gachaType.ToString(CultureInfo.InvariantCulture));
                if (queryType is not (100 or 200 or 301 or 302 or 500))
                {
                    queryType = gachaType == 400 ? 301 : 200;
                }

                var endId = endIds.GetValueOrDefault(queryType.ToString(CultureInfo.InvariantCulture), 0L);
                if (id <= endId)
                {
                    alreadySyncedCount++;
                    continue;
                }

                var itemId = ResolveItemId(rec, catalog);
                if (itemId <= 0)
                {
                    skippedInvalidCount++;
                    continue;
                }

                var formattedTime = FormatHutaoIsoTime(rec.Time, tz);
                if (formattedTime == null)
                {
                    skippedInvalidCount++;
                    continue;
                }

                toUpload.Add(new HutaoGachaItem
                {
                    GachaType = gachaType,
                    QueryType = queryType,
                    ItemId = itemId,
                    Time = formattedTime,
                    Id = id
                });
            }

            if (toUpload.Count == 0) continue;

            toUpload.Sort((a, b) => a.Id.CompareTo(b.Id));

            const int chunkSize = 5000;
            for (var i = 0; i < toUpload.Count; i += chunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = toUpload.Skip(i).Take(chunkSize).ToList();
                var uploadPayload = new
                {
                    Uid = entry.Uid,
                    Items = batch
                };

                var uploadRes = await SendAuthenticatedAsync<JsonElement?>(
                    HttpMethod.Post,
                    ApiEndpoints.HutaoGachaUploadPath,
                    uploadPayload,
                    cancellationToken);

                if (!uploadRes.Ok)
                {
                    DebugLog.Info("HutaoCloudService", $"[Hutao] PushGacha Upload failed for UID {entry.Uid} (batchCount={batch.Count}): {uploadRes.Message}");
                    return new HutaoOperationResult<HutaoBridgeTransferSummary>
                    {
                        Ok = false,
                        Message = uploadRes.Message,
                        AuthInvalidated = uploadRes.AuthInvalidated
                    };
                }

                totalUploaded += batch.Count;
            }
        }

        var totalSkipped = alreadySyncedCount + skippedInvalidCount;
        if (totalUploaded == 0)
        {
            if (alreadySyncedCount == 0 && skippedInvalidCount > 0)
            {
                return Fail<HutaoBridgeTransferSummary>("HutaoSync_ErrArchiveNoValidRecords".GetLocalized());
            }

            return new HutaoOperationResult<HutaoBridgeTransferSummary>
            {
                Ok = true,
                Message = "HutaoSync_MsgPushGachaUpToDate".GetLocalized(),
                Data = new HutaoBridgeTransferSummary
                {
                    TargetSlot = slot.Trim(),
                    UidCount = targetEntries.Count,
                    TransferredRecords = 0,
                    SkippedRecords = totalSkipped
                }
            };
        }

        return new HutaoOperationResult<HutaoBridgeTransferSummary>
        {
            Ok = true,
            Message = string.Format(
                "HutaoSync_MsgPushGachaSuccess".GetLocalized(),
                targetEntries.Count,
                totalUploaded),
            Data = new HutaoBridgeTransferSummary
            {
                TargetSlot = slot.Trim(),
                UidCount = targetEntries.Count,
                TransferredRecords = totalUploaded,
                SkippedRecords = totalSkipped
            }
        };
    }

    private static UIGFItem? ConvertHutaoItemToUigf(
        HutaoGachaItem item,
        int timezoneOffset,
        (Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName) catalog)
    {
        var idStr = item.Id.ToString(CultureInfo.InvariantCulture);
        if (!ValidIdRegex().IsMatch(idStr)) return null;

        var gachaType = ValidGachaTypes.Contains(item.GachaType)
            ? item.GachaType.ToString(CultureInfo.InvariantCulture)
            : (ValidGachaTypes.Contains(item.QueryType) ? item.QueryType.ToString(CultureInfo.InvariantCulture) : "200");

        var queryTypeStr = item.QueryType is 100 or 200 or 301 or 302 or 500
            ? item.QueryType.ToString(CultureInfo.InvariantCulture)
            : (gachaType == "400" ? "301" : gachaType);

        var timeFormatted = FormatUigfLocalTime(item.Time, timezoneOffset);
        if (timeFormatted == null || !ValidTimeRegex().IsMatch(timeFormatted)) return null;

        catalog.ById.TryGetValue(item.ItemId, out var meta);
        var itemType = !string.IsNullOrEmpty(meta.ItemType)
            ? meta.ItemType
            : (item.ItemId >= 10_000_000 ? "角色" : "武器");

        return new UIGFItem
        {
            UigfGachaType = queryTypeStr,
            GachaType = gachaType,
            ItemId = item.ItemId > 0 ? item.ItemId.ToString(CultureInfo.InvariantCulture) : string.Empty,
            Count = "1",
            Time = timeFormatted,
            Name = meta.Name ?? string.Empty,
            ItemType = itemType,
            RankType = meta.RankType ?? string.Empty,
            Id = idStr
        };
    }

    private static Dictionary<string, UIGFEntry> ParseUigfEntries(string jsonContent)
    {
        var result = new Dictionary<string, UIGFEntry>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(jsonContent)) return result;

        UIGFJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<UIGFJson>(jsonContent, JsonOptions);
        }
        catch
        {
            return result;
        }

        if (parsed == null) return result;

        if (parsed.Hk4e != null && parsed.Hk4e.Count > 0)
        {
            foreach (var entry in parsed.Hk4e)
            {
                var uid = (entry.Uid ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(uid) || entry.List == null || entry.List.Count == 0) continue;

                var normalizedLang = (entry.Lang ?? string.Empty).Trim().ToLowerInvariant();
                if (!SupportedUigfLangs.Contains(normalizedLang)) normalizedLang = "zh-cn";

                var tz = entry.Timezone is >= -12 and <= 14 ? entry.Timezone : ResolveDefaultTimezone(uid);

                if (!result.TryGetValue(uid, out var bucket))
                {
                    bucket = new UIGFEntry
                    {
                        Uid = uid,
                        Timezone = tz,
                        Lang = normalizedLang,
                        List = new List<UIGFItem>()
                    };
                    result[uid] = bucket;
                }

                bucket.List.AddRange(entry.List);
            }
        }
        else if (parsed.List != null && parsed.List.Count > 0)
        {
            var uid = (parsed.Info?.Uid ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(uid))
            {
                var lang = (parsed.Info?.Lang ?? string.Empty).Trim().ToLowerInvariant();
                if (!SupportedUigfLangs.Contains(lang)) lang = "zh-cn";

                result[uid] = new UIGFEntry
                {
                    Uid = uid,
                    Timezone = ResolveDefaultTimezone(uid),
                    Lang = lang,
                    List = parsed.List
                };
            }
        }

        return result;
    }

    private static void MergeExistingUigfIntoEntries(
        string existingJson,
        Dictionary<string, UIGFEntry> targetEntries,
        (Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName) catalog)
    {
        var existing = ParseUigfEntries(existingJson);
        foreach (var (uid, oldEntry) in existing)
        {
            if (!targetEntries.TryGetValue(uid, out var target))
            {
                targetEntries[uid] = oldEntry;
                continue;
            }

            var mergedMap = new Dictionary<string, UIGFItem>(StringComparer.Ordinal);
            foreach (var rec in oldEntry.List)
            {
                var id = (rec.Id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(id)) continue;
                EnrichUigfItemFromCatalog(rec, catalog);
                mergedMap[id] = rec;
            }

            foreach (var rec in target.List)
            {
                var id = (rec.Id ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(id)) continue;
                EnrichUigfItemFromCatalog(rec, catalog);
                if (!mergedMap.TryGetValue(id, out var prior) ||
                    (string.IsNullOrWhiteSpace(prior.Name) && !string.IsNullOrWhiteSpace(rec.Name)))
                {
                    mergedMap[id] = rec;
                }
            }

            target.List = mergedMap.Values
                .OrderByDescending(x => long.TryParse(x.Id, out var idVal) ? idVal : 0L)
                .ToList();
        }
    }

    private static void EnrichUigfItemFromCatalog(
        UIGFItem rec,
        (Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName) catalog)
    {
        if (int.TryParse(rec.ItemId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
            id > 0 &&
            catalog.ById.TryGetValue(id, out var meta))
        {
            if (string.IsNullOrWhiteSpace(rec.Name) && !string.IsNullOrEmpty(meta.Name)) rec.Name = meta.Name;
            if (string.IsNullOrWhiteSpace(rec.ItemType) && !string.IsNullOrEmpty(meta.ItemType)) rec.ItemType = meta.ItemType;
            if (string.IsNullOrWhiteSpace(rec.RankType) && !string.IsNullOrEmpty(meta.RankType)) rec.RankType = meta.RankType;
        }
        else if (string.IsNullOrWhiteSpace(rec.ItemId) &&
                 !string.IsNullOrWhiteSpace(rec.Name) &&
                 catalog.ByName.TryGetValue(rec.Name.Trim(), out var resolvedId))
        {
            rec.ItemId = resolvedId.ToString(CultureInfo.InvariantCulture);
            if (catalog.ById.TryGetValue(resolvedId, out var resolvedMeta))
            {
                if (string.IsNullOrWhiteSpace(rec.ItemType)) rec.ItemType = resolvedMeta.ItemType;
                if (string.IsNullOrWhiteSpace(rec.RankType)) rec.RankType = resolvedMeta.RankType;
            }
        }
    }

    private static string BuildStrictUigfV4Json(Dictionary<string, UIGFEntry> uidEntries)
    {
        var entries = new List<object>();
        var totalCount = 0;

        foreach (var (uid, entry) in uidEntries.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(uid) || uid.Length > 64) continue;

            var strictList = new List<object>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rec in entry.List)
            {
                if (totalCount >= 250_000) break;

                var id = (rec.Id ?? string.Empty).Trim();
                if (!ValidIdRegex().IsMatch(id) || !seenIds.Add(id)) continue;

                var time = NormalizeTimeForUigf(rec.Time);
                if (time == null || !ValidTimeRegex().IsMatch(time)) continue;

                var gachaTypeInt = ParseGachaTypeInt(rec.GachaType, rec.UigfGachaType);
                if (gachaTypeInt == 0) continue;

                var gachaType = gachaTypeInt.ToString(CultureInfo.InvariantCulture);
                var uigfType = gachaType == "400"
                    ? "301"
                    : ParseGachaTypeInt(rec.UigfGachaType, gachaType).ToString(CultureInfo.InvariantCulture);
                if (uigfType is not ("100" or "200" or "301" or "302" or "500"))
                {
                    uigfType = gachaType == "400" ? "301" : "200";
                }

                strictList.Add(new
                {
                    uigf_gacha_type = uigfType,
                    gacha_type = gachaType,
                    item_id = TruncateString(rec.ItemId, 64),
                    count = string.IsNullOrWhiteSpace(rec.Count) ? "1" : TruncateString(rec.Count, 16),
                    time,
                    name = TruncateString(rec.Name, 128),
                    item_type = TruncateString(rec.ItemType, 64),
                    rank_type = TruncateString(rec.RankType, 16),
                    id
                });

                totalCount++;
            }

            if (strictList.Count == 0) continue;

            var lang = (entry.Lang ?? string.Empty).Trim().ToLowerInvariant();
            if (!SupportedUigfLangs.Contains(lang)) lang = "zh-cn";

            entries.Add(new
            {
                uid,
                timezone = entry.Timezone is >= -12 and <= 14 ? entry.Timezone : ResolveDefaultTimezone(uid),
                lang,
                list = strictList
            });

            if (totalCount >= 250_000) break;
        }

        if (entries.Count == 0) return string.Empty;

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

        return JsonSerializer.Serialize(root, WriteJsonOptions);
    }

    private static int ResolveItemId(
        UIGFItem rec,
        (Dictionary<int, (string Name, string ItemType, string RankType)> ById, Dictionary<string, int> ByName) catalog)
    {
        if (int.TryParse((rec.ItemId ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var directId) && directId > 0)
        {
            return directId;
        }

        if (!string.IsNullOrWhiteSpace(rec.Name) &&
            catalog.ByName.TryGetValue(rec.Name.Trim(), out var resolved))
        {
            return resolved;
        }

        return 0;
    }

    private static int ParseGachaTypeInt(string? primary, string? fallback)
    {
        if (int.TryParse((primary ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) &&
            ValidGachaTypes.Contains(p))
        {
            return p;
        }

        if (int.TryParse((fallback ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var f) &&
            ValidGachaTypes.Contains(f))
        {
            return f;
        }

        return 0;
    }

    private static string? FormatUigfLocalTime(string? rawTime, int tzHours)
    {
        if (string.IsNullOrWhiteSpace(rawTime)) return null;

        if (DateTimeOffset.TryParse(rawTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.ToOffset(TimeSpan.FromHours(tzHours)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(rawTime.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt))
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string? NormalizeTimeForUigf(string? rawTime)
    {
        if (string.IsNullOrWhiteSpace(rawTime)) return null;
        var trimmed = rawTime.Trim();
        if (ValidTimeRegex().IsMatch(trimmed)) return trimmed;

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt)
            ? dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? FormatHutaoIsoTime(string? rawTime, int tzHours)
    {
        if (string.IsNullOrWhiteSpace(rawTime)) return null;

        var trimmed = rawTime.Trim();
        var tzOffset = TimeSpan.FromHours(tzHours);

        if (DateTime.TryParseExact(
                trimmed,
                ExactWishTimeFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var exactDt))
        {
            var offsetDto = new DateTimeOffset(DateTime.SpecifyKind(exactDt, DateTimeKind.Unspecified), tzOffset);
            return offsetDto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDt))
        {
            var offsetDto = new DateTimeOffset(DateTime.SpecifyKind(parsedDt, DateTimeKind.Unspecified), tzOffset);
            return offsetDto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static int ResolveDefaultTimezone(string uid)
    {
        if (uid.StartsWith('6')) return -5;
        if (uid.StartsWith('7')) return 1;
        return 8;
    }
}
