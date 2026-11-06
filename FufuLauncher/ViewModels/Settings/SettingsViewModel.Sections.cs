/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using Microsoft.UI.Dispatching;
using FufuLauncher.Helpers;

namespace FufuLauncher.ViewModels;

public static class SettingsSectionIds
{
    public const string Appearance = "AppearanceItem";
    public const string HomeCards = "HomeCardsItem";
    public const string GameAnnouncement = "GameAnnouncementItem";
    public const string Widgets = "WidgetsItem";
    public const string Notes = "NotesItem";
    public const string HomeText = "HomeTextItem";
    public const string ImageSource = "ImageSourceItem";
    public const string Background = "BackgroundItem";
    public const string WindowEffects = "WindowEffectsItem";
    public const string LaunchConfig = "LaunchConfigItem";
    public const string Screenshots = "ScreenshotSettingsItem";
    public const string QuickFps = "QuickFpsItem";
    public const string Checkin = "CheckinSettingsItem";
    public const string Language = "LanguageItem";
    public const string WindowBehavior = "WindowBehaviorItem";
    public const string Startup = "StartupItem";
    public const string StartupSound = "StartupSoundItem";
    public const string AdvancedOptions = "AdvancedOptionsItem";
    public const string StoragePaths = "StoragePathItem";
    public const string Updates = "UpdateItem";
    public const string About = "AboutItem";
    public const string SecurityAuth = "SecurityAuthItem";
    public const string ServiceStatus = "ServiceStatusItem";
    public const string Mcp = "McpItem";
}

public partial class SettingsViewModel
{
    private static readonly string[] _immediateSectionIds =
    {
        SettingsSectionIds.Appearance,
        SettingsSectionIds.HomeCards
    };

    private readonly List<string> _sectionOrder = new();
    private readonly Dictionary<string, Func<Task>> _sectionLoaders = new();
    private readonly Dictionary<string, TaskCompletionSource<bool>> _sectionSignals = new();
    private readonly HashSet<string> _pendingSections = new();
    private readonly HashSet<string> _loadingSections = new();
    private readonly List<string> _requestedSections = new();
    private readonly object _sectionLock = new();

    private bool _sectionPassRunning;

    public event Func<string, Task>? SectionLoadCompleted;

    private void RegisterSettingSections()
    {
        RegisterSection(SettingsSectionIds.Appearance, LoadAppearanceSectionAsync);
        RegisterSection(SettingsSectionIds.HomeCards, LoadHomeCardsSectionAsync);
        RegisterSection(SettingsSectionIds.GameAnnouncement, LoadGameAnnouncementSectionAsync);
        RegisterSection(SettingsSectionIds.Widgets, LoadWidgetsSectionAsync);
        RegisterSection(SettingsSectionIds.Notes, LoadNotesSectionAsync);
        RegisterSection(SettingsSectionIds.HomeText, LoadHomeTextSectionAsync);
        RegisterSection(SettingsSectionIds.ImageSource, () => Task.CompletedTask);
        RegisterSection(SettingsSectionIds.Background, LoadBackgroundSectionAsync);
        RegisterSection(SettingsSectionIds.WindowEffects, LoadWindowEffectsSectionAsync);
        RegisterSection(SettingsSectionIds.LaunchConfig, LoadLaunchConfigSectionAsync);
        RegisterSection(SettingsSectionIds.Screenshots, LoadScreenshotSectionAsync);
        RegisterSection(SettingsSectionIds.QuickFps, () => Task.CompletedTask);
        RegisterSection(SettingsSectionIds.Checkin, LoadCheckinSectionAsync);
        RegisterSection(SettingsSectionIds.Language, LoadLanguageSectionAsync);
        RegisterSection(SettingsSectionIds.WindowBehavior, LoadWindowBehaviorSectionAsync);
        RegisterSection(SettingsSectionIds.Startup, LoadStartupSectionAsync);
        RegisterSection(SettingsSectionIds.StartupSound, LoadStartupSoundSectionAsync);
        RegisterSection(SettingsSectionIds.AdvancedOptions, LoadAdvancedOptionsSectionAsync);
        RegisterSection(SettingsSectionIds.StoragePaths, LoadStoragePathSectionAsync);
        RegisterSection(SettingsSectionIds.Updates, LoadUpdateSectionAsync);
        RegisterSection(SettingsSectionIds.About, LoadAboutSectionAsync);
        RegisterSection(SettingsSectionIds.SecurityAuth, LoadSecurityAuthSectionAsync);
        RegisterSection(SettingsSectionIds.ServiceStatus, () => Task.CompletedTask);
        RegisterSection(SettingsSectionIds.Mcp, () => Task.CompletedTask);
    }

    private void RegisterSection(string sectionId, Func<Task> loader)
    {
        if (_sectionLoaders.ContainsKey(sectionId))
            return;

        _sectionOrder.Add(sectionId);
        _sectionLoaders[sectionId] = loader;

        lock (_sectionLock)
        {
            _pendingSections.Add(sectionId);
        }
    }

    public async Task ReloadSettingsAsync()
    {
        ResetSectionLoadState();

        foreach (var sectionId in _immediateSectionIds)
        {
            await EnsureSectionLoadedAsync(sectionId);
        }

        EnsurePassRunning();
    }

    public async Task RequestSectionAsync(string sectionId)
    {
        while (true)
        {
            lock (_sectionLock)
            {
                if (!_pendingSections.Contains(sectionId))
                    return;

                if (!_requestedSections.Contains(sectionId))
                    _requestedSections.Add(sectionId);
            }

            EnsurePassRunning();
            await EnsureSectionLoadedAsync(sectionId);

            lock (_sectionLock)
            {
                if (!_pendingSections.Contains(sectionId))
                    return;
            }
        }
    }

    private Task EnsureSectionLoadedAsync(string sectionId)
    {
        TaskCompletionSource<bool> signal;
        var startLoad = false;

        lock (_sectionLock)
        {
            if (!_sectionLoaders.ContainsKey(sectionId) || !_pendingSections.Contains(sectionId))
                return Task.CompletedTask;

            if (_sectionSignals.TryGetValue(sectionId, out var existing))
            {
                signal = existing;
            }
            else
            {
                signal = CreateSectionSignal();
                _sectionSignals[sectionId] = signal;
            }

            if (_loadingSections.Add(sectionId))
                startLoad = true;
        }

        if (startLoad)
            _ = RunSectionLoadAsync(sectionId, signal);

        return signal.Task;
    }

    private async Task RunSectionLoadAsync(string sectionId, TaskCompletionSource<bool> signal)
    {
        try
        {
            await LoadSectionAsync(sectionId);
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Sections",
                $"SettingsViewModel: section {sectionId} load exception - {ex.Message}");
        }
        finally
        {
            lock (_sectionLock)
            {
                if (ReferenceEquals(GetSectionSignal(sectionId), signal))
                {
                    _loadingSections.Remove(sectionId);
                    _pendingSections.Remove(sectionId);
                }
            }

            signal.TrySetResult(true);
        }
    }

    private async Task LoadSectionAsync(string sectionId)
    {
        var wasInitializing = _isInitializing;
        _isInitializing = true;
        try
        {
            if (_sectionLoaders.TryGetValue(sectionId, out var loader))
                await loader();
        }
        catch (Exception ex)
        {
            DebugLog.Info("SettingsViewModel.Sections",
                $"SettingsViewModel: section {sectionId} load failed - {ex.Message}");
        }
        finally
        {
            _isInitializing = wasInitializing;
        }

        await NotifySectionLoadCompletedAsync(sectionId);
    }

    private async Task NotifySectionLoadCompletedAsync(string sectionId)
    {
        var handlers = SectionLoadCompleted;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                await ((Func<string, Task>)handler)(sectionId);
            }
            catch (Exception ex)
            {
                DebugLog.Info("SettingsViewModel.Sections",
                    $"SettingsViewModel: section {sectionId} UI refresh exception - {ex.Message}");
            }
        }
    }

    private void EnsurePassRunning()
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(EnsurePassRunning);
            return;
        }

        lock (_sectionLock)
        {
            if (_sectionPassRunning || _pendingSections.Count == 0)
                return;

            _sectionPassRunning = true;
        }

        _ = RunSectionPassAsync();
    }

    private async Task RunSectionPassAsync()
    {
        while (true)
        {
            string? next;
            lock (_sectionLock)
            {
                next = NextPendingSection();

                if (next is null)
                {
                    _sectionPassRunning = false;
                    return;
                }
            }

            await EnsureSectionLoadedAsync(next);
            await YieldToDispatcherAsync();
        }
    }

    private string? NextPendingSection()
    {
        foreach (var sectionId in _requestedSections)
        {
            if (_pendingSections.Contains(sectionId))
                return sectionId;
        }

        foreach (var sectionId in _sectionOrder)
        {
            if (_pendingSections.Contains(sectionId))
                return sectionId;
        }

        return null;
    }

    private void ResetSectionLoadState()
    {
        lock (_sectionLock)
        {
            foreach (var signal in _sectionSignals.Values)
            {
                signal.TrySetResult(true);
            }

            _sectionSignals.Clear();
            _pendingSections.Clear();
            _loadingSections.Clear();
            _requestedSections.Clear();

            foreach (var sectionId in _sectionOrder)
            {
                _pendingSections.Add(sectionId);
                _sectionSignals[sectionId] = CreateSectionSignal();
            }
        }
    }

    private TaskCompletionSource<bool>? GetSectionSignal(string sectionId) =>
        _sectionSignals.TryGetValue(sectionId, out var signal) ? signal : null;

    private static TaskCompletionSource<bool> CreateSectionSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task YieldToDispatcherAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => completion.TrySetResult(true)))
            completion.TrySetResult(true);

        return completion.Task;
    }
}