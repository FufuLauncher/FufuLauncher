/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Text.Json;

namespace FufuLauncher.Services
{
    public class DailyNoteCardData
    {
        public int CurrentResin { get; set; }
        public int MaxResin { get; set; }
        public int FinishedTaskNum { get; set; }
        public int TotalTaskNum { get; set; }
        public int CurrentHomeCoin { get; set; }
        public int MaxHomeCoin { get; set; }
        public int CurrentExpeditionNum { get; set; }
        public int MaxExpeditionNum { get; set; }
        public bool IsTransformerObtained { get; set; }
        public string TransformerRecoveryTime { get; set; } = "";
    }

    public static class DailyNoteParser
    {
        public static DailyNoteCardData Parse(string json)
        {
            Debug.WriteLine($"[DailyNoteParser] 解析JSON: {json?.Substring(0, Math.Min(200, json?.Length ?? 0))}...");

            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int retcode = root.TryGetProperty("retcode", out var retcodeProp) ? retcodeProp.GetInt32() : -1;

            if (retcode != 0)
            {
                string message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "未知错误";
                string error = string.IsNullOrWhiteSpace(message)
                    ? $"接口返回错误，retcode={retcode}"
                    : $"{message}，retcode={retcode}";
                throw new InvalidOperationException($"获取便签数据失败: {error}");
            }

            if (!root.TryGetProperty("data", out var data))
            {
                throw new InvalidOperationException("获取便签数据失败: 响应格式错误");
            }

            var result = new DailyNoteCardData
            {
                CurrentResin = data.TryGetProperty("current_resin", out var resin) ? resin.GetInt32() : 0,
                MaxResin = data.TryGetProperty("max_resin", out var maxResin) ? maxResin.GetInt32() : 160,
                FinishedTaskNum = data.TryGetProperty("finished_task_num", out var finishedTask) ? finishedTask.GetInt32() : 0,
                TotalTaskNum = data.TryGetProperty("total_task_num", out var totalTask) ? totalTask.GetInt32() : 4,
                CurrentHomeCoin = data.TryGetProperty("current_home_coin", out var homeCoin) ? homeCoin.GetInt32() : 0,
                MaxHomeCoin = data.TryGetProperty("max_home_coin", out var maxHomeCoin) ? maxHomeCoin.GetInt32() : 2400,
                CurrentExpeditionNum = data.TryGetProperty("current_expedition_num", out var expeditionNum) ? expeditionNum.GetInt32() : 0,
                MaxExpeditionNum = data.TryGetProperty("max_expedition_num", out var maxExpedition) ? maxExpedition.GetInt32() : 5
            };

            if (data.TryGetProperty("transformer", out var transformer))
            {
                result.IsTransformerObtained = transformer.TryGetProperty("obtained", out var obtained) && obtained.GetBoolean();

                if (transformer.TryGetProperty("recovery_time", out var recoveryTime))
                {
                    bool reached = recoveryTime.TryGetProperty("reached", out var reachedProp) && reachedProp.GetBoolean();
                    if (!reached)
                    {
                        int day = recoveryTime.TryGetProperty("Day", out var d) ? d.GetInt32() : 0;
                        int hour = recoveryTime.TryGetProperty("Hour", out var h) ? h.GetInt32() : 0;
                        int minute = recoveryTime.TryGetProperty("Minute", out var m) ? m.GetInt32() : 0;
                        result.TransformerRecoveryTime = $"{day}天 {hour}时 {minute}分";
                    }
                    else
                    {
                        result.TransformerRecoveryTime = result.IsTransformerObtained ? "已获取" : "未获取";
                    }
                }
            }

            Debug.WriteLine("[DailyNoteParser] 解析成功");
            return result;
        }
    }
}

