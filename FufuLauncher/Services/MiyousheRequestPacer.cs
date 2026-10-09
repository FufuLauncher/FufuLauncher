/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services;

// 只错开发送时刻，不等待前一个 HTTP 请求完成；同账号的游戏共享冷却时间。
internal sealed class MiyousheRequestPacer(TimeProvider clock, Func<TimeSpan, Task> delay) : IDisposable
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _sync = new();
    private readonly long _started = clock.GetTimestamp();
    private TimeSpan _nextRequestAt;
    private TimeSpan _cooldownUntil;

    public async Task<bool> WaitAsync()
    {
        await _gate.WaitAsync();
        try
        {
            while (true)
            {
                TimeSpan wait;
                lock (_sync)
                {
                    var now = clock.GetElapsedTime(_started);
                    var next = _nextRequestAt > _cooldownUntil ? _nextRequestAt : _cooldownUntil;
                    wait = next - now;
                    if (wait <= TimeSpan.Zero)
                    {
                        // 试用间隔，避免查询、提交和确认请求集中在同一时刻。
                        _nextRequestAt = now + TimeSpan.FromMilliseconds(Random.Shared.Next(500, 1001));
                        return true;
                    }
                }
                // 服务端要求长时间冷却时，本轮结束该项，不截短 Retry-After 后继续请求。
                if (wait > MaxWait) return false;
                await delay(wait);
            }
        }
        finally { _gate.Release(); }
    }

    public bool Defer(TimeSpan? retryAfter, int retry)
    {
        var cooldown = retryAfter ??
            (TimeSpan.FromSeconds(2 << retry) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 501)));
        if (cooldown < TimeSpan.Zero) cooldown = TimeSpan.Zero;
        lock (_sync)
        {
            var until = clock.GetElapsedTime(_started) + cooldown;
            if (until > _cooldownUntil) _cooldownUntil = until;
        }
        return cooldown <= MaxWait;
    }

    public void Dispose() => _gate.Dispose();
}
