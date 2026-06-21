using FufuLauncher.Messages;

namespace FufuLauncher.Models;

public class UnifiedCheckinResult
{
    private IEnumerable<CheckinTypeResult> ExecutedResults =>
        new[] { GameResult, CommunityResult, CloudGameResult }.Where(r => r.Executed);

    public bool OverallSuccess
    {
        get
        {
            var executedResults = ExecutedResults.ToList();
            return executedResults.Count > 0 && executedResults.All(r => r.Success);
        }
    }

    public NotificationType NotificationType
    {
        get
        {
            var executedResults = ExecutedResults.ToList();
            if (executedResults.Count == 0) return NotificationType.Error;
            if (executedResults.All(r => r.Success)) return NotificationType.Success;
            if (executedResults.Any(r => r.Success))
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
    public List<AccountCheckinDetail> AccountResults { get; set; } = new();

    public string GetDetailedSummary()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var account in AccountResults)
        {
            sb.AppendLine($"[{account.Nickname}]");
            foreach (var item in account.Items)
            {
                string status = item.Success == true ? "完成" :
                                item.Success == false ? "失败" : "跳过";
                string extra = string.IsNullOrEmpty(item.Message) ? "" : $" - {item.Message}";
                sb.AppendLine($"  {item.TypeName}: {status}{extra}");
            }
        }
        sb.AppendLine(SummaryMessage);
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
        var countMsg = SuccessCount > 0 ? $"{SuccessCount}个账号成功" : "";
        var failMsg = FailCount > 0 ? $"{FailCount}个失败" : "";
        var sep = string.IsNullOrEmpty(countMsg) || string.IsNullOrEmpty(failMsg) ? "" : "，";
        var msg = string.Join("，", new[] { countMsg, failMsg }.Where(s => !string.IsNullOrEmpty(s)));
        if (!string.IsNullOrEmpty(Message)) msg += $" | {Message}";
        return msg;
    }
}

public class AccountCheckinDetail
{
    public string Nickname { get; set; } = "";
    public List<(string TypeName, bool? Success, string Message)> Items { get; set; } = new();
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
