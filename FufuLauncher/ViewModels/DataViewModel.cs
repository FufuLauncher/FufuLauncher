using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.ViewModels;


public class RootObject
{
    [JsonPropertyName("result")]
    public List<RoleData> Result { get; set; }
}

public class RoleData
{
    [JsonPropertyName("role")]
    public string Role { get; set; } 

    [JsonPropertyName("ename")]
    public string Ename { get; set; } 

    [JsonPropertyName("star")]
    public int Star { get; set; } 

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } 

    [JsonPropertyName("avg_level")]
    public double AvgLevel { get; set; } 
    
    [JsonPropertyName("ability1")]
    public double Ability1 { get; set; }
    [JsonPropertyName("ability2")]
    public double Ability2 { get; set; }
    [JsonPropertyName("ability3")]
    public double Ability3 { get; set; }
    
    [JsonPropertyName("c0")] public double C0 { get; set; }
    [JsonPropertyName("c1")] public double C1 { get; set; }
    [JsonPropertyName("c2")] public double C2 { get; set; }
    [JsonPropertyName("c3")] public double C3 { get; set; }
    [JsonPropertyName("c4")] public double C4 { get; set; }
    [JsonPropertyName("c5")] public double C5 { get; set; }
    [JsonPropertyName("c6")] public double C6 { get; set; }

    [JsonPropertyName("damage")]
    public int Damage { get; set; } 

    [JsonPropertyName("damage_name")]
    public string DamageName { get; set; } 

    [JsonPropertyName("weapon")]
    public List<WeaponData> Weapons { get; set; } 

    [JsonPropertyName("artifacts_set")]
    public List<ArtifactSetData> Artifacts { get; set; } 
    
    public List<WeaponData> TopWeapons => Weapons?.Take(3).ToList();
    public List<ArtifactSetData> TopArtifacts => Artifacts?.Take(3).ToList();
    public string LevelDisplay => $"Lv.{AvgLevel:F1}";
    public string TalentsDisplay => $"A:{Ability1:F1} / E:{Ability2:F1} / Q:{Ability3:F1}";
    public string DamageDisplay => $"{Damage:N0}"; 
    public string ConstellationSummary => $"C0: {C0}%  C1: {C1}%  C2: {C2}%";
    public string Name => Role; 
    public string RateDisplay => $"{AvgLevel:F1}"; 
    public string FirstIcon => Avatar;
}

public class RerunRoot
{
    [JsonPropertyName("result")]
    public List<List<RerunRoleData>> Result { get; set; }
}

public class RerunRoleData
{
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }

    [JsonPropertyName("star")]
    public int Star { get; set; }

    [JsonPropertyName("days")]
    public object Days { get; set; }

    [JsonPropertyName("intro")]
    public string Intro { get; set; }

    [JsonPropertyName("avg_days")]
    public object AvgDays { get; set; }

    [JsonPropertyName("up_times")]
    public int UpTimes { get; set; }

    [JsonPropertyName("history")]
    public List<string> History { get; set; }

    [JsonPropertyName("max_gap_days")]
    public object MaxGapDays { get; set; }

    [JsonPropertyName("min_gap_pool")]
    public string MinGapPool { get; set; }
    
    [JsonIgnore]
    public string HistoryString => History != null ? string.Join(", ", History) : "无";
    
    [JsonIgnore]
    public string AvgDaysString => AvgDays?.ToString() ?? "暂无";
}

public class WeaponData
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }
    [JsonPropertyName("rate")]
    public double Rate { get; set; }
    public string RateDisplay => $"{Rate}%";
}

public class ArtifactSetData
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
    [JsonPropertyName("avatars")]
    public List<string> Avatars { get; set; }
    [JsonPropertyName("rate")]
    public double Rate { get; set; }
    public string RateDisplay => $"{Rate}%";
    public string FirstIcon => Avatars != null && Avatars.Count > 0 ? Avatars[0] : null;
}

public class AbyssRootObject
{
    [JsonPropertyName("has_list")]
    public List<AbyssRoleData> HasList { get; set; }
}

public class AbyssRoleData
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("star")]
    public int Star { get; set; }

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }

    [JsonPropertyName("use_rate")]
    public double UseRate { get; set; }

    [JsonPropertyName("own_rate")]
    public double OwnRate { get; set; }

    [JsonPropertyName("collection")]
    public object Collection { get; set; }

    [JsonPropertyName("time")]
    public object Time { get; set; }

    [JsonPropertyName("rank_class")]
    public string RankClass { get; set; }
    

    public string UseRateStr => $"{UseRate}%";
    public string OwnRateStr => $"{OwnRate}%";
    
    public double TimeValue
    {
        get
        {
            if (Time is JsonElement element && element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDouble();
            }
            if (double.TryParse(Time?.ToString(), out double val))
            {
                return val;
            }
            return 999999; 
        }
    }

    public string CollectionStr
    {
        get
        {
            if (Collection is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return element.GetDouble().ToString("F1");
                return element.ToString();
            }
            return Collection?.ToString() ?? "-";
        }
    }

    public string TimeStr
    {
        get
        {
            string val;
            if (Time is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number)
                    val = element.GetDouble().ToString("F0");
                else
                    val = element.ToString();
            }
            else
            {
                val = Time?.ToString() ?? "-";
            }
            return val == "-" ? "-" : $"{val}s";
        }
    }
}

public class WishRootObject
{
    [JsonPropertyName("result")]
    public List<WishHistoryItem> Result { get; set; }
}

public class WishHistoryItem
{
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; }

    [JsonPropertyName("star5_role")]
    public List<string> Star5Role { get; set; }

    [JsonPropertyName("star4_role")]
    public List<string> Star4Role { get; set; }
    
    public string Star5Display => Star5Role != null && Star5Role.Count > 0 
        ? string.Join(" / ", Star5Role) 
        : "";

    public string Star4Display => Star4Role != null && Star4Role.Count > 0 
        ? string.Join(" / ", Star4Role) 
        : "";
}

public class SpiralAbyssRoot
{
    [JsonPropertyName("has_list")]
    public List<SpiralAbyssRole> Roles { get; set; }
    
    [JsonPropertyName("list")]
    public List<SpiralAbyssTeam> Teams { get; set; }
}

public class SpiralAbyssRole
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }

    [JsonPropertyName("star")]
    public int Star { get; set; }

    [JsonPropertyName("use_rate")]
    public double UseRate { get; set; }

    [JsonPropertyName("own_rate")]
    public double OwnRate { get; set; }

    [JsonPropertyName("rank_class")]
    public string RankClass { get; set; }
    
    public string UseRateDisplay => $"{UseRate}%";
}

public class SpiralAbyssTeam
{
    [JsonPropertyName("role")]
    public List<SpiralAbyssTeamRole> Roles { get; set; }

    [JsonPropertyName("use_rate")]
    public double UseRate { get; set; }

    [JsonPropertyName("use")]
    public int UseNum { get; set; }
    
    public string UseRateDisplay => $"{UseRate}%";
    public string UseNumDisplay => $"{UseNum}次";
}

public class SpiralAbyssTeamRole
{
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }

    [JsonPropertyName("star")]
    public int Star { get; set; }
}

public class DataViewModel : INotifyPropertyChanged
{
    private ObservableCollection<RoleData> _roles;
    private List<AbyssRoleData> _allAbyssDataCache = new();
    
    private int _currentAbyssStarFilter = 0;
    private int _currentAbyssSortType = 0;
    public ObservableCollection<RoleData> Roles
    {
        get => _roles;
        set { if (_roles != value) { _roles = value; OnPropertyChanged(); } }
    }
    
    private ObservableCollection<AbyssRoleData> _abyssRoles;
    public ObservableCollection<AbyssRoleData> AbyssRoles
    {
        get => _abyssRoles;
        set { if (_abyssRoles != value) { _abyssRoles = value; OnPropertyChanged(); } }
    }
    
    private ObservableCollection<WishHistoryItem> _wishHistory;
    public ObservableCollection<WishHistoryItem> WishHistory
    {
        get => _wishHistory;
        set { if (_wishHistory != value) { _wishHistory = value; OnPropertyChanged(); } }
    }
    
    private ObservableCollection<SpiralAbyssRole> _spiralAbyssRoles;
    public ObservableCollection<SpiralAbyssRole> SpiralAbyssRoles
    {
        get => _spiralAbyssRoles;
        set { if (_spiralAbyssRoles != value) { _spiralAbyssRoles = value; OnPropertyChanged(); } }
    }

    private ObservableCollection<SpiralAbyssTeam> _spiralAbyssTeams;
    public ObservableCollection<SpiralAbyssTeam> SpiralAbyssTeams
    {
        get => _spiralAbyssTeams;
        set { if (_spiralAbyssTeams != value) { _spiralAbyssTeams = value; OnPropertyChanged(); } }
    }
    
    private int _viewMode = 0;

    public void SetViewMode(int mode)
    {
        if (_viewMode != mode)
        {
            _viewMode = mode;
            OnPropertyChanged(nameof(IsRoleMode));
            OnPropertyChanged(nameof(IsAbyssMode));
            OnPropertyChanged(nameof(IsWishMode));
            OnPropertyChanged(nameof(IsSpiralAbyssMode));
            OnPropertyChanged(nameof(IsRerunMode));
            OnPropertyChanged(nameof(IsTimelineMode));
        }
    }

    public bool IsRoleMode => _viewMode == 0;
    
    public bool IsAbyssMode
    {
        get => _viewMode == 1;
        set 
        { 
            if (value) SetViewMode(1); 
            else if (_viewMode == 1) SetViewMode(0); 
        }
    }

    public bool IsWishMode => _viewMode == 2;
    
    public bool IsSpiralAbyssMode
    {
        get => _viewMode == 3;
        set
        {
            if (value) SetViewMode(3);
            else if (_viewMode == 3) SetViewMode(0);
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public DataViewModel()
    {
        Roles = new ObservableCollection<RoleData>();
        AbyssRoles = new ObservableCollection<AbyssRoleData>();
        WishHistory = new ObservableCollection<WishHistoryItem>();
        SpiralAbyssRoles = new ObservableCollection<SpiralAbyssRole>();
        SpiralAbyssTeams = new ObservableCollection<SpiralAbyssTeam>();
        
        _viewMode = 0;
    }
    
    private List<RoleData> _allRoleDataCache = new();
    
    public async Task LoadDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            using var client = new HttpClient();
            var url = "http://154.44.25.230:8080/getRoleAvg";
            var json = await client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<RootObject>(json);

            if (data?.Result != null)
            {
                _allRoleDataCache = data.Result;

                ApplyRoleFilter(0); 
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
    private int _currentRoleFilterType;
    private string _currentRoleSearchText = "";
    public void ApplyRoleFilter(int filterType)
    {
        _currentRoleFilterType = filterType;
        ExecuteRoleFilter();
    }
    
    public void SearchRoles(string keyword)
    {
        _currentRoleSearchText = keyword?.Trim() ?? "";
        ExecuteRoleFilter();
    }
    
    private void ExecuteRoleFilter()
    {
        if (_allRoleDataCache == null) return;

        IEnumerable<RoleData> query = _allRoleDataCache;
        
        if (_currentRoleFilterType == 1)
        {
            query = query.Where(x => x.Star == 5);
        }
        else if (_currentRoleFilterType == 2)
        {
            query = query.Where(x => x.Star == 4);
        }
        
        if (!string.IsNullOrEmpty(_currentRoleSearchText))
        {
            query = query.Where(x => 
                (x.Role != null && x.Role.Contains(_currentRoleSearchText)) || 
                (x.Ename != null && x.Ename.Contains(_currentRoleSearchText, StringComparison.OrdinalIgnoreCase))
            );
        }
        
        Roles.Clear();
        foreach (var role in query)
        {
            Roles.Add(role);
        }
    }
    
    public async Task LoadAbyssDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            using var client = new HttpClient();
            var url = "http://154.44.25.230:8080/getAbyssRank2";
            var json = await client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<AbyssRootObject>(json);

            if (data?.HasList != null)
            {
                _allAbyssDataCache = data.HasList;
                
                ApplyAbyssFilter(_currentAbyssStarFilter, _currentAbyssSortType);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching abyss data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    public void ApplyAbyssFilter(int filterType, int sortType)
    {
        if (_allAbyssDataCache == null) return;
        
        _currentAbyssStarFilter = filterType;
        _currentAbyssSortType = sortType;

        IEnumerable<AbyssRoleData> query = _allAbyssDataCache;
        
        if (filterType == 1)
        {
            query = query.Where(x => x.Star == 5);
        }
        else if (filterType == 2)
        {
            query = query.Where(x => x.Star == 4);
        }
        
        if (sortType == 0)
        {
            query = query.OrderByDescending(x => x.UseRate);
        }
        else if (sortType == 1)
        {
            query = query.OrderBy(x => x.TimeValue);
        }
        
        AbyssRoles.Clear();
        foreach (var role in query)
        {
            AbyssRoles.Add(role);
        }
    }
    
    public async Task LoadWishDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            using var client = new HttpClient();
            var url = "http://154.44.25.230:8080/getWishHistory";
            var json = await client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<WishRootObject>(json);

            if (data?.Result != null)
            {
                WishHistory.Clear();
                foreach (var item in data.Result)
                {
                    WishHistory.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching wish data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadSpiralAbyssDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;

        try
        {
            using var client = new HttpClient();
            var url = "http://154.44.25.230:8080/getAbyssRank";
            var json = await client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<SpiralAbyssRoot>(json);

            if (data != null)
            {
                if (data.Roles != null)
                {
                    SpiralAbyssRoles.Clear();
                    var sortedList = data.Roles.OrderByDescending(x => x.UseRate).ToList();
                    foreach (var item in sortedList)
                    {
                        SpiralAbyssRoles.Add(item);
                    }
                }
                
                if (data.Teams != null)
                {
                    SpiralAbyssTeams.Clear();
                    foreach (var item in data.Teams)
                    {
                        SpiralAbyssTeams.Add(item);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching spiral abyss data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    
    private bool _isRerunMode;
    public bool IsRerunMode
    {
        get => _viewMode == 4;
        set { if (value) SetViewMode(4); }
    }
    public bool IsTimelineMode
    {
        get => _viewMode == 5;
        set { if (value) SetViewMode(5); }
    }
    
    public ObservableCollection<RerunRoleData> RerunRoles { get; } = new();
    
    public async Task LoadRerunDataAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            string url = "http://154.44.25.230:8080/getRerunList";
            using var client = new HttpClient();
            var json = await client.GetStringAsync(url);
            
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var data = JsonSerializer.Deserialize<RerunRoot>(json, options);

            if (data != null && data.Result != null && data.Result.Count > 0)
            {
                RerunRoles.Clear();
                foreach (var item in data.Result[0])
                {
                    RerunRoles.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching rerun data: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }
}