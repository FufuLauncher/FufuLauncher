using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using FufuLauncher.Models.Genshin;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public class GenshinViewModel : INotifyPropertyChanged
{
    private readonly IGenshinService _genshinService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IUserInfoService _userInfoService;

    private string _uid = string.Empty;
    public string Uid
    {
        get => _uid;
        set
        {
            _uid = value; OnPropertyChanged();
        }
    }

    private string _nickname = string.Empty;
    public string Nickname
    {
        get => _nickname;
        set
        {
            _nickname = value; OnPropertyChanged();
        }
    }

    private TravelersDiarySummary? _travelersDiary;
    public TravelersDiarySummary? TravelersDiary
    {
        get => _travelersDiary;
        set
        {
            _travelersDiary = value; OnPropertyChanged();
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value; OnPropertyChanged();
        }
    }

    private string _statusMessage = "等待加载数据...";
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value; OnPropertyChanged();
        }
    }

    public string TodayPrimogems => $"今日原石: {TravelersDiary?.Data.DayData.CurrentPrimogems ?? 0} (+{TravelersDiary?.Data.DayData.CurrentPrimogems ?? 0 - (TravelersDiary?.Data.DayData.LastPrimogems ?? 0)})";
    public string TodayMora => $"今日摩拉: {(TravelersDiary?.Data.DayData.CurrentMora ?? 0):N0}";

    public string MonthPrimogems => $"本月原石: {(TravelersDiary?.Data.MonthData.CurrentPrimogems ?? 0):N0}";
    public string MonthMora => $"本月摩拉: {(TravelersDiary?.Data.MonthData.CurrentMora ?? 0):N0}";

    public string LastMonthPrimogems => $"上月同期: {(TravelersDiary?.Data.MonthData.LastPrimogems ?? 0):N0}";
    public string LastMonthMora => $"上月同期: {(TravelersDiary?.Data.MonthData.LastMora ?? 0):N0}";

    public string PrimogemsLevel => $"收入等级: Lv.{TravelersDiary?.Data.MonthData.CurrentPrimogemsLevel ?? 0}";
    public string PrimogemsGrowth => $"原石增长率: {TravelersDiary?.Data.MonthData.PrimogemsRate ?? 0}%";
    public string MoraGrowth => $"摩拉增长率: {TravelersDiary?.Data.MonthData.MoraRate ?? 0}%";

    public List<IncomeSourceViewModel> IncomeSources
    {
        get
        {
            if (TravelersDiary?.Data.MonthData.GroupBy == null) return new List<IncomeSourceViewModel>();

            return TravelersDiary.Data.MonthData.GroupBy
                .Where(s => s.Num > 0)
                .OrderByDescending(s => s.Num)
                .Select(s => new IncomeSourceViewModel
                {
                    Action = s.Action,
                    Num = s.Num,
                    Percent = s.Percent,
                    Color = GetIncomeSourceColor(s.ActionId)
                })
                .ToList();
        }
    }

    public IAsyncRelayCommand LoadDataCommand
    {
        get;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public GenshinViewModel(
        IGenshinService genshinService,
        ILocalSettingsService localSettingsService,
        IUserInfoService userInfoService)
    {
        _genshinService = genshinService;
        _localSettingsService = localSettingsService;
        _userInfoService = userInfoService;
        LoadDataCommand = new AsyncRelayCommand(LoadDataAsync);
    }

    private async Task LoadDataAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;
            StatusMessage = "正在加载旅行札记数据...";

            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(configPath))
            {
                StatusMessage = "错误：找不到配置文件，请先登录";
                return;
            }

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (string.IsNullOrEmpty(config?.Account?.Cookie))
            {
                StatusMessage = "错误：无效的登录信息";
                return;
            }

            var cookie = config.Account.Cookie;

            StatusMessage = "正在获取用户信息...";
            var rolesResponse = await _userInfoService.GetUserGameRolesAsync(cookie);
            var role = rolesResponse?.data?.list?.FirstOrDefault();

            if (role == null)
            {
                StatusMessage = "错误：无法获取角色信息";
                return;
            }

            Uid = role.game_uid;
            Nickname = role.nickname;

            StatusMessage = "正在加载旅行札记数据...";
            TravelersDiary = await _genshinService.GetTravelersDiarySummaryAsync(Uid, cookie, 12);

            OnPropertyChanged(nameof(TodayPrimogems));
            OnPropertyChanged(nameof(TodayMora));
            OnPropertyChanged(nameof(MonthPrimogems));
            OnPropertyChanged(nameof(MonthMora));
            OnPropertyChanged(nameof(LastMonthPrimogems));
            OnPropertyChanged(nameof(LastMonthMora));
            OnPropertyChanged(nameof(PrimogemsLevel));
            OnPropertyChanged(nameof(PrimogemsGrowth));
            OnPropertyChanged(nameof(MoraGrowth));
            OnPropertyChanged(nameof(IncomeSources));

            StatusMessage = $"数据加载完成 - {Nickname} ({Uid})";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载旅行札记失败: {ex.Message}");
            Debug.WriteLine($"堆栈: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Debug.WriteLine($"内部异常: {ex.InnerException.Message}");
            }
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string GetIncomeSourceColor(int actionId)
    {
        return actionId switch
        {
            1 => "#FF6B6B",
            3 => "#4ECDC4",
            5 => "#45B7D1",
            4 => "#96CEB4",
            6 => "#FFEAA7",
            7 => "#DDA0DD",
            2 => "#FFB347",
            _ => "#95A5A6"
        };
    }
}

public class IncomeSourceViewModel
{
    public string Action { get; set; } = "";
    public int Num
    {
        get; set;
    }
    public int Percent
    {
        get; set;
    }
    public string Color { get; set; } = "#000000";
}