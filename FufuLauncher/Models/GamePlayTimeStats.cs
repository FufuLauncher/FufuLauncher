using System;
using System.Collections.Generic;
using System.Linq;

namespace FufuLauncher.Models
{
    public class GamePlayTimeRecord
    {
        public DateTime Date { get; set; }
        public long PlayTimeSeconds { get; set; }
        
        public TimeSpan PlayTime => TimeSpan.FromSeconds(PlayTimeSeconds);
        public string DisplayDate => Date.ToString("MM-dd");
        public string DayOfWeek => GetDayOfWeekString(Date.DayOfWeek);
        public string DisplayTime => PlayTime.TotalHours >= 1 ? 
            $"{(int)PlayTime.TotalHours}h {PlayTime.Minutes}m" : 
            $"{PlayTime.Minutes}m";

        private static string GetDayOfWeekString(System.DayOfWeek dayOfWeek)
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
    }

    public class WeeklyPlayTimeStats
    {
        public List<GamePlayTimeRecord> DailyRecords { get; set; } = new();
        
        public long TotalPlayTimeSeconds => DailyRecords.Sum(r => r.PlayTimeSeconds);
        public TimeSpan TotalPlayTime => TimeSpan.FromSeconds(TotalPlayTimeSeconds);
        public TimeSpan AverageDailyPlayTime => DailyRecords.Count > 0 ? 
            TimeSpan.FromSeconds(TotalPlayTimeSeconds / DailyRecords.Count) : TimeSpan.Zero;
        
        public string DisplayTotalTime => TotalPlayTime.Days > 0 ? 
            $"{TotalPlayTime.Days}d {TotalPlayTime.Hours}h {TotalPlayTime.Minutes}m" :
            $"{TotalPlayTime.Hours}h {TotalPlayTime.Minutes}m";
            
        public string DisplayAverageTime => $"{AverageDailyPlayTime.Hours}h {AverageDailyPlayTime.Minutes}m";
    }
}