/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.MiHoYo;
using FufuLauncher.Services;
using FufuLauncher.Services.MiHoYo.DailyNote;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels;

public partial class MainViewModel
{
    #region 每日便签

    [ObservableProperty] private int _currentResin;
    [ObservableProperty] private int _maxResin;
    [ObservableProperty] private string _resinRecoveryTime = "";
    [ObservableProperty] private int _finishedTaskNum;
    [ObservableProperty] private int _totalTaskNum;
    [ObservableProperty] private int _currentHomeCoin;
    [ObservableProperty] private int _maxHomeCoin;
    [ObservableProperty] private int _currentExpeditionNum;
    [ObservableProperty] private int _maxExpeditionNum;
    [ObservableProperty] private bool _isTransformerObtained;
    [ObservableProperty] private string _transformerRecoveryTime = "";

    [ObservableProperty] private Visibility _showResin = Visibility.Visible;
    [ObservableProperty] private Visibility _showDailyTasks = Visibility.Visible;
    [ObservableProperty] private Visibility _showHomeCoin = Visibility.Visible;
    [ObservableProperty] private Visibility _showExpeditions = Visibility.Visible;
    [ObservableProperty] private Visibility _showTransformer = Visibility.Visible;
    [ObservableProperty] private bool _isDailyNoteLoaded;
    [ObservableProperty] private string _dailyNoteStatus = "Home_NoData".GetLocalized();
    private int _dailyNoteVersion;

    public async Task LoadDailyNoteAsync(bool force = false)
    {
        var hideJson = await _localSettingsService.ReadSettingAsync("IsHideDailyNoteCardEnabled");
        if (hideJson != null && Convert.ToBoolean(hideJson))
        {
            IsDailyNoteLoaded = false;
            DebugLog.Info("MainViewModel.DailyNote", "[DailyNote] Note card hidden, skipping API request");
            return;
        }

        if (!force && IsDailyNoteLoaded)
        {
            DebugLog.Info("MainViewModel.DailyNote", "[DailyNote] Data already present, skipping request");
            return;
        }

        var version = Interlocked.Increment(ref _dailyNoteVersion);
        try
        {
            await ClearDailyNoteDataAsync(version);
            var roles = App.GetService<GameRoleService>();
            var selected = await roles.GetCurrentAsync();
            if (version != _dailyNoteVersion) return;
            var dailyNoteData = selected == null
                ? null
                : await _dailyNoteCardService.LoadCardDataAsync(
                    selected.Role.game_uid, selected.Role.region, selected.AccountId);

            await UpdateUI(() =>
            {
                if (version != _dailyNoteVersion) return;
                if (selected == null || dailyNoteData == null || !roles.IsCurrent(selected))
                {
                    DailyNoteStatus = "Home_NoData".GetLocalized();
                    return;
                }
                CurrentResin = dailyNoteData.CurrentResin;
                MaxResin = dailyNoteData.MaxResin;
                FinishedTaskNum = dailyNoteData.FinishedTaskNum;
                TotalTaskNum = dailyNoteData.TotalTaskNum;
                CurrentHomeCoin = dailyNoteData.CurrentHomeCoin;
                MaxHomeCoin = dailyNoteData.MaxHomeCoin;
                CurrentExpeditionNum = dailyNoteData.CurrentExpeditionNum;
                MaxExpeditionNum = dailyNoteData.MaxExpeditionNum;
                IsTransformerObtained = dailyNoteData.IsTransformerObtained;
                TransformerRecoveryTime = dailyNoteData.TransformerRecoveryTime;

                IsDailyNoteLoaded = true;
                DebugLog.Info("MainViewModel.DailyNote", "[DailyNote] Note data loaded");
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainViewModel.DailyNote", $"[DailyNote] Failed to load note data: {ex.Message}");
            await UpdateUI(() =>
            {
                if (version == _dailyNoteVersion)
                {
                    DailyNoteStatus = ex is DailyNoteApiException error &&
                                      MiyousheCheckinGame.IsLoginExpired(error.Retcode)
                        ? "DailyNote_ReloginRequired".GetLocalized()
                        : ex.Message;
                }
            });
        }
    }

    private Task ClearDailyNoteDataAsync(int? loadingVersion = null)
    {
        var version = loadingVersion ?? Interlocked.Increment(ref _dailyNoteVersion);
        return UpdateUI(() =>
        {
            if (version != _dailyNoteVersion) return;
            DailyNoteStatus = (loadingVersion.HasValue ? "Common_Loading" : "Home_NoData").GetLocalized();
            IsDailyNoteLoaded = false;
            CurrentResin = 0;
            MaxResin = 0;
            FinishedTaskNum = 0;
            TotalTaskNum = 0;
            CurrentHomeCoin = 0;
            MaxHomeCoin = 0;
            CurrentExpeditionNum = 0;
            MaxExpeditionNum = 0;
            IsTransformerObtained = false;
            TransformerRecoveryTime = "";
        });
    }

    #endregion
}
