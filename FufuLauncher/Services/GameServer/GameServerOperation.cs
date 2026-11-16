/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.GameServer;

internal static class GameServerOperation
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<IDisposable> EnterAsync(string gameDir, CancellationToken token, bool requireClosed = true)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDir));
        SemaphoreSlim gate = Gates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, token).ConfigureAwait(false))
        {
            throw new InvalidOperationException("GameServer_OperationInProgress".GetLocalized());
        }

        var lease = new Lease(gate);
        try
        {
            if (requireClosed)
                await EnsureGameClosedAsync().ConfigureAwait(false);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public static async Task EnsureGameClosedAsync()
    {
        if (await GameProcessHelper.IsGameRunningAsync().ConfigureAwait(false))
        {
            throw new InvalidOperationException("GameServer_CloseGameFirst".GetLocalized());
        }
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}