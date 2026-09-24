using System.Collections.Concurrent;
using Swifter.Core.Storage;

namespace Swifter.Core.Workspace;

public sealed class HibernationService
{
    private readonly SessionRepository _sessions;
    private readonly ConcurrentDictionary<Guid, DateTime> _lastActive = new ConcurrentDictionary<Guid, DateTime>();
    private readonly ConcurrentDictionary<Guid, HibernatedTab> _hibernated = new ConcurrentDictionary<Guid, HibernatedTab>();
    private readonly Timer _timer;
    private readonly object _gate = new object();
    private bool _running;
    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(5);
    public int IntervalSeconds { get; set; } = 60;
    public bool Enabled { get; set; } = true;
    public event Action<Guid> TabHibernated;
    public event Action<Guid> TabRevived;
    public event Func<Guid, bool> AllowHibernateQuery;

    public HibernationService(SessionRepository sessions)
    {
        _sessions = sessions;
        _timer = new Timer(_ => Sweep(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _timer.Change(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(IntervalSeconds));
    }

    public void Stop()
    {
        _running = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Touch(Guid tabId)
    {
        _lastActive[tabId] = DateTime.UtcNow;
        if (_hibernated.TryRemove(tabId, out _))
        {
            TabRevived?.Invoke(tabId);
        }
    }

    public void Forget(Guid tabId)
    {
        _lastActive.TryRemove(tabId, out _);
        _hibernated.TryRemove(tabId, out _);
    }

    private void Sweep()
    {
        if (!Enabled) return;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _lastActive.ToArray())
            {
                if (now - kv.Value < IdleThreshold) continue;
                if (_hibernated.ContainsKey(kv.Key)) continue;
                if (AllowHibernateQuery != null && !AllowHibernateQuery(kv.Key)) continue;
                try
                {
                    HibernateTab(kv.Key);
                }
                catch
                {
                }
            }
        }
    }

    public HibernatedTab HibernateTab(Guid tabId)
    {
        if (!_lastActive.TryGetValue(tabId, out var last)) last = DateTime.UtcNow;
        var state = new HibernatedTab
        {
            TabId = tabId,
            HibernatedAtUtc = DateTime.UtcNow,
            LastActiveUtc = last
        };
        _hibernated[tabId] = state;
        TabHibernated?.Invoke(tabId);
        return state;
    }

    public bool IsHibernated(Guid tabId) => _hibernated.ContainsKey(tabId);

    public IReadOnlyList<HibernatedTab> GetHibernated() => _hibernated.Values.ToList();

    public void MarkRevived(Guid tabId)
    {
        _hibernated.TryRemove(tabId, out _);
        Touch(tabId);
    }

    public int Count => _hibernated.Count;
}

public sealed class HibernatedTab
{
    public Guid TabId { get; set; }
    public DateTime HibernatedAtUtc { get; set; }
    public DateTime LastActiveUtc { get; set; }
    public string SerializedState { get; set; }
    public long SavedBytes { get; set; }
}
