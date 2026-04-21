using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class ControlPanelModel : ObservableObject
{
    private readonly string _configPath;
    private bool _isLoaded;
    private CancellationTokenSource _cancellationTokenSource;
    private readonly Dictionary<string, long> _playTimeData;

    [ObservableProperty] private WeeklyPlayTimeStats _weeklyStats = new();
    [ObservableProperty] private bool _isGameRunning;

    private List<InventoryItem> _cachedInventory = new();
    private readonly string _inventoryDataPath = Path.Combine(AppContext.BaseDirectory, "inventory.json");

    public List<InventoryGroup> GetGroupedInventory()
    {
        if (!_cachedInventory.Any() && File.Exists(_inventoryDataPath))
        {
            var json = File.ReadAllText(_inventoryDataPath);
            _cachedInventory = JsonSerializer.Deserialize<List<InventoryItem>>(json) ?? new();
        }
        return _cachedInventory.GroupBy(x => x.Category)
            .Select(g => new InventoryGroup { Category = g.Key, Items = g.ToList() })
            .ToList();
    }

    public ControlPanelModel()
    {
        var baseDocsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var folder = Path.Combine(baseDocsFolder, "fufu");

        try
        {
            if (File.Exists(folder))
            {
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher");
            }
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }
        catch
        {
            folder = Path.Combine(AppContext.BaseDirectory, "fufu_data");
        }
        
        try 
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }
        catch 
        {
             folder = AppContext.BaseDirectory;
        }
        
        _configPath = Path.Combine(folder, "FufuConfig.cfg");
        _cancellationTokenSource = new CancellationTokenSource();
        _playTimeData = new Dictionary<string, long>();

        LoadConfig();
        _ = StartGameMonitoringLoopAsync(_cancellationTokenSource.Token);
    }

    public void UpdateAndSavePlayTime(int secondsToAdd)
    {
        var dateKey = DateTime.Now.ToString("yyyy-MM-dd");
        
        if (_playTimeData.ContainsKey(dateKey)) _playTimeData[dateKey] += secondsToAdd;
        else _playTimeData[dateKey] = secondsToAdd;
        
        _ = SaveConfigAsync();
    }
    
    private async Task SaveConfigAsync()
    {
        try
        {
            var config = new ControlPanelConfig
            {
                GamePlayTimeData = _playTimeData,
                LastPlayDate = DateTime.Now.ToString("yyyy-MM-dd")
            };
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存游戏时间配置失败: {ex.Message}");
        }
    }

    public async Task<bool> SyncInventoryFromMihoyoAsync()
    {
        try
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath)) return false;

            var configJson = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!configJson.RootElement.TryGetProperty("cookie", out var cookieElement)) return false;
            string cookie = cookieElement.GetString();
            if (string.IsNullOrEmpty(cookie)) return false;

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            client.DefaultRequestHeaders.Add("Referer", "https://webstatic.mihoyo.com/");
            
            var roleResp = await client.GetStringAsync("https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn");
            using var roleDoc = JsonDocument.Parse(roleResp);
            var role = roleDoc.RootElement.GetProperty("data").GetProperty("list")[0];
            string uid = role.GetProperty("game_uid").GetString();
            string region = role.GetProperty("region").GetString();
            
            var avatarUrl = $"https://api-takumi.mihoyo.com/event/e20200928calculate/v1/sync/avatar/list?game_biz=hk4e_cn&region={region}&uid={uid}";
            var avatarResp = await client.GetStringAsync(avatarUrl);
            using var avatarDoc = JsonDocument.Parse(avatarResp);
            var avatars = avatarDoc.RootElement.GetProperty("data").GetProperty("list");
            var avatarIds = avatars.EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).Take(20).ToList();
            
            var computeUrl = "https://api-takumi.mihoyo.com/event/e20200928calculate/v1/batch_compute";
            var requestData = new { items = avatarIds.Select(id => new { avatar_id = id, level_current = 1, level_target = 90 }).ToList() };

            var content = new StringContent(JsonSerializer.Serialize(requestData), System.Text.Encoding.UTF8, "application/json");
            var computeResp = await client.PostAsync(computeUrl, content);
            var computeResult = await computeResp.Content.ReadAsStringAsync();

            using var computeDoc = JsonDocument.Parse(computeResult);
            var data = computeDoc.RootElement.GetProperty("data");
            
            var newList = new List<InventoryItem>();
            if (data.TryGetProperty("inventory_items", out var invItems))
            {
                foreach (var item in invItems.EnumerateArray())
                {
                    int id = item.GetProperty("id").GetInt32();
                    newList.Add(new InventoryItem
                    {
                        Id = id,
                        Name = item.GetProperty("name").GetString(),
                        OwnedCount = item.GetProperty("num").GetInt32(),
                        IconUrl = item.GetProperty("icon").GetString(),
                        Category = GetCategoryById(id)
                    });
                }
            }
            
            _cachedInventory = newList;
            File.WriteAllText(_inventoryDataPath, JsonSerializer.Serialize(_cachedInventory, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private string GetCategoryById(int id)
    {
        if (id == 202) return "货币";
        if (id >= 104000 && id <= 104003) return "经验书";
        if (id >= 100000 && id <= 110000) return "角色培养";
        return "其它材料";
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
                    CalculateMonthlyStats();
                }
            }
            else _isLoaded = true;
        }
        catch
        {
            _isLoaded = true;
        }
    }

    private void CalculateMonthlyStats()
    {
        var stats = new WeeklyPlayTimeStats();
        var today = DateTime.Now.Date;
        double totalSeconds = 0;
        
        for (int i = 0; i < 30; i++)
        {
            var date = today.AddDays(-i);
            var dateKey = date.ToString("yyyy-MM-dd");

            if (_playTimeData.TryGetValue(dateKey, out var seconds) && seconds > 0)
            {
                stats.DailyRecords.Add(new GamePlayTimeRecord { Date = date, PlayTimeSeconds = seconds });
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
            try
            {
                var isRunning = Process.GetProcessesByName("YuanShen").Any() || Process.GetProcessesByName("GenshinImpact").Any();
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    IsGameRunning = isRunning;
                    if (isRunning)
                    {
                        UpdateAndSavePlayTime(5);
                        if (WeeklyStats != null)
                        {
                            var today = DateTime.Today;
                            var todayRecord = WeeklyStats.DailyRecords.FirstOrDefault(r => r.Date.Date == today);
                            if (todayRecord == null)
                            {
                                todayRecord = new GamePlayTimeRecord { Date = today, PlayTimeSeconds = 0 };
                                WeeklyStats.DailyRecords.Insert(0, todayRecord);
                            }
                            todayRecord.PlayTimeSeconds += 5;
                        }
                    }
                });
            }
            catch { }
            await Task.Delay(5000, token);
        }
    }

    public class InventoryItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public int OwnedCount { get; set; }
        public int TotalRequired { get; set; }
        public int LackCount => Math.Max(0, TotalRequired - OwnedCount);
        public string IconUrl { get; set; }
        public string OwnedDisplay => OwnedCount >= 10000 ? $"{OwnedCount / 10000.0:F1}w" : OwnedCount.ToString();
        public string StatusColor => LackCount > 0 ? "#FF9664" : "#96FF96";
    }

    public class InventoryGroup
    {
        public string Category { get; set; }
        public List<InventoryItem> Items { get; set; }
    }
}

public class ControlPanelConfig
{
    public Dictionary<string, long> GamePlayTimeData { get; set; }
    public string LastPlayDate { get; set; }
}