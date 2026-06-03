using FufuLauncher.Messages;

namespace FufuLauncher.Models;

public class UnifiedCheckinResult
{
    public bool OverallSuccess => GameResult.Success && CommunityResult.Success && CloudGameResult.Success;
    public NotificationType NotificationType
    {
        get
        {
            if (OverallSuccess) return NotificationType.Success;
            if (GameResult.Success || CommunityResult.Success || CloudGameResult.Success)
                return NotificationType.Warning;
            return NotificationType.Error;
        }
    }
    public CheckinTypeResult GameResult { get; set; } = new() { TypeName = "游戏签到" };
    public CheckinTypeResult CommunityResult { get; set; } = new() { TypeName = "社区签到" };
    public CheckinTypeResult CloudGameResult { get; set; } = new() { TypeName = "云原神签到" };
    public string SummaryMessage { get; set; } = "";
    public string GameSignDays { get; set; } = "";
    public string GameRewardItem { get; set; } = "";

    public string GetDetailedSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(SummaryMessage);
        if (GameResult.Executed) sb.AppendLine($"├─ {GameResult.GetSummary()}");
        if (CommunityResult.Executed) sb.AppendLine($"├─ {CommunityResult.GetSummary()}");
        if (CloudGameResult.Executed) sb.AppendLine($"└─ {CloudGameResult.GetSummary()}");
        return sb.ToString().TrimEnd();
    }
}

public class CheckinTypeResult
{
    public string TypeName { get; set; } = "";
    public bool Executed { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int SuccessCount { get; set; }
    public int FailCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Details { get; set; } = new();

    public string GetSummary()
    {
        var icon = Success ? "✅" : "❌";
        var countMsg = SuccessCount > 0 ? $"{SuccessCount}个账号成功" : "";
        var failMsg = FailCount > 0 ? $"{FailCount}个失败" : "";
        var sep = string.IsNullOrEmpty(countMsg) || string.IsNullOrEmpty(failMsg) ? "" : "，";
        var msg = $"{icon} {string.Join("，", new[] { countMsg, failMsg }.Where(s => !string.IsNullOrEmpty(s)))}";
        if (!string.IsNullOrEmpty(Message) && Success) msg += $" | {Message}";
        if (!string.IsNullOrEmpty(Message) && !Success) msg += $" | {Message}";
        return msg;
    }
}

public class AccountCheckinResult
{
    public string Uid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}

public class AccountCredentials
{
    public string Uid { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string Stuid { get; set; } = "";
    public string Stoken { get; set; } = "";
    public string Mid { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string CloudComboToken { get; set; } = "";

    public string GetStokenCookie() => $"stuid={Stuid};stoken={Stoken};mid={Mid}";
}
