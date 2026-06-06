using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;
using MihoyoBBS;

namespace FufuLauncher.Services;

public class HoyoverseCheckinService : IHoyoverseCheckinService
{
    private readonly ILocalSettingsService _localSettingsService;

    public HoyoverseCheckinService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    private async Task<Config> LoadConfigWithLoggingAsync()
    {
        var path = Helpers.AppPaths.ConfigFile;
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

    private async Task<HashSet<string>> GetDisabledUidsAsync()
    {
        var disabledUidsJson = await _localSettingsService.ReadSettingAsync("CheckinDisabledUids");
        if (disabledUidsJson != null)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(disabledUidsJson.ToString() ?? "[]");
                if (list != null) return new HashSet<string>(list);
            }
            catch { }
        }
        return new HashSet<string>();
    }

    private async Task<(string? cookie, string? configPath)> GetOsConfigAsync()
    {
        var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
        var activeFile = activeFileObj?.ToString() ?? "config.lab.json";
        var paths = new[]
        {
            Path.Combine(Helpers.AppPaths.DataDir, "config.lab.json"),
            Path.Combine(AppContext.BaseDirectory, "config.lab.json"),
            Path.Combine(Environment.CurrentDirectory, "config.lab.json"),
        };
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var doc = JsonDocument.Parse(json);
                var cookie = doc.RootElement.GetProperty("Account").GetProperty("Cookie").GetString();
                if (!string.IsNullOrEmpty(cookie) && IsOsCookie(cookie)) return (cookie, path);
            }
            catch { }
        }
        return (null, null);
    }

    private static bool IsOsCookie(string cookie)
    {
        return cookie.Contains("ltuid") || cookie.Contains("ltoken") || cookie.Contains("account_id");
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
        var isIntlRaw = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
        bool isOs = isIntlRaw != null && isIntlRaw.ToString().ToLower() == "true";
        Debug.WriteLine($"[GetCheckinStatus] isOs={isOs}");

        if (isOs)
        {
            var (cookie, cfgPath) = await GetOsConfigAsync();
            Debug.WriteLine($"[GetCheckinStatus] OS cookie found={!string.IsNullOrEmpty(cookie)} path={cfgPath}");
            if (string.IsNullOrEmpty(cookie))
                return ("HoYoLAB 未登录", "请先登录国际服账号");

            var os = new HoyolabCheckinService();
            await os.InitializeAsync(cookie).ConfigureAwait(false);
            Debug.WriteLine($"[GetCheckinStatus] OS accounts={os.AccountList.Count} lastError={HoyolabCheckinService.LastApiError}");

            if (os.AccountList.Count == 0)
                return ("未检测到绑定", HoyolabCheckinService.LastApiError);

            var account = os.AccountList[0];
            var isData = await os.IsSignAsync(account.Region, account.GameUid).ConfigureAwait(false);
            Debug.WriteLine($"[GetCheckinStatus] IsSign result={isData != null} lastError={HoyolabCheckinService.LastApiError}");

            if (isData == null)
                return ("获取状态失败", HoyolabCheckinService.LastApiError);

            var signedText = isData.IsSign ? "今日已签到" : "今日未签到";
            Debug.WriteLine($"[GetCheckinStatus] signed={isData.IsSign}");
            return (signedText, $"HoYoLAB: {account.Nickname}");
        }

        var config = await LoadConfigWithLoggingAsync();
        if (!config.Games.Cn.Enable || !config.Games.Cn.Genshin.Checkin)
            return ("签到功能未启用", "config.json中设置Enable=true");

        var genshin = new Genshin();
        await genshin.InitializeAsync(config).ConfigureAwait(false);

        if (genshin.AccountList.Count == 0)
        {
            string errorSummary = !string.IsNullOrEmpty(GameCheckin.LastApiError)
                ? $"初始化失败: {GameCheckin.LastApiError}"
                : "请检查Cookie和绑定";
            return ("未检测到账号", errorSummary);
        }

        var cnAccount = string.IsNullOrEmpty(targetUid)
            ? genshin.AccountList[0]
            : genshin.AccountList.FirstOrDefault(a => a.GameUid == targetUid) ?? genshin.AccountList[0];

        var isSignData = await genshin.IsSignAsync(cnAccount.Region, cnAccount.GameUid, false).ConfigureAwait(false);

        if (isSignData == null)
        {
            string errorSummary = !string.IsNullOrEmpty(GameCheckin.LastApiError)
                ? $"获取状态失败: {GameCheckin.LastApiError}"
                : "未知网络错误";
            return ("获取状态失败", errorSummary);
        }

        return isSignData.IsSign == true
            ? ("今日已签到", $"账号: {cnAccount.Nickname}")
            : ("今日未签到", $"账号: {cnAccount.Nickname} (可签到)");
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

        var disabledUids = await GetDisabledUidsAsync();
        var result = await genshin.SignAccountAsync(config, targetUid, disabledUids).ConfigureAwait(false);
        var isSuccess = !result.Contains("失败") && !result.Contains("异常");

        var summary = string.Join(" ", result.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        return (isSuccess, summary);
    }
}