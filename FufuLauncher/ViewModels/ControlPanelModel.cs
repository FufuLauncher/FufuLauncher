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