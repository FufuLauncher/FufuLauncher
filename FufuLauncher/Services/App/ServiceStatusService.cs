using System.Globalization;
using System.Text.Json;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed class ServiceStatusService
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FufuLauncher/1.7");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public async Task<ServiceStatusPage> GetPageAsync(int page, CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(ApiEndpoints.GetStatsMonitorListUrl(page), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("status", out var status) &&
            !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Service status request failed.");
        }

        var monitors = new List<ServiceStatusMonitor>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                var monitor = ParseMonitor(item);
                if (monitor != null)
                {
                    monitors.Add(monitor);
                }
            }
        }

        var totalMonitors = 0;
        var perPage = 30;
        if (root.TryGetProperty("psp", out var psp))
        {
            totalMonitors = GetInt32(psp, "totalMonitors");
            var pageSize = GetInt32(psp, "perPage");
            if (pageSize > 0)
            {
                perPage = pageSize;
            }
        }

        var up = 0;
        var down = 0;
        var paused = 0;
        if (root.TryGetProperty("statistics", out var statistics) &&
            statistics.TryGetProperty("counts", out var counts))
        {
            up = GetInt32(counts, "up");
            down = GetInt32(counts, "down");
            paused = GetInt32(counts, "paused");
        }
        else
        {
            up = monitors.Count(m => m.StatusClass == "success");
            down = monitors.Count(m => m.StatusClass == "danger");
            paused = monitors.Count(m => m.StatusClass == "paused");
        }

        return new ServiceStatusPage
        {
            Monitors = monitors,
            TotalMonitors = totalMonitors,
            PerPage = perPage,
            UpCount = up,
            DownCount = down,
            PausedCount = paused
        };
    }

    public async Task<ServiceMonitorDetail> GetMonitorDetailAsync(int monitorId,
        CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(ApiEndpoints.GetStatsMonitorDetailUrl(monitorId),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("status", out var status) &&
            !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Service detail request failed.");
        }

        if (!root.TryGetProperty("monitor", out var monitor) || monitor.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Service detail request failed.");
        }

        var points = new List<ServiceResponseTimePoint>();
        if (monitor.TryGetProperty("responseTimes", out var times) && times.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in times.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                DateTime? pointTime = null;
                if (TryParseResponseTime(GetString(entry, "datetime"), out var parsedTime))
                {
                    pointTime = parsedTime;
                }

                points.Add(new ServiceResponseTimePoint
                {
                    Value = GetDouble(entry, "value"),
                    Time = pointTime
                });
            }
        }

        var regions = new List<string>();
        if (monitor.TryGetProperty("regions", out var regionElements) &&
            regionElements.ValueKind == JsonValueKind.Array)
        {
            foreach (var region in regionElements.EnumerateArray())
            {
                var regionName = region.ValueKind == JsonValueKind.String ? region.GetString() : null;
                if (!string.IsNullOrWhiteSpace(regionName))
                {
                    regions.Add(regionName);
                }
            }
        }

        DateTime? createdAt = null;
        if (DateTime.TryParseExact(GetString(monitor, "createdAt"), "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var created))
        {
            createdAt = created;
        }

        var name = GetString(monitor, "name")?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = $"#{monitorId}";
        }

        return new ServiceMonitorDetail
        {
            MonitorId = monitorId,
            Name = name,
            StatusClass = GetString(monitor, "statusClass") ?? string.Empty,
            Type = GetString(monitor, "type") ?? string.Empty,
            CheckInterval = GetString(monitor, "checkInterval") ?? string.Empty,
            CreatedAt = createdAt,
            Url = GetString(monitor, "url"),
            Timezone = GetString(root, "timezone"),
            Days = ParseDays(monitor, "dailyRatios"),
            ResponseTimes = points,
            ResponseTimeStats = ParseResponseTimeStats(monitor),
            Events = ParseEvents(monitor),
            Regions = regions,
            Uptime1 = ParseRatio(monitor, "1dRatio"),
            Uptime7 = ParseRatio(monitor, "7dRatio"),
            Uptime30 = ParseRatio(monitor, "30dRatio"),
            Uptime90 = ParseRatio(monitor, "90dRatio")
        };
    }

    private static ServiceStatusMonitor? ParseMonitor(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var monitorId = GetInt32(item, "monitorId");
        if (monitorId <= 0)
        {
            return null;
        }

        var days = ParseDays(item, "dailyRatios");

        double uptime90 = 0;
        if (item.TryGetProperty("90dRatio", out var ratio90) && ratio90.ValueKind == JsonValueKind.Object)
        {
            double.TryParse(GetString(ratio90, "ratio"), NumberStyles.Float, CultureInfo.InvariantCulture,
                out uptime90);
        }

        DateTime? lastDowntime = null;
        if (item.TryGetProperty("lastDowntime", out var last) && last.ValueKind == JsonValueKind.Object)
        {
            if (DateTime.TryParseExact(GetString(last, "date"), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                lastDowntime = parsed;
            }
        }

        var name = GetString(item, "name")?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = $"#{monitorId}";
        }

        return new ServiceStatusMonitor
        {
            MonitorId = monitorId,
            Name = name,
            StatusClass = GetString(item, "statusClass") ?? string.Empty,
            Days = days,
            Uptime90 = uptime90,
            LastDowntime = lastDowntime
        };
    }

    private static List<ServiceStatusDay> ParseDays(JsonElement parent, string propertyName)
    {
        var days = new List<ServiceStatusDay>();
        if (!parent.TryGetProperty(propertyName, out var dailyRatios) ||
            dailyRatios.ValueKind != JsonValueKind.Array)
        {
            return days;
        }

        foreach (var entry in dailyRatios.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var dateText = GetString(entry, "date");
            if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                continue;
            }

            var hasData = double.TryParse(GetString(entry, "ratio"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var ratio);
            days.Add(new ServiceStatusDay
            {
                Date = date,
                Ratio = hasData ? ratio : 0,
                HasData = hasData
            });
        }

        days.Sort((left, right) => left.Date.CompareTo(right.Date));
        return days;
    }

    private static double ParseRatio(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var ratio) || ratio.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        return GetDouble(ratio, "ratio");
    }

    private static ServiceResponseTimeStats ParseResponseTimeStats(JsonElement monitor)
    {
        if (!monitor.TryGetProperty("responseTimeStats", out var stats) ||
            stats.ValueKind != JsonValueKind.Object)
        {
            return new ServiceResponseTimeStats();
        }

        return new ServiceResponseTimeStats
        {
            Average = GetDouble(stats, "avg_response_time"),
            Min = GetDouble(stats, "min_response_time"),
            Max = GetDouble(stats, "max_response_time"),
            TotalRequests = GetInt32(stats, "total_requests")
        };
    }

    private static List<ServiceMonitorEvent> ParseEvents(JsonElement monitor)
    {
        var events = new List<ServiceMonitorEvent>();
        if (!monitor.TryGetProperty("logs", out var logs) || logs.ValueKind != JsonValueKind.Array)
        {
            return events;
        }

        foreach (var entry in logs.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            DateTimeOffset? occurredAt = null;
            if (DateTimeOffset.TryParse(GetString(entry, "dateGMTISO"), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                occurredAt = parsed;
            }

            if (occurredAt == null)
            {
                var unixTime = GetInt64(entry, "time");
                if (unixTime > 0)
                {
                    occurredAt = DateTimeOffset.FromUnixTimeSeconds(unixTime);
                }
            }

            string? reasonCode = null;
            string? reasonShort = null;
            string? reasonFull = null;
            if (entry.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.Object)
            {
                reasonCode = GetString(reason, "code");
                if (reason.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.Object)
                {
                    reasonShort = GetString(detail, "short");
                    reasonFull = GetString(detail, "full");
                }
            }

            events.Add(new ServiceMonitorEvent
            {
                Class = GetString(entry, "class") ?? string.Empty,
                Label = GetString(entry, "label") ?? string.Empty,
                OccurredAt = occurredAt,
                DateText = GetString(entry, "date") ?? string.Empty,
                Duration = GetString(entry, "duration") ?? string.Empty,
                ReasonCode = reasonCode,
                ReasonShort = reasonShort,
                ReasonFull = reasonFull
            });
        }

        return events;
    }

    private static bool TryParseResponseTime(string? text, out DateTime value)
    {
        return DateTime.TryParseExact(text, "MMMM d, yyyy, HH:mm", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out value) ||
               DateTime.TryParseExact(text, "MMMM d, yyyy, HH:mm:ss", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out value);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        var text = property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static long GetInt64(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return value;
        }

        var text = property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static double GetDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
        {
            return value;
        }

        var text = property.ValueKind == JsonValueKind.String ? property.GetString() : property.GetRawText();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }
}