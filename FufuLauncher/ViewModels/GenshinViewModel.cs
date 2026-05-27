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
            _travelersDiary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedDate));
            OnPropertyChanged(nameof(FormattedMonthPrimogems));
            OnPropertyChanged(nameof(FormattedMonthMora));
            OnPropertyChanged(nameof(IncomeSources));
        }
    }

    public string FormattedDate => _travelersDiary?.Data?.Date ?? "--";
    public string FormattedMonthPrimogems => _travelersDiary?.Data?.MonthData?.CurrentPrimogems.ToString("N0") ?? "0";
    public string FormattedMonthMora => _travelersDiary?.Data?.MonthData?.CurrentMora.ToString("N0") ?? "0";


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
            StatusMessage = "正在连接米游社...";

            var configPath = Helpers.AppPaths.ConfigFile;
            if (!File.Exists(configPath))
            {
                StatusMessage = "需先登录账号";
                return;
            }

            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonSerializer.Deserialize<HoyoverseCheckinConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (string.IsNullOrEmpty(config?.Account?.Cookie))
            {
                StatusMessage = "登录信息无效";
                return;
            }

            var cookie = config.Account.Cookie;

            StatusMessage = "获取角色信息...";
            var rolesResponse = await _userInfoService.GetUserGameRolesAsync(cookie);
            var role = rolesResponse?.data?.list?.FirstOrDefault();

            if (role == null)
            {
                StatusMessage = "无法获取角色";
                return;
            }

            Uid = role.game_uid;
            Nickname = role.nickname;

            StatusMessage = "分析旅行札记..."; 
            
            TravelersDiary = await _genshinService.GetTravelersDiarySummaryAsync(Uid, cookie, DateTime.Now.Month);

            StatusMessage = $"";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error: {ex.Message}");
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
            1 => "#FF7675",
            2 => "#FAB1A0",
            3 => "#74B9FF",
            4 => "#55EFC4",
            5 => "#81ECEC",
            6 => "#FFEAA7",
            11 => "#A29BFE",
            _ => "#B2BEC3"
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
    public string FormattedPercent => $"{Percent}%";
    public string Color { get; set; } = "#000000";
}