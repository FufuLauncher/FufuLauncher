using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Models;
using FufuLauncher.Services; 

namespace FufuLauncher.ViewModels;

public class LocalGachaData
{
    public string Url { get; set; }
    public List<GachaLogItem> CharacterLogs { get; set; } = new();
    public List<GachaLogItem> WeaponLogs { get; set; } = new();
    public List<GachaLogItem> StandardLogs { get; set; } = new();
    
    public List<ScrapedMetadata> CachedMetadata { get; set; } = new();
}

public partial class ControlPanelModel : ObservableObject
{
    private readonly string _configPath;
    private readonly string _gachaDataPath;
    private bool _isLoaded;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly Dictionary<string, long> _playTimeData;
    private readonly GachaService _gachaService; 
    private List<GachaLogItem> _cachedCharacterLogs = new();
    private List<GachaLogItem> _cachedWeaponLogs = new();
    private List<GachaLogItem> _cachedStandardLogs = new();
    private List<ScrapedMetadata> _savedMetadata = new();
    
    [ObservableProperty] private WeeklyPlayTimeStats _weeklyStats = new();
    
    [ObservableProperty] private bool _isGameRunning; 
    
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

    public Action RequestMetadataScrapeAction;

    public ControlPanelModel()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu");
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        _configPath = Path.Combine(folder, "FufuConfig.cfg");
        _gachaDataPath = Path.Combine(folder, "gacha_data.json");

        _cancellationTokenSource = new CancellationTokenSource();
        _playTimeData = new Dictionary<string, long>();
        _gachaService = new GachaService(); 

        LoadConfig();
        
        _ = LoadSavedGachaDataAsync(); 
        
        _ = StartGameMonitoringLoopAsync(_cancellationTokenSource.Token);
    }
    
    private async Task LoadSavedGachaDataAsync()
    {
        if (!File.Exists(_gachaDataPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(_gachaDataPath);
            var data = JsonSerializer.Deserialize<LocalGachaData>(json);

            if (data != null)
            {
                GachaUrl = data.Url;

                _cachedCharacterLogs = data.CharacterLogs ?? new();
                _cachedWeaponLogs = data.WeaponLogs ?? new();
                _cachedStandardLogs = data.StandardLogs ?? new();
                _savedMetadata = data.CachedMetadata ?? new();

                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    RefreshUIFromCache();
                    
                    if (_savedMetadata.Count > 0)
                    {
                        ApplyMetadataToUI(_savedMetadata);
                        CrawlerStatus = "已加载本地数据和图片缓存";
                    }
                    else
                    {
                        CrawlerStatus = "已加载本地历史记录";
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载本地数据失败: {ex.Message}");
        }
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
                StandardLogs = _cachedStandardLogs,
                CachedMetadata = _savedMetadata
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_gachaDataPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存数据失败: {ex.Message}");
        }
    }

    private List<GachaLogItem> MergeLogs(List<GachaLogItem> existing, List<GachaLogItem> incoming)
    {
        if (existing == null) existing = new List<GachaLogItem>();
        if (incoming == null || incoming.Count == 0) return existing;

        var dict = existing.ToDictionary(x => x.Id);
        foreach (var item in incoming)
        {
            if (!dict.ContainsKey(item.Id))
            {
                dict[item.Id] = item;
            }
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

        if (_savedMetadata != null && _savedMetadata.Count > 0)
        {
            ApplyMetadataToUI(_savedMetadata);
        }
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
            var newCharLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "301");
            _cachedCharacterLogs = MergeLogs(_cachedCharacterLogs, newCharLogs);

            CrawlerStatus = "正在更新武器活动记录...";
            var newWeaponLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "302");
            _cachedWeaponLogs = MergeLogs(_cachedWeaponLogs, newWeaponLogs);

            CrawlerStatus = "正在更新常驻祈愿记录...";
            var newStandardLogs = await _gachaService.FetchGachaLogAsync(baseUrl, "200");
            _cachedStandardLogs = MergeLogs(_cachedStandardLogs, newStandardLogs);

            RefreshUIFromCache();
            
            await SaveGachaDataAsync();

            CrawlerStatus = "数据更新完成，正在检查图片资源...";
            
            IsScraping = true;
            RequestMetadataScrapeAction?.Invoke();
        }
        catch (Exception ex)
        {
            CrawlerStatus = $"更新失败: {ex.Message}";
            Debug.WriteLine(ex);
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
        
        if (scrapedData == null || scrapedData.Count == 0)
        {
            CrawlerStatus = "未找到新图片资源，使用缓存或默认图标";
            IsScraping = false;
            return;
        }

        CrawlerStatus = $"更新了 {scrapedData.Count} 个图片资源";
        
        var metaDict = _savedMetadata.ToDictionary(x => x.Name);
        foreach (var item in scrapedData)
        {
            metaDict[item.Name] = item;
        }
        _savedMetadata = metaDict.Values.ToList();
        
        ApplyMetadataToUI(_savedMetadata);
        
        _ = SaveGachaDataAsync();

        IsScraping = false;
    }
    
    private void ApplyMetadataToUI(List<ScrapedMetadata> metadataList)
    {
        if (metadataList == null || metadataList.Count == 0) return;

        var metaDict = metadataList
            .GroupBy(x => x.Name)
            .ToDictionary(g => g.Key, g => g.First());

        UpdateCollectionImages(CharacterFiveStars, metaDict);
        UpdateCollectionImages(CharacterFourStars, metaDict);
        UpdateCollectionImages(WeaponFiveStars, metaDict);
        UpdateCollectionImages(WeaponFourStars, metaDict);
        UpdateCollectionImages(StandardFiveStars, metaDict);
        UpdateCollectionImages(StandardFourStars, metaDict);
    }

    private void UpdateCollectionImages(ObservableCollection<GachaDisplayItem> collection, Dictionary<string, ScrapedMetadata> metaDict)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var item in collection)
            {
                ScrapedMetadata match = null;

                if (metaDict.TryGetValue(item.Name, out var exactMatch))
                {
                    match = exactMatch;
                }
                else
                {
                    match = metaDict.Values.FirstOrDefault(x => x.Name.Contains(item.Name) || item.Name.Contains(x.Name));
                }

                if (match != null)
                {
                    if (!string.IsNullOrEmpty(match.ImgSrc))
                        item.ImageUrl = match.ImgSrc;
                    
                    if ((item.Type == "角色" || item.Type == "常驻") && !string.IsNullOrEmpty(match.ElementSrc)) 
                    {
                         item.ElementUrl = match.ElementSrc; 
                    }
                }
            }
        });
    }
    
    private async void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = await File.ReadAllTextAsync(_configPath);
                var config = JsonSerializer.Deserialize<ControlPanelConfig>(json);
                if (config != null)
                {
                    _isLoaded = false;
                    if (config.GamePlayTimeData != null)
                    {
                        foreach (var kvp in config.GamePlayTimeData) _playTimeData[kvp.Key] = kvp.Value;
                    }
                    _isLoaded = true;
                    CalculateWeeklyStats();
                }
            }
            else _isLoaded = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading config: {ex.Message}");
            _isLoaded = true;
        }
    }

    private void CalculateWeeklyStats()
    {
        var stats = new WeeklyPlayTimeStats();
        var today = DateTime.Now.Date;
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek); 
        double totalSeconds = 0;

        for (int i = 0; i < 7; i++)
        {
            var date = startOfWeek.AddDays(i);
            var dateKey = date.ToString("yyyy-MM-dd");
            if (_playTimeData.TryGetValue(dateKey, out var seconds) && seconds > 0)
            {
                stats.DailyRecords.Add(new GamePlayTimeRecord
                {
                    DayOfWeek = date.ToString("ddd"),
                    DisplayDate = date.ToString("MM/dd"),
                    DisplayTime = TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm")
                });
                totalSeconds += seconds;
            }
        }
        
        stats.TotalHours = totalSeconds / 3600.0;
        stats.AverageHours = stats.DailyRecords.Count > 0 ? stats.TotalHours / stats.DailyRecords.Count : 0;
        
        App.MainWindow.DispatcherQueue.TryEnqueue(() => WeeklyStats = stats);
    }

    private async Task StartGameMonitoringLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(5000, token);
        }
    }
}


public class ControlPanelConfig
{
    public Dictionary<string, long> GamePlayTimeData { get; set; }
    public string LastPlayDate { get; set; }
}

public class WeeklyPlayTimeStats
{
    public double TotalHours { get; set; }
    public double AverageHours { get; set; }
    public string TotalHoursFormatted => $"{TotalHours:F1}h";
    public string AverageHoursFormatted => $"{AverageHours:F1}h";
    public ObservableCollection<GamePlayTimeRecord> DailyRecords { get; set; } = new();
}

public class GamePlayTimeRecord
{
    public string DayOfWeek { get; set; }
    public string DisplayDate { get; set; }
    public string DisplayTime { get; set; }
}