using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Data.Entities;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.UIGF;
using FufuLauncher.ViewModels;

namespace FufuLauncher.Services;

public sealed class LocalAchievementProfileOption
{
    public string DisplayName
    {
        get;
        init;
    } = string.Empty;

    public string ProfileName
    {
        get;
        init;
    } = string.Empty;

    public string DbPath
    {
        get;
        init;
    } = string.Empty;

    public bool IsCurrentActive
    {
        get;
        init;
    }

    public bool IsNewProfileOption
    {
        get;
        init;
    }

    public override string ToString() => DisplayName;
}

public static class CloudArchiveLocalBridge
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed class UiafBridgeDocument
    {
        [JsonPropertyName("list")]
        public List<UiafBridgeItem>? List
        {
            get;
            set;
        }
    }

    private sealed class UiafBridgeItem
    {
        [JsonPropertyName("id")]
        public int Id
        {
            get;
            set;
        }

        [JsonPropertyName("current")]
        public int Current
        {
            get;
            set;
        }

        [JsonPropertyName("status")]
        public int Status
        {
            get;
            set;
        }

        [JsonPropertyName("timestamp")]
        public long Timestamp
        {
            get;
            set;
        }
    }

    public static List<FufuCloudUigfUidItem> InspectUigfUids(string jsonContent)
    {
        var result = new List<FufuCloudUigfUidItem>();
        if (string.IsNullOrWhiteSpace(jsonContent)) return result;

        UIGFJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<UIGFJson>(jsonContent, ReadOptions);
        }
        catch
        {
            return result;
        }

        if (parsed == null) return result;

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (parsed.Hk4e != null && parsed.Hk4e.Count > 0)
        {
            foreach (var entry in parsed.Hk4e)
            {
                var uid = (entry.Uid ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(uid) || entry.List == null || entry.List.Count == 0) continue;
                counts[uid] = counts.GetValueOrDefault(uid, 0) + entry.List.Count;
            }
        }
        else if (parsed.List != null && parsed.List.Count > 0)
        {
            var uid = string.IsNullOrWhiteSpace(parsed.Info?.Uid) ? "unknown" : parsed.Info.Uid.Trim();
            counts[uid] = parsed.List.Count;
        }

        foreach (var (uid, count) in counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            result.Add(new FufuCloudUigfUidItem
            {
                Uid = uid,
                RecordCount = count,
                IsSelected = true
            });
        }

        return result;
    }

    public static string? FilterUigfJsonByUids(string jsonContent, IReadOnlySet<string> selectedUids)
    {
        if (string.IsNullOrWhiteSpace(jsonContent) || selectedUids.Count == 0) return null;

        UIGFJson? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<UIGFJson>(jsonContent, ReadOptions);
        }
        catch
        {
            return null;
        }

        if (parsed == null) return null;

        if (parsed.Hk4e != null && parsed.Hk4e.Count > 0)
        {
            var filteredEntries = parsed.Hk4e
                .Where(e => !string.IsNullOrWhiteSpace(e.Uid) &&
                            selectedUids.Contains(e.Uid.Trim()) &&
                            e.List is { Count: > 0 })
                .ToList();

            if (filteredEntries.Count == 0) return null;

            return JsonSerializer.Serialize(new
            {
                info = parsed.Info,
                hk4e = filteredEntries
            }, WriteOptions);
        }

        if (parsed.List != null && parsed.List.Count > 0)
        {
            var uid = string.IsNullOrWhiteSpace(parsed.Info?.Uid) ? "unknown" : parsed.Info.Uid.Trim();
            if (!selectedUids.Contains(uid)) return null;
            return jsonContent;
        }

        return null;
    }

    public static async Task<(bool Success, int ImportedAccounts, int ImportedRecords, string? DisplayUid)>
        ImportUigfToLocalAsync(string jsonContent, IReadOnlySet<string>? selectedUids)
    {
        var payload = selectedUids != null
            ? FilterUigfJsonByUids(jsonContent, selectedUids)
            : jsonContent;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return (false, 0, 0, null);
        }

        var vm = App.GetService<GachaAnalysisViewModel>();
        return await vm.ImportUigfJsonFromCloudAsync(payload);
    }

    public static (bool Ok, int TotalItems, int CompletedItems) InspectUiafJson(string jsonContent)
    {
        if (string.IsNullOrWhiteSpace(jsonContent)) return (false, 0, 0);

        try
        {
            var doc = JsonSerializer.Deserialize<UiafBridgeDocument>(jsonContent, ReadOptions);
            if (doc?.List == null || doc.List.Count == 0) return (false, 0, 0);

            var validItems = doc.List.Where(i => i.Id > 0).ToList();
            if (validItems.Count == 0) return (false, 0, 0);

            var completed = validItems.Count(i => i.Status == 2 || i.Status == 3);
            return (true, validItems.Count, completed);
        }
        catch
        {
            return (false, 0, 0);
        }
    }

    public static List<LocalAchievementProfileOption> GetLocalAchievementProfiles()
    {
        var dataDir = AppPaths.DataDir;
        var archivesDir = Path.Combine(dataDir, "archives");
        var workFilePath = Path.Combine(dataDir, "achievements.db");
        var profileRecordPath = Path.Combine(dataDir, "current_profile.txt");

        var activeName = File.Exists(profileRecordPath)
            ? File.ReadAllText(profileRecordPath).Trim()
            : string.Empty;

        var defaultLabel = string.IsNullOrWhiteSpace(activeName)
            ? "CloudSync_UiafTargetCurrentDefault".GetLocalized()
            : string.Format("CloudSync_UiafTargetCurrentNamedFormat".GetLocalized(), activeName);

        var options = new List<LocalAchievementProfileOption>
        {
            new()
            {
                DisplayName = defaultLabel,
                ProfileName = activeName,
                DbPath = workFilePath,
                IsCurrentActive = true
            }
        };

        if (Directory.Exists(archivesDir))
        {
            var dbFiles = Directory.GetFiles(archivesDir, "*.db")
                .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase);

            foreach (var file in dbFiles)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!string.IsNullOrWhiteSpace(activeName) &&
                    string.Equals(name, activeName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                options.Add(new LocalAchievementProfileOption
                {
                    DisplayName = string.Format("CloudSync_UiafTargetNamedFormat".GetLocalized(), name),
                    ProfileName = name,
                    DbPath = file,
                    IsCurrentActive = false
                });
            }
        }

        options.Add(new LocalAchievementProfileOption
        {
            DisplayName = "CloudSync_UiafTargetNewProfile".GetLocalized(),
            IsNewProfileOption = true
        });

        return options;
    }

    public static async Task<(bool Ok, int UpdatedCount, string TargetDisplayName, string? ErrorMessage)>
        MergeUiafToLocalProfileAsync(
            string jsonContent,
            LocalAchievementProfileOption targetOption,
            string? newProfileName)
    {
        UiafBridgeDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<UiafBridgeDocument>(jsonContent, ReadOptions);
        }
        catch
        {
            return (false, 0, string.Empty, "CloudSync_ErrInvalidFormat".GetLocalized());
        }

        if (doc?.List == null || doc.List.Count == 0)
        {
            return (false, 0, string.Empty, "CloudSync_ErrInvalidFormat".GetLocalized());
        }

        var dataDir = AppPaths.DataDir;
        var archivesDir = Path.Combine(dataDir, "archives");
        var workFilePath = Path.Combine(dataDir, "achievements.db");
        var profileRecordPath = Path.Combine(dataDir, "current_profile.txt");

        string targetDbPath;
        string displayName;
        var shouldSyncNamedBackup = false;
        string? syncBackupPath = null;

        if (targetOption.IsNewProfileOption)
        {
            var rawName = (newProfileName ?? string.Empty).Trim();
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                rawName = rawName.Replace(ch, '_');
            }

            if (string.IsNullOrWhiteSpace(rawName))
            {
                return (false, 0, string.Empty, "CloudSync_UiafNewProfileEmptyErr".GetLocalized());
            }

            Directory.CreateDirectory(archivesDir);
            targetDbPath = Path.Combine(archivesDir, $"{rawName}.db");
            if (File.Exists(targetDbPath))
            {
                return (false, 0, string.Empty, "CloudSync_UiafNewProfileExistsErr".GetLocalized());
            }

            displayName = rawName;
        }
        else if (targetOption.IsCurrentActive)
        {
            Directory.CreateDirectory(dataDir);
            targetDbPath = workFilePath;
            var activeName = File.Exists(profileRecordPath) ? File.ReadAllText(profileRecordPath).Trim() : string.Empty;
            displayName = string.IsNullOrWhiteSpace(activeName)
                ? "CloudSync_UiafDefaultProfileName".GetLocalized()
                : activeName;

            if (!string.IsNullOrWhiteSpace(activeName))
            {
                var candidateBackup = Path.Combine(archivesDir, $"{activeName}.db");
                if (File.Exists(candidateBackup))
                {
                    shouldSyncNamedBackup = true;
                    syncBackupPath = candidateBackup;
                }
            }
        }
        else
        {
            targetDbPath = targetOption.DbPath;
            displayName = targetOption.ProfileName;
        }

        var repo = App.GetService<AchievementRepository>();
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets", "achievements.json");

        try
        {
            var updatedCount = await Task.Run(() =>
            {
                try
                {
                    repo.ChangeDatabase(targetDbPath);
                    repo.InvalidateMigrationCache(targetDbPath);

                    var existing = repo.GetAllAchievements();
                    if (existing.Count == 0 && File.Exists(assetsPath))
                    {
                        var masterJson = File.ReadAllText(assetsPath);
                        var rawCategories = JsonSerializer.Deserialize<List<AchievementCategory>>(masterJson, ReadOptions);
                        if (rawCategories != null && rawCategories.Count > 0)
                        {
                            var catalogEntities = new List<AchievementEntity>();
                            foreach (var cat in rawCategories)
                            {
                                if (cat.Achievements == null) continue;
                                var catName = string.IsNullOrWhiteSpace(cat.Name)
                                    ? "AchievementWindow_UnknownCategory".GetLocalized()
                                    : cat.Name;
                                foreach (var item in cat.Achievements)
                                {
                                    catalogEntities.Add(new AchievementEntity
                                    {
                                        Id = item.Id,
                                        Title = item.Title ?? string.Empty,
                                        CategoryName = catName,
                                        RawJson = JsonSerializer.Serialize(item, WriteOptions),
                                        IsCompleted = item.IsCompleted ? 1 : 0,
                                        CurrentProgress = item.CurrentProgress,
                                        MaxProgress = item.MaxProgress,
                                        CompletionTimestamp = item.CompletionTimestamp
                                    });
                                }
                            }

                            repo.SynchronizeCatalog(
                                rawCategories.Select(c => (
                                    string.IsNullOrWhiteSpace(c.Name)
                                        ? "AchievementWindow_UnknownCategory".GetLocalized()
                                        : c.Name,
                                    c.IconUrl)),
                                catalogEntities);

                            existing = repo.GetAllAchievements();
                        }
                    }

                    var byAchievementId = existing
                        .Where(e => e.Id > 0)
                        .GroupBy(e => e.Id)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    var updates = new Dictionary<int, (bool IsCompleted, int CurrentProgress, int MaxProgress, long CompletionTimestamp)>();
                    var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var count = 0;

                    foreach (var uiafItem in doc.List)
                    {
                        if (uiafItem.Id <= 0 || !byAchievementId.TryGetValue(uiafItem.Id, out var matchedRows))
                        {
                            continue;
                        }

                        var incomingCompleted = uiafItem.Status == 2 || uiafItem.Status == 3;
                        foreach (var row in matchedRows)
                        {
                            var alreadyCompleted = row.IsCompleted != 0;
                            var needUpdate = uiafItem.Current > row.CurrentProgress ||
                                             (incomingCompleted && !alreadyCompleted);

                            if (!needUpdate) continue;

                            var nextCompleted = alreadyCompleted || incomingCompleted;
                            var nextProgress = Math.Max(row.CurrentProgress, uiafItem.Current);
                            if (nextCompleted && row.MaxProgress > 0 && nextProgress < row.MaxProgress)
                            {
                                nextProgress = row.MaxProgress;
                            }

                            var nextTimestamp = row.CompletionTimestamp;
                            if (nextCompleted)
                            {
                                if (uiafItem.Timestamp > 0)
                                {
                                    nextTimestamp = uiafItem.Timestamp;
                                }
                                else if (nextTimestamp == 0)
                                {
                                    nextTimestamp = nowUnix;
                                }
                            }

                            updates[row.Uid] = (nextCompleted, nextProgress, row.MaxProgress, nextTimestamp);
                            count++;
                        }
                    }

                    if (updates.Count > 0)
                    {
                        repo.UpdateAchievementsBatch(updates);
                    }

                    AchievementRepository.ClearConnectionPool();

                    if (shouldSyncNamedBackup && !string.IsNullOrEmpty(syncBackupPath) && File.Exists(targetDbPath))
                    {
                        File.Copy(targetDbPath, syncBackupPath, overwrite: true);
                    }

                    return count;
                }
                finally
                {
                    repo.ChangeDatabase(null);
                    AchievementRepository.ClearConnectionPool();
                }
            });

            return (true, updatedCount, displayName, null);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudArchiveLocalBridge", $"[CloudArchiveLocalBridge] UIAF merge failed: {ex.Message}");
            return (false, 0, displayName, "CloudSync_UiafMergeFailed".GetLocalized());
        }
    }
}
