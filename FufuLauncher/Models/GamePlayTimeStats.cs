/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models
{
    public class WeeklyPlayTimeStats : INotifyPropertyChanged
    {
        private double _totalHours;
        public double TotalHours
        {
            get => _totalHours;
            set { _totalHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalHoursFormatted)); }
        }

        private double _averageHours;
        public double AverageHours
        {
            get => _averageHours;
            set { _averageHours = value; OnPropertyChanged(); OnPropertyChanged(nameof(AverageHoursFormatted)); }
        }

        public string TotalHoursFormatted => $"{TotalHours:F1}h";
        public string AverageHoursFormatted => $"{AverageHours:F1}h";
        public ObservableCollection<GamePlayTimeRecord> DailyRecords { get; set; } = new();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class GamePlayTimeRecord : INotifyPropertyChanged
    {
        private DateTime _date;
        public DateTime Date
        {
            get => _date;
            set { _date = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayDate)); OnPropertyChanged(nameof(DayOfWeek)); }
        }

        private long _playTimeSeconds;
        public long PlayTimeSeconds
        {
            get => _playTimeSeconds;
            set { _playTimeSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlayTime)); OnPropertyChanged(nameof(DisplayTime)); }
        }

        public TimeSpan PlayTime => TimeSpan.FromSeconds(PlayTimeSeconds);
        public string DisplayDate => Date.ToString("MM-dd");
        public string DayOfWeek => GetDayOfWeekString(Date.DayOfWeek);
        public string DisplayTime => PlayTime.TotalHours >= 1 ?
            $"{(int)PlayTime.TotalHours}h {PlayTime.Minutes}m" :
            $"{PlayTime.Minutes}m";

        private static string GetDayOfWeekString(DayOfWeek dayOfWeek)
        {
            return dayOfWeek switch
            {
                System.DayOfWeek.Sunday => "Day_Sunday".GetLocalized(),
                System.DayOfWeek.Monday => "Day_Monday".GetLocalized(),
                System.DayOfWeek.Tuesday => "Day_Tuesday".GetLocalized(),
                System.DayOfWeek.Wednesday => "Day_Wednesday".GetLocalized(),
                System.DayOfWeek.Thursday => "Day_Thursday".GetLocalized(),
                System.DayOfWeek.Friday => "Day_Friday".GetLocalized(),
                System.DayOfWeek.Saturday => "Day_Saturday".GetLocalized(),
                _ => ""
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
