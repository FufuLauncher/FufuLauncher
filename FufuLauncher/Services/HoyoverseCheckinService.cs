using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using MihoyoBBS;

namespace FufuLauncher.Services;

public class HoyoverseCheckinService : IHoyoverseCheckinService
{
    private async Task<Config> LoadConfigWithLoggingAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config.json");
        Debug.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Debug.WriteLine($" [签到] 尝试加载配置文件");
        Debug.WriteLine($" [签到] 文件路径: {path}");
        Debug.WriteLine($" [签到] 文件存在: {File.Exists(path)}");

        if (!File.Exists(path))
        {
            Debug.WriteLine(" [签到] 配置文件不存在！");
            return new Config();
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            Debug.WriteLine($" [签到] 文件内容:\n{json}");

            var config = JsonSerializer.Deserialize<Config>(json);
            Debug.WriteLine($" [签到] 反序列化成功");
            Debug.WriteLine($" [签到] Cookie长度: {config?.Account?.Cookie?.Length ?? 0}");
            Debug.WriteLine($" [签到] Enable状态: {config?.Games?.Cn?.Enable}");
            Debug.WriteLine($" [签到] Checkin状态: {config?.Games?.Cn?.Genshin?.Checkin}");

            return config ?? new Config();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($" [签到] 反序列化失败: {ex.Message}");
            Debug.WriteLine($" [签到] 异常详情:\n{ex.StackTrace}");
            return new Config();
        }
    }

    public async Task<(string status, string summary)> GetCheckinStatusAsync()
    {
        var config = await LoadConfigWithLoggingAsync();
        if (!config.Games.Cn.Enable || !config.Games.Cn.Genshin.Checkin)
            return ("签到功能未启用", "config.json中设置Enable=true");

        var genshin = new Genshin();
        await genshin.InitializeAsync(config).ConfigureAwait(false);

        if (genshin.AccountList.Count == 0)
            return ("未检测到账号", "请检查Cookie和绑定");

        var account = genshin.AccountList[0];
        var isSignData = await genshin.IsSignAsync(account.Region, account.GameUid, false).ConfigureAwait(false);

        return isSignData?.IsSign == true
            ? ("今日已签到", $"账号: {account.Nickname}")
            : ("今日未签到", $"账号: {account.Nickname} (可签到)");
    }

    public async Task<(bool success, string message)> ExecuteCheckinAsync()
    {
        Debug.WriteLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Debug.WriteLine($" [签到] 开始执行签到");

        var config = await LoadConfigWithLoggingAsync();

        if (!config.Games.Cn.Enable || !config.Games.Cn.Genshin.Checkin)
        {
            Debug.WriteLine(" [签到] 功能未启用");
            return (false, "功能未启用");
        }

        Debug.WriteLine(" [签到] 功能已启用，准备初始化Genshin");
        var genshin = new Genshin();

        Debug.WriteLine(" [签到] 调用InitializeAsync...");
        await genshin.InitializeAsync(config).ConfigureAwait(false);

        Debug.WriteLine($" [签到] 初始化完成，账号数量: {genshin.AccountList.Count}");

        Debug.WriteLine(" [签到] 调用SignAccountAsync...");
        var result = await genshin.SignAccountAsync(config).ConfigureAwait(false);

        Debug.WriteLine($" [签到] 签到结果:\n{result}");

        var isSuccess = !result.Contains("失败") && !result.Contains("异常");
        var summary = string.Join(" | ", result.Split('\n').Take(2));

        Debug.WriteLine($" [签到] 执行完成: success={isSuccess}, summary={summary}");

        return (isSuccess, summary);
    }
}