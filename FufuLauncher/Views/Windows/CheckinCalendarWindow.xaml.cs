/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MihoyoBBS;

namespace FufuLauncher.Views
{
    public sealed partial class CheckinCalendarWindow : Window
    {
        public ObservableCollection<CalendarRewardItem> Rewards
        {
            get;
        } = new();

        public GameRoleScope RoleSelection
        {
            get;
        }

        private int _calendarVersion;
        private bool _closed;

        public CheckinCalendarWindow(GameRoleScope? scope = null)
        {
            RoleSelection = scope ?? new();
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            SystemBackdrop = new MicaBackdrop();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(680, 720));
            WeakReferenceMessenger.Default.Register<AccountChangedMessage>(this, (r, m) =>
                DispatcherQueue.TryEnqueue(async () => await LoadCalendarDataAsync()));
            Closed += (_, _) =>
            {
                _closed = true;
                ++_calendarVersion;
                WeakReferenceMessenger.Default.UnregisterAll(this);
            };

            _ = LoadCalendarDataAsync();
        }

        private async Task LoadCalendarDataAsync()
        {
            if (_closed) return;
            var version = ++_calendarVersion;
            Rewards.Clear();
            try
            {
                var accountManager = App.GetService<AccountManager>();
                var activeId = accountManager.ActiveAccountId;
                if (activeId == null) return;
                var cookies = await accountManager.LoadCookiesAsync(activeId);
                if (cookies == null || cookies.Count == 0) return;


                var checkinService = App.GetService<IHoyoverseCheckinService>();
                var entry = accountManager.GetActiveAccountEntry();
                if (entry == null) return;


                var calendarData = await checkinService.GetCalendarDataAsync(cookies, entry.ServerType);
                if (calendarData != null && !_closed && version == _calendarVersion &&
                    accountManager.ActiveAccountId == activeId)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_closed || version != _calendarVersion ||
                            accountManager.ActiveAccountId != activeId) return;
                        TitleText.Text = string.Format("CheckinCalendar_MonthTitle_Format".GetLocalized(), calendarData.Month);
                        Rewards.Clear();
                        foreach (var item in calendarData.Awards)
                            Rewards.Add(item);
                        CalendarGridView.ItemsSource = Rewards;
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("CheckinCalendarWindow", $"[CheckinCalendar] {ex.Message}");
            }
        }

        private async void ResignButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var roles = App.GetService<GameRoleService>();
                var selected = await roles.GetCurrentAsync(RoleSelection);
                if (selected == null) return;
                var cookies = selected.Cookies;
                var checkinService = App.GetService<IHoyoverseCheckinService>();
                bool isOs = selected.ServerType == "os";
                string uid = selected.Role.game_uid;
                ResignButton.IsEnabled = false;
                try
                {
                    var resignInfo = await checkinService.GetResignInfoAsync(uid, cookies, selected.ServerType);
                    if (!roles.IsCurrent(selected, RoleSelection)) return;
                    if (resignInfo == null)
                    {
                        string lastError =
                            isOs ? HoyolabCheckinService.LastApiError : MihoyoBBS.GameCheckin.LastApiError;
                        await ShowMessageAsync(string.IsNullOrEmpty(lastError)
                            ? "Checkin_ResignQueryFailed".GetLocalized()
                            : lastError);
                        return;
                    }

                    string confirmKey = isOs ? "Checkin_ResignConfirmFormat" : "Checkin_ResignConfirmFormatCn";
                    int cost = isOs ? resignInfo.QualityCount : resignInfo.CoinCost;

                    var confirmDialog = new ContentDialog
                    {
                        Title = "Checkin_ResignTitle".GetLocalized(),
                        Content = $"{selected.Role.region_name} · {uid}\n" + string.Format(confirmKey.GetLocalized(),
                            resignInfo.RemainingMonthly, cost),
                        PrimaryButtonText = "OkBtn".GetLocalized(),
                        CloseButtonText = "CancelBtn".GetLocalized(),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = Content.XamlRoot
                    };

                    if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
                        return;

                    if (!roles.IsCurrent(selected, RoleSelection)) return;
                    var (success, message) = await checkinService.ExecuteResignAsync(uid, cookies, selected.ServerType);
                    await ShowMessageAsync(message);
                    if (success)
                    {
                        await LoadCalendarDataAsync();
                    }
                }
                finally
                {
                    ResignButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("CheckinCalendarWindow", $"[CheckinCalendar] Make-up check-in exception: {ex.Message}");
            }
        }

        private async Task ShowMessageAsync(string message)
        {
            var dialog = new ContentDialog
            {
                Content = message,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}