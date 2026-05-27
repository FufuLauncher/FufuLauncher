using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Models.UIGF;
using FufuLauncher.Services;
using Microsoft.Data.Sqlite;

namespace FufuLauncher.ViewModels;

public class LocalGachaData
{
    public string Url { get; set; }
    public List<GachaLogItem> CharacterLogs { get; set; } = new();
    public List<GachaLogItem> WeaponLogs { get; set; } = new();
    public List<GachaLogItem> StandardLogs { get; set; } = new();
}

public partial class GachaAnalysisModel : ObservableObject
{
    private readonly string _gachaDataPath;
    private readonly string _dbConnectionString;
    private readonly GachaService _gachaService;
    private readonly ILocalSettingsService _localSettingsService;
    private const string LastSelectedUidKey = "GachaLastSelectedUid";
    private static readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });

    static GachaAnalysisModel()
    {
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://webstatic.mihoyo.com");
        }
    }

    private List<GachaLogItem> _cachedCharacterLogs = new();
    private List<GachaLogItem> _cachedWeaponLogs = new();
    private List<GachaLogItem> _cachedStandardLogs = new();
    private List<ScrapedMetadata> _savedMetadata = new();
    private string _currentUid = "";
    private int _refreshVersion;

    [ObservableProperty] private string _gachaUrl;
    [ObservableProperty] private string _crawlerStatus = "等待获取数据...";
    [ObservableProperty] private bool _isFetching;
    [ObservableProperty] private bool _isScraping;
    [ObservableProperty] private GachaStatistic _characterStats = new() { PoolName = "角色活动" };
    [ObservableProperty] private GachaStatistic _weaponStats = new() { PoolName = "武器活动" };
    [ObservableProperty] private GachaStatistic _standardStats = new() { PoolName = "常驻祈愿" };

    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _characterFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _weaponFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _standardFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _characterFourStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _weaponFourStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _standardFourStars = new();
    
    [ObservableProperty] private ObservableCollection<ScrapedMetadata> _allMetadataPreview = new();
    [ObservableProperty] private ObservableCollection<string> _knownUids = new();
    [ObservableProperty] private ObservableCollection<string> _uidComboItems = new();
    [ObservableProperty] private string _selectedUid = "";

    // 添加四星视图的控制开关
    [ObservableProperty] private bool _isCharacterFourStarVisible;
    [ObservableProperty] private bool _isWeaponFourStarVisible;
    [ObservableProperty] private bool _isStandardFourStarVisible;

    public const string AddNewUserItem = "＋ 添加新用户";
    [ObservableProperty] private bool _hasGachaData;
    [ObservableProperty] private bool _isDataLoaded;

    public Action RequestMetadataScrapeAction;
    public Action<string> OnErrorAction;
    public Func<IntPtr> GetWindowHandle;
    public Func<string, string, Task<bool>> OnUidMismatchAsync;

    public GachaAnalysisModel(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;

        _gachaDataPath = Helpers.AppPaths.GachaDataFile;
        _dbConnectionString = $"Data Source={Helpers.AppPaths.MetadataDb}";
        _gachaService = new GachaService();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Metadata (
                Name TEXT PRIMARY KEY,
                ImgSrc TEXT,
                ElementSrc TEXT,
                Type TEXT
            );
            CREATE TABLE IF NOT EXISTS GachaLogs (
                Id TEXT NOT NULL,
                Uid TEXT NOT NULL,
                GachaType TEXT NOT NULL,
                ItemId TEXT,
                Count TEXT,
                Time TEXT,
                Name TEXT,
                Lang TEXT,
                ItemType TEXT,
                RankType TEXT,
                PRIMARY KEY (Id, Uid)
            );
        ";
        command.ExecuteNonQuery();

        try
        {
            using var checkConn = new SqliteConnection(_dbConnectionString);
            checkConn.Open();
            var checkCmd = checkConn.CreateCommand();
            checkCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='GachaLogs'";
            var createSql = checkCmd.ExecuteScalar() as string ?? "";
            if (createSql.Contains("Id TEXT PRIMARY KEY") && !createSql.Contains("PRIMARY KEY (Id, Uid)"))
            {
                Debug.WriteLine("[Gacha] 迁移 GachaLogs 表：主键从 Id 改为 (Id, Uid)");
                using var migrateConn = new SqliteConnection(_dbConnectionString);
                migrateConn.Open();
                using var transaction = migrateConn.BeginTransaction();
                var migrateCmd = migrateConn.CreateCommand();
                migrateCmd.CommandText = @"
                    CREATE TABLE GachaLogs_new (
                        Id TEXT NOT NULL,
                        Uid TEXT NOT NULL,
                        GachaType TEXT NOT NULL,
                        ItemId TEXT,
                        Count TEXT,
                        Time TEXT,
                        Name TEXT,
                        Lang TEXT,
                        ItemType TEXT,
                        RankType TEXT,
                        PRIMARY KEY (Id, Uid)
                    );
                    INSERT OR IGNORE INTO GachaLogs_new SELECT * FROM GachaLogs;
                    DROP TABLE GachaLogs;
                    ALTER TABLE GachaLogs_new RENAME TO GachaLogs;
                ";
                migrateCmd.ExecuteNonQuery();
                transaction.Commit();
                Debug.WriteLine("[Gacha] GachaLogs 表迁移完成");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 表迁移检查失败: {ex.Message}");
        }

        command.CommandText = "CREATE INDEX IF NOT EXISTS idx_gacha_uid ON GachaLogs(Uid);";
        command.ExecuteNonQuery();

        static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string type)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(1) == column) return;
            cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
            cmd.ExecuteNonQuery();
        }
        AddColumnIfMissing(connection, "Metadata", "Rank", "TEXT");
        AddColumnIfMissing(connection, "Metadata", "ItemId", "TEXT");
    }

    private void LoadMetadataFromDb()
    {
        _savedMetadata.Clear();
        App.MainWindow.DispatcherQueue.TryEnqueue(() => AllMetadataPreview.Clear());

        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, ImgSrc, ElementSrc, Type, Rank, ItemId FROM Metadata";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var imgSrc = reader.IsDBNull(1) ? null : reader.GetString(1);
            var elementSrc = reader.IsDBNull(2) ? null : reader.GetString(2);

            var item = new ScrapedMetadata
            {
                Name = reader.GetString(0),
                ImgSrc = string.IsNullOrWhiteSpace(imgSrc) ? null : imgSrc,
                ElementSrc = string.IsNullOrWhiteSpace(elementSrc) ? null : elementSrc,
                Type = reader.IsDBNull(3) ? null : reader.GetString(3)
            };
            if (!reader.IsDBNull(4)) item.Rank = reader.GetString(4);
            if (!reader.IsDBNull(5)) item.ItemId = reader.GetString(5);
            _savedMetadata.Add(item);
            App.MainWindow.DispatcherQueue.TryEnqueue(() => AllMetadataPreview.Add(item));
        }
    }

    private void SaveMetadataToDb(List<ScrapedMetadata> newItems)
    {
        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Metadata (Name, ImgSrc, ElementSrc, Type, Rank, ItemId)
            VALUES ($name, $imgSrc, $elementSrc, $type, $rank, $itemId)
            ON CONFLICT(Name) DO UPDATE SET
                ImgSrc=excluded.ImgSrc,
                ElementSrc=excluded.ElementSrc,
                Type=excluded.Type,
                Rank=excluded.Rank,
                ItemId=excluded.ItemId;
        ";

        var nameParam = command.CreateParameter(); nameParam.ParameterName = "$name"; command.Parameters.Add(nameParam);
        var imgParam = command.CreateParameter(); imgParam.ParameterName = "$imgSrc"; command.Parameters.Add(imgParam);
        var eleParam = command.CreateParameter(); eleParam.ParameterName = "$elementSrc"; command.Parameters.Add(eleParam);
        var typeParam = command.CreateParameter(); typeParam.ParameterName = "$type"; command.Parameters.Add(typeParam);
        var rankParam = command.CreateParameter(); rankParam.ParameterName = "$rank"; command.Parameters.Add(rankParam);
        var itemIdParam = command.CreateParameter(); itemIdParam.ParameterName = "$itemId"; command.Parameters.Add(itemIdParam);

        foreach (var item in newItems)
        {
            nameParam.Value = item.Name ?? "";
            imgParam.Value = item.ImgSrc ?? "";
            eleParam.Value = item.ElementSrc ?? "";
            typeParam.Value = item.Type ?? "";
            rankParam.Value = item.Rank ?? "";
            itemIdParam.Value = item.ItemId ?? "";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private List<string> QueryKnownUidsFromDb()
    {
        var uids = new List<string>();
        try
        {
            using var connection = new SqliteConnection(_dbConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT Uid FROM GachaLogs ORDER BY Uid";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                uids.Add(reader.GetString(0));
        }
        catch { }
        return uids;
    }

    private void RefreshKnownUids()
    {
        var uids = QueryKnownUidsFromDb();
        var current = _currentUid;
        Debug.WriteLine($"[Gacha] RefreshKnownUids: 查询到 {uids.Count} 个 UID: [{string.Join(", ", uids)}], current={current}");
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            RefreshKnownUidsUI(uids);
            if (!string.IsNullOrEmpty(current) && UidComboItems.Contains(current))
                SelectedUid = current;
        });
    }

    private void RefreshKnownUidsUI(List<string> uids)
    {
        KnownUids.Clear();
        UidComboItems.Clear();
        foreach (var uid in uids)
        {
            KnownUids.Add(uid);
            UidComboItems.Add(uid);
        }
        UidComboItems.Add(AddNewUserItem);
    }

    private void LoadGachaLogsFromDb(string uid)
    {
        _cachedCharacterLogs.Clear();
        _cachedWeaponLogs.Clear();
        _cachedStandardLogs.Clear();

        if (string.IsNullOrEmpty(uid)) return;

        try
        {
            using var connection = new SqliteConnection(_dbConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, GachaType, ItemId, Count, Time, Name, Lang, ItemType, RankType FROM GachaLogs WHERE Uid = $uid";
            command.Parameters.AddWithValue("$uid", uid);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var item = new GachaLogItem
                {
                    Id = reader.GetString(0),
                    Uid = uid,
                    GachaType = reader.GetString(1),
                    ItemId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Count = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Time = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Name = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Lang = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ItemType = reader.IsDBNull(7) ? null : reader.GetString(7),
                    RankType = reader.IsDBNull(8) ? null : reader.GetString(8)
                };
                var gt = item.GachaType;
                if (gt == "301") _cachedCharacterLogs.Add(item);
                else if (gt == "302") _cachedWeaponLogs.Add(item);
                else _cachedStandardLogs.Add(item);
            }
            Debug.WriteLine($"[Gacha] 加载完成 UID={uid}: 角色{_cachedCharacterLogs.Count} 武器{_cachedWeaponLogs.Count} 常驻{_cachedStandardLogs.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 加载抽卡数据失败: {ex.Message}");
        }
    }

    private void SaveGachaLogsToDb()
    {
        if (string.IsNullOrEmpty(_currentUid)) { Debug.WriteLine("[Gacha] SaveGachaLogsToDb: _currentUid 为空，跳过保存"); return; }
        try
        {
            var totalBefore = _cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedStandardLogs.Count;
            Debug.WriteLine($"[Gacha] SaveGachaLogsToDb: 开始保存 UID={_currentUid}, 共 {totalBefore} 条记录");
            using var connection = new SqliteConnection(_dbConnectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var command = connection.CreateCommand();

            command.CommandText = "DELETE FROM GachaLogs WHERE Uid = $uid";
            command.Parameters.AddWithValue("$uid", _currentUid);
            command.ExecuteNonQuery();

            command.CommandText = @"
                INSERT INTO GachaLogs (Id, Uid, GachaType, ItemId, Count, Time, Name, Lang, ItemType, RankType)
                VALUES ($id, $uid, $gachaType, $itemId, $count, $time, $name, $lang, $itemType, $rankType)";
            command.Parameters.Clear();
            var pId = command.CreateParameter(); pId.ParameterName = "$id"; command.Parameters.Add(pId);
            var pUid = command.CreateParameter(); pUid.ParameterName = "$uid"; pUid.Value = _currentUid; command.Parameters.Add(pUid);
            var pGt = command.CreateParameter(); pGt.ParameterName = "$gachaType"; command.Parameters.Add(pGt);
            var pIt = command.CreateParameter(); pIt.ParameterName = "$itemId"; command.Parameters.Add(pIt);
            var pCt = command.CreateParameter(); pCt.ParameterName = "$count"; command.Parameters.Add(pCt);
            var pTm = command.CreateParameter(); pTm.ParameterName = "$time"; command.Parameters.Add(pTm);
            var pNm = command.CreateParameter(); pNm.ParameterName = "$name"; command.Parameters.Add(pNm);
            var pLg = command.CreateParameter(); pLg.ParameterName = "$lang"; command.Parameters.Add(pLg);
            var pTp = command.CreateParameter(); pTp.ParameterName = "$itemType"; command.Parameters.Add(pTp);
            var pRk = command.CreateParameter(); pRk.ParameterName = "$rankType"; command.Parameters.Add(pRk);

            void InsertItems(List<GachaLogItem> items)
            {
                foreach (var item in items)
                {
                    pId.Value = item.Id ?? "";
                    pGt.Value = item.GachaType ?? "";
                    pIt.Value = (object?)item.ItemId ?? DBNull.Value;
                    pCt.Value = (object?)item.Count ?? DBNull.Value;
                    pTm.Value = (object?)item.Time ?? DBNull.Value;
                    pNm.Value = (object?)item.Name ?? DBNull.Value;
                    pLg.Value = (object?)item.Lang ?? DBNull.Value;
                    pTp.Value = (object?)item.ItemType ?? DBNull.Value;
                    pRk.Value = (object?)item.RankType ?? DBNull.Value;
                    command.ExecuteNonQuery();
                }
            }

            InsertItems(_cachedCharacterLogs);
            InsertItems(_cachedWeaponLogs);
            InsertItems(_cachedStandardLogs);
            transaction.Commit();
            Debug.WriteLine($"[Gacha] 保存完成 UID={_currentUid}: 角色{_cachedCharacterLogs.Count} 武器{_cachedWeaponLogs.Count} 常驻{_cachedStandardLogs.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 保存抽卡数据失败: {ex.Message}");
        }
    }

    private async Task SwitchToUidAsync(string uid)
    {
        if (_currentUid == uid) return;
        if (!string.IsNullOrEmpty(_currentUid))
            SaveGachaLogsToDb();

        _currentUid = uid;
        LoadGachaLogsFromDb(uid);

        _ = _localSettingsService.SaveSettingAsync(LastSelectedUidKey, uid);

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            SelectedUid = uid;
            if (_cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedStandardLogs.Count > 0)
            {
                RefreshUIFromCache();
                HasGachaData = true;
                CrawlerStatus = $"已切换到 UID: {uid}";
            }
            else
            {
                ClearCollections();
                CharacterStats = new GachaStatistic { PoolName = "角色活动" };
                WeaponStats = new GachaStatistic { PoolName = "武器活动" };
                StandardStats = new GachaStatistic { PoolName = "常驻祈愿" };
                HasGachaData = false;
                CrawlerStatus = "该账号暂无抽卡记录";
            }
        });
    }

    private async Task<bool> HandleUidMismatchAsync(string incomingUid)
    {
        if (string.IsNullOrEmpty(incomingUid)) return true;
        if (string.IsNullOrEmpty(_currentUid)) return true;
        if (_currentUid == incomingUid) return true;

        if (OnUidMismatchAsync != null)
        {
            var accepted = await OnUidMismatchAsync(_currentUid, incomingUid);
            if (accepted)
            {
                await SwitchToUidAsync(incomingUid);
                return true;
            }
            return false;
        }
        return false;
    }

    public async Task ClearGachaDataAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_currentUid))
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage("删除失败", "当前没有选中任何账号", NotificationType.Error, 3000));
                return;
            }

            var deletedUid = _currentUid;

            using var connection = new SqliteConnection(_dbConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM GachaLogs WHERE Uid = $uid";
            command.Parameters.AddWithValue("$uid", deletedUid);
            command.ExecuteNonQuery();

            var remainingUids = QueryKnownUidsFromDb();

            if (remainingUids.Count > 0)
            {
                var switchToUid = remainingUids[0];
                _currentUid = switchToUid;
                LoadGachaLogsFromDb(switchToUid);

                _ = _localSettingsService.SaveSettingAsync(LastSelectedUidKey, switchToUid);

                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    RefreshKnownUidsUI(remainingUids);
                    SelectedUid = switchToUid;
                    RefreshUIFromCache();
                    HasGachaData = true;
                    CrawlerStatus = $"已删除 UID: {deletedUid} 的记录，已切换到 UID: {switchToUid}";
                });
            }
            else
            {
                _currentUid = "";
                _cachedCharacterLogs.Clear();
                _cachedWeaponLogs.Clear();
                _cachedStandardLogs.Clear();

                _ = _localSettingsService.SaveSettingAsync(LastSelectedUidKey, "");

                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    RefreshKnownUidsUI(remainingUids);
                    ClearCollections();
                    CharacterStats = new GachaStatistic { PoolName = "角色活动" };
                    WeaponStats = new GachaStatistic { PoolName = "武器活动" };
                    StandardStats = new GachaStatistic { PoolName = "常驻祈愿" };
                    GachaUrl = string.Empty;
                    HasGachaData = false;
                    SelectedUid = "";
                    CrawlerStatus = "数据已清空";
                });
            }

            WeakReferenceMessenger.Default.Send(new NotificationMessage("删除成功", $"已删除 UID: {deletedUid} 的抽卡记录", NotificationType.Success, 3000));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("删除失败", $"详细信息: {ex.Message}", NotificationType.Error, 5000));
        }
    }

    public async Task LoadSavedGachaDataAsync()
    {
        Debug.WriteLine("[Gacha] LoadSavedGachaDataAsync: 开始加载数据");
        var (uids, metadataCount) = await Task.Run(() =>
        {
            InitializeDatabase();
            LoadMetadataFromDb();

            if (File.Exists(_gachaDataPath))
            {
                try
                {
                    var json = File.ReadAllText(_gachaDataPath);
                    var data = JsonSerializer.Deserialize<LocalGachaData>(json);
                    if (data != null)
                    {
                        GachaUrl = data.Url;
                        var allLogs = (data.CharacterLogs ?? new())
                            .Concat(data.WeaponLogs ?? new())
                            .Concat(data.StandardLogs ?? new()).ToList();
                        if (allLogs.Count > 0)
                        {
                            MigrateJsonToDb(allLogs);
                            File.Move(_gachaDataPath, _gachaDataPath + ".bak");
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[Gacha] JSON 迁移失败: {ex.Message}"); }
            }

            var uids = QueryKnownUidsFromDb();
            Debug.WriteLine($"[Gacha] QueryKnownUids 返回 {uids.Count} 个 UID: [{string.Join(", ", uids)}]");

            string lastUid = "";
            try
            {
                var lastUidObj = _localSettingsService.ReadSettingAsync(LastSelectedUidKey).GetAwaiter().GetResult();
                lastUid = lastUidObj as string ?? "";
            }
            catch { }

            if (uids.Count > 0)
            {
                if (!string.IsNullOrEmpty(lastUid) && uids.Contains(lastUid))
                    _currentUid = lastUid;
                else
                    _currentUid = uids[0];
                LoadGachaLogsFromDb(_currentUid);
                _ = _localSettingsService.SaveSettingAsync(LastSelectedUidKey, _currentUid);
            }
            return (uids, _savedMetadata.Count);
        });

        Debug.WriteLine($"[Gacha] 加载完成 - {uids.Count} UIDs, metadata {metadataCount} 条");
        if (uids.Count > 0)
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                KnownUids.Clear();
                UidComboItems.Clear();
                foreach (var uid in uids)
                {
                    KnownUids.Add(uid);
                    UidComboItems.Add(uid);
                }
                UidComboItems.Add(AddNewUserItem);
                SelectedUid = _currentUid;
                RefreshUIFromCache();
                HasGachaData = true;
                IsDataLoaded = true;
                CrawlerStatus = metadataCount > 0 ? "已加载本地数据和图片资源缓存" : "已加载本地历史记录";
            });
        }
        else
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                UidComboItems.Clear();
                UidComboItems.Add(AddNewUserItem);
                IsDataLoaded = true;
            });
        }
    }

    private void MigrateJsonToDb(List<GachaLogItem> logs)
    {
        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT OR IGNORE INTO GachaLogs (Id, Uid, GachaType, ItemId, Count, Time, Name, Lang, ItemType, RankType)
            VALUES ($id, $uid, $gachaType, $itemId, $count, $time, $name, $lang, $itemType, $rankType)";
        var pId = command.CreateParameter(); pId.ParameterName = "$id"; command.Parameters.Add(pId);
        var pUid = command.CreateParameter(); pUid.ParameterName = "$uid"; command.Parameters.Add(pUid);
        var pGt = command.CreateParameter(); pGt.ParameterName = "$gachaType"; command.Parameters.Add(pGt);
        var pIt = command.CreateParameter(); pIt.ParameterName = "$itemId"; command.Parameters.Add(pIt);
        var pCt = command.CreateParameter(); pCt.ParameterName = "$count"; command.Parameters.Add(pCt);
        var pTm = command.CreateParameter(); pTm.ParameterName = "$time"; command.Parameters.Add(pTm);
        var pNm = command.CreateParameter(); pNm.ParameterName = "$name"; command.Parameters.Add(pNm);
        var pLg = command.CreateParameter(); pLg.ParameterName = "$lang"; command.Parameters.Add(pLg);
        var pTp = command.CreateParameter(); pTp.ParameterName = "$itemType"; command.Parameters.Add(pTp);
        var pRk = command.CreateParameter(); pRk.ParameterName = "$rankType"; command.Parameters.Add(pRk);
        foreach (var item in logs)
        {
            pId.Value = item.Id ?? "";
            pUid.Value = item.Uid ?? "unknown";
            pGt.Value = item.GachaType ?? "";
            pIt.Value = (object?)item.ItemId ?? DBNull.Value;
            pCt.Value = (object?)item.Count ?? DBNull.Value;
            pTm.Value = (object?)item.Time ?? DBNull.Value;
            pNm.Value = (object?)item.Name ?? DBNull.Value;
            pLg.Value = (object?)item.Lang ?? DBNull.Value;
            pTp.Value = (object?)item.ItemType ?? DBNull.Value;
            pRk.Value = (object?)item.RankType ?? DBNull.Value;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
        Debug.WriteLine($"[Gacha] 已从 JSON 迁移 {logs.Count} 条记录到数据库");
    }

    private void SaveGachaDataAsync()
    {
        Debug.WriteLine($"[Gacha] SaveGachaDataAsync: 开始, _currentUid={_currentUid}");
        SaveGachaLogsToDb();
        RefreshKnownUids();
        Debug.WriteLine("[Gacha] SaveGachaDataAsync: 完成");
    }

    private List<GachaLogItem> MergeLogs(List<GachaLogItem> existing, List<GachaLogItem> incoming)
    {
        if (existing == null) existing = new List<GachaLogItem>();
        if (incoming == null || incoming.Count == 0) return existing;

        var dict = existing.ToDictionary(x => x.Id);
        foreach (var item in incoming)
        {
            if (!dict.ContainsKey(item.Id)) dict[item.Id] = item;
        }
        return dict.Values.OrderBy(x => x.Id).ToList();
    }

    private void RefreshUIFromCache()
    {
        var charLogs = _cachedCharacterLogs.OrderBy(x => x.Id).ToList();
        var weaponLogs = _cachedWeaponLogs.OrderBy(x => x.Id).ToList();
        var standardLogs = _cachedStandardLogs.OrderBy(x => x.Id).ToList();

        var version = ++_refreshVersion;

        _ = Task.Run(() =>
        {
            var charStats = _gachaService.AnalyzePool("301", charLogs);
            var weaponStats = _gachaService.AnalyzePool("302", weaponLogs);
            var standardStats = _gachaService.AnalyzePool("200", standardLogs);

            var charFive = BuildDisplayCollection(charStats.FiveStarRecords, "角色");
            var charFour = BuildDisplayCollection(charStats.FourStarRecords, "角色");
            var weaponFive = BuildDisplayCollection(weaponStats.FiveStarRecords, "武器");
            var weaponFour = BuildDisplayCollection(weaponStats.FourStarRecords, "武器");
            var standardFive = BuildDisplayCollection(standardStats.FiveStarRecords, "常驻");
            var standardFour = BuildDisplayCollection(standardStats.FourStarRecords, "常驻");

            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (_refreshVersion != version) return;

                CharacterStats = charStats;
                WeaponStats = weaponStats;
                StandardStats = standardStats;
                CharacterFiveStars = charFive;
                CharacterFourStars = charFour;
                WeaponFiveStars = weaponFive;
                WeaponFourStars = weaponFour;
                StandardFiveStars = standardFive;
                StandardFourStars = standardFour;

                if (_savedMetadata != null && _savedMetadata.Count > 0) _ = ApplyMetadataToUIAsync(_savedMetadata);
            });
        });
    }

    [RelayCommand]
    private async Task SwitchUidAsync(string uid)
    {
        if (string.IsNullOrEmpty(uid) || uid == _currentUid) return;
        await SwitchToUidAsync(uid);
    }

    [RelayCommand]
    private async Task AddNewUserAsync()
    {
        if (!string.IsNullOrEmpty(_currentUid))
            SaveGachaLogsToDb();

        _currentUid = "";
        _cachedCharacterLogs.Clear();
        _cachedWeaponLogs.Clear();
        _cachedStandardLogs.Clear();

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            SelectedUid = "";
            ClearCollections();
            CharacterStats = new GachaStatistic { PoolName = "角色活动" };
            WeaponStats = new GachaStatistic { PoolName = "武器活动" };
            StandardStats = new GachaStatistic { PoolName = "常驻祈愿" };
            HasGachaData = false;
            CrawlerStatus = "等待获取数据...";
        });
    }

    [RelayCommand]
    private void PreFetchMetadata()
    {
        if (IsScraping) return;
        IsScraping = true;
        CrawlerStatus = "正在预爬取全部图片资源...";
        RequestMetadataScrapeAction?.Invoke();
    }

    [RelayCommand]
    private async Task FetchFromMiYouSheAsync()
    {
        var configPath = Helpers.AppPaths.ConfigFile;

        if (!File.Exists(configPath))
        {
            CrawlerStatus = "未找到登录配置，请先登录米游社账号";
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        string stoken, mid, stuid, gameUid;
        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            using var doc = JsonDocument.Parse(json);
            var account = doc.RootElement.GetProperty("Account");
            stoken = account.TryGetProperty("Stoken", out var st) ? st.GetString() ?? "" : "";
            mid = account.TryGetProperty("Mid", out var mi) ? mi.GetString() ?? "" : "";
            stuid = account.TryGetProperty("Stuid", out var si) ? si.GetString() ?? "" : "";

            var userConfigService = App.GetService<Services.IUserConfigService>();
            var displayConfig = await userConfigService.LoadDisplayConfigAsync();
            gameUid = displayConfig.GameUid ?? stuid;
        }
        catch
        {
            CrawlerStatus = "读取登录配置失败";
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        if (string.IsNullOrEmpty(stoken))
        {
            CrawlerStatus = "stoken 为空，请重新登录米游社账号";
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        if (!await HandleUidMismatchAsync(gameUid)) { IsFetching = false; return; }

        _currentUid = gameUid;

        IsFetching = true;
        CrawlerStatus = "正在生成认证密钥...";

        try
        {
            var authkey = await _gachaService.GenerateAuthKeyAsync(stoken, mid, stuid, gameUid);

            if (string.IsNullOrEmpty(authkey))
            {
                CrawlerStatus = "认证密钥生成失败，请重新登录后重试";
                IsFetching = false;
                OnErrorAction?.Invoke(CrawlerStatus);
                return;
            }

            var baseUrl = $"https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey={Uri.EscapeDataString(authkey)}&authkey_ver=1&sign_type=2&game=hk4e&lang=zh-cn";

            void OnProgress(string pool, int count) =>
                App.MainWindow.DispatcherQueue.TryEnqueue(() => CrawlerStatus = $"正在获取{pool}记录... (已获取 {count} 条)");

            CrawlerStatus = "正在获取角色活动记录...";
            var charLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "301", count => OnProgress("角色活动", count));
            foreach (var l in charLogs) l.Uid = gameUid;
            _cachedCharacterLogs = MergeLogs(_cachedCharacterLogs, charLogs);

            CrawlerStatus = $"角色活动 {charLogs.Count} 条，正在获取武器活动记录...";
            var weaponLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "302", count => OnProgress("武器活动", count));
            foreach (var l in weaponLogs) l.Uid = gameUid;
            _cachedWeaponLogs = MergeLogs(_cachedWeaponLogs, weaponLogs);

            CrawlerStatus = $"武器活动 {weaponLogs.Count} 条，正在获取常驻祈愿记录...";
            var standardLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "200", count => OnProgress("常驻祈愿", count));
            foreach (var l in standardLogs) l.Uid = gameUid;
            _cachedStandardLogs = MergeLogs(_cachedStandardLogs, standardLogs);

            var total = charLogs.Count + weaponLogs.Count + standardLogs.Count;
            CrawlerStatus = $"获取完成，共 {total} 条记录，正在检查图片资源...";

            RefreshUIFromCache();
            HasGachaData = true;
            SaveGachaDataAsync();

            IsScraping = true;
            RequestMetadataScrapeAction?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 获取异常: {ex}");
            CrawlerStatus = $"获取失败: {ex.Message}";
            IsFetching = false;
            OnErrorAction?.Invoke(CrawlerStatus);
        }

        if (!IsScraping) IsFetching = false;
    }

    [RelayCommand]
    private async Task ExportUigfAsync()
    {
        try
        {
            var allLogs = _cachedCharacterLogs.Concat(_cachedWeaponLogs).Concat(_cachedStandardLogs).ToList();
            if (allLogs.Count == 0)
            {
                OnErrorAction?.Invoke("没有可导出的抽卡记录");
                return;
            }

            var uid = _currentUid;
            if (string.IsNullOrEmpty(uid)) uid = "unknown";

            var uigf = new UIGFJson
            {
                Info = new UIGFInfo
                {
                    ExportTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ExportAppVersion = $"{System.Reflection.Assembly.GetEntryAssembly().GetName().Version}"
                },
                Hk4e = new List<UIGFEntry>
                {
                    new()
                    {
                        Uid = uid,
                        List = allLogs.Select(log => new UIGFItem
                        {
                            UigfGachaType = GameToUigfGachaType(log.GachaType),
                            GachaType = log.GachaType,
                            ItemId = log.ItemId,
                            Time = log.Time,
                            Id = log.Id
                        }).ToList()
                    }
                }
            };

            var json = JsonSerializer.Serialize(uigf, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            var hwnd = GetWindowHandle?.Invoke() ?? IntPtr.Zero;
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            if (hwnd != IntPtr.Zero) WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("JSON 文件", new List<string> { ".json" });
            savePicker.SuggestedFileName = $"UIGF_{uid}_{DateTimeOffset.UtcNow:yyyyMMdd}";

            var file = await savePicker.PickSaveFileAsync();
            if (file == null) return;

            await System.IO.File.WriteAllTextAsync(file.Path, json);
            WeakReferenceMessenger.Default.Send(new NotificationMessage("导出成功", $"已导出 {allLogs.Count} 条记录到 {file.Name}", NotificationType.Success, 3000));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 导出失败: {ex}");
            CrawlerStatus = $"导出失败: {ex.Message}";
            OnErrorAction?.Invoke(CrawlerStatus);
        }
    }

    [RelayCommand]
    private async Task ImportUigfAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = GetWindowHandle?.Invoke() ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero) WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".json");

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            IsFetching = true;
            CrawlerStatus = "正在读取 UIGF 文件...";

            var json = await System.IO.File.ReadAllTextAsync(file.Path);
            var uigf = JsonSerializer.Deserialize<UIGFJson>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var version = uigf?.Info?.Version ?? "";
            if (string.IsNullOrEmpty(version) || !version.StartsWith("v4"))
            {
                CrawlerStatus = string.IsNullOrEmpty(version)
                    ? "无法识别 UIGF 版本，请确认文件格式正确"
                    : $"不支持的 UIGF 版本：{version}，仅支持 v4.x";
                IsFetching = false;
                OnErrorAction?.Invoke(CrawlerStatus);
                return;
            }

            if (uigf?.Hk4e == null || uigf.Hk4e.Count == 0)
            {
                CrawlerStatus = "文件中未找到有效的抽卡记录";
                IsFetching = false;
                OnErrorAction?.Invoke(CrawlerStatus);
                return;
            }

            var entry = uigf.Hk4e[0];
            var entryLang = entry.Lang ?? "zh-cn";

            if (entry.List == null || entry.List.Count == 0)
            {
                CrawlerStatus = "文件中未找到抽卡记录";
                IsFetching = false;
                OnErrorAction?.Invoke(CrawlerStatus);
                return;
            }

            foreach (var x in entry.List)
            {
                if (string.IsNullOrEmpty(x.Id) || string.IsNullOrEmpty(x.ItemId)
                    || string.IsNullOrEmpty(x.Time) || string.IsNullOrEmpty(x.GachaType))
                {
                    CrawlerStatus = "文件中存在不完整的记录（缺少 id/item_id/time/gacha_type），请检查文件格式";
                    IsFetching = false;
                    OnErrorAction?.Invoke(CrawlerStatus);
                    return;
                }
            }

            var items = entry.List;
            var importUid = entry.Uid ?? "";
            var entryTimezone = entry.Timezone;
            if (!await HandleUidMismatchAsync(importUid)) { IsFetching = false; return; }

            _currentUid = importUid;
            Debug.WriteLine($"[Gacha] ImportUigf: 设置 _currentUid={importUid}");

            if (_savedMetadata.Count == 0)
            {
                CrawlerStatus = "正在获取物品元数据用于名称映射...";
                await FetchMetadataFromApiAsync();
                IsFetching = true;
            }

            CrawlerStatus = $"正在导入 {items.Count} 条记录...";

            var metaByItemId = _savedMetadata
                .Where(m => !string.IsNullOrEmpty(m.ItemId))
                .GroupBy(m => m.ItemId)
                .ToDictionary(g => g.Key, g => g.First());

            var newLogs = items.Select(uigfItem =>
            {
                var gachaType = !string.IsNullOrEmpty(uigfItem.UigfGachaType)
                    ? UigfToGameGachaType(uigfItem.UigfGachaType)
                    : uigfItem.GachaType;

                var name = uigfItem.Name;
                if (string.IsNullOrEmpty(name) && metaByItemId.TryGetValue(uigfItem.ItemId, out var meta))
                    name = meta.Name;
                if (string.IsNullOrEmpty(name))
                    name = uigfItem.ItemId;

                var rankType = uigfItem.RankType;
                if (string.IsNullOrEmpty(rankType) && metaByItemId.TryGetValue(uigfItem.ItemId, out meta) && !string.IsNullOrEmpty(meta.Rank))
                    rankType = meta.Rank;
                if (string.IsNullOrEmpty(rankType))
                    rankType = "3";

                var time = uigfItem.Time ?? "";
                if (entryTimezone != 8 && !string.IsNullOrEmpty(time))
                {
                    try
                    {
                        if (DateTime.TryParse(time, out var dt))
                        {
                            dt = dt.AddHours(8 - entryTimezone);
                            time = dt.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                    }
                    catch { }
                }

                return new GachaLogItem
                {
                    Id = uigfItem.Id,
                    Uid = importUid,
                    GachaType = gachaType,
                    ItemId = uigfItem.ItemId,
                    Time = time,
                    Name = name,
                    RankType = rankType,
                    ItemType = uigfItem.ItemType,
                    Lang = entryLang
                };
            }).ToList();

            _cachedCharacterLogs = MergeLogs(_cachedCharacterLogs, newLogs.Where(x => x.GachaType == "301").ToList());
            _cachedWeaponLogs = MergeLogs(_cachedWeaponLogs, newLogs.Where(x => x.GachaType == "302").ToList());
            _cachedStandardLogs = MergeLogs(_cachedStandardLogs, newLogs.Where(x => x.GachaType == "200" || x.GachaType == "100").ToList());

            RefreshUIFromCache();
            HasGachaData = true;
            SaveGachaDataAsync();

            var total = _cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedStandardLogs.Count;
            CrawlerStatus = $"导入完成，共 {total} 条记录，正在检查图片资源...";
            IsScraping = true;
            RequestMetadataScrapeAction?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 导入失败: {ex}");
            CrawlerStatus = $"导入失败: {ex.Message}";
            IsFetching = false;
            OnErrorAction?.Invoke(CrawlerStatus);
        }

        if (!IsScraping) IsFetching = false;
    }

    private static string GameToUigfGachaType(string gameType) => gameType switch
    {
        "100" => "100",
        "200" => "200",
        "301" => "301",
        "302" => "302",
        "400" => "500",
        "500" => "500",
        _ => gameType
    };

    private static string UigfToGameGachaType(string uigfType) => uigfType switch
    {
        "100" => "100",
        "200" => "200",
        "301" => "301",
        "302" => "302",
        "500" => "500",
        _ => uigfType
    };

    [RelayCommand]
    private async Task FetchGachaDataAsync()
    {
        if (string.IsNullOrWhiteSpace(GachaUrl))
        {
            CrawlerStatus = "请输入有效的抽卡链接";
            return;
        }

        IsFetching = true;
        CrawlerStatus = "正在解析 API 链接...";

        try
        {
            var baseUrl = _gachaService.ExtractBaseUrl(GachaUrl);
            if (string.IsNullOrEmpty(baseUrl))
            {
                CrawlerStatus = "链接格式错误，无法提取 API 地址";
                IsFetching = false;
                return;
            }

            void OnProgress(string pool, int count) =>
                App.MainWindow.DispatcherQueue.TryEnqueue(() => CrawlerStatus = $"正在获取{pool}记录... (已获取 {count} 条)");

            CrawlerStatus = "正在获取角色活动记录...";
            var charLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "301", count => OnProgress("角色活动", count));

            CrawlerStatus = $"角色活动 {charLogs.Count} 条，正在获取武器活动记录...";
            var weaponLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "302", count => OnProgress("武器活动", count));

            CrawlerStatus = $"武器活动 {weaponLogs.Count} 条，正在获取常驻祈愿记录...";
            var standardLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "200", count => OnProgress("常驻祈愿", count));

            var allFetched = charLogs.Concat(weaponLogs).Concat(standardLogs).ToList();
            var fetchedUid = allFetched.FirstOrDefault(l => !string.IsNullOrEmpty(l.Uid))?.Uid ?? "";

            if (!await HandleUidMismatchAsync(fetchedUid)) { IsFetching = false; return; }

            _currentUid = fetchedUid;

            _cachedCharacterLogs = MergeLogs(_cachedCharacterLogs, charLogs);
            _cachedWeaponLogs = MergeLogs(_cachedWeaponLogs, weaponLogs);
            _cachedStandardLogs = MergeLogs(_cachedStandardLogs, standardLogs);

            var total = charLogs.Count + weaponLogs.Count + standardLogs.Count;
            CrawlerStatus = $"获取完成，共 {total} 条记录，正在检查图片资源...";

            RefreshUIFromCache();
            HasGachaData = true;
            SaveGachaDataAsync();

            IsScraping = true;
            RequestMetadataScrapeAction?.Invoke();
        }
        catch (Exception ex)
        {
            CrawlerStatus = $"更新失败: {ex.Message}";
            IsFetching = false;
        }

        if (!IsScraping) IsFetching = false;
    }

    private void ClearCollections()
    {
        CharacterFiveStars = new();
        CharacterFourStars = new();
        WeaponFiveStars = new();
        WeaponFourStars = new();
        StandardFiveStars = new();
        StandardFourStars = new();
    }

    private ObservableCollection<GachaDisplayItem> BuildDisplayCollection(List<FiveStarRecord> records, string typeHint)
    {
        var items = new GachaDisplayItem[records.Count];
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            items[i] = new GachaDisplayItem
            {
                Name = record.Name,
                Count = record.PityUsed,
                Time = record.Time,
                Rank = record.Rank,
                Type = typeHint,
                ImageUrl = "ms-appx:///Assets/StoreLogo.png"
            };
        }
        return new ObservableCollection<GachaDisplayItem>(items);
    }

    public async Task FetchMetadataFromApiAsync()
    {
        IsScraping = true;
        CrawlerStatus = "正在通过 API 获取角色和武器元数据...";

        try
        {
            string? cookie = null;
            try
            {
                var configPath = Helpers.AppPaths.ConfigFile;
                if (File.Exists(configPath))
                {
                    var configJson = await File.ReadAllTextAsync(configPath);
                    using var configDoc = JsonDocument.Parse(configJson);
                    cookie = configDoc.RootElement.GetProperty("Account").TryGetProperty("Cookie", out var c) ? c.GetString() : null;
                }
            }
            catch { }

            var results = new List<ScrapedMetadata>();

            var chars = await FetchCalculatorListAsync(ApiEndpoints.CalculateAvatarListUrl,
                new { page = 1, size = 1000, is_all = true }, cookie, "char");
            results.AddRange(chars);

            var weapons = await FetchCalculatorListAsync(ApiEndpoints.CalculateWeaponListUrl,
                new { page = 1, size = 1000, weapon_levels = new[] { 1, 2, 3, 4, 5 } }, cookie, "weapon");
            results.AddRange(weapons);

            UpdateMetadata(results);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] API 元数据获取失败: {ex.Message}");
            UpdateMetadata(null);
        }
    }

    private async Task<List<ScrapedMetadata>> FetchCalculatorListAsync(string url, object payload, string? cookie, string type)
    {
        var list = new List<ScrapedMetadata>();
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrEmpty(cookie))
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            if (!doc.RootElement.TryGetProperty("data", out var data)) return list;
            if (!data.TryGetProperty("list", out var items)) return list;

            foreach (var item in items.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name) || name == "旅行者") continue;

                var id = item.TryGetProperty("id", out var i) ? i.GetInt32().ToString() : "";
                var icon = item.TryGetProperty("icon", out var ic) ? ic.GetString() : null;

                var rank = "";
                if (type == "char")
                {
                    if (item.TryGetProperty("avatar_level", out var avLv)) rank = avLv.GetInt32().ToString();
                }
                else
                {
                    if (item.TryGetProperty("weapon_level", out var wpLv)) rank = wpLv.GetInt32().ToString();
                }

                var elementSrc = "";
                if (type == "char" && item.TryGetProperty("element_attr_id", out var elemId))
                {
                    var elementId = elemId.GetInt32();
                    elementSrc = ElementMapping.GetElementIconUrl(elementId) ?? "";
                }

                list.Add(new ScrapedMetadata
                {
                    Name = name!,
                    ImgSrc = icon,
                    ElementSrc = elementSrc,
                    Type = type,
                    ItemId = id,
                    Rank = rank
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 获取 {type} 列表失败: {ex.Message}");
        }
        return list;
    }

    public void UpdateMetadata(List<ScrapedMetadata> scrapedData)
    {
        IsFetching = false;
        IsScraping = false;

        if (scrapedData == null || scrapedData.Count == 0)
        {
            CrawlerStatus = "未找到新图片资源，将使用现有缓存或默认图标";
            return;
        }

        CrawlerStatus = $"更新了 {scrapedData.Count} 个图片资源，并存入数据库";
        
        SaveMetadataToDb(scrapedData);
        LoadMetadataFromDb();
        
        _ = ApplyMetadataToUIAsync(_savedMetadata);
    }

    private async Task ApplyMetadataToUIAsync(List<ScrapedMetadata> metadataList)
    {
        if (metadataList == null || metadataList.Count == 0) return;
        var metaDict = metadataList.GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First());
        
        await UpdateCollectionImagesAsync(CharacterFiveStars, metaDict);
        await UpdateCollectionImagesAsync(CharacterFourStars, metaDict);
        await UpdateCollectionImagesAsync(WeaponFiveStars, metaDict);
        await UpdateCollectionImagesAsync(WeaponFourStars, metaDict);
        await UpdateCollectionImagesAsync(StandardFiveStars, metaDict);
        await UpdateCollectionImagesAsync(StandardFourStars, metaDict);
    }

    private async Task UpdateCollectionImagesAsync(ObservableCollection<GachaDisplayItem> collection, Dictionary<string, ScrapedMetadata> metaDict)
    {
        var items = collection.ToList();
        var updates = new List<(GachaDisplayItem item, string imgUrl, string elementUrl)>();

        foreach (var item in items)
        {
            ScrapedMetadata match = null;
            if (metaDict.TryGetValue(item.Name, out var exactMatch)) match = exactMatch;
            else match = metaDict.Values.FirstOrDefault(x => x.Name.Contains(item.Name) || item.Name.Contains(x.Name));

            if (match != null)
            {
                var imgUrl = !string.IsNullOrEmpty(match.ImgSrc) ? match.ImgSrc : null;
                var elementUrl = (item.Type == "角色" || item.Type == "常驻") && !string.IsNullOrEmpty(match.ElementSrc) ? match.ElementSrc : null;
                if (imgUrl != null || elementUrl != null)
                    updates.Add((item, imgUrl, elementUrl));
            }
        }

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var (item, imgUrl, elementUrl) in updates)
            {
                if (imgUrl != null) item.ImageUrl = imgUrl;
                if (elementUrl != null) item.ElementUrl = elementUrl;
            }
        });
    }
}