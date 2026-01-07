using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class ControlPanelModel : ObservableObject
{
    private const string TargetProcessName = "yuanshen";
    private const string TargetProcessNameAlt = "GenshinImpact";
    
    private readonly string _configPath;
    private bool _isLoaded;
    private CancellationTokenSource _cancellationTokenSource;

    private DateTime? _gameStartTime;
    private readonly Dictionary<string, long> _playTimeData;

    [ObservableProperty]
    private WeeklyPlayTimeStats _weeklyStats = new();

    [ObservableProperty]
    private bool _isGameRunning;

    public ControlPanelModel()
    {
        _configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
        _cancellationTokenSource = new CancellationTokenSource();
        _playTimeData = new Dictionary<string, long>();

        LoadConfig();
        
        _ = StartGameMonitoringLoopAsync(_cancellationTokenSource.Token);
    }
    
    private (string Name, int Id)? FindTargetProcess()
    {
        var processes = Process.GetProcessesByName(TargetProcessName);
        if (processes.Length > 0) return (processes[0].ProcessName, processes[0].Id);

        processes = Process.GetProcessesByName(TargetProcessNameAlt);
        if (processes.Length > 0) return (processes[0].ProcessName, processes[0].Id);

        return null;
    }

    private async Task StartGameMonitoringLoopAsync(CancellationToken token)
    {
        bool wasRunning = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var processInfo = FindTargetProcess();
                bool isRunning = processInfo.HasValue;

                if (isRunning && !wasRunning)
                {
                    _gameStartTime = DateTime.Now;
                    App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        IsGameRunning = true;
                    });
                    Debug.WriteLine($"[GameMonitor] Game started at {_gameStartTime}");
                }
                else if (!isRunning && wasRunning)
                {
                    if (_gameStartTime.HasValue)
                    {
                        var playTime = DateTime.Now - _gameStartTime.Value;
                        UpdateTodayPlayTime(playTime);
                        Debug.WriteLine($"[GameMonitor] Game stopped. Play time: {playTime}");
                    }

                    App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        IsGameRunning = false;
                    });
                    _gameStartTime = null;
                }

                wasRunning = isRunning;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameMonitor] Error: {ex.Message}");
            }

            await Task.Delay(5000, token);
        }
    }

    private void UpdateTodayPlayTime(TimeSpan additionalTime)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        if (!_playTimeData.ContainsKey(today))
        {
            _playTimeData[today] = 0;
        }

        _playTimeData[today] += (long)additionalTime.TotalSeconds;

        var thirtyDaysAgo = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");
        var keysToRemove = _playTimeData.Keys.Where(k => string.Compare(k, thirtyDaysAgo) < 0).ToList();
        foreach (var key in keysToRemove)
        {
            _playTimeData.Remove(key);
        }

        CalculateWeeklyStats();
        SaveConfig();
    }

    private void CalculateWeeklyStats()
    {
        var stats = new WeeklyPlayTimeStats();
        var today = DateTime.Now.Date;
        var startOfWeek = today.AddDays(-(int)today.DayOfWeek);

        for (int i = 0; i < 7; i++)
        {
            var date = startOfWeek.AddDays(i);
            var dateKey = date.ToString("yyyy-MM-dd");

            if (_playTimeData.TryGetValue(dateKey, out var seconds) && seconds > 0)
            {
                stats.DailyRecords.Add(new GamePlayTimeRecord
                {
                    Date = date,
                    PlayTimeSeconds = seconds
                });
            }
        }

        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            WeeklyStats = stats;
        });
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<ControlPanelConfig>(json);
                if (config != null)
                {
                    _isLoaded = false;
                    
                    if (config.GamePlayTimeData != null)
                    {
                        foreach (var kvp in config.GamePlayTimeData)
                        {
                            _playTimeData[kvp.Key] = kvp.Value;
                        }
                    }

                    _isLoaded = true;
                    CalculateWeeklyStats();
                }
            }
            else
            {
                _isLoaded = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading config: {ex.Message}");
            _isLoaded = true;
        }
    }

    private async void SaveConfig()
    {
        if (!_isLoaded) return;
        try
        {
            var config = new ControlPanelConfig
            {
                GamePlayTimeData = _playTimeData,
                LastPlayDate = DateTime.Now.ToString("yyyy-MM-dd")
            };

            var dir = Path.GetDirectoryName(_configPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config);
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error saving config: {ex.Message}");
        }
    }
}

public class ControlPanelConfig
{
    public Dictionary<string, long> GamePlayTimeData { get; set; }
    public string LastPlayDate { get; set; }

}