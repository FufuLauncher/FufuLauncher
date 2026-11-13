/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Models;
using FufuLauncher.Helpers;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class ControlPanelViewModel : ObservableObject
{
    private readonly GamePlayTimeStore _playTimeStore;
    private CancellationTokenSource _cancellationTokenSource;

    [ObservableProperty] private WeeklyPlayTimeStats _weeklyStats = new();
    [ObservableProperty] private bool _isGameRunning;

    public ControlPanelViewModel(GamePlayTimeStore playTimeStore)
    {
        _playTimeStore = playTimeStore;
        _playTimeStore.PlayTimeChanged += OnPlayTimeStoreChanged;
        _cancellationTokenSource = new CancellationTokenSource();

        _ = InitializeAsync(_cancellationTokenSource.Token);
    }

    private void OnPlayTimeStoreChanged(object? sender, EventArgs e)
    {
        CalculateMonthlyStats();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _playTimeStore.LoadAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            DebugLog.Info("ControlPanelViewModel", $"Failed to load game play time: {ex.Message}");
        }

        if (cancellationToken.IsCancellationRequested) return;

        CalculateMonthlyStats();
        await StartGameMonitoringLoopAsync(cancellationToken);
    }

    public void Cleanup()
    {
        _playTimeStore.PlayTimeChanged -= OnPlayTimeStoreChanged;
        try
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
        }
        catch
        {
        }
    }

    public void UpdateAndSavePlayTime(int secondsToAdd)
    {
        _playTimeStore.AddPlayTime(DateTime.Now, secondsToAdd);
        _ = SavePlayTimeAsync();
    }

    private async Task SavePlayTimeAsync()
    {
        try
        {
            await _playTimeStore.SaveAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ControlPanelViewModel", $"Failed to save game play time: {ex.Message}");
        }
    }

    private void CalculateMonthlyStats()
    {
        var stats = new WeeklyPlayTimeStats();
        var today = DateTime.Now.Date;
        var playTimeData = _playTimeStore.GetDailyPlayTimeSnapshot();
        double totalSeconds = 0;

        for (int i = 0; i < 30; i++)
        {
            var date = today.AddDays(-i);
            var dateKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (playTimeData.TryGetValue(dateKey, out var seconds) && seconds > 0)
            {
                stats.DailyRecords.Add(new GamePlayTimeRecord { Date = date, PlayTimeSeconds = seconds });
                totalSeconds += seconds;
            }
        }

        stats.TotalHours = totalSeconds / 3600.0;
        stats.AverageHours = stats.DailyRecords.Count > 0 ? stats.TotalHours / stats.DailyRecords.Count : 0;
        if (App.MainWindow?.DispatcherQueue is { } dispatcherQueue)
        {
            dispatcherQueue.TryEnqueue(() => WeeklyStats = stats);
        }
    }

    private async Task StartGameMonitoringLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var isRunning = Process.GetProcessesByName("YuanShen").Any() ||
                                Process.GetProcessesByName("GenshinImpact").Any();

                if (App.MainWindow?.DispatcherQueue != null)
                {
                    App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            IsGameRunning = isRunning;
                            if (isRunning)
                            {
                                UpdateAndSavePlayTime(20);
                                if (WeeklyStats?.DailyRecords != null)
                                {
                                    var today = DateTime.Today;
                                    var todayRecord =
                                        WeeklyStats.DailyRecords.FirstOrDefault(r => r.Date.Date == today);
                                    if (todayRecord == null)
                                    {
                                        todayRecord = new GamePlayTimeRecord { Date = today, PlayTimeSeconds = 0 };
                                        WeeklyStats.DailyRecords.Insert(0, todayRecord);
                                    }

                                    todayRecord.PlayTimeSeconds += 20;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLog.Info("ControlPanelViewModel",
                                $"Game monitor inner dispatch exception: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("ControlPanelViewModel", $"Game monitor outer exception: {ex.Message}");
            }

            try
            {
                await Task.Delay(20000, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public class InventoryItem
    {
        public int Id
        {
            get;
            set;
        }

        public string Name
        {
            get;
            set;
        }

        public string Category
        {
            get;
            set;
        }

        public int OwnedCount
        {
            get;
            set;
        }

        public int TotalRequired
        {
            get;
            set;
        }

        public int LackCount => Math.Max(0, TotalRequired - OwnedCount);

        public string IconUrl
        {
            get;
            set;
        }

        public string OwnedDisplay => OwnedCount >= 10000 ? $"{OwnedCount / 10000.0:F1}w" : OwnedCount.ToString();
        public string StatusColor => LackCount > 0 ? "#FF9664" : "#96FF96";
    }

    public class InventoryGroup
    {
        public string Category
        {
            get;
            set;
        }

        public List<InventoryItem> Items
        {
            get;
            set;
        }
    }
}