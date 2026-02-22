using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
                System.DayOfWeek.Sunday => "周日",
                System.DayOfWeek.Monday => "周一",
                System.DayOfWeek.Tuesday => "周二",
                System.DayOfWeek.Wednesday => "周三",
                System.DayOfWeek.Thursday => "周四",
                System.DayOfWeek.Friday => "周五",
                System.DayOfWeek.Saturday => "周六",
                _ => ""
            };
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}