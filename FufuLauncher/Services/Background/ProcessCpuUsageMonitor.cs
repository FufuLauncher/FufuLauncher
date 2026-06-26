/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using FufuLauncher.Contracts.Services;
using Microsoft.UI.Dispatching;
using FufuLauncher.Views;

namespace FufuLauncher.Services.Background;

public sealed class ProcessCpuUsageMonitor : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequiredHighUsageDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(30);
    public const double DefaultCpuThreshold = 30.0;
    public const string IsEnabledSettingKey = "IsCpuUsageWarningEnabled";
    public const string ThresholdSettingKey = "CpuUsageWarningThreshold";

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Process _currentProcess = Process.GetCurrentProcess();
    private Task? _monitorTask;
    private TimeSpan _lastProcessorTime;
    private DateTimeOffset _lastSampleTime;
    private TimeSpan _highUsageDuration;
    private DateTimeOffset _lastAlertTime = DateTimeOffset.MinValue;
    private bool _isAlertOpen;

    public ProcessCpuUsageMonitor(DispatcherQueue dispatcherQueue, ILocalSettingsService localSettingsService)
    {
        _dispatcherQueue = dispatcherQueue;
        _localSettingsService = localSettingsService;
        _lastProcessorTime = _currentProcess.TotalProcessorTime;
        _lastSampleTime = DateTimeOffset.UtcNow;
    }

    public void Start()
    {
        if (_monitorTask != null)
        {
            return;
        }

        _monitorTask = Task.Run(MonitorAsync);
    }

    private async Task MonitorAsync()
    {
        using var timer = new PeriodicTimer(SampleInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(_cancellationTokenSource.Token))
            {
                var settings = await ReadSettingsAsync();
                if (!settings.IsEnabled)
                {
                    _highUsageDuration = TimeSpan.Zero;
                    continue;
                }

                var cpuUsage = GetCurrentCpuUsage();
                if (cpuUsage > settings.Threshold)
                {
                    _highUsageDuration += SampleInterval;
                }
                else
                {
                    _highUsageDuration = TimeSpan.Zero;
                }

                if (_highUsageDuration >= RequiredHighUsageDuration)
                {
                    TryShowAlert(cpuUsage);
                    _highUsageDuration = TimeSpan.Zero;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuUsageMonitor] 检测已停止: {ex.Message}");
        }
    }

    private async Task<(bool IsEnabled, double Threshold)> ReadSettingsAsync()
    {
        try
        {
            var enabledValue = await _localSettingsService.ReadSettingAsync(IsEnabledSettingKey);
            var thresholdValue = await _localSettingsService.ReadSettingAsync(ThresholdSettingKey);

            var isEnabled = enabledValue == null || Convert.ToBoolean(enabledValue);
            var threshold = thresholdValue != null ? Convert.ToDouble(thresholdValue) : DefaultCpuThreshold;
            return (isEnabled, Math.Clamp(threshold, 5.0, 100.0));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuUsageMonitor] 读取设置失败: {ex.Message}");
            return (true, DefaultCpuThreshold);
        }
    }

    private double GetCurrentCpuUsage()
    {
        try
        {
            _currentProcess.Refresh();
            var now = DateTimeOffset.UtcNow;
            var processorTime = _currentProcess.TotalProcessorTime;
            var processorDelta = processorTime - _lastProcessorTime;
            var timeDelta = now - _lastSampleTime;

            _lastProcessorTime = processorTime;
            _lastSampleTime = now;

            if (timeDelta.TotalMilliseconds <= 0)
            {
                return 0;
            }

            return processorDelta.TotalMilliseconds / (timeDelta.TotalMilliseconds * Environment.ProcessorCount) * 100.0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CpuUsageMonitor] CPU采样失败: {ex.Message}");
            return 0;
        }
    }

    private void TryShowAlert(double cpuUsage)
    {
        if (_isAlertOpen || DateTimeOffset.UtcNow - _lastAlertTime < AlertCooldown || IsObsRunning())
        {
            return;
        }

        _lastAlertTime = DateTimeOffset.UtcNow;
        _isAlertOpen = true;

        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var window = new HighCpuUsageWarningWindow(cpuUsage);
                window.Closed += (_, _) => _isAlertOpen = false;
                window.Activate();
            }
            catch (Exception ex)
            {
                _isAlertOpen = false;
                Debug.WriteLine($"[CpuUsageMonitor] 显示提示窗口失败: {ex.Message}");
            }
        });
    }

    private static bool IsObsRunning()
    {
        return Process.GetProcessesByName("obs64").Length > 0
            || Process.GetProcessesByName("obs32").Length > 0
            || Process.GetProcessesByName("obs").Length > 0
            || Process.GetProcessesByName("obs-studio").Length > 0;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _currentProcess.Dispose();
    }
}

