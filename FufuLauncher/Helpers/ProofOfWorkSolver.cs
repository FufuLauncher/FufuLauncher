/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.Cryptography;
using System.Text;

namespace FufuLauncher.Helpers;

internal static class ProofOfWorkSolver
{
    private const int HashLength = 32;
    private const int NonceBufferLength = 20;
    private const int MaxDifficulty = 48;

    public static long? Solve(string challengeId, long expiresAt, int difficulty, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (difficulty <= 0) return 0L;
        if (difficulty > MaxDifficulty) return null;

        var prefix = Encoding.UTF8.GetBytes($"{challengeId}:{expiresAt}:");
        var workers = Math.Clamp(Environment.ProcessorCount, 1, 8);
        var attemptBudget = difficulty >= 30 ? long.MaxValue : 1L << (difficulty + 12);
        var perWorkerBudget = Math.Max(2_000_000L, attemptBudget / workers);
        var deadline = DateTime.UtcNow + timeout;
        var result = new long[] { -1L };
        var tasks = new Task[workers];

        for (var worker = 0; worker < workers; worker++)
        {
            var start = (long)worker;
            tasks[worker] = Task.Run(
                () => SearchWorker(prefix, difficulty, start, workers, perWorkerBudget, deadline, result,
                    cancellationToken), CancellationToken.None);
        }

        try
        {
            Task.WaitAll(tasks);
        }
        catch (AggregateException ex)
        {
            DebugLog.Info("ProofOfWorkSolver", $"[ProofOfWork] Worker failure: {ex.InnerException?.Message}");
        }

        var nonce = Volatile.Read(ref result[0]);
        return nonce >= 0 ? nonce : null;
    }

    private static void SearchWorker(byte[] prefix, int difficulty, long start, int stride, long budget,
        DateTime deadline, long[] result, CancellationToken cancellationToken)
    {
        var buffer = new byte[prefix.Length + NonceBufferLength];
        prefix.CopyTo(buffer, 0);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> hash = stackalloc byte[HashLength];

        for (var nonce = start; ; nonce += stride)
        {
            if (Volatile.Read(ref result[0]) >= 0) return;
            if (cancellationToken.IsCancellationRequested) return;
            if (nonce < 0) return;

            var attempts = nonce - start;
            if (attempts >= budget) return;
            if ((attempts & 0x3FFF) == 0 && DateTime.UtcNow >= deadline) return;

            var length = prefix.Length + WriteDigits(buffer.AsSpan(prefix.Length), nonce);
            hasher.AppendData(buffer, 0, length);
            hasher.TryGetHashAndReset(hash, out _);

            if (!MeetsDifficulty(hash, difficulty)) continue;

            Interlocked.CompareExchange(ref result[0], nonce, -1L);
            return;
        }
    }

    private static bool MeetsDifficulty(ReadOnlySpan<byte> hash, int bits)
    {
        var fullBytes = bits / 8;
        for (var i = 0; i < fullBytes; i++)
        {
            if (hash[i] != 0) return false;
        }

        var remainingBits = bits % 8;
        if (remainingBits == 0) return true;

        var mask = (0xFF << (8 - remainingBits)) & 0xFF;
        return (hash[fullBytes] & mask) == 0;
    }

    private static int WriteDigits(Span<byte> destination, long value)
    {
        if (value <= 0)
        {
            destination[0] = (byte)'0';
            return 1;
        }

        var digitCount = 0;
        for (var probe = value; probe > 0; probe /= 10)
        {
            digitCount++;
        }

        var remaining = value;
        for (var i = digitCount - 1; i >= 0; i--)
        {
            destination[i] = (byte)('0' + (int)(remaining % 10));
            remaining /= 10;
        }

        return digitCount;
    }
}
