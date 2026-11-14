/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using FufuLauncher.Models.CycleChallenge;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public sealed class CycleChallengeRepository
{
    private const int CurrentSchemaVersion = 1;

    private readonly string _dbPath;
    private readonly object _initLock = new();
    private bool _initialized;

    public CycleChallengeRepository()
        : this(Path.Combine(AppPaths.DataDir, "cycle_challenge.db"))
    {
    }

    public CycleChallengeRepository(string dbPath)
    {
        _dbPath = dbPath;
        var folder = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }
    }

    public string DatabasePath => _dbPath;

    private CycleChallengeDbContext CreateContext() => new(_dbPath);


    public void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            using var db = CreateContext();
            var connection = db.Database.GetDbConnection();
            connection.Open();

            int version;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA user_version;";
                version = Convert.ToInt32(command.ExecuteScalar() ?? 0);
            }

            if (version < 1)
            {
                foreach (var sql in SchemaV1)
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.ExecuteNonQuery();
                }

                version = 1;
            }

            connection.Close();
            _initialized = true;
        }
    }

    private static readonly string[] SchemaV1 =
    [
        """
        CREATE TABLE IF NOT EXISTS CycleChallengeRecord
        (
            Uid        TEXT    NOT NULL,
            Mode       INTEGER NOT NULL,
            ScheduleId TEXT    NOT NULL,
            AccountId  TEXT,
            StartUtc   TEXT,
            EndUtc     TEXT,
            Summary    TEXT,
            Payload    TEXT    NOT NULL,
            FetchedAt  TEXT    NOT NULL,
            ApiVersion TEXT,
            CONSTRAINT PK_CycleChallengeRecord PRIMARY KEY (Uid, Mode, ScheduleId)
        );
        """,
        "CREATE INDEX IF NOT EXISTS IX_CycleChallengeRecord_Lookup ON CycleChallengeRecord (Uid, Mode);",
        $"PRAGMA user_version = {CurrentSchemaVersion};",
    ];


    public void Upsert(CycleChallengeRecordEntity record)
    {
        EnsureInitialized();
        lock (_initLock)
        {
            using var db = CreateContext();
            var existing = db.CycleChallengeRecords.Find(record.Uid, record.Mode, record.ScheduleId);
            if (existing is null)
            {
                db.CycleChallengeRecords.Add(record);
            }
            else
            {
                db.Entry(existing).CurrentValues.SetValues(record);
            }

            db.SaveChanges();
        }
    }

    public IReadOnlyList<ChallengePeriod> GetPeriods(string uid, ChallengeMode mode)
    {
        EnsureInitialized();
        using var db = CreateContext();
        var modeValue = (int)mode;
        var rows = db.CycleChallengeRecords
            .AsNoTracking()
            .Where(x => x.Uid == uid && x.Mode == modeValue)
            .Select(x => new { x.ScheduleId, x.StartUtc, x.EndUtc, x.Summary, x.FetchedAt })
            .ToList();

        return rows
            .OrderByDescending(x => ParseSchedule(x.ScheduleId))
            .Select(x => new ChallengePeriod(
                uid,
                mode,
                x.ScheduleId,
                x.StartUtc,
                x.EndUtc,
                x.Summary,
                DateTimeOffset.TryParse(x.FetchedAt, out var fetched) ? fetched : DateTimeOffset.MinValue))
            .ToList();
    }

    public CycleChallengeRecordEntity? Get(string uid, ChallengeMode mode, string scheduleId)
    {
        EnsureInitialized();
        using var db = CreateContext();
        var modeValue = (int)mode;
        return db.CycleChallengeRecords
            .AsNoTracking()
            .FirstOrDefault(x => x.Uid == uid && x.Mode == modeValue && x.ScheduleId == scheduleId);
    }

    public CycleChallengeRecordEntity? GetLatest(string uid, ChallengeMode mode)
    {
        var periods = GetPeriods(uid, mode);
        if (periods.Count == 0)
        {
            return null;
        }

        return Get(uid, mode, periods[0].ScheduleId);
    }

    public int Count(string uid, ChallengeMode mode)
    {
        EnsureInitialized();
        using var db = CreateContext();
        var modeValue = (int)mode;
        return db.CycleChallengeRecords.Count(x => x.Uid == uid && x.Mode == modeValue);
    }

    public IReadOnlyList<string> GetAvailableUids()
    {
        EnsureInitialized();
        using var db = CreateContext();
        return db.CycleChallengeRecords
            .AsNoTracking()
            .Select(x => x.Uid)
            .Distinct()
            .ToList()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<(string Uid, int Mode, int Count)> GetUidModeCounts()
    {
        EnsureInitialized();
        using var db = CreateContext();
        return db.CycleChallengeRecords
            .AsNoTracking()
            .GroupBy(x => new { x.Uid, x.Mode })
            .Select(g => new { g.Key.Uid, g.Key.Mode, Count = g.Count() })
            .ToList()
            .Select(x => (x.Uid, x.Mode, x.Count))
            .OrderBy(x => x.Uid, StringComparer.Ordinal)
            .ThenBy(x => x.Mode)
            .ToList();
    }

    public IReadOnlyList<CycleChallengeRecordEntity> GetRecords(IReadOnlyCollection<string>? uids = null)
    {
        EnsureInitialized();
        using var db = CreateContext();
        IQueryable<CycleChallengeRecordEntity> query = db.CycleChallengeRecords.AsNoTracking();
        if (uids is { Count: > 0 })
        {
            var filter = uids
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Select(u => u.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (filter.Count == 0)
            {
                return Array.Empty<CycleChallengeRecordEntity>();
            }

            query = query.Where(x => filter.Contains(x.Uid));
        }

        return query
            .ToList()
            .OrderBy(x => x.Uid, StringComparer.Ordinal)
            .ThenBy(x => x.Mode)
            .ThenByDescending(x => ParseSchedule(x.ScheduleId))
            .ThenBy(x => x.ScheduleId, StringComparer.Ordinal)
            .ToList();
    }

    public void UpsertRange(IEnumerable<CycleChallengeRecordEntity> records)
    {
        var list = records as IList<CycleChallengeRecordEntity> ?? records.ToList();
        if (list.Count == 0)
        {
            return;
        }

        EnsureInitialized();
        lock (_initLock)
        {
            using var db = CreateContext();
            var existingMap = db.CycleChallengeRecords
                .ToList()
                .ToDictionary(x => (x.Uid, x.Mode, x.ScheduleId));

            foreach (var record in list)
            {
                var key = (record.Uid, record.Mode, record.ScheduleId);
                if (existingMap.TryGetValue(key, out var existing))
                {
                    db.Entry(existing).CurrentValues.SetValues(record);
                }
                else
                {
                    db.CycleChallengeRecords.Add(record);
                    existingMap[key] = record;
                }
            }

            db.SaveChanges();
        }
    }


    public int Trim(string uid, ChallengeMode mode, int keep)
    {
        var periods = GetPeriods(uid, mode);
        if (periods.Count <= keep)
        {
            return 0;
        }

        var remove = periods.Skip(keep).Select(x => x.ScheduleId).ToHashSet(StringComparer.Ordinal);
        lock (_initLock)
        {
            using var db = CreateContext();
            var modeValue = (int)mode;
            var rows = db.CycleChallengeRecords
                .Where(x => x.Uid == uid && x.Mode == modeValue && remove.Contains(x.ScheduleId))
                .ToList();
            db.CycleChallengeRecords.RemoveRange(rows);
            db.SaveChanges();
            return rows.Count;
        }
    }

    private static long ParseSchedule(string scheduleId) =>
        long.TryParse(scheduleId, out var value) ? value : long.MinValue;
}