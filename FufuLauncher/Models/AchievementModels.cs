using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Models;

public partial class AchievementViewModel : ObservableObject
{
    [ObservableProperty] 
    private ObservableCollection<AchievementCategory> _categories = new();

    [ObservableProperty] 
    private ObservableCollection<AchievementItem> _filteredAchievements = new();

    [ObservableProperty] 
    private AchievementCategory _selectedCategory;

    [ObservableProperty] 
    private bool _isLoading;

    [ObservableProperty] 
    private string _statusMessage;
    
    [ObservableProperty]
    private string _serverStatusText = "服务未启动";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryGridVisibility))]
    [NotifyPropertyChangedFor(nameof(DetailListVisibility))]
    [NotifyPropertyChangedFor(nameof(ViewToggleIcon))]
    private bool _isCategoryGridMode = true;

    public Visibility CategoryGridVisibility => IsCategoryGridMode ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailListVisibility => IsCategoryGridMode ? Visibility.Collapsed : Visibility.Visible;
    public string ViewToggleIcon => IsCategoryGridMode ? "\uE8FD" : "\uE80A";
    
    [ObservableProperty]
    private string _primogemStatText = "0/0";
    
    [ObservableProperty]
    private string _progressStatText = "0/0 (0%)";
    
    [ObservableProperty]
    private double _globalProgressPercent;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _hideCompleted;

    [ObservableProperty]
    private string _selectedVersion = "所有版本";

    [ObservableProperty]
    private ObservableCollection<string> _availableVersions = new() { "所有版本" };
}

public partial class AchievementCategory : ObservableObject
{
    private string _name;
    [JsonPropertyName("title")]
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _iconUrl;
    [JsonPropertyName("icon_url")]
    public string IconUrl
    {
        get => _iconUrl;
        set { if (SetProperty(ref _iconUrl, value)) OnPropertyChanged(nameof(SafeIconUrl)); }
    }

    public string SafeIconUrl => string.IsNullOrEmpty(IconUrl) ? "ms-appx:///Assets/WindowIcon.ico" : IconUrl;

    [JsonPropertyName("achievements")]
    public ObservableCollection<AchievementItem> Achievements { get; set; } = new();
    
    public int TotalCount => Achievements?.Sum(x => x.IsGroup ? x.Children.Count : 1) ?? 0;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(ProgressPercentText))]
    private int _completedCount;
    public string ProgressDisplay => $"{CompletedCount}/{TotalCount}";
    public string ProgressPercentText => $"{(int)ProgressPercent}%";
    public double ProgressPercent => TotalCount == 0 ? 0 : (double)CompletedCount / TotalCount * 100;

    public void RefreshProgress()
    {
        if (Achievements == null) return;
        int count = 0;
        foreach (var item in Achievements)
        {
            if (item.IsGroup)
            {
                item.RefreshGroupStatus();
                count += item.Children.Count(c => c.IsCompleted);
            }
            else
            {
                if (item.IsCompleted) count++;
            }
        }
        CompletedCount = count;
    }
}

public partial class AchievementItem : ObservableObject
{
    private string _title;
    [JsonPropertyName("title")]
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    private string _description;
    [JsonPropertyName("description")]
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    
    public int RewardValue => int.TryParse(RewardCount, out int val) ? val : 0;
    
    private string _rewardCount;
    [JsonPropertyName("reward")]
    public string RewardCount { get => _rewardCount; set => SetProperty(ref _rewardCount, value); }
    
    private string _primogemIcon;
    [JsonPropertyName("primogem_icon")]
    public string PrimogemIcon
    {
        get => _primogemIcon;
        set { if (SetProperty(ref _primogemIcon, value)) OnPropertyChanged(nameof(SafePrimogemUrl)); }
    }
    
    private int _currentProgress;
    [JsonPropertyName("current")]
    public int CurrentProgress 
    { 
        get => _currentProgress; 
        set 
        {
            if (SetProperty(ref _currentProgress, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(ProgressVisibility));
            }
        }
    }
    
    private int _maxProgress;
    [JsonPropertyName("total")]
    public int MaxProgress 
    { 
        get => _maxProgress; 
        set 
        {
            if (SetProperty(ref _maxProgress, value))
            {
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(ProgressVisibility));
            }
        }
    }
    
    [JsonIgnore]
    public string ProgressText => $"{CurrentProgress} / {MaxProgress}";
    
    [JsonIgnore]
    public Visibility ProgressVisibility => (MaxProgress > 1) ? Visibility.Visible : Visibility.Collapsed;

    private string _version;
    [JsonPropertyName("version")]
    public string Version { get => _version; set => SetProperty(ref _version, value); }

    private string _itemIconUrl;
    [JsonPropertyName("icon_url")]
    public string ItemIconUrl
    {
        get => _itemIconUrl;
        set { if (SetProperty(ref _itemIconUrl, value)) OnPropertyChanged(nameof(SafeItemIconUrl)); }
    }
    
    [JsonPropertyName("series_id")]
    public string SeriesId { get; set; }

    [JsonPropertyName("series_master_title")]
    public string SeriesMasterTitle { get; set; }

    [JsonPropertyName("stage_index")]
    public int StageIndex { get; set; } = 1;

    [JsonPropertyName("stage_total")]
    public int StageTotal { get; set; } = 1;

    public string SafeItemIconUrl => string.IsNullOrEmpty(ItemIconUrl) ? "ms-appx:///Assets/WindowIcon.ico" : ItemIconUrl;
    public string SafePrimogemUrl => string.IsNullOrEmpty(PrimogemIcon) ? "ms-appx:///Assets/WindowIcon.ico" : PrimogemIcon;

    private bool _isCompleted;
    [JsonPropertyName("is_completed")]
    public bool IsCompleted 
    { 
        get => _isCompleted; 
        set 
        {
            if (SetProperty(ref _isCompleted, value))
            {
                OnPropertyChanged(nameof(Opacity));
            }
        } 
    }

    [JsonIgnore] 
    public ObservableCollection<AchievementItem> Children { get; set; } = new();
    [JsonPropertyName("id")]
    public int Id { get; set; }
    [JsonIgnore]
    public bool IsGroup => Children != null && Children.Count > 0;
    
    [JsonIgnore]
    public Visibility SingleItemVisibility => IsGroup ? Visibility.Collapsed : Visibility.Visible;
    
    [JsonIgnore]
    public Visibility GroupVisibility => IsGroup ? Visibility.Visible : Visibility.Collapsed;
    
    [ObservableProperty]
    [JsonIgnore]
    private string _groupProgressText;
    
    public void RefreshGroupStatus()
    {
        if (!IsGroup) return;
        
        int done = Children.Count(c => c.IsCompleted);
        int total = Children.Count;
        GroupProgressText = $"{done}/{total}";

        var activeChild = Children.FirstOrDefault(c => !c.IsCompleted) ?? Children.LastOrDefault();

        if (activeChild != null)
        {
            CurrentProgress = activeChild.CurrentProgress;
            MaxProgress = activeChild.MaxProgress;
            
            Description = activeChild.Description;
            
            Version = activeChild.Version;
        }
        
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(CurrentProgress));
        OnPropertyChanged(nameof(MaxProgress));
        OnPropertyChanged(nameof(ProgressVisibility));
        
        if (done == total)
        {
            OnPropertyChanged(nameof(Opacity));
        }
    }

    public double Opacity 
    {
        get 
        {
            if (IsGroup) 
            {
                return (Children.Count > 0 && Children.All(x => x.IsCompleted)) ? 0.6 : 1.0;
            }
            return IsCompleted ? 0.6 : 1.0; 
        }
    }
}