/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class GamePlayTimeStore
{
    public const int CurrentSchemaVersion = 2;
    public const string StorageIdentifier = "fufu.game-play-time";
    public const string CloudPlayTimeVersion = "v1.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CompactCloudJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _sync = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Dictionary<string, long> _dailyPlayTimeSeconds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _legacyDailySeconds = new(StringComparer.Ordinal);
    private readonly List<PlayTimeSegment> _segments = new();
    private string? _lastPlayDate;
    private bool _hasVerifiedStorageIdentity;
    private bool _isLoaded;

    public event EventHandler? PlayTimeChanged;

    public IReadOnlyDictionary<string, long> GetDailyPlayTimeSnapshot()
    {
        lock (_sync)
        {
            return new Dictionary<string, long>(_dailyPlayTimeSeconds, StringComparer.Ordinal);
        }
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_isLoaded) return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            lock (_sync)
            {
                if (_isLoaded) return;
            }

            await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PlayTimeLocalSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        lock (_sync)
        {
            var segSeconds = SumSegmentSeconds(_segments);
            var totalSeconds = _dailyPlayTimeSeconds.Values.Sum();
            var legacySeconds = _legacyDailySeconds.Values.Sum();
            var activeDays = _dailyPlayTimeSeconds.Count(kv => kv.Value > 0);
            return new PlayTimeLocalSummary
            {
                TotalDays = activeDays,
                TotalSeconds = totalSeconds,
                SegmentCount = _segments.Count,
                SegmentedSeconds = segSeconds,
                LegacySeconds = legacySeconds
            };
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        PlayTimeChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _dailyPlayTimeSeconds.Clear();
            _legacyDailySeconds.Clear();
            _segments.Clear();
            _lastPlayDate = null;
            _hasVerifiedStorageIdentity = false;
        }

        var storagePath = AppPaths.GamePlayTimeFile;
        if (File.Exists(storagePath))
        {
            if (await TryLoadAsync(storagePath, cancellationToken))
            {
                lock (_sync)
                {
                    _isLoaded = true;
                }

                if (_hasVerifiedStorageIdentity)
                {
                    await RetireMigratedLegacyAsync(cancellationToken);
                }

                return;
            }

            BackupCorruptStorage(storagePath);
        }

        await MigrateLegacyCoreAsync(cancellationToken);
        lock (_sync)
        {
            _isLoaded = true;
        }
    }

    public void AddPlayTime(DateTime localDate, long secondsToAdd)
    {
        if (secondsToAdd <= 0) return;

        var endUnix = Math.Max(secondsToAdd, ToDateTimeOffset(localDate).ToUnixTimeSeconds());
        var startUnix = Math.Max(0, endUnix - secondsToAdd);
        var dateKey = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        lock (_sync)
        {
            if (_segments.Count > 0)
            {
                var last = _segments[^1];
                if (endUnix >= last.End - 5 && endUnix <= last.End + secondsToAdd + 25)
                {
                    last.End += secondsToAdd;
                }
                else
                {
                    _segments.Add(new PlayTimeSegment { Start = startUnix, End = endUnix });
                    ReplaceSegmentsNormalizedLocked(_segments);
                }
            }
            else
            {
                _segments.Add(new PlayTimeSegment { Start = startUnix, End = endUnix });
            }

            var deltaDateKey = dateKey;
            RecalculateDailyTotalsLocked(beforeDailyFloor: null);

            if (string.IsNullOrEmpty(_lastPlayDate) || string.CompareOrdinal(deltaDateKey, _lastPlayDate) > 0)
            {
                _lastPlayDate = deltaDateKey;
            }
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var document = BuildDocumentSnapshot();
            await SaveDocumentCoreAsync(document, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private GamePlayTimeDocument BuildDocumentSnapshot()
    {
        lock (_sync)
        {
            return new GamePlayTimeDocument
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
    }

    private static async Task SaveDocumentCoreAsync(GamePlayTimeDocument document, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await WriteAtomicallyAsync(AppPaths.GamePlayTimeFile, json, cancellationToken);
    }

    private bool IsLoadedFlag()
    {
        lock (_sync)
        {
            return _isLoaded;
        }
    }

    private async Task<bool> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var document = JsonSerializer.Deserialize<GamePlayTimeDocument>(json, JsonOptions);
            if (document == null) return false;

            var dailySeconds = document.DailyPlayTimeSeconds;
            lock (_sync)
            {
                _dailyPlayTimeSeconds.Clear();
                _legacyDailySeconds.Clear();
                _segments.Clear();

                if (document.Segments != null)
                {
                    ReplaceSegmentsNormalizedLocked(document.Segments);
                }

                var segDaily = ComputeSegmentDailySeconds(_segments);
                if (document.LegacyDailySeconds != null)
                {
                    foreach (var entry in document.LegacyDailySeconds)
                    {
                        if (!IsValidDateKey(entry.Key) || entry.Value <= 0) continue;
                        _legacyDailySeconds[entry.Key] = entry.Value;
                    }
                }

                var loadedDaily = new Dictionary<string, long>(StringComparer.Ordinal);
                if (dailySeconds != null)
                {
                    foreach (var entry in dailySeconds)
                    {
                        if (!IsValidDateKey(entry.Key) || entry.Value < 0) continue;
                        loadedDaily[entry.Key] = entry.Value;

                        var segCovered = segDaily.GetValueOrDefault(entry.Key);
                        if (entry.Value > segCovered && document.LegacyDailySeconds == null)
                        {
                            _legacyDailySeconds[entry.Key] = Math.Max(
                                _legacyDailySeconds.GetValueOrDefault(entry.Key),
                                entry.Value - segCovered);
                        }
                    }
                }

                _lastPlayDate = document.LastPlayDate;
                RecalculateDailyTotalsLocked(loadedDaily);

                _hasVerifiedStorageIdentity =
                    string.Equals(document.StorageKey, StorageIdentifier, StringComparison.Ordinal) &&
                    document.SchemaVersion is 1 or CurrentSchemaVersion;
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to load {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private static async Task WriteAtomicallyAsync(string path, string json,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void BackupCorruptStorage(string path)
    {
        try
        {
            var backupPath = $"{path}.corrupt.{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: true);
            DebugLog.Info("GamePlayTimeStore", $"Backed up corrupt game time storage to {backupPath}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore", $"Failed to back up corrupt game time storage: {ex.Message}");
        }
    }
}

internal sealed class GamePlayTimeDocument
{
    public int SchemaVersion
    {
        get;
        set;
    }

    public string StorageKey
    {
        get;
        set;
    } = string.Empty;

    public string? LastPlayDate
    {
        get;
        set;
    }

    public DateTimeOffset UpdatedAt
    {
        get;
        set;
    }

    public Dictionary<string, long> DailyPlayTimeSeconds
    {
        get;
        set;
    } = new();

    public Dictionary<string, long>? LegacyDailySeconds
    {
        get;
        set;
    }

    public List<PlayTimeSegment>? Segments
    {
        get;
        set;
    }
}

internal sealed class LegacyGamePlayTimeConfig
{
    public Dictionary<string, long>? GamePlayTimeData
    {
        get;
        set;
    }

    public string? LastPlayDate
    {
        get;
        set;
    }
}
