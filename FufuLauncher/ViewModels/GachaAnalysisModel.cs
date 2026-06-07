using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.Data.Sqlite;
using MihoyoBBS;

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
    private bool _isFetchingPoolMetadata;
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
    private List<GachaLogItem> _cachedChronicledLogs = new();
    private List<GachaLogItem> _cachedNoviceLogs = new();
    private List<GachaLogItem> _cachedStandardLogs = new();
    private List<ScrapedMetadata> _savedMetadata = new();
    private string _currentUid = "";
    private string _uidBeforeAddNew = "";
    private int _refreshVersion;
    private bool _analysisDashboardDirty = true;

    [ObservableProperty] private string _gachaUrl;
    [ObservableProperty] private string _crawlerStatus = "等待获取数据...";
    [ObservableProperty] private bool _isFetching;
    [ObservableProperty] private bool _isScraping;
    [ObservableProperty] private GachaStatistic _characterStats = new() { PoolName = "角色活动" };
    [ObservableProperty] private GachaStatistic _weaponStats = new() { PoolName = "武器活动" };
    [ObservableProperty] private GachaStatistic _chronicledStats = new() { PoolName = "集录祈愿" };
    [ObservableProperty] private GachaStatistic _standardStats = new() { PoolName = "常驻祈愿" };

    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _characterFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _weaponFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _chronicledFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _standardFiveStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _characterFourStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _weaponFourStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _chronicledFourStars = new();
    [ObservableProperty] private ObservableCollection<GachaDisplayItem> _standardFourStars = new();
    
    [ObservableProperty] private ObservableCollection<ScrapedMetadata> _characterMetadataPreview = new();
    [ObservableProperty] private ObservableCollection<ScrapedMetadata> _weaponMetadataPreview = new();
    [ObservableProperty] private ObservableCollection<string> _knownUids = new();
    [ObservableProperty] private ObservableCollection<string> _uidComboItems = new();
    [ObservableProperty] private string _selectedUid = "";

    // 添加四星视图的控制开关
    [ObservableProperty] private bool _isCharacterFourStarVisible;
    [ObservableProperty] private bool _isWeaponFourStarVisible;
    [ObservableProperty] private bool _isChronicledFourStarVisible;
    [ObservableProperty] private bool _isStandardFourStarVisible;

    // 四星分割线显示条件
    public bool ShowCharacterFourDivider => IsCharacterFourStarVisible && CharacterFourStars?.Count > 0;
    public bool ShowWeaponFourDivider => IsWeaponFourStarVisible && WeaponFourStars?.Count > 0;
    public bool ShowChronicledFourDivider => IsChronicledFourStarVisible && ChronicledFourStars?.Count > 0;
    public bool ShowStandardFourDivider => IsStandardFourStarVisible && StandardFourStars?.Count > 0;

    // "暂无记录"显示条件
    public bool ShowCharacterNoRecords => CharacterStats?.FiveStarCount == 0 && (!IsCharacterFourStarVisible || CharacterFourStars?.Count == 0);
    public bool ShowWeaponNoRecords => WeaponStats?.FiveStarCount == 0 && (!IsWeaponFourStarVisible || WeaponFourStars?.Count == 0);
    public bool ShowChronicledNoRecords => ChronicledStats?.FiveStarCount == 0 && (!IsChronicledFourStarVisible || ChronicledFourStars?.Count == 0);
    public bool ShowStandardNoRecords => StandardStats?.FiveStarCount == 0 && (!IsStandardFourStarVisible || StandardFourStars?.Count == 0);

    public const string AddNewUserItem = "＋ 添加新用户";
    [ObservableProperty] private bool _hasGachaData;
    [ObservableProperty] private bool _isDataLoaded;
    [ObservableProperty] private bool _isOverviewSelected = true;
    [ObservableProperty] private bool _isAnalysisLoading;
    [ObservableProperty] private bool _isAnalysisReady;
    [ObservableProperty] private GachaAnalysisDashboard _analysisDashboard = GachaAnalysisDashboard.Empty();

    public bool IsAnalysisSelected => !IsOverviewSelected;
    public bool ShowAnalysisLoading => IsAnalysisSelected && IsAnalysisLoading;
    public bool ShowAnalysisContent => IsAnalysisSelected && IsAnalysisReady && !IsAnalysisLoading;

    public Action RequestMetadataScrapeAction;
    public Action<string> OnErrorAction;
    public Func<IntPtr> GetWindowHandle;
    public Func<string, string, Task<bool>> OnUidMismatchAsync;
    public Func<string, string, string, Task> OnShowConfirmDialogAsync;

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
            CREATE TABLE IF NOT EXISTS GachaPoolMetadata (
                Version TEXT NOT NULL,
                PoolType TEXT NOT NULL,
                StartTime TEXT NOT NULL,
                EndTime TEXT NOT NULL,
                UpItems TEXT NOT NULL,
                PRIMARY KEY (Version, PoolType)
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
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            CharacterMetadataPreview.Clear();
            WeaponMetadataPreview.Clear();
        });

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
            var isChar = item.Type == "char";
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (isChar) CharacterMetadataPreview.Add(item);
                else WeaponMetadataPreview.Add(item);
            });
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
        _cachedChronicledLogs.Clear();
        _cachedNoviceLogs.Clear();
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
                var gt = GetNormalizedGachaType(item.GachaType);
                if (gt == "301") _cachedCharacterLogs.Add(item);
                else if (gt == "302") _cachedWeaponLogs.Add(item);
                else if (gt == "500") _cachedChronicledLogs.Add(item);
                else if (gt == "100") _cachedNoviceLogs.Add(item);
                else _cachedStandardLogs.Add(item);
            }
            Debug.WriteLine($"[Gacha] 加载完成 UID={uid}: 角色{_cachedCharacterLogs.Count} 武器{_cachedWeaponLogs.Count} 集录{_cachedChronicledLogs.Count} 新手{_cachedNoviceLogs.Count} 常驻{_cachedStandardLogs.Count}");
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
                var totalBefore = _cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count + _cachedNoviceLogs.Count + _cachedStandardLogs.Count;
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
            InsertItems(_cachedChronicledLogs);
            InsertItems(_cachedNoviceLogs);
            InsertItems(_cachedStandardLogs);
            transaction.Commit();
            Debug.WriteLine($"[Gacha] 保存完成 UID={_currentUid}: 角色{_cachedCharacterLogs.Count} 武器{_cachedWeaponLogs.Count} 集录{_cachedChronicledLogs.Count} 新手{_cachedNoviceLogs.Count} 常驻{_cachedStandardLogs.Count}");
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
            if (_cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count + _cachedStandardLogs.Count > 0)
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
                ChronicledStats = new GachaStatistic { PoolName = "集录祈愿" };
                StandardStats = new GachaStatistic { PoolName = "常驻祈愿" };
                InvalidateAnalysisDashboard();
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
                _cachedChronicledLogs.Clear();
                _cachedNoviceLogs.Clear();
                _cachedStandardLogs.Clear();

                _ = _localSettingsService.SaveSettingAsync(LastSelectedUidKey, "");

                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    RefreshKnownUidsUI(remainingUids);
                    ClearCollections();
                    CharacterStats = new GachaStatistic { PoolName = "角色活动" };
                    WeaponStats = new GachaStatistic { PoolName = "武器活动" };
                    ChronicledStats = new GachaStatistic { PoolName = "集录祈愿" };
                    StandardStats = new GachaStatistic { PoolName = "常驻祈愿" };
                    InvalidateAnalysisDashboard();
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

            if (!HasPoolMetadataCache())
            {
                _ = FetchGachaPoolMetadataAsync();
            }
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

        _ = FetchGachaPoolMetadataAsync();
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
        var chronicledLogs = _cachedChronicledLogs.OrderBy(x => x.Id).ToList();
        var standardLogs = _cachedStandardLogs.OrderBy(x => x.Id).ToList();

        var version = ++_refreshVersion;

        _ = Task.Run(() =>
        {
            var charPools = LoadPoolMetadataFromDb("301");
            var weaponPools = LoadPoolMetadataFromDb("302");

            var charStats = _gachaService.AnalyzePool("301", charLogs);
            var weaponStats = _gachaService.AnalyzePool("302", weaponLogs);
            var chronicledStats = _gachaService.AnalyzePool("500", chronicledLogs);
            var standardStats = _gachaService.AnalyzePool("200", standardLogs);

            var charFive = BuildDisplayCollection(charStats.FiveStarRecords, "角色", charPools);
            var charFour = BuildDisplayCollection(charStats.FourStarRecords, "角色", charPools);
            var weaponFive = BuildDisplayCollection(weaponStats.FiveStarRecords, "武器", weaponPools);
            var weaponFour = BuildDisplayCollection(weaponStats.FourStarRecords, "武器", weaponPools);
            var chronicledFive = BuildDisplayCollection(chronicledStats.FiveStarRecords, "集录");
            var chronicledFour = BuildDisplayCollection(chronicledStats.FourStarRecords, "集录");
            var standardFive = BuildDisplayCollection(standardStats.FiveStarRecords, "常驻");
            var standardFour = BuildDisplayCollection(standardStats.FourStarRecords, "常驻");

            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (_refreshVersion != version) return;

                CharacterStats = charStats;
                WeaponStats = weaponStats;
                ChronicledStats = chronicledStats;
                StandardStats = standardStats;
                CharacterFiveStars = charFive;
                CharacterFourStars = charFour;
                WeaponFiveStars = weaponFive;
                WeaponFourStars = weaponFour;
                ChronicledFiveStars = chronicledFive;
                ChronicledFourStars = chronicledFour;
                StandardFiveStars = standardFive;
                StandardFourStars = standardFour;
                InvalidateAnalysisDashboard();

                // 通知相关属性更新
                OnPropertyChanged(nameof(ShowCharacterNoRecords));
                OnPropertyChanged(nameof(ShowWeaponNoRecords));
                OnPropertyChanged(nameof(ShowChronicledNoRecords));
                OnPropertyChanged(nameof(ShowStandardNoRecords));
                OnPropertyChanged(nameof(ShowCharacterFourDivider));
                OnPropertyChanged(nameof(ShowWeaponFourDivider));
                OnPropertyChanged(nameof(ShowChronicledFourDivider));
                OnPropertyChanged(nameof(ShowStandardFourDivider));

                if (_savedMetadata != null && _savedMetadata.Count > 0) _ = ApplyMetadataToUIAsync(_savedMetadata);
                if (IsAnalysisSelected) _ = EnsureAnalysisDashboardAsync();
            });
        });
    }

    public async Task ShowOverviewAsync()
    {
        IsOverviewSelected = true;
        await Task.CompletedTask;
    }

    public async Task ShowAnalysisAsync()
    {
        IsOverviewSelected = false;
        await EnsureAnalysisDashboardAsync();
    }

    private async Task EnsureAnalysisDashboardAsync()
    {
        if (IsAnalysisLoading) return;
        if (IsAnalysisReady && !_analysisDashboardDirty) return;

        var version = _refreshVersion;
        var charLogs = _cachedCharacterLogs.OrderBy(x => x.Id).ToList();
        var weaponLogs = _cachedWeaponLogs.OrderBy(x => x.Id).ToList();
        var chronicledLogs = _cachedChronicledLogs.OrderBy(x => x.Id).ToList();
        var noviceLogs = _cachedNoviceLogs.OrderBy(x => x.Id).ToList();
        var standardLogs = _cachedStandardLogs.OrderBy(x => x.Id).ToList();

        IsAnalysisLoading = true;
        IsAnalysisReady = false;
        AnalysisDashboard = GachaAnalysisDashboard.Empty();

        try
        {
            var dashboard = await Task.Run(() => BuildAnalysisDashboard(charLogs, weaponLogs, chronicledLogs, noviceLogs, standardLogs));

            if (_refreshVersion == version)
            {
                AnalysisDashboard = dashboard;
                _analysisDashboardDirty = false;
                IsAnalysisReady = true;
            }
        }
        finally
        {
            IsAnalysisLoading = false;
        }
    }

    private void InvalidateAnalysisDashboard()
    {
        _analysisDashboardDirty = true;
        IsAnalysisReady = false;
        AnalysisDashboard = GachaAnalysisDashboard.Empty();
    }

    private GachaAnalysisDashboard BuildAnalysisDashboard(
        List<GachaLogItem> charLogs,
        List<GachaLogItem> weaponLogs,
        List<GachaLogItem> chronicledLogs,
        List<GachaLogItem> noviceLogs,
        List<GachaLogItem> standardLogs)
    {
        var allLogs = charLogs
            .Concat(weaponLogs)
            .Concat(chronicledLogs)
            .Concat(noviceLogs)
            .Concat(standardLogs)
            .OrderBy(x => x.Id)
            .ToList();

        if (allLogs.Count == 0) return GachaAnalysisDashboard.Empty();

        var totalCount = allLogs.Count;
        var fiveStarCount = allLogs.Count(x => x.RankType == "5");
        var fourStarCount = allLogs.Count(x => x.RankType == "4");
        var threeStarCount = totalCount - fiveStarCount - fourStarCount;
        var primogems = totalCount * 160;
        var fiveStarRate = FormatRate(fiveStarCount, totalCount);
        var fourStarRate = FormatRate(fourStarCount, totalCount);
        var averageCharacterPities = CalculateFiveStarCharacterPities(charLogs, weaponLogs, chronicledLogs, noviceLogs, standardLogs);
        var averageCharacterPulls = averageCharacterPities.Count == 0 ? 0 : averageCharacterPities.Average();
        var fiveStarTimeline = BuildFiveStarTimeline(charLogs, "角色活动")
            .Concat(BuildFiveStarTimeline(weaponLogs, "武器活动"))
            .Concat(BuildFiveStarTimeline(chronicledLogs, "集录祈愿"))
            .Concat(BuildFiveStarTimeline(noviceLogs, "新手祈愿"))
            .Concat(BuildFiveStarTimeline(standardLogs, "常驻祈愿"))
            .OrderBy(x => x.Time)
            .ToList();
        var averageFiveStarPulls = fiveStarTimeline.Count == 0 ? 0 : fiveStarTimeline.Average(x => x.Pity);
        var bestFiveStar = fiveStarTimeline.OrderBy(x => x.Pity).FirstOrDefault();
        var worstFiveStar = fiveStarTimeline.OrderByDescending(x => x.Pity).FirstOrDefault();

        var currentPities = new[]
        {
            ("角色活动", CalculateCurrentFiveStarPity(charLogs)),
            ("武器活动", CalculateCurrentFiveStarPity(weaponLogs)),
            ("集录祈愿", CalculateCurrentFiveStarPity(chronicledLogs)),
            ("新手祈愿", CalculateCurrentFiveStarPity(noviceLogs)),
            ("常驻祈愿", CalculateCurrentFiveStarPity(standardLogs))
        };
        var deepestPity = currentPities.OrderByDescending(x => x.Item2).First();

        var monthlyGroups = allLogs
            .Select(x => TryParseTime(x.Time, out var dt) ? dt : (DateTime?)null)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .GroupBy(x => x.ToString("yyyy-MM"))
            .OrderBy(x => x.Key)
            .ToList();
        var activeMonthCount = monthlyGroups.Count;
        var monthlyAveragePulls = activeMonthCount == 0 ? 0 : monthlyGroups.Average(x => x.Count());
        var busiestMonth = monthlyGroups.OrderByDescending(x => x.Count()).ThenByDescending(x => x.Key).FirstOrDefault();

        var groupedByTime = allLogs
            .Where(x => !string.IsNullOrWhiteSpace(x.Time))
            .GroupBy(x => $"{GetNormalizedGachaType(x.GachaType)}|{x.Time}")
            .ToList();
        var tenPullGroups = groupedByTime.Where(x => x.Count() >= 10).ToList();
        var singlePullGroups = groupedByTime.Where(x => x.Count() == 1).ToList();
        var tenPullGoldCount = tenPullGroups.Count(x => x.Any(i => i.RankType == "5"));
        var singlePullGoldCount = singlePullGroups.Count(x => x.Any(i => i.RankType == "5"));

        var dashboard = new GachaAnalysisDashboard
        {
            TenPullCount = tenPullGroups.Count,
            TenPullGoldCount = tenPullGoldCount,
            TenPullGoldRateText = FormatRate(tenPullGoldCount, tenPullGroups.Count),
            SinglePullCount = singlePullGroups.Count,
            SinglePullGoldCount = singlePullGoldCount,
            SinglePullGoldRateText = FormatRate(singlePullGoldCount, singlePullGroups.Count),
            AverageFiveStarCharacterPulls = averageCharacterPulls,
            AverageFiveStarCharacterPullsText = averageCharacterPulls <= 0 ? "0" : averageCharacterPulls.ToString("0.#"),
            AverageFiveStarCharacterPrimogems = (int)Math.Round(averageCharacterPulls * 160),
            AverageFiveStarCharacterPrimogemsText = averageCharacterPulls <= 0 ? "0" : ((int)Math.Round(averageCharacterPulls * 160)).ToString("N0"),
            AverageFiveStarPullsText = averageFiveStarPulls <= 0 ? "0" : averageFiveStarPulls.ToString("0.#"),
            CurrentDeepestPityText = $"{deepestPity.Item2} 抽",
            CurrentDeepestPityHint = deepestPity.Item2 <= 0 ? "暂无五星垫数" : $"当前最深：{deepestPity.Item1}",
            BestFiveStarPityText = fiveStarTimeline.Count == 0 ? "0 抽" : $"{bestFiveStar.Pity} 抽",
            BestFiveStarPityHint = fiveStarTimeline.Count == 0 ? "暂无五星记录" : $"{bestFiveStar.PoolName} · {bestFiveStar.Name}",
            WorstFiveStarPityText = fiveStarTimeline.Count == 0 ? "0 抽" : $"{worstFiveStar.Pity} 抽",
            WorstFiveStarPityHint = fiveStarTimeline.Count == 0 ? "暂无五星记录" : $"{worstFiveStar.PoolName} · {worstFiveStar.Name}",
            ActiveMonthCountText = activeMonthCount.ToString(),
            MonthlyAveragePullsText = monthlyAveragePulls <= 0 ? "0" : monthlyAveragePulls.ToString("0.#"),
            BusiestMonthText = busiestMonth == null ? "暂无" : busiestMonth.Key,
            BusiestMonthPullsText = busiestMonth == null ? "0 抽" : $"{busiestMonth.Count()} 抽",
            DateRangeText = BuildDateRangeText(allLogs)
        };

        dashboard.KpiItems = new ObservableCollection<GachaKpiItem>
        {
            new GachaKpiItem { Glyph = "\uE8EF", Label = "总抽数", Value = totalCount.ToString(), Hint = $"约 {primogems:N0} 原石" },
            new GachaKpiItem { Glyph = "\uE8C7", Label = "原石估算", Value = primogems.ToString("N0"), Hint = "按每抽 160 原石" },
            new GachaKpiItem { Glyph = "\uE735", Label = "五星出货", Value = fiveStarCount.ToString(), Hint = fiveStarRate },
            new GachaKpiItem { Glyph = "\uE734", Label = "四星出货", Value = fourStarCount.ToString(), Hint = fourStarRate },
            new GachaKpiItem { Glyph = "\uE7C1", Label = "五星角色均耗", Value = $"{dashboard.AverageFiveStarCharacterPullsText} 抽", Hint = $"约 {dashboard.AverageFiveStarCharacterPrimogemsText} 原石" },
            new GachaKpiItem { Glyph = "\uE7C1", Label = "五星均抽", Value = $"{dashboard.AverageFiveStarPullsText} 抽", Hint = "全部卡池五星" },
            new GachaKpiItem { Glyph = "\uE8A5", Label = "当前最深垫数", Value = dashboard.CurrentDeepestPityText, Hint = dashboard.CurrentDeepestPityHint },
            new GachaKpiItem { Glyph = "\uE74C", Label = "最欧五星", Value = dashboard.BestFiveStarPityText, Hint = dashboard.BestFiveStarPityHint },
            new GachaKpiItem { Glyph = "\uE7BA", Label = "最非五星", Value = dashboard.WorstFiveStarPityText, Hint = dashboard.WorstFiveStarPityHint },
            new GachaKpiItem { Glyph = "\uE787", Label = "活跃月份", Value = dashboard.ActiveMonthCountText, Hint = $"月均 {dashboard.MonthlyAveragePullsText} 抽" }
        };

        dashboard.PoolDistribution = BuildSharePoints(new[]
        {
            ("角色活动", (double)charLogs.Count, $"{charLogs.Count} 抽"),
            ("武器活动", (double)weaponLogs.Count, $"{weaponLogs.Count} 抽"),
            ("集录祈愿", (double)chronicledLogs.Count, $"{chronicledLogs.Count} 抽"),
            ("新手祈愿", (double)noviceLogs.Count, $"{noviceLogs.Count} 抽"),
            ("常驻祈愿", (double)standardLogs.Count, $"{standardLogs.Count} 抽")
        });
        dashboard.PoolPieSlices = BuildPieSlices(dashboard.PoolDistribution);

        dashboard.RarityDistribution = BuildSharePoints(new[]
        {
            ("五星", (double)fiveStarCount, $"{fiveStarCount} 个"),
            ("四星", (double)fourStarCount, $"{fourStarCount} 个"),
            ("三星", (double)threeStarCount, $"{threeStarCount} 个")
        });
        dashboard.RarityPieSlices = BuildPieSlices(dashboard.RarityDistribution);

        dashboard.RecentFiveStarPities = BuildRelativePoints(
            fiveStarTimeline
                .TakeLast(12)
                .Select(x => (x.Name, (double)x.Pity, $"{x.Pity} 抽", $"{x.PoolName} · {FormatShortDate(x.TimeText)}")));

        dashboard.FourStarTopItems = BuildRelativePoints(
            allLogs
                .Where(x => x.RankType == "4")
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Name) ? "未知四星" : x.Name)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key)
                .Take(10)
                .Select(x => (x.Key, (double)x.Count(), $"{x.Count()} 次", "四星出货")));

        dashboard.PityBuckets = BuildRelativePoints(
            fiveStarTimeline
                .GroupBy(x => GetPityBucket(x.Pity))
                .Select(x => (x.Key.Label, (double)x.Count(), $"{x.Count()} 次", x.Key.Hint))
                .OrderBy(x => GetPityBucketOrder(x.Item1)));

        dashboard.MonthlyPulls = BuildRelativePoints(
            monthlyGroups
                .Select(x => (x.Key, (double)x.Count(), $"{x.Count()} 抽", "1-5星全部记录")));

        return dashboard;
    }

    private const double AnalysisColumnMaxHeight = 132;

    private static int CalculateCurrentFiveStarPity(List<GachaLogItem> logs)
    {
        var pity = 0;
        foreach (var item in logs.OrderBy(x => x.Id))
        {
            pity++;
            if (item.RankType == "5")
                pity = 0;
        }

        return pity;
    }

    private static ObservableCollection<GachaChartPoint> BuildSharePoints(IEnumerable<(string Label, double Value, string Display)> values)
    {
        var list = values.ToList();
        var total = list.Sum(x => x.Value);
        return new ObservableCollection<GachaChartPoint>(list.Select((x, index) =>
        {
            var percentage = total <= 0 ? 0 : x.Value * 100 / total;
            return new GachaChartPoint
            {
                Label = x.Label,
                Value = x.Value,
                Percentage = percentage,
                BarWidth = ToColumnWidth(percentage),
                BarHeight = ToColumnHeight(percentage),
                DisplayValue = total <= 0 ? x.Display : $"{x.Display} · {percentage:0.#}%",
                ColorIndex = index
            };
        }));
    }

    private static ObservableCollection<GachaChartPoint> BuildRelativePoints(IEnumerable<(string Label, double Value, string Display, string SubLabel)> values)
    {
        var list = values.ToList();
        var max = list.Count == 0 ? 0 : list.Max(x => x.Value);
        return new ObservableCollection<GachaChartPoint>(list.Select((x, index) =>
        {
            var percentage = max <= 0 ? 0 : Math.Max(4, x.Value * 100 / max);
            return new GachaChartPoint
            {
                Label = x.Label,
                SubLabel = x.SubLabel,
                Value = x.Value,
                Percentage = percentage,
                BarWidth = ToColumnWidth(percentage),
                BarHeight = ToColumnHeight(percentage),
                DisplayValue = x.Display,
                ColorIndex = index
            };
        }));
    }

    private static double ToColumnHeight(double percentage)
    {
        if (percentage <= 0) return 0;
        return Math.Max(8, percentage / 100 * AnalysisColumnMaxHeight);
    }

    private static double ToColumnWidth(double percentage)
    {
        if (percentage <= 0) return 14;
        return Math.Clamp(14 + percentage * 0.1, 16, 24);
    }

    private static ObservableCollection<GachaPieSlice> BuildPieSlices(IEnumerable<GachaChartPoint> points)
    {
        var slices = new ObservableCollection<GachaPieSlice>();
        var visiblePoints = points.Where(x => x.Value > 0).ToList();
        var total = visiblePoints.Sum(x => x.Value);
        if (total <= 0) return slices;

        double startAngle = -90;
        for (var i = 0; i < visiblePoints.Count; i++)
        {
            var point = visiblePoints[i];
            var sweepAngle = point.Value / total * 360;
            if (i == visiblePoints.Count - 1)
                sweepAngle = 270 - startAngle;

            slices.Add(new GachaPieSlice
            {
                Label = point.Label,
                DisplayValue = point.DisplayValue,
                Percentage = point.Percentage,
                StartAngle = startAngle,
                SweepAngle = sweepAngle,
                ColorIndex = point.ColorIndex
            });

            startAngle += sweepAngle;
        }

        return slices;
    }

    private static List<double> CalculateFiveStarCharacterPities(params List<GachaLogItem>[] logLists)
    {
        var pities = new List<double>();
        foreach (var logs in logLists)
        {
            var pity = 0;
            foreach (var item in logs.OrderBy(x => x.Id))
            {
                pity++;
                if (item.RankType != "5") continue;

                if (item.ItemType?.Contains("角色") == true
                    || GetNormalizedGachaType(item.GachaType) is "100" or "301")
                    pities.Add(pity);

                pity = 0;
            }
        }
        return pities;
    }

    private static List<(string Name, string PoolName, int Pity, DateTime Time, string TimeText)> BuildFiveStarTimeline(List<GachaLogItem> logs, string poolName)
    {
        var result = new List<(string Name, string PoolName, int Pity, DateTime Time, string TimeText)>();
        var pity = 0;
        foreach (var item in logs.OrderBy(x => x.Id))
        {
            pity++;
            if (item.RankType != "5") continue;

            result.Add((
                string.IsNullOrWhiteSpace(item.Name) ? "未知五星" : item.Name,
                poolName,
                pity,
                TryParseTime(item.Time, out var time) ? time : DateTime.MinValue,
                item.Time ?? ""));
            pity = 0;
        }
        return result;
    }

    private static string BuildDateRangeText(List<GachaLogItem> logs)
    {
        var dates = logs
            .Select(x => TryParseTime(x.Time, out var dt) ? dt : (DateTime?)null)
            .Where(x => x.HasValue)
            .Select(x => x.Value)
            .OrderBy(x => x)
            .ToList();

        if (dates.Count == 0) return "暂无时间记录";
        return $"{dates.First():yyyy.MM.dd} - {dates.Last():yyyy.MM.dd}";
    }

    private static string FormatRate(int hit, int total) => total <= 0 ? "0%" : $"{hit * 100d / total:0.##}%";

    private static bool TryParseTime(string time, out DateTime dateTime) => DateTime.TryParse(time, out dateTime);

    private static string FormatShortDate(string time) => TryParseTime(time, out var dt) ? dt.ToString("MM.dd") : "未知";

    private static (string Label, string Hint) GetPityBucket(int pity) => pity switch
    {
        <= 30 => ("1-30", "欧气区间"),
        <= 60 => ("31-60", "中段出货"),
        <= 75 => ("61-75", "接近软保底"),
        <= 90 => ("76-90", "保底区间"),
        _ => ("90+", "异常记录")
    };

    private static int GetPityBucketOrder(string label) => label switch
    {
        "1-30" => 0,
        "31-60" => 1,
        "61-75" => 2,
        "76-90" => 3,
        _ => 4
    };

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
        {
            SaveGachaLogsToDb();
            _uidBeforeAddNew = _currentUid;
        }

        _currentUid = "";
        _cachedCharacterLogs.Clear();
        _cachedWeaponLogs.Clear();
        _cachedChronicledLogs.Clear();
        _cachedNoviceLogs.Clear();
        _cachedStandardLogs.Clear();

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            SelectedUid = "";
            ClearCollections();
            CharacterStats = new GachaStatistic { PoolName = "角色活动" };
            WeaponStats = new GachaStatistic { PoolName = "武器活动" };
            ChronicledStats = new GachaStatistic { PoolName = "集录祈愿" };
            StandardStats = new GachaStatistic { PoolName = "常驻祈愿" };
            InvalidateAnalysisDashboard();
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

    private async Task<(string stoken, string mid, string stuid, string cookie, string configPath)> FindAccountByGameUidAsync(string targetGameUid)
    {
        var baseDir = Helpers.AppPaths.DataDir;
        var filesToTry = Directory.GetFiles(baseDir, "config*.json")
            .Concat(Directory.GetFiles(baseDir, "config.lab*.json"))
            .Distinct()
            .ToList();

        foreach (var file in filesToTry)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Display", out var display)) continue;
                var displayGameUid = display.TryGetProperty("GameUid", out var gu) ? gu.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(displayGameUid)) continue;
                if (displayGameUid != targetGameUid) continue;

                var account = doc.RootElement.GetProperty("Account");
                var stoken = account.TryGetProperty("Stoken", out var st) ? st.GetString() ?? "" : "";
                var mid = account.TryGetProperty("Mid", out var mi) ? mi.GetString() ?? "" : "";
                var stuid = account.TryGetProperty("Stuid", out var si) ? si.GetString() ?? "" : "";
                var cookie = account.TryGetProperty("Cookie", out var ck) ? ck.GetString() ?? "" : "";

                bool needSave = false;

                if (string.IsNullOrEmpty(stoken) && !string.IsNullOrEmpty(cookie))
                {
                    var stokenMatch = Regex.Match(cookie, @"stoken=([^;]+)");
                    if (stokenMatch.Success) { stoken = stokenMatch.Groups[1].Value; needSave = true; }
                }
                if (string.IsNullOrEmpty(mid) && !string.IsNullOrEmpty(cookie))
                {
                    var midMatch = Regex.Match(cookie, @"mid=([^;]+)");
                    if (midMatch.Success) { mid = midMatch.Groups[1].Value; needSave = true; }
                }

                if (needSave)
                {
                    try
                    {
                        var configObj = JsonSerializer.Deserialize<Config>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (configObj?.Account != null)
                        {
                            if (!string.IsNullOrEmpty(stoken)) configObj.Account.Stoken = stoken;
                            if (!string.IsNullOrEmpty(mid)) configObj.Account.Mid = mid;
                            var updatedJson = JsonSerializer.Serialize(configObj, new JsonSerializerOptions { WriteIndented = true });
                            await File.WriteAllTextAsync(file, updatedJson);
                            Debug.WriteLine($"[Gacha] 已回写 stoken/mid 到 {Path.GetFileName(file)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Gacha] 回写 stoken/mid 失败: {ex.Message}");
                    }
                }

                return (stoken, mid, stuid, cookie, file);
            }
            catch { }
        }

        return (null, null, null, null, null);
    }

    [RelayCommand]
    private async Task FetchFromMiYouSheAsync()
    {
        string gameUid = _currentUid;
        if (string.IsNullOrEmpty(gameUid))
        {
            try
            {
                var userConfigService = App.GetService<Services.IUserConfigService>();
                var displayConfig = await userConfigService.LoadDisplayConfigAsync();
                gameUid = displayConfig.GameUid ?? "";
            }
            catch { }
        }

        if (string.IsNullOrEmpty(gameUid))
        {
            CrawlerStatus = "当前账号未绑定游戏角色，无法获取祈愿记录";
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        var (stoken, mid, stuid, cookie, _) = await FindAccountByGameUidAsync(gameUid);

        if (stoken == null)
        {
            CrawlerStatus = $"请先登录 UID {gameUid} 对应的米游社账号后重试";
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        if (string.IsNullOrEmpty(stoken))
        {
            CrawlerStatus = $"UID {gameUid} 对应账号的 stoken 已失效，请重新登录该账号后重试";
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        var previousUid = string.IsNullOrEmpty(_currentUid) ? _uidBeforeAddNew : _currentUid;
        _uidBeforeAddNew = "";
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

            CrawlerStatus = $"武器活动 {weaponLogs.Count} 条，正在获取集录祈愿记录...";
            var chronicledLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "500", count => OnProgress("集录祈愿", count));
            foreach (var l in chronicledLogs) l.Uid = gameUid;
            _cachedChronicledLogs = MergeLogs(_cachedChronicledLogs, chronicledLogs);

            CrawlerStatus = $"集录祈愿 {chronicledLogs.Count} 条，正在获取新手祈愿记录...";
            var noviceLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "100", count => OnProgress("新手祈愿", count));
            foreach (var l in noviceLogs) l.Uid = gameUid;
            _cachedNoviceLogs = MergeLogs(_cachedNoviceLogs, noviceLogs);

            CrawlerStatus = $"新手祈愿 {noviceLogs.Count} 条，正在获取常驻祈愿记录...";
            var standardLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "200", count => OnProgress("常驻祈愿", count));
            foreach (var l in standardLogs) l.Uid = gameUid;
            _cachedStandardLogs = MergeLogs(_cachedStandardLogs, standardLogs);

            FillMissingFieldsFromMetadata(charLogs, weaponLogs, chronicledLogs, noviceLogs, standardLogs);

            var total = charLogs.Count + weaponLogs.Count + chronicledLogs.Count + noviceLogs.Count + standardLogs.Count;

            if (total == 0)
            {
                var hasExistingRecords = _cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count + _cachedNoviceLogs.Count + _cachedStandardLogs.Count > 0;

                IsFetching = false;

                if (hasExistingRecords)
                {
                    CrawlerStatus = $"UID {gameUid} 未获取到新记录，已保留现有数据";
                    return;
                }

                _cachedCharacterLogs.Clear();
                _cachedWeaponLogs.Clear();
                _cachedChronicledLogs.Clear();
                _cachedNoviceLogs.Clear();
                _cachedStandardLogs.Clear();

                if (!string.IsNullOrEmpty(previousUid))
                {
                    if (OnShowConfirmDialogAsync != null)
                    {
                        await OnShowConfirmDialogAsync(
                            "无祈愿记录",
                            $"UID {gameUid} 没有获取到祈愿记录，已切回 UID {previousUid}",
                            "确定");
                    }
                    await SwitchToUidAsync(previousUid);
                }
                else
                {
                    if (OnShowConfirmDialogAsync != null)
                    {
                        await OnShowConfirmDialogAsync(
                            "无祈愿记录",
                            $"UID {gameUid} 没有获取到祈愿记录",
                            "确定");
                    }
                    await AddNewUserAsync();
                }
                return;
            }

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
private async Task ExportUigfAsync(string version)
{
    if (string.IsNullOrEmpty(version)) version = "v4.2";

    try
    {
        var allLogs = _cachedCharacterLogs
            .Concat(_cachedWeaponLogs)
            .Concat(_cachedChronicledLogs)
            .Concat(_cachedNoviceLogs)
            .Concat(_cachedStandardLogs)
            .ToList();

        if (allLogs.Count == 0)
        {
            OnErrorAction?.Invoke("没有可导出的抽卡记录");
            return;
        }

        var uid = _currentUid;
        if (string.IsNullOrEmpty(uid)) uid = "unknown";

        object finalObj;

        if (version.StartsWith("v4"))
        {
            var hk4eObj = new
            {
                uid = uid,
                timezone = 8,
                lang = "zh-cn",
                list = allLogs.Select(log => new
                {
                    uigf_gacha_type = GameToUigfGachaType(log.GachaType),
                    gacha_type = log.GachaType,
                    item_id = log.ItemId ?? "",
                    count = log.Count ?? "1",
                    time = log.Time ?? "",
                    name = log.Name ?? "",
                    item_type = log.ItemType ?? "",
                    rank_type = log.RankType ?? "",
                    id = log.Id ?? ""
                }).ToList()
            };

            var infoObj = new
            {
                export_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                export_app = "FufuLauncher",
                export_app_version = $"{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version}",
                version = version
            };

            if (version == "v4.2")
            {
                finalObj = new
                {
                    hkrpg = Array.Empty<object>(),
                    hk4e_ugc = Array.Empty<object>(),
                    info = infoObj,
                    hk4e = new[] { hk4eObj }
                };
            }
            else
            {
                finalObj = new
                {
                    info = infoObj,
                    hk4e = new[] { hk4eObj }
                };
            }
        }
        else
        {
            var exportTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            finalObj = new
            {
                info = new
                {
                    uid = uid,
                    lang = "zh-cn",
                    export_time = exportTime,
                    export_app = "FufuLauncher",
                    export_app_version = $"{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version}",
                    uigf_version = version,
                    export_timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                },
                list = allLogs.Select(log => new
                {
                    uigf_gacha_type = GameToUigfGachaType(log.GachaType),
                    gacha_type = log.GachaType,
                    item_id = log.ItemId ?? "",
                    count = log.Count ?? "1",
                    time = log.Time ?? "",
                    name = log.Name ?? "",
                    item_type = log.ItemType ?? "",
                    rank_type = log.RankType ?? "",
                    id = log.Id ?? ""
                }).ToList()
            };
        }

        var json = JsonSerializer.Serialize(finalObj, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var hwnd = GetWindowHandle?.Invoke() ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var savePicker = new Windows.Storage.Pickers.FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

        savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        savePicker.FileTypeChoices.Add("JSON 文件", new List<string> { ".json" });
        savePicker.SuggestedFileName = $"UIGF_{version.Replace(".", "")}_{uid}_{DateTimeOffset.UtcNow:yyyyMMdd}";

        var file = await savePicker.PickSaveFileAsync();
        if (file == null) return;

        await File.WriteAllTextAsync(file.Path, json);
        WeakReferenceMessenger.Default.Send(new NotificationMessage("导出成功", $"已导出 {allLogs.Count} 条记录到 {file.Name} ({version})", NotificationType.Success, 3000));
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
        if (hwnd == IntPtr.Zero) hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        IsFetching = true;
        CrawlerStatus = "正在读取 UIGF 文件...";

        var json = await File.ReadAllTextAsync(file.Path);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string version = "";
        if (root.TryGetProperty("info", out var infoNode))
        {
            if (infoNode.TryGetProperty("version", out var vNode)) version = vNode.GetString() ?? "";
            else if (infoNode.TryGetProperty("uigf_version", out var uvNode)) version = uvNode.GetString() ?? "";
        }

        if (string.IsNullOrEmpty(version))
        {
            CrawlerStatus = "无法识别 UIGF 版本，请确认文件格式正确";
            IsFetching = false;
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        List<JsonElement> items = new();
        string importUid = "";
        int entryTimezone = 8;
        string entryLang = "zh-cn";

        if (version.StartsWith("v4"))
        {
            if (!root.TryGetProperty("hk4e", out var hk4eList) || hk4eList.GetArrayLength() == 0)
            {
                CrawlerStatus = "文件中未找到有效的抽卡记录";
                IsFetching = false;
                OnErrorAction?.Invoke(CrawlerStatus);
                return;
            }

            var entry = hk4eList.EnumerateArray().FirstOrDefault();
            importUid = entry.TryGetProperty("uid", out var u) ? u.GetString() ?? "" : "";
            entryTimezone = entry.TryGetProperty("timezone", out var tz) ? tz.GetInt32() : 8;
            entryLang = entry.TryGetProperty("lang", out var lg) ? lg.GetString() ?? "zh-cn" : "zh-cn";
            if (entry.TryGetProperty("list", out var listNode) && listNode.ValueKind == JsonValueKind.Array)
            {
                items = listNode.EnumerateArray().ToList();
            }
        }
        else if (version.StartsWith("v2") || version.StartsWith("v3"))
        {
            importUid = infoNode.TryGetProperty("uid", out var u) ? u.GetString() ?? "" : "";
            entryLang = infoNode.TryGetProperty("lang", out var lg) ? lg.GetString() ?? "zh-cn" : "zh-cn";
            if (root.TryGetProperty("list", out var listNode) && listNode.ValueKind == JsonValueKind.Array)
            {
                items = listNode.EnumerateArray().ToList();
            }
        }
        else
        {
            CrawlerStatus = $"不支持的 UIGF 版本：{version}";
            IsFetching = false;
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        if (items.Count == 0)
        {
            CrawlerStatus = "文件中未找到抽卡记录";
            IsFetching = false;
            OnErrorAction?.Invoke(CrawlerStatus);
            return;
        }

        foreach (var x in items)
        {
            if (!x.TryGetProperty("id", out _) || !x.TryGetProperty("item_id", out _) ||
                !x.TryGetProperty("time", out _) || !x.TryGetProperty("gacha_type", out _))
            {
                CrawlerStatus = "文件中存在不完整的记录（缺少 id/item_id/time/gacha_type），请检查文件格式";
                IsFetching = false;
                OnErrorAction?.Invoke(CrawlerStatus);
                return;
            }
        }

        if (!await HandleUidMismatchAsync(importUid)) { IsFetching = false; return; }

        _currentUid = importUid;

        if (_savedMetadata.Count == 0)
        {
            CrawlerStatus = "正在获取物品元数据用于名称映射...";
            await FetchMetadataFromApiAsync();
            IsFetching = true;
        }

        CrawlerStatus = $"正在导入 {items.Count} 条记录...";

        var newLogs = items.Select(uigfItem =>
        {
            var gachaType = uigfItem.GetProperty("gacha_type").GetString() ?? "";
            var time = uigfItem.GetProperty("time").GetString() ?? "";

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
                Id = uigfItem.GetProperty("id").GetString() ?? "",
                Uid = importUid,
                GachaType = gachaType,
                ItemId = uigfItem.GetProperty("item_id").GetString() ?? "",
                Time = time,
                Name = uigfItem.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                RankType = uigfItem.TryGetProperty("rank_type", out var rt) ? rt.GetString() ?? "" : "",
                ItemType = uigfItem.TryGetProperty("item_type", out var it) ? it.GetString() ?? "" : "",
                Lang = entryLang
            };
        }).ToList();

        FillMissingFieldsFromMetadata(newLogs);
        _cachedCharacterLogs = MergeLogs(_cachedCharacterLogs, newLogs.Where(x => GetNormalizedGachaType(x.GachaType) == "301").ToList());
        _cachedWeaponLogs = MergeLogs(_cachedWeaponLogs, newLogs.Where(x => GetNormalizedGachaType(x.GachaType) == "302").ToList());
        _cachedChronicledLogs = MergeLogs(_cachedChronicledLogs, newLogs.Where(x => GetNormalizedGachaType(x.GachaType) == "500").ToList());
        _cachedNoviceLogs = MergeLogs(_cachedNoviceLogs, newLogs.Where(x => GetNormalizedGachaType(x.GachaType) == "100").ToList());
        _cachedStandardLogs = MergeLogs(_cachedStandardLogs, newLogs.Where(x => GetNormalizedGachaType(x.GachaType) == "200").ToList());

        RefreshUIFromCache();
        HasGachaData = true;
        SaveGachaDataAsync();

        var total = _cachedCharacterLogs.Count + _cachedWeaponLogs.Count + _cachedChronicledLogs.Count + _cachedNoviceLogs.Count + _cachedStandardLogs.Count;
        CrawlerStatus = $"导入完成，共 {total} 条记录，正在检查图片资源...";
        IsScraping = true;
        RequestMetadataScrapeAction?.Invoke();
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[Gacha] 导入失败: {ex}");
        CrawlerStatus = $"导入失败: [{ex.GetType().Name}] {ex.Message}";
        IsFetching = false;
        OnErrorAction?.Invoke(CrawlerStatus);
    }

    if (!IsScraping) IsFetching = false;
}

    private void FillMissingFieldsFromMetadata(params List<GachaLogItem>[] logLists)
    {
        if (_savedMetadata.Count == 0) return;
        var byName = new Dictionary<string, ScrapedMetadata>();
        var byItemId = new Dictionary<string, ScrapedMetadata>();
        foreach (var m in _savedMetadata)
        {
            if (!string.IsNullOrEmpty(m.Name) && !string.IsNullOrEmpty(m.ItemId))
                byName[m.Name] = m;
            if (!string.IsNullOrEmpty(m.ItemId))
                byItemId[m.ItemId] = m;
        }

        foreach (var logs in logLists)
        {
            foreach (var log in logs)
            {
                if (string.IsNullOrEmpty(log.ItemId) && !string.IsNullOrEmpty(log.Name)
                    && byName.TryGetValue(log.Name, out var byNameMeta))
                    log.ItemId = byNameMeta.ItemId;

                if (string.IsNullOrEmpty(log.Name) && !string.IsNullOrEmpty(log.ItemId)
                    && byItemId.TryGetValue(log.ItemId, out var byIdMeta))
                    log.Name = byIdMeta.Name;

                if (string.IsNullOrEmpty(log.RankType) && !string.IsNullOrEmpty(log.ItemId)
                    && byItemId.TryGetValue(log.ItemId, out var byIdRankMeta)
                    && !string.IsNullOrEmpty(byIdRankMeta.Rank))
                    log.RankType = byIdRankMeta.Rank;
            }
        }
    }

    private static string GetNormalizedGachaType(string gachaType) => gachaType switch
    {
        "301" or "400" => "301",
        "302" => "302",
        "200" => "200",
        "100" => "100",
        "500" => "500",
        _ => "200"
    };

    private static string GameToUigfGachaType(string gameType) => gameType switch
    {
        "100" => "100",
        "200" => "200",
        "301" => "301",
        "302" => "302",
        "400" => "301",
        "500" => "500",
        _ => gameType
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

            CrawlerStatus = $"武器活动 {weaponLogs.Count} 条，正在获取集录祈愿记录...";
            var chronicledLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "500", count => OnProgress("集录祈愿", count));

            CrawlerStatus = $"集录祈愿 {chronicledLogs.Count} 条，正在获取新手祈愿记录...";
            var noviceLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "100", count => OnProgress("新手祈愿", count));

            CrawlerStatus = $"新手祈愿 {noviceLogs.Count} 条，正在获取常驻祈愿记录...";
            var standardLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "200", count => OnProgress("常驻祈愿", count));

            var allFetched = charLogs.Concat(weaponLogs).Concat(chronicledLogs).Concat(noviceLogs).Concat(standardLogs).ToList();
            var fetchedUid = allFetched.FirstOrDefault(l => !string.IsNullOrEmpty(l.Uid))?.Uid ?? "";

            if (!await HandleUidMismatchAsync(fetchedUid)) { IsFetching = false; return; }

            _currentUid = fetchedUid;

            _cachedCharacterLogs = MergeLogs(_cachedCharacterLogs, charLogs);
            _cachedWeaponLogs = MergeLogs(_cachedWeaponLogs, weaponLogs);
            _cachedChronicledLogs = MergeLogs(_cachedChronicledLogs, chronicledLogs);
            _cachedNoviceLogs = MergeLogs(_cachedNoviceLogs, noviceLogs);
            _cachedStandardLogs = MergeLogs(_cachedStandardLogs, standardLogs);

            FillMissingFieldsFromMetadata(charLogs, weaponLogs, chronicledLogs, noviceLogs, standardLogs);

            var total = charLogs.Count + weaponLogs.Count + chronicledLogs.Count + noviceLogs.Count + standardLogs.Count;
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
        ChronicledFiveStars = new();
        ChronicledFourStars = new();
        StandardFiveStars = new();
        StandardFourStars = new();
        InvalidateAnalysisDashboard();
    }

    private ObservableCollection<GachaDisplayItem> BuildDisplayCollection(List<FiveStarRecord> records, string typeHint, List<GachaPoolMetadata> pools = null)
    {
        var pityStatuses = new PityStatus[records.Count];
        bool wasPreviousLost = false;

        for (var i = records.Count - 1; i >= 0; i--)
        {
            var record = records[i];

            var logItem = new GachaLogItem
            {
                Name = record.Name,
                Time = record.Time,
                RankType = record.Rank.ToString(),
                ItemId = record.ItemId ?? ""
            };

            var pityStatus = pools != null ?
                DeterminePityStatus(logItem, pools, record.PityUsed, wasPreviousLost) :
                PityStatus.None;

            if (record.Rank == 5)
            {
                wasPreviousLost = (pityStatus == PityStatus.LostPity);
            }

            pityStatuses[i] = pityStatus;
        }

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
                ImageUrl = "ms-appx:///Assets/StoreLogo.png",
                PityStatus = pityStatuses[i]
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
            await FetchGachaPoolMetadataAsync();

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
        if (collection == null || collection.Count == 0) return;

        var items = collection.ToList();
        var updates = new List<(GachaDisplayItem item, string imgUrl, string elementUrl)>();

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                ScrapedMetadata match = null;
                if (metaDict.TryGetValue(item.Name, out var exactMatch))
                {
                    match = exactMatch;
                }
                else
                {
                    match = metaDict.Values.FirstOrDefault(x => 
                        x.Name != null && item.Name != null && 
                        (x.Name.Contains(item.Name) || item.Name.Contains(x.Name)));
                }

                if (match != null)
                {
                    var imgUrl = !string.IsNullOrEmpty(match.ImgSrc) ? match.ImgSrc : null;
                    var elementUrl = (item.Type == "角色" || item.Type == "常驻") && !string.IsNullOrEmpty(match.ElementSrc) ? match.ElementSrc : null;
                    if (imgUrl != null || elementUrl != null)
                    {
                        updates.Add((item, imgUrl, elementUrl));
                    }
                }
            }
        });

        if (updates.Count > 0)
        {
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

    private async Task FetchGachaPoolMetadataAsync()
    {
        if (_isFetchingPoolMetadata) return;
        _isFetchingPoolMetadata = true;

        try
        {
            CrawlerStatus = "正在获取卡池元数据...";

            var charMetadata = await _httpClient.GetStringAsync(ApiEndpoints.GachaCharacterMetadataUrl);
            var charPools = JsonSerializer.Deserialize<List<GachaPoolMetadata>>(charMetadata);

            var weaponMetadata = await _httpClient.GetStringAsync(ApiEndpoints.GachaWeaponMetadataUrl);
            var weaponPools = JsonSerializer.Deserialize<List<GachaPoolMetadata>>(weaponMetadata);

            await SavePoolMetadataToDbAsync(charPools, "301");
            await SavePoolMetadataToDbAsync(weaponPools, "302");

            CrawlerStatus = $"卡池元数据更新完成";

            if (_cachedCharacterLogs.Count + _cachedWeaponLogs.Count > 0)
            {
                App.MainWindow.DispatcherQueue.TryEnqueue(() => RefreshUIFromCache());
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] 获取卡池元数据失败: {ex.Message}");
        }
        finally
        {
            _isFetchingPoolMetadata = false;
        }
    }

    private async Task SavePoolMetadataToDbAsync(List<GachaPoolMetadata> pools, string poolType)
    {
        if (pools == null) return;

        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var command = connection.CreateCommand();

        command.CommandText = @"
            INSERT INTO GachaPoolMetadata (Version, PoolType, StartTime, EndTime, UpItems)
            VALUES ($version, $poolType, $startTime, $endTime, $upItems)
            ON CONFLICT(Version, PoolType) DO UPDATE SET
                StartTime=excluded.StartTime,
                EndTime=excluded.EndTime,
                UpItems=excluded.UpItems;
        ";

        foreach (var pool in pools)
        {
            var upItemsJson = JsonSerializer.Serialize(pool.Items.Select(i => i.ItemId));
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$version", pool.Version);
            command.Parameters.AddWithValue("$poolType", poolType);
            command.Parameters.AddWithValue("$startTime", pool.Start);
            command.Parameters.AddWithValue("$endTime", pool.End);
            command.Parameters.AddWithValue("$upItems", upItemsJson);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private List<GachaPoolMetadata> LoadPoolMetadataFromDb(string poolType)
    {
        var pools = new List<GachaPoolMetadata>();
        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Version, StartTime, EndTime, UpItems FROM GachaPoolMetadata WHERE PoolType = $poolType ORDER BY StartTime DESC";
        command.Parameters.AddWithValue("$poolType", poolType);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var upItemsJson = reader.GetString(3);
            var upItems = JsonSerializer.Deserialize<List<int>>(upItemsJson);

            var pool = new GachaPoolMetadata
            {
                Version = reader.GetString(0),
                Start = reader.GetString(1),
                End = reader.GetString(2),
                Items = upItems.Select(itemId => new GachaPoolItem { ItemId = itemId }).ToList()
            };
            pools.Add(pool);
        }

        return pools;
    }

    private bool HasPoolMetadataCache()
    {
        try
        {
            using var connection = new SqliteConnection(_dbConnectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM GachaPoolMetadata";
            var count = Convert.ToInt32(command.ExecuteScalar());
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    private PityStatus DeterminePityStatus(GachaLogItem item, List<GachaPoolMetadata> pools, int pityCount, bool wasPreviousLost)
    {
        if (pools == null || pools.Count == 0)
            return PityStatus.None;

        if (!DateTime.TryParse(item.Time, out var pullTime))
            return PityStatus.None;

        var pool = pools.FirstOrDefault(p =>
        {
            if (!DateTime.TryParse(p.Start, out var startTime) ||
                !DateTime.TryParse(p.End, out var endTime))
                return false;
            return pullTime >= startTime && pullTime <= endTime;
        });

        if (pool == null)
            return PityStatus.None;

        var isUpItem = pool.Items.Any(p => p.ItemId.ToString() == item.ItemId);

        if (item.RankType == "5")
        {
            if (isUpItem)
            {
                return wasPreviousLost ? PityStatus.Guaranteed : PityStatus.SmallPity;
            }
            else
            {
                return PityStatus.LostPity;
            }
        }
        else if (item.RankType == "4" && isUpItem)
        {
            return PityStatus.Up;
        }

        return PityStatus.None;
    }

    partial void OnIsCharacterFourStarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCharacterNoRecords));
        OnPropertyChanged(nameof(ShowCharacterFourDivider));
    }

    partial void OnIsWeaponFourStarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWeaponNoRecords));
        OnPropertyChanged(nameof(ShowWeaponFourDivider));
    }

    partial void OnIsChronicledFourStarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowChronicledNoRecords));
        OnPropertyChanged(nameof(ShowChronicledFourDivider));
    }

    partial void OnIsStandardFourStarVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowStandardNoRecords));
        OnPropertyChanged(nameof(ShowStandardFourDivider));
    }

    partial void OnIsOverviewSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAnalysisSelected));
        OnPropertyChanged(nameof(ShowAnalysisLoading));
        OnPropertyChanged(nameof(ShowAnalysisContent));
    }

    partial void OnIsAnalysisLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAnalysisLoading));
        OnPropertyChanged(nameof(ShowAnalysisContent));
    }

    partial void OnIsAnalysisReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAnalysisLoading));
        OnPropertyChanged(nameof(ShowAnalysisContent));
    }
}
