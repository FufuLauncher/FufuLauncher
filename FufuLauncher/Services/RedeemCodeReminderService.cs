/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;

namespace FufuLauncher.Helpers
{
    public class RedeemCodeReminderService
    {
        private readonly ILocalSettingsService _localSettingsService;

        private const string FingerprintSettingKey = "LastRedeemCodeFingerprint";
        private const string LastReminderDateSettingKey = "LastRedeemCodeReminderDate";

        public RedeemCodeReminderService(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;
        }

        /// <summary>
        /// 检查兑换码是否有变更，如有则即时推送通知（发布触发）。
        /// 同时保留辅助的"最后一天"提醒功能。
        /// </summary>
        public async Task CheckRedeemCodesAsync(Action<NotificationMessage> showNotificationAction)
        {
            try
            {
                bool isOs = false;
                var gamePathObj = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
                if (gamePathObj is string gamePath && !string.IsNullOrEmpty(gamePath))
                {
                    var dir = gamePath;
                    if (System.IO.File.Exists(dir))
                        dir = System.IO.Path.GetDirectoryName(dir) ?? dir;
                    isOs = dir != null && System.IO.File.Exists(System.IO.Path.Combine(dir, "GenshinImpact.exe"));
                }

                // 获取当前兑换码列表
                var codesList = await FetchRedeemCodesAsync(isOs);
                if (codesList == null || codesList.Count == 0)
                    return;

                // 计算所有兑换码的指纹（用于检测变更）
                var currentFingerprint = ComputeFingerprint(codesList);
                var lastFingerprintObj = await _localSettingsService.ReadSettingAsync(FingerprintSettingKey);
                var lastFingerprint = lastFingerprintObj?.ToString();

                var todayStr = DateTime.Now.ToString("yyyy-MM-dd");

                // 1. 首次运行（指纹为空）→ 仅注册，不通知
                if (string.IsNullOrEmpty(lastFingerprint))
                {
                    await _localSettingsService.SaveSettingAsync(FingerprintSettingKey, currentFingerprint);
                    Debug.WriteLine("[RedeemCodes] 首次注册兑换码指纹，跳过通知");
                    return;
                }

                // 2. 检测是否有变更 → 发布触发通知
                if (lastFingerprint != currentFingerprint)
                {
                    await NotifyNewCodesAsync(codesList, showNotificationAction);
                    await _localSettingsService.SaveSettingAsync(FingerprintSettingKey, currentFingerprint);
                    Debug.WriteLine("[RedeemCodes] 兑换码有变更，已推送通知");
                    return; // 有变更时不再触发辅助提醒
                }

                // 3. 无变更 → 辅助提醒：检查今天过期的兑换码（原"最后一天"提醒）
                await CheckExpiringTodayAsync(codesList, todayStr, showNotificationAction);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RedeemCodes] 兑换码检查失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 从 API 获取兑换码列表
        /// </summary>
        private async Task<List<RedeemCodeItem>?> FetchRedeemCodesAsync(bool isOs)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip
            };

            if (isOs)
            {
                var json = await client.GetStringAsync(ApiEndpoints.RedeemCodesOsUrl);
                var response = System.Text.Json.JsonSerializer.Deserialize<HoyoCodeResponse>(json, options);
                return response?.Codes?
                    .Where(c => string.Equals(c.Status, "OK", StringComparison.OrdinalIgnoreCase))
                    .Select(c => new RedeemCodeItem
                    {
                        Title = c.Rewards,
                        Codes = new List<string> { c.Code }
                    })
                    .ToList();
            }
            else
            {
                var json = await client.GetStringAsync(ApiEndpoints.RedeemCodesUrl);
                return System.Text.Json.JsonSerializer.Deserialize<List<RedeemCodeItem>>(json, options);
            }
        }

        /// <summary>
        /// 计算兑换码列表的 SHA256 指纹
        /// </summary>
        private static string ComputeFingerprint(List<RedeemCodeItem> codes)
        {
            var sb = new StringBuilder();
            foreach (var code in codes.OrderBy(c => c.Title))
            {
                sb.Append(code.Title);
                sb.Append('|');
                foreach (var c in code.Codes.OrderBy(x => x))
                {
                    sb.Append(c);
                    sb.Append(',');
                }
                sb.Append('|');
                sb.Append(code.Valid);
                sb.Append('|');
                sb.Append(code.Time);
                sb.Append(';');
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        /// <summary>
        /// 计算有效期倒计时文本
        /// </summary>
        private static string FormatRemainingTime(string? validStr)
        {
            if (string.IsNullOrEmpty(validStr))
                return "未知";

            // 尝试解析日期范围，例如 "2024-01-01 ~ 2024-01-03" 或 "2024-01-01"
            var match = System.Text.RegularExpressions.Regex.Match(validStr, @"(\d{4}[-/]\d{1,2}[-/]\d{1,2})");
            if (!match.Success)
                return validStr;

            if (DateTime.TryParse(match.Groups[1].Value, out var endDate))
            {
                // 如果取到的是开始日期，尝试找结束日期
                var matches = System.Text.RegularExpressions.Regex.Matches(validStr, @"(\d{4}[-/]\d{1,2}[-/]\d{1,2})");
                if (matches.Count >= 2)
                {
                    // 用第二个日期作为结束日期
                    if (DateTime.TryParse(matches[1].Value, out var secondDate))
                        endDate = secondDate;
                }

                var remaining = endDate.Date - DateTime.Now.Date;
                if (remaining.TotalDays < 0)
                    return "已过期";
                else if (remaining.TotalDays == 0)
                    return "今天到期";
                else if (remaining.TotalDays < 1)
                    return $"剩余 {remaining.Hours} 小时";
                else if (remaining.TotalDays < 30)
                    return $"剩余 {(int)remaining.TotalDays} 天";
                else
                    return $"剩余 {(int)remaining.TotalDays} 天";
            }

            return validStr;
        }

        /// <summary>
        /// 推送新兑换码发布通知
        /// </summary>
        private static async Task NotifyNewCodesAsync(List<RedeemCodeItem> codes, Action<NotificationMessage> showNotificationAction)
        {
            // 只取前几个活动，避免通知过长
            var recentCodes = codes.Take(5).ToList();

            var lines = new List<string>();
            foreach (var item in recentCodes)
            {
                var remaining = FormatRemainingTime(!string.IsNullOrEmpty(item.Valid) ? item.Valid : item.Time);
                var codesStr = string.Join(", ", item.Codes.Take(3));
                lines.Add($"• {item.Title} → {codesStr}（{remaining}）");
            }

            if (codes.Count > 5)
            {
                lines.Add($"……以及另外 {codes.Count - 5} 个活动");
            }

            var msg = new NotificationMessage(
                "新兑换码已发布",
                "发现新的兑换码活动：\n" + string.Join("\n", lines) + "\n请及时前往游戏内兑换！",
                NotificationType.Information,
                0
            );

            showNotificationAction?.Invoke(msg);
            await Task.CompletedTask;
        }

        /// <summary>
        /// 辅助提醒：检查今天过期的兑换码（原"最后一天"提醒，降级为辅助）
        /// </summary>
        private async Task CheckExpiringTodayAsync(List<RedeemCodeItem> codes, string todayStr, Action<NotificationMessage> showNotificationAction)
        {
            var lastRemindedObj = await _localSettingsService.ReadSettingAsync(LastReminderDateSettingKey);
            if (lastRemindedObj != null && lastRemindedObj.ToString() == todayStr)
            {
                return; // 今天已提醒过
            }

            var todaysCodes = codes.Where(c =>
                (!string.IsNullOrEmpty(c.Valid) && c.Valid.Contains(todayStr)) ||
                (!string.IsNullOrEmpty(c.Time) && c.Time.Contains(todayStr))
            ).ToList();

            if (todaysCodes.Count > 0)
            {
                var titles = string.Join("、", todaysCodes.Select(c => c.Title));
                var codesContent = string.Join("\n", todaysCodes.SelectMany(c => c.Codes));

                var msg = new NotificationMessage(
                    "兑换码即将失效",
                    $"活动 {titles} 包含可用兑换码：\n{codesContent}\n请尽快前往游戏内使用，将在今天之后失效！",
                    NotificationType.Warning,
                    0
                );

                showNotificationAction?.Invoke(msg);
                await _localSettingsService.SaveSettingAsync(LastReminderDateSettingKey, todayStr);
            }
        }
    }
}
