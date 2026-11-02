/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO;
using System.Text;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed class PluginConfigGuardService
{
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

    private readonly IDevBuildDetectionService _devBuildDetection;

    public PluginConfigGuardService(IDevBuildDetectionService devBuildDetection)
    {
        _devBuildDetection = devBuildDetection;
        PluginConfigGuard.AttachExemptBuildProvider(() =>
            AppVersionHelper.IsPreviewBuild || _devBuildDetection.IsDevBuild);
    }

    public async Task<bool> RefreshBaselineAsync(CancellationToken cancellationToken = default)
    {
        if (PluginConfigGuard.IsExemptBuild)
        {
            DebugLog.Info("PluginConfigGuardService",
                "[PluginConfigGuard] Skipped baseline refresh: dev or preview build");
            return false;
        }

        await FetchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FufuLauncher");

            foreach (var url in new[] { ApiEndpoints.PluginConfigIniUrl, ApiEndpoints.PluginConfigIniFallbackUrl })
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var bytes = await client.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
                    var baseline = CreateBaseline(url, Decode(bytes));
                    if (baseline == null)
                    {
                        DebugLog.Info("PluginConfigGuardService",
                            $"[PluginConfigGuard] Rejected baseline from {url}: unexpected content");
                        continue;
                    }

                    PluginConfigBaseline.Write(AppPaths.MainPluginConfigBaselineFile, baseline);
                    DebugLog.Info("PluginConfigGuardService",
                        $"[PluginConfigGuard] Baseline saved from {url} ({baseline.BuildDateText}, {baseline.Content.Length} chars)");
                    return true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    DebugLog.Info("PluginConfigGuardService",
                        $"[PluginConfigGuard] Failed to fetch {url}: {ex.Message}");
                }
            }

            return false;
        }
        finally
        {
            FetchGate.Release();
        }
    }

    private static PluginConfigBaseline? CreateBaseline(string url, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.TrimStart('\uFEFF');
        var document = PluginConfigDocument.Parse(PluginConfigBaseline.SplitLines(text));

        var generalFile = document.GetValue("General", "File");
        if (!string.Equals(Path.GetFileName(generalFile?.Trim()), LightweightPluginService.MainPluginDllName,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var description = document.GetValue("General", "Description") ?? string.Empty;
        if (!PluginConfigBaseline.TryParseBuildDate(description, out var month, out var day, out var dateText))
        {
            return null;
        }

        return new PluginConfigBaseline
        {
            SourceUrl = url,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Description = description,
            BuildMonth = month,
            BuildDay = day,
            BuildDateText = dateText,
            Content = text
        };
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
