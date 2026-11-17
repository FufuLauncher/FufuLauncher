/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;

namespace FufuLauncher.Services.GameServer;

public sealed partial class GameUpdateService
{
    private async Task<List<AssetProperty>> VerifyAssetsAsync(GameUpdateContext context, SophonManifestProto manifest)
    {
        var brokenAssets = new ConcurrentBag<AssetProperty>();
        int totalAssets = manifest.Assets.Count;
        int doneAssets = 0;
        int skippedSlimmed = 0;

        await Parallel.ForEachAsync(manifest.Assets, context.ParallelOptions, async (asset, token) =>
        {
            token.ThrowIfCancellationRequested();

            string assetPath = context.GetGameFilePath(asset.AssetName);
            if (IsSlimmableAssetPath(asset.AssetName) && CutsceneProtectionService.IsStubbedAndLocked(assetPath))
            {
                Interlocked.Increment(ref skippedSlimmed);
                int skippedCurrent = Interlocked.Increment(ref doneAssets);
                if (skippedCurrent % 100 == 0 || skippedCurrent == totalAssets)
                {
                    context.Progress.Report(new GameServerConversionProgress(
                        string.Format("GameServer_VerifyProgress".GetLocalized(), skippedCurrent, totalAssets),
                        totalAssets, skippedCurrent, 0, 0, null));
                }
                return;
            }

            bool isBroken;
            try
            {
                var info = new FileInfo(assetPath);
                isBroken = !info.Exists || info.Length != asset.AssetSize ||
                           !asset.AssetHashMd5.Equals(
                               await HashUtility.Md5FileAsync(assetPath, token).ConfigureAwait(false),
                               StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                isBroken = true;
            }

            if (isBroken)
            {
                brokenAssets.Add(asset);
            }

            int current = Interlocked.Increment(ref doneAssets);
            if (current % 100 == 0 || current == totalAssets)
            {
                context.Progress.Report(new GameServerConversionProgress(
                    string.Format("GameServer_VerifyProgress".GetLocalized(), current, totalAssets),
                    totalAssets, current, 0, 0, null));
            }
        }).ConfigureAwait(false);

        if (skippedSlimmed > 0)
        {
            context.Log(string.Format("Cutscene_VerifySkippedSlimmed".GetLocalized(), skippedSlimmed));
        }

        return brokenAssets.ToList();
    }

    private static bool IsSlimmableAssetPath(string assetName)
    {
        string normalized = assetName.Replace('\\', '/');
        return normalized.Contains("/StreamingAssets/VideoAssets/StandaloneWindows64/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/StreamingAssets/AudioAssets/", StringComparison.OrdinalIgnoreCase);
    }
}