/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.ObjectModel;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using MihoyoBBS;

namespace FufuLauncher.Views
{
    public sealed partial class CheckinCalendarWindow : Window
    {
        public ObservableCollection<CalendarRewardItem> Rewards { get; } = new();

        public CheckinCalendarWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            SystemBackdrop = new MicaBackdrop();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(680, 720));

            _ = LoadCalendarDataAsync();
        }

        private async Task LoadCalendarDataAsync()
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
            if (calendarData != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    TitleText.Text = $"{calendarData.Month}月 签到奖励日历";
                    Rewards.Clear();
                    foreach (var item in calendarData.Awards)
                        Rewards.Add(item);
                    CalendarGridView.ItemsSource = Rewards;
                });
            }
        }
    }
}
