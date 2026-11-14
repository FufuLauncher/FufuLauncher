/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Download;

namespace FufuLauncher.Services.Download;

public sealed partial class ParallelDownloadService
{
    private static async Task VerifyHashAsync(
        string path, string url, ParallelDownloadOptions opt, long totalSize, CancellationToken ct)
    {
        if (opt.HashAlgorithm == DownloadHashAlgorithm.None || string.IsNullOrWhiteSpace(opt.ExpectedHash))
        {
            return;
        }

        string actual;
        if (opt.HashAlgorithm == DownloadHashAlgorithm.Md5)
        {
            actual = await HashUtility.Md5FileAsync(path, ct).ConfigureAwait(false);
        }
        else
        {
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, useAsync: true);
            actual = await HashUtility.XxHash64HexAsync(stream, ct).ConfigureAwait(false);
        }

        if (!actual.Equals(opt.ExpectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new DownloadFailedException(
                $"哈希校验失败: 实际 {actual}，预期 {opt.ExpectedHash}",
                url, totalSize, 0, DownloadFailureReason.IntegrityMismatch);
        }
    }


    private static bool IsWorkFileIntact(string workPath, long totalSize)
    {
        try
        {
            var info = new FileInfo(workPath);
            return info.Exists && info.Length == totalSize;
        }
        catch (Exception ex)
        {
            DebugLog.Info("ParallelDownloadService", $"[ParallelDownload] Failed to check .part file: {ex.Message}");
            return false;
        }
    }

    private sealed record ResumeState(long TotalSize, int TotalChunks, string UrlHash, List<int> Completed);


    private static string ComputeUrlHash(string url) =>
        HashUtility.Md5Bytes(System.Text.Encoding.UTF8.GetBytes(url));

    private static HashSet<int> LoadResumeState(
        string statePath, string url, long totalSize, int totalChunks)
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return new HashSet<int>();
            }

            var state = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(statePath));

            if (state == null
                || state.TotalSize != totalSize
                || state.TotalChunks != totalChunks
                || !string.Equals(state.UrlHash, ComputeUrlHash(url), StringComparison.OrdinalIgnoreCase))
            {
                DebugLog.Info("ParallelDownloadService",
                    "[ParallelDownload] Resume state does not match current source or parameters, ignoring");
                return new HashSet<int>();
            }

            return new HashSet<int>(state.Completed.Where(i => i >= 0 && i < totalChunks));
        }
        catch (Exception ex)
        {
            DebugLog.Info("ParallelDownloadService",
                $"[ParallelDownload] Failed to read resume state, ignoring: {ex.Message}");
            return new HashSet<int>();
        }
    }

    private static void SaveResumeState(
        string statePath, string url, long totalSize, int totalChunks, HashSet<int> completed)
    {
        try
        {
            var state = new ResumeState(
                totalSize, totalChunks, ComputeUrlHash(url), completed.OrderBy(i => i).ToList());
            File.WriteAllText(statePath, JsonSerializer.Serialize(state));
        }
        catch (Exception ex)
        {
            DebugLog.Info("ParallelDownloadService", $"[ParallelDownload] Failed to write resume state: {ex.Message}");
        }
    }


    private static void Cleanup(string workPath, string resumeStatePath, ParallelDownloadOptions opt)
    {
        if (opt.EnableResume)
        {
            return;
        }

        if (opt.DeleteOnFailure && opt.UseTemporaryFile)
        {
            TryDelete(workPath);
        }

        if (opt.DeleteOnFailure)
        {
            TryDelete(resumeStatePath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ParallelDownloadService", $"[ParallelDownload] Failed to delete {path}: {ex.Message}");
        }
    }
}
