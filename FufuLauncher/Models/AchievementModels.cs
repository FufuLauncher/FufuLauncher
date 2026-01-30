using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FufuLauncher.Models;

public class AchievementUserData
{
    public List<AchievementCategory> Categories { get; set; } = new();
    public Dictionary<string, List<AchievementItem>> AchievementData { get; set; } = new();
}

public partial class AchievementCategory : ObservableObject
{
    [ObservableProperty] private string _name;
    
    [ObservableProperty] private string _progress;

    [ObservableProperty] private bool _isActive;
    
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProgressDisplay))] private int _completedCount;
    
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ProgressDisplay))] private int _totalCount = 1;
    
    public string ProgressDisplay => $"{CompletedCount}/{TotalCount}";
}

public partial class AchievementItem : ObservableObject
{
    [ObservableProperty] private string _title;

    [ObservableProperty] private string _description;

    [ObservableProperty] private string _rewardCount;
    
    [ObservableProperty] private bool _isCompleted;

    [ObservableProperty] private string _version;
}

public partial class AchievementViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<AchievementCategory> _categories = new();

    [ObservableProperty] private ObservableCollection<AchievementItem> _currentAchievements = new();

    [ObservableProperty] private AchievementCategory _selectedCategory;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _statusMessage;
    
    [ObservableProperty] private string _importScriptContent;
    
    [ObservableProperty] private bool _isImportPanelVisible;
}