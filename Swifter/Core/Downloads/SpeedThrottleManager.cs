using System.Collections.Concurrent;

namespace Swifter.Core.Downloads;

public sealed class ThrottleEntry
{
    public long BytesPerSec { get; set; }
    public long WindowBytes;
    public long WindowStartMs;
    public object Gate { get; } = new object();
}

public sealed class SpeedThrottleRegistry
{
    private readonly ConcurrentDictionary<Guid, ThrottleEntry> _entries = new ConcurrentDictionary<Guid, ThrottleEntry>();
    public long GlobalBytesPerSec { get; set; }
    public int ActiveThrottles => _entries.Count;

    public void SetThrottle(Guid id, long bytesPerSec)
    {
        if (bytesPerSec <= 0) _entries.TryRemove(id, out _);
        else
        {
            _entries[id] = new ThrottleEntry
            {
                BytesPerSec = bytesPerSec,
                WindowStartMs = Environment.TickCount64
            };
        }
    }

    public void Remove(Guid id) => _entries.TryRemove(id, out _);

    public long GetThrottle(Guid id) => _entries.TryGetValue(id, out var e) ? e.BytesPerSec : 0;

    public IReadOnlyDictionary<Guid, long> Snapshot()
    {
        return _entries.ToDictionary(kv => kv.Key, kv => kv.Value.BytesPerSec);
    }

    public void Clear() => _entries.Clear();
}

public sealed class SpeedThrottleManager : IDisposable
{
    private readonly SpeedThrottleRegistry _registry;
    private readonly ConcurrentDictionary<Guid, long> _sentWindow = new ConcurrentDictionary<Guid, long>();

    public SpeedThrottleManager(SpeedThrottleRegistry registry = null)
    {
        _registry = registry ?? App.SpeedThrottle ?? new SpeedThrottleRegistry();
    }

    public async Task WaitAsync(Guid id, int bytes, long itemLimitBytesPerSec, CancellationToken token)
    {
        var limit = itemLimitBytesPerSec > 0 ? itemLimitBytesPerSec : _registry.GlobalBytesPerSec;
        if (limit <= 0) return;
        var slot = TimeSpan.FromSeconds(1);
        var sent = _sentWindow.AddOrUpdate(id, bytes, (_, cur) => cur + bytes);
        if (sent <= limit) return;

        var elapsedMs = 50;
        var over = sent - limit;
        var delayMs = (int)Math.Clamp(over * 1000.0 / limit, 5, 1000);
        await Task.Delay(delayMs, token).ConfigureAwait(false);
        _sentWindow.AddOrUpdate(id, 0, (_, cur) => Math.Max(0, cur - limit * delayMs / 1000));
    }

    public void ResetWindow(Guid id)
    {
        _sentWindow[id] = 0;
    }

    public void Dispose()
    {
        _sentWindow.Clear();
    }
}

public sealed class SpeedThrottleManagerAlias
{
}
