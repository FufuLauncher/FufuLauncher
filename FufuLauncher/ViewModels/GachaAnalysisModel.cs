using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Models;
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

    private List<GachaLogItem> _cachedCharacterLogs = new();
    private List<GachaLogItem> _cachedWeaponLogs = new();
    private List<GachaLogItem> _cachedStandardLogs = new();
    private List<ScrapedMetadata> _savedMetadata = new();

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

    public Action RequestMetadataScrapeAction;

    public GachaAnalysisModel()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu");
        try
        {
            if (File.Exists(folder)) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }
        catch
        {
            folder = Path.Combine(AppContext.BaseDirectory, "fufu_data");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }

        _gachaDataPath = Path.Combine(folder, "gacha_data.json");
        _dbConnectionString = $"Data Source={Path.Combine(folder, "metadata.db")}";
        _gachaService = new GachaService();

        InitializeDatabase();
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
        ";
        command.ExecuteNonQuery();
    }

    private void LoadMetadataFromDb()
    {
        _savedMetadata.Clear();
        App.MainWindow.DispatcherQueue.TryEnqueue(() => AllMetadataPreview.Clear());

        using var connection = new SqliteConnection(_dbConnectionString);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, ImgSrc, ElementSrc, Type FROM Metadata";
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
            INSERT INTO Metadata (Name, ImgSrc, ElementSrc, Type)
            VALUES ($name, $imgSrc, $elementSrc, $type)
            ON CONFLICT(Name) DO UPDATE SET
                ImgSrc=excluded.ImgSrc,
                ElementSrc=excluded.ElementSrc,
                Type=excluded.Type;
        ";

        var nameParam = command.CreateParameter(); nameParam.ParameterName = "$name"; command.Parameters.Add(nameParam);
        var imgParam = command.CreateParameter(); imgParam.ParameterName = "$imgSrc"; command.Parameters.Add(imgParam);
        var eleParam = command.CreateParameter(); eleParam.ParameterName = "$elementSrc"; command.Parameters.Add(eleParam);
        var typeParam = command.CreateParameter(); typeParam.ParameterName = "$type"; command.Parameters.Add(typeParam);

        foreach (var item in newItems)
        {
            nameParam.Value = item.Name ?? "";
            imgParam.Value = item.ImgSrc ?? "";
            eleParam.Value = item.ElementSrc ?? "";
            typeParam.Value = item.Type ?? "";
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public async Task ClearGachaDataAsync()
    {
        try
        {
            _cachedCharacterLogs.Clear();
            _cachedWeaponLogs.Clear();
            _cachedStandardLogs.Clear();
            
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ClearCollections();
                CharacterStats = new GachaStatistic { PoolName = "角色活动" };
                WeaponStats = new GachaStatistic { PoolName = "武器活动" };
                StandardStats = new GachaStatistic { PoolName = "常驻祈愿" };
                GachaUrl = string.Empty;
                CrawlerStatus = "数据已清空";
            });
            
            if (File.Exists(_gachaDataPath)) File.Delete(_gachaDataPath);
            WeakReferenceMessenger.Default.Send(new NotificationMessage("删除成功", "本地抽卡记录已被彻底清除", NotificationType.Success, 3000));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage("删除失败", $"详细信息: {ex.Message}", NotificationType.Error, 5000));
        }
    }

    public async Task LoadSavedGachaDataAsync()
    {
        LoadMetadataFromDb();

        if (!File.Exists(_gachaDataPath)) return;
        try
        {
            if (_cachedCharacterLogs.Count > 0) { RefreshUIFromCache(); return; }
            var json = await File.ReadAllTextAsync(_gachaDataPath);
            var data = JsonSerializer.Deserialize<LocalGachaData>(json);

            if (data != null)
            {
                GachaUrl = data.Url;
                _cachedCharacterLogs = data.CharacterLogs ?? new();
                _cachedWeaponLogs = data.WeaponLogs ?? new();
                _cachedStandardLogs = data.StandardLogs ?? new();

                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    RefreshUIFromCache();
                    CrawlerStatus = _savedMetadata.Count > 0 ? "已加载本地数据和图片资源缓存" : "已加载本地历史记录";
                });
            }
        }
        catch { }
    }

    private async Task SaveGachaDataAsync()
    {
        try
        {
            var data = new LocalGachaData
            {
                Url = GachaUrl,
                CharacterLogs = _cachedCharacterLogs,
                WeaponLogs = _cachedWeaponLogs,
                StandardLogs = _cachedStandardLogs
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_gachaDataPath, json);
        }
        catch { }
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
        ClearCollections();
        CharacterStats = _gachaService.AnalyzePool("301", _cachedCharacterLogs.OrderBy(x => x.Id).ToList());
        PopulateDisplayCollection(CharacterFiveStars, CharacterStats.FiveStarRecords, "角色");
        PopulateDisplayCollection(CharacterFourStars, CharacterStats.FourStarRecords, "角色");

        WeaponStats = _gachaService.AnalyzePool("302", _cachedWeaponLogs.OrderBy(x => x.Id).ToList());
        PopulateDisplayCollection(WeaponFiveStars, WeaponStats.FiveStarRecords, "武器");
        PopulateDisplayCollection(WeaponFourStars, WeaponStats.FourStarRecords, "武器");

        StandardStats = _gachaService.AnalyzePool("200", _cachedStandardLogs.OrderBy(x => x.Id).ToList());
        PopulateDisplayCollection(StandardFiveStars, StandardStats.FiveStarRecords, "常驻");
        PopulateDisplayCollection(StandardFourStars, StandardStats.FourStarRecords, "常驻");

        if (_savedMetadata != null && _savedMetadata.Count > 0) _ = ApplyMetadataToUIAsync(_savedMetadata);
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

            CrawlerStatus = "正在更新角色活动记录...";
            _cachedCharacterLogs = MergeLogs(_cachedCharacterLogs, await _gachaService.FetchGachaLogAsync(baseUrl, "301"));

            CrawlerStatus = "正在更新武器活动记录...";
            _cachedWeaponLogs = MergeLogs(_cachedWeaponLogs, await _gachaService.FetchGachaLogAsync(baseUrl, "302"));

            CrawlerStatus = "正在更新常驻祈愿记录...";
            _cachedStandardLogs = MergeLogs(_cachedStandardLogs, await _gachaService.FetchGachaLogAsync(baseUrl, "200"));

            RefreshUIFromCache();
            await SaveGachaDataAsync();

            CrawlerStatus = "数据更新完成，正在检查图片资源...";
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
        CharacterFiveStars.Clear();
        CharacterFourStars.Clear();
        WeaponFiveStars.Clear();
        WeaponFourStars.Clear();
        StandardFiveStars.Clear();
        StandardFourStars.Clear();
    }

    private void PopulateDisplayCollection(ObservableCollection<GachaDisplayItem> collection, List<FiveStarRecord> records, string typeHint)
    {
        foreach (var record in records)
        {
            collection.Add(new GachaDisplayItem
            {
                Name = record.Name,
                Count = record.PityUsed,
                Time = record.Time,
                Rank = record.Rank,
                Type = typeHint,
                ImageUrl = "ms-appx:///Assets/StoreLogo.png"
            });
        }
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
        foreach (var item in items)
        {
            ScrapedMetadata match = null;
            if (metaDict.TryGetValue(item.Name, out var exactMatch)) match = exactMatch;
            else match = metaDict.Values.FirstOrDefault(x => x.Name.Contains(item.Name) || item.Name.Contains(x.Name));

            if (match != null)
            {
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!string.IsNullOrEmpty(match.ImgSrc)) item.ImageUrl = match.ImgSrc;
                    if ((item.Type == "角色" || item.Type == "常驻") && !string.IsNullOrEmpty(match.ElementSrc))
                    {
                        item.ElementUrl = match.ElementSrc;
                    }
                });
                await Task.Delay(10); 
            }
        }
    }
}