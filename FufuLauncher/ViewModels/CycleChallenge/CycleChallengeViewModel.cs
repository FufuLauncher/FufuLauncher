/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models.CycleChallenge;
using FufuLauncher.Services;
using FufuLauncher.Services.CycleChallenge;

namespace FufuLauncher.ViewModels;

public sealed class CycleChallengePeriodOption
{
    public CycleChallengePeriodOption(ChallengePeriod period, TimeSpan displayOffset)
    {
        Period = period;
        ScheduleId = period.ScheduleId;
        ScheduleText = $"{"CycleChallenge_PeriodLabel".GetLocalized()} {period.ScheduleId}";
        RangeText = BuildRange(period, displayOffset);

        var name = ReadSummaryString(period.Summary, "name").Trim();
        TitleText = name.Length == 0 ? ScheduleText : name;
        SubtitleText = name.Length == 0
            ? RangeText
            : string.Join(" · ", new[] { ScheduleText, RangeText }.Where(text => text.Length > 0));
        HasSubtitle = SubtitleText.Length > 0;

        var timeState = GetTimeState(period);
        IsActive = timeState == PeriodTimeState.Active;
        StatusText = timeState switch
        {
            PeriodTimeState.Active => "CycleChallenge_PeriodCurrent".GetLocalized(),
            PeriodTimeState.Upcoming => "CycleChallenge_Upcoming".GetLocalized(),
            PeriodTimeState.Settled => "CycleChallenge_Settled".GetLocalized(),
            _ => string.Empty,
        };
        HasStatus = StatusText.Length > 0;
        ShowInactiveStatus = HasStatus && !IsActive;

        MetricText = BuildMetric(period);
        HasMetric = MetricText.Length > 0;
    }

    public ChallengePeriod Period
    {
        get;
    }

    public string ScheduleId
    {
        get;
    }

    public string ScheduleText
    {
        get;
    }

    public string TitleText
    {
        get;
    }

    public string SubtitleText
    {
        get;
    }

    public bool HasSubtitle
    {
        get;
    }

    public string RangeText
    {
        get;
    }

    public string StatusText
    {
        get;
    }

    public string MetricText
    {
        get;
    }

    public bool HasMetric
    {
        get;
    }

    public bool HasStatus
    {
        get;
    }

    public bool IsActive
    {
        get;
    }

    public bool ShowInactiveStatus
    {
        get;
    }

    private enum PeriodTimeState
    {
        Unknown,
        Upcoming,
        Active,
        Settled,
    }

    private static PeriodTimeState GetTimeState(ChallengePeriod period)
    {
        var start = ParseStoredTime(period.StartUtc);
        var end = ParseStoredTime(period.EndUtc);
        if (start is null && end is null)
        {
            return PeriodTimeState.Unknown;
        }

        var now = DateTimeOffset.UtcNow;
        if (start is { } startValue && now < startValue)
        {
            return PeriodTimeState.Upcoming;
        }

        if (end is { } endValue && now > endValue)
        {
            return PeriodTimeState.Settled;
        }

        return PeriodTimeState.Active;
    }

    private static DateTimeOffset? ParseStoredTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string BuildRange(ChallengePeriod period, TimeSpan displayOffset)
    {
        var start = CycleChallengeTime.FormatStoredString(period.StartUtc, "yyyy-MM-dd HH:mm", displayOffset);
        var end = CycleChallengeTime.FormatStoredString(period.EndUtc, "yyyy-MM-dd HH:mm", displayOffset);
        if (start.Length == 0)
        {
            return end;
        }

        return end.Length == 0 ? start : $"{start} ~ {end}";
    }

    private static string BuildMetric(ChallengePeriod period)
    {
        if (string.IsNullOrWhiteSpace(period.Summary))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(period.Summary);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            if (!ReadBool(root, "is_unlock", true))
            {
                return "CycleChallenge_NotUnlocked".GetLocalized();
            }

            if (!ReadBool(root, "has_data", false))
            {
                return "CycleChallenge_NotParticipated".GetLocalized();
            }

            var parts = new List<string>();
            switch (period.Mode)
            {
                case ChallengeMode.SpiralAbyss:
                {
                    var stars = ReadInt(root, "total_star");
                    var floor = ReadString(root, "max_floor");
                    if (stars > 0)
                    {
                        parts.Add($"★ {stars}");
                    }

                    if (floor.Length > 0)
                    {
                        parts.Add(floor);
                    }

                    break;
                }
                case ChallengeMode.Theater:
                {
                    var difficulty = ReadInt(root, "difficulty_id");
                    var medals = ReadInt(root, "medal_num");
                    if (difficulty > 0)
                    {
                        parts.Add(
                            $"{"Theater_Difficulty".GetLocalized()} {TheaterDifficulty.GetDisplayName(difficulty)}");
                    }

                    if (medals > 0)
                    {
                        parts.Add($"{"Theater_Medals".GetLocalized()} {medals}");
                    }

                    break;
                }
                case ChallengeMode.Stygian:
                {
                    var difficulty = ReadInt(root, "difficulty");
                    var seconds = ReadInt(root, "second");
                    if (difficulty > 0)
                    {
                        parts.Add(
                            $"{"Stygian_Difficulty".GetLocalized()} {StygianDifficulty.GetDisplayName(difficulty)}");
                    }

                    if (seconds > 0)
                    {
                        parts.Add(FormatDuration(seconds));
                    }

                    break;
                }
            }

            return string.Join(" · ", parts);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string FormatDuration(int seconds)
    {
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture,
                $"{(int)span.TotalHours:D2}:{span.Minutes:D2}:{span.Seconds:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes:D2}:{span.Seconds:D2}");
    }

    private static string ReadSummaryString(string? summary, string property)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(summary);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object ? ReadString(root, property) : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static int ReadInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0,
        };
    }

    private static bool ReadBool(JsonElement element, string property, bool fallback)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback,
        };
    }
}

public sealed partial class CycleChallengeViewModel : ObservableObject
{
    private readonly AccountManager _accountManager;
    private readonly GameRoleService _gameRoleService;
    private readonly CycleChallengeService _service;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue =
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    private CancellationTokenSource? _cts;
    private int _loadVersion;
    private bool _suppressPeriodReload;
    private bool _isCloudListenerAttached;
    private DateTimeOffset? _lastFetchedAt;


    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    public CycleChallengeViewModel(
        AccountManager accountManager,
        GameRoleService gameRoleService,
        CycleChallengeService service)
    {
        _accountManager = accountManager;
        _gameRoleService = gameRoleService;
        _service = service;

        WeakReferenceMessenger.Default.Register<CycleChallengeViewModel, AccountChangedMessage>(
            this, static (recipient, _) => recipient.OnContextChanged());
        WeakReferenceMessenger.Default.Register<CycleChallengeViewModel, GameRoleChangedMessage>(
            this, static (recipient, _) => recipient.OnContextChanged());
        WeakReferenceMessenger.Default.Register<CycleChallengeViewModel, FeatureGameRoleChangedMessage>(
            this, static (recipient, message) =>
            {
                if (ReferenceEquals(message.Scope, recipient.RoleSelection))
                {
                    recipient.Payload = null;
                    recipient.HasData = false;
                    recipient.RefreshHeader();
                    _ = recipient.ReloadAfterRoleChangeAsync();
                }
            });
    }


    public GameRoleScope RoleSelection
    {
        get;
    } = new();

    public ObservableCollection<CycleChallengePeriodOption> Periods
    {
        get;
    } = [];

    [ObservableProperty] private ChallengeMode _currentMode = ChallengeMode.SpiralAbyss;

    [ObservableProperty] private CycleChallengePeriodOption? _selectedPeriod;

    [ObservableProperty] private string? _payload;

    [ObservableProperty] private string _accountText = string.Empty;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private bool _hasError;

    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private bool _isOffline;

    public bool HasBanner => HasError || IsOffline;

    partial void OnHasErrorChanged(bool value) => OnPropertyChanged(nameof(HasBanner));

    partial void OnIsOfflineChanged(bool value) => OnPropertyChanged(nameof(HasBanner));

    [ObservableProperty] private string? _statusText;

    [ObservableProperty] private bool _hasData;


    public string? CurrentRegion
    {
        get;
        private set;
    }

    partial void OnSelectedPeriodChanged(CycleChallengePeriodOption? value)
    {
        if (value is null || _suppressPeriodReload)
        {
            return;
        }

        _ = LoadPeriodAsync(value.ScheduleId);
    }

    public async Task InitializeAsync()
    {
        RefreshHeader();


        _ = Task.Run(async () =>
        {
            try
            {
                await RemoteImageCache.CleanupAsync().ConfigureAwait(false);
            }
            catch
            {
            }
        });

        await ReloadAsync(forceRefresh: false);


        if (ShouldRefresh())
        {
            await ReloadAsync(forceRefresh: true);
        }
    }

    private bool ShouldRefresh() =>
        !HasData || (_lastFetchedAt is { } fetched && DateTimeOffset.UtcNow - fetched > CacheTtl);


    private async Task ReloadAfterRoleChangeAsync()
    {
        await ReloadAsync(forceRefresh: false);

        if (ShouldRefresh())
        {
            await ReloadAsync(forceRefresh: true);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await ReloadAsync(forceRefresh: true);

    public async Task SetModeAsync(ChallengeMode mode)
    {
        if (CurrentMode == mode)
        {
            return;
        }

        CurrentMode = mode;
        Periods.Clear();
        SelectedPeriod = null;
        Payload = null;
        HasData = false;
        await ReloadAsync(forceRefresh: false);

        if (CurrentMode != mode)
        {
            return;
        }

        if (ShouldRefresh())
        {
            await ReloadAsync(forceRefresh: true);
        }
    }


    public void OnContextChanged()
    {
        _cts?.Cancel();
        RoleSelection.Reset();
        RefreshHeader();
        Periods.Clear();
        SelectedPeriod = null;
        Payload = null;
        HasData = false;
        _ = ReloadAsync(forceRefresh: false);
    }

    private async Task ReloadAsync(bool forceRefresh)
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        var version = ++_loadVersion;

        IsLoading = true;
        HasError = false;
        ErrorMessage = null;
        IsOffline = false;
        StatusText = null;

        try
        {
            FufuLauncher.Services.SelectedGameRole? role;
            try
            {
                role = await _gameRoleService.GetCurrentAsync(RoleSelection).ConfigureAwait(true);
                if (role is null)
                {
                    SetNoAccountState();
                    return;
                }

                CurrentRegion = role.Role.region;
            }
            catch (Exception ex)
            {
                SetError(ChallengeErrorKind.LoginExpired, ex.Message);
                return;
            }

            var result = forceRefresh
                ? await _service.RefreshAsync(CurrentMode, role, cts.Token).ConfigureAwait(true)
                : _service.LoadFromCache(CurrentMode, role, null);

            if (version != _loadVersion)
            {
                return;
            }

            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                SetError(ChallengeErrorKind.NetworkError, ex.Message);
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private async Task LoadPeriodAsync(string scheduleId)
    {
        var mode = CurrentMode;
        try
        {
            var role = await _gameRoleService.GetCurrentAsync(RoleSelection).ConfigureAwait(true);
            if (role is null || CurrentMode != mode || SelectedPeriod?.ScheduleId != scheduleId)
            {
                return;
            }

            CurrentRegion = role.Role.region;

            var result = _service.LoadFromCache(CurrentMode, role, scheduleId);
            if (result.Success && CurrentMode == mode && SelectedPeriod?.ScheduleId == scheduleId)
            {
                Payload = result.Payload;
                HasData = !string.IsNullOrEmpty(result.Payload);
                IsOffline = false;
                HasError = false;
                _lastFetchedAt = result.FetchedAt;
                StatusText = "CycleChallenge_DataStatusCached".GetLocalized();
            }
        }
        catch
        {
        }
    }

    private void ApplyResult(ChallengeLoadResult result)
    {
        HasData = result.Success && !string.IsNullOrEmpty(result.Payload);
        Payload = result.Payload;

        Periods.Clear();
        var displayOffset = CycleChallengeTime.GetDisplayOffset(CurrentRegion);
        foreach (var period in result.Periods)
        {
            Periods.Add(new CycleChallengePeriodOption(period, displayOffset));
        }

        var active = Periods.FirstOrDefault(x => x.ScheduleId == result.ActiveScheduleId) ?? Periods.FirstOrDefault();
        if (active is not null && !ReferenceEquals(active, SelectedPeriod))
        {
            _suppressPeriodReload = true;
            try
            {
                SelectedPeriod = active;
            }
            finally
            {
                _suppressPeriodReload = false;
            }
        }

        if (!result.Success)
        {
            SetError(result.Kind, result.Message);
            return;
        }

        _lastFetchedAt = result.FetchedAt;

        IsOffline = result.IsFromCache && result.Kind != ChallengeErrorKind.Success;
        HasError = IsOffline;
        ErrorMessage = HasError ? result.Message : null;
        StatusText = result.IsFromCache
            ? "CycleChallenge_DataStatusCached".GetLocalized()
            : "CycleChallenge_DataStatusFresh".GetLocalized();

        if (!HasData)
        {
            StatusText = "CycleChallenge_NotParticipated".GetLocalized();
        }
    }

    private void SetError(ChallengeErrorKind kind, string? message)
    {
        HasData = false;
        Payload = null;
        HasError = true;
        ErrorMessage = kind switch
        {
            ChallengeErrorKind.LoginExpired => "CycleChallenge_LoginExpired".GetLocalized(),
            ChallengeErrorKind.RiskControlRequired => "CycleChallenge_RiskControl".GetLocalized(),
            ChallengeErrorKind.RateLimited => "CycleChallenge_RateLimited".GetLocalized(),
            ChallengeErrorKind.Unsupported => "CycleChallenge_Unsupported".GetLocalized(),
            ChallengeErrorKind.NotParticipated => "CycleChallenge_NotParticipated".GetLocalized(),
            _ => message ?? "CycleChallenge_LoadFailed".GetLocalized(),
        };
    }

    private void SetNoAccountState()
    {
        HasData = false;
        Payload = null;
        HasError = true;
        ErrorMessage = "CycleChallenge_NoAccount".GetLocalized();
        StatusText = null;
    }

    public string? GetCurrentRoleUid()
    {
        var account = _accountManager.GetActiveAccountEntry();
        return account is null ? null : RoleSelection.Current(account)?.game_uid;
    }

    public void AttachCloudSyncListener()
    {
        if (_isCloudListenerAttached)
        {
            return;
        }

        _service.ChallengesChanged += OnChallengesChanged;
        _isCloudListenerAttached = true;
    }

    public void DetachCloudSyncListener()
    {
        if (!_isCloudListenerAttached)
        {
            return;
        }

        _service.ChallengesChanged -= OnChallengesChanged;
        _isCloudListenerAttached = false;
    }

    public Task ReloadAfterCloudSyncAsync()
    {
        RefreshHeader();
        return ReloadAsync(forceRefresh: false);
    }

    private void OnChallengesChanged(object? sender, EventArgs e)
    {
        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            _ = ReloadAfterCloudSyncAsync();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => _ = ReloadAfterCloudSyncAsync());
    }

    private void RefreshHeader()
    {
        var account = _accountManager.GetActiveAccountEntry();
        if (account is null)
        {
            AccountText = "CycleChallenge_NoAccount".GetLocalized();
            return;
        }


        var role = RoleSelection.Current(account);
        AccountText = role is null
            ? $"{account.Nickname}"
            : $"{account.Nickname}  ·  {role.game_uid}  {role.region_name}";
    }

    private readonly record struct SelectedRoleResult(FufuLauncher.Services.SelectedGameRole Role);
}