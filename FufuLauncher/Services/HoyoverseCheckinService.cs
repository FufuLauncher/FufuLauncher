using System.Text.Json;
using FufuLauncher.Contracts.Services;
using MihoyoBBS;

namespace FufuLauncher.Services;

public class HoyoverseCheckinService : IHoyoverseCheckinService
{
    private async Task<Config> LoadConfigWithLoggingAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(path)) return new Config();

        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<Config>(json) ?? new Config();
        }
        catch
        {
            return new Config();
        }
    }

    public async Task<List<string>> GetBoundUidsAsync()
    {
        var config = await LoadConfigWithLoggingAsync();
        if (!config.Games.Cn.Enable) return new List<string>();

        var genshin = new Genshin();
        await genshin.InitializeAsync(config).ConfigureAwait(false);
        return genshin.AccountList.Select(a => a.GameUid).ToList();
    }

    public async Task<(string status, string summary)> GetCheckinStatusAsync(string targetUid = null)
    {
        var config = await LoadConfigWithLoggingAsync();
        if (!config.Games.Cn.Enable || !config.Games.Cn.Genshin.Checkin)
            return ("签到功能未启用", "config.json中设置Enable=true");

        var genshin = new Genshin();
        await genshin.InitializeAsync(config).ConfigureAwait(false);

        if (genshin.AccountList.Count == 0)
            return ("未检测到账号", "请检查Cookie和绑定");

        var account = string.IsNullOrEmpty(targetUid) 
            ? genshin.AccountList[0] 
            : genshin.AccountList.FirstOrDefault(a => a.GameUid == targetUid) ?? genshin.AccountList[0];

        var isSignData = await genshin.IsSignAsync(account.Region, account.GameUid, false).ConfigureAwait(false);

        return isSignData?.IsSign == true
            ? ("今日已签到", $"账号: {account.Nickname}")
            : ("今日未签到", $"账号: {account.Nickname} (可签到)");
    }

    public async Task<(bool success, string message)> ExecuteCheckinAsync(string targetUid = null)
    {
        var config = await LoadConfigWithLoggingAsync();
        if (!config.Games.Cn.Enable || !config.Games.Cn.Genshin.Checkin)
        {
            return (false, "功能未启用");
        }
        
        var genshin = new Genshin();
        await genshin.InitializeAsync(config).ConfigureAwait(false);
        
        var result = await genshin.SignAccountAsync(config, targetUid).ConfigureAwait(false);
        var isSuccess = !result.Contains("失败") && !result.Contains("异常");
        var summary = string.Join("", result.Split('\n').Take(2));

        return (isSuccess, summary);
    }
}