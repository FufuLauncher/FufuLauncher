/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.Services;

public static class PluginConfigGuard
{
    public static readonly TimeSpan TemporaryBypassDuration = TimeSpan.FromMinutes(10);

    private const string MainPluginConfigName = "config.ini";

    private static readonly object StateLock = new();

    private static DateTimeOffset? _temporaryBypassUntilUtc;
    private static Func<bool>? _exemptBuildProvider;
    private static PluginConfigBaseline? _cachedBaseline;
    private static DateTime _cachedBaselineStampUtc;
    private static DateTime _lastRepairNoticeUtc;

    internal static void AttachExemptBuildProvider(Func<bool> provider) => _exemptBuildProvider = provider;

    public static bool IsExemptBuild
    {
        get
        {
            try
            {
                if (Microsoft.UI.Xaml.Application.Current is not App)
                {
                    return true;
                }

                if (AppVersionHelper.IsPreviewBuild)
                {
                    return true;
                }

                return _exemptBuildProvider?.Invoke() == true;
            }
            catch
            {
                return true;
            }
        }
    }

    public static bool IsTemporaryBypassActive
    {
        get
        {
            lock (StateLock)
            {
                if (_temporaryBypassUntilUtc is not { } until)
                {
                    return false;
                }

                if (DateTimeOffset.UtcNow < until)
                {
                    return true;
                }

                _temporaryBypassUntilUtc = null;
                return false;
            }
        }
    }

    public static TimeSpan TemporaryBypassRemaining
    {
        get
        {
            lock (StateLock)
            {
                return _temporaryBypassUntilUtc is { } until && until > DateTimeOffset.UtcNow
                    ? until - DateTimeOffset.UtcNow
                    : TimeSpan.Zero;
            }
        }
    }

    public static bool IsValidationActive => !IsExemptBuild && !IsTemporaryBypassActive;

    public static void StartTemporaryBypass()
    {
        lock (StateLock)
        {
            _temporaryBypassUntilUtc = DateTimeOffset.UtcNow + TemporaryBypassDuration;
        }
    }

    public static void StopTemporaryBypass()
    {
        lock (StateLock)
        {
            _temporaryBypassUntilUtc = null;
        }
    }

    public static bool TryRepairLines(string configPath, IReadOnlyList<string> lines, out List<string> repaired)
    {
        repaired = [];

        try
        {
            if (!TryPrepare(configPath, lines, out var baseline))
            {
                return false;
            }

            var result = PluginConfigRepair.Repair(baseline, lines, out var summary);
            if (!summary.HasChanges)
            {
                return false;
            }

            repaired = result;
            NotifyRepaired(summary);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryRepairText(string configPath, string content, out string repaired)
    {
        repaired = content;

        try
        {
            var lineEnding = content.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n"
                : content.Contains('\n')
                    ? "\n"
                    : content.Contains('\r')
                        ? "\r"
                        : Environment.NewLine;

            var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var lines = normalized.Split('\n');

            if (!TryRepairLines(configPath, lines, out var repairedLines))
            {
                return false;
            }

            repaired = string.Join(lineEnding, repairedLines);
            return true;
        }
        catch
        {
            repaired = content;
            return false;
        }
    }

    public static bool TryRepairFileInPlace(string configPath)
    {
        if (!IsValidationActive || !IsMainPluginConfigPath(configPath) || !File.Exists(configPath))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(configPath);
        }
        catch
        {
            return false;
        }

        var (encoding, offset) = DetectEncoding(bytes);

        string content;
        try
        {
            content = encoding.GetString(bytes, offset, bytes.Length - offset);
        }
        catch
        {
            return false;
        }

        if (!TryRepairText(configPath, content, out var repaired))
        {
            return false;
        }

        try
        {
            File.WriteAllText(configPath, repaired, encoding);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (Encoding Encoding, int Offset) DetectEncoding(byte[] bytes) => bytes switch
    {
        [0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(false, true, true), 4),
        [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(true, true, true), 4),
        [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(true, true), 3),
        [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, true, true), 2),
        [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, true, true), 2),
        _ => (new UTF8Encoding(false, true), 0)
    };

    private static bool TryPrepare(string configPath, IReadOnlyList<string> liveLines, out PluginConfigBaseline baseline)
    {
        baseline = null!;

        if (!IsValidationActive || !IsMainPluginConfigPath(configPath))
        {
            return false;
        }

        var stored = ReadBaselineCached();
        if (stored == null)
        {
            return false;
        }

        var liveDocument = PluginConfigDocument.Parse(liveLines);
        var liveFile = liveDocument.GetValue("General", "File");
        var baselineFile = PluginConfigDocument.Parse(PluginConfigBaseline.SplitLines(stored.Content))
            .GetValue("General", "File");

        if (string.IsNullOrWhiteSpace(baselineFile))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(liveFile) &&
            !string.Equals(Path.GetFileName(liveFile.Trim()), Path.GetFileName(baselineFile.Trim()),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsBaselineCompatibleWithPlugin(stored))
        {
            return false;
        }

        baseline = stored;
        return true;
    }

    private static bool IsMainPluginConfigPath(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
        {
            return false;
        }

        try
        {
            var mainConfigPath = Path.Combine(LightweightPluginService.MainPluginDir, MainPluginConfigName);
            return string.Equals(Path.GetFullPath(configPath), Path.GetFullPath(mainConfigPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBaselineCompatibleWithPlugin(PluginConfigBaseline baseline)
    {
        if (baseline.BuildMonth is < 1 or > 12 || baseline.BuildDay is < 1 or > 31)
        {
            return false;
        }

        var pluginPath = File.Exists(LightweightPluginService.MainPluginDllPath)
            ? LightweightPluginService.MainPluginDllPath
            : LightweightPluginService.FindMainPluginDisabledPath();

        if (string.IsNullOrEmpty(pluginPath) || !File.Exists(pluginPath))
        {
            return false;
        }

        var pluginDate = File.GetLastWriteTime(pluginPath).Date;
        var buildDate = ResolveBuildDate(pluginDate, baseline.BuildMonth, baseline.BuildDay);

        return buildDate != null && buildDate.Value <= pluginDate;
    }

    private static DateTime? ResolveBuildDate(DateTime reference, int month, int day)
    {
        DateTime? best = null;
        var bestDistance = double.MaxValue;

        foreach (var year in new[] { reference.Year - 1, reference.Year, reference.Year + 1 })
        {
            DateTime candidate;
            try
            {
                candidate = new DateTime(year, month, day);
            }
            catch
            {
                continue;
            }

            var distance = Math.Abs((candidate - reference).TotalDays);
            if (distance >= bestDistance)
            {
                continue;
            }

            best = candidate;
            bestDistance = distance;
        }

        return best;
    }

    private static PluginConfigBaseline? ReadBaselineCached()
    {
        try
        {
            var path = AppPaths.MainPluginConfigBaselineFile;
            if (!File.Exists(path))
            {
                return null;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            lock (StateLock)
            {
                if (_cachedBaseline != null && _cachedBaselineStampUtc == stamp)
                {
                    return _cachedBaseline;
                }
            }

            var baseline = PluginConfigBaseline.Read(path);

            lock (StateLock)
            {
                _cachedBaseline = baseline;
                _cachedBaselineStampUtc = stamp;
            }

            return baseline;
        }
        catch
        {
            return null;
        }
    }

    private static void NotifyRepaired(PluginConfigRepairSummary summary)
    {
        DebugLog.Info("PluginConfigGuard",
            $"[PluginConfigGuard] Repaired main plugin config: +{summary.AddedSections} sections, -{summary.RemovedSections} sections, +{summary.AddedKeys} keys, -{summary.RemovedKeys} keys");

        lock (StateLock)
        {
            if (DateTime.UtcNow - _lastRepairNoticeUtc < TimeSpan.FromSeconds(30))
            {
                return;
            }

            _lastRepairNoticeUtc = DateTime.UtcNow;
        }

        try
        {
            var dispatcher = App.MainWindow?.DispatcherQueue;
            if (dispatcher == null)
            {
                return;
            }

            dispatcher.TryEnqueue(() =>
            {
                try
                {
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "PluginConfigGuard_RepairedTitle".GetLocalized(),
                        string.Format("PluginConfigGuard_RepairedMessage".GetLocalized(), summary.AddedTotal,
                            summary.RemovedTotal),
                        NotificationType.Success,
                        5000));
                }
                catch
                {
                }
            });
        }
        catch
        {
        }
    }
}
