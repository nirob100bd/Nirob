using System.Diagnostics;

namespace Swifter.Core.Engine;

public sealed class SharedProcessInfo
{
    public int ProcessId { get; set; }
    public string Kind { get; set; }
    public long WorkingSet { get; set; }
    public DateTime LastSeen { get; set; }
}

public sealed class ProcessSharingManager
{
    private readonly Dictionary<int, SharedProcessInfo> _processes = new Dictionary<int, SharedProcessInfo>();
    private readonly object _lock = new object();
    private readonly Timer _pollTimer;
    public bool RendererPoolingEnabled { get; set; } = true;
    public int MaxRenderers { get; set; } = 8;
    public event Action<IReadOnlyList<SharedProcessInfo>> ProcessesChanged;

    public ProcessSharingManager()
    {
        _pollTimer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
    }

    public void Track(int processId, string kind)
    {
        lock (_lock)
        {
            _processes[processId] = new SharedProcessInfo
            {
                ProcessId = processId,
                Kind = kind,
                LastSeen = DateTime.UtcNow
            };
        }
    }

    public void Untrack(int processId)
    {
        lock (_lock) _processes.Remove(processId);
    }

    public void Clear()
    {
        lock (_lock) _processes.Clear();
    }

    private void Poll()
    {
        try
        {
            List<SharedProcessInfo> snapshot;
            lock (_lock) snapshot = _processes.Values.ToList();
            var alive = new List<SharedProcessInfo>();
            foreach (var info in snapshot)
            {
                try
                {
                    using var p = Process.GetProcessById(info.ProcessId);
                    p.Refresh();
                    info.WorkingSet = p.WorkingSet64;
                    info.LastSeen = DateTime.UtcNow;
                    alive.Add(info);
                }
                catch
                {
                    lock (_lock) _processes.Remove(info.ProcessId);
                }
            }
            ProcessesChanged?.Invoke(alive);
        }
        catch
        {
        }
    }

    public long TotalSharedWorkingSet()
    {
        lock (_lock) return _processes.Values.Sum(p => p.WorkingSet);
    }

    public int RendererCount()
    {
        lock (_lock) return _processes.Values.Count(p => p.Kind == "renderer");
    }

    public void TrimBackgroundRenderers(IEnumerable<int> activeProcessIds)
    {
        var active = new HashSet<int>(activeProcessIds);
        List<SharedProcessInfo> snapshot;
        lock (_lock) snapshot = _processes.Values.Where(p => p.Kind == "renderer" && !active.Contains(p.ProcessId)).ToList();
        foreach (var info in snapshot)
        {
            try
            {
                using var p = Process.GetProcessById(info.ProcessId);
                NativeMemoryBridge.TrimProcess(p.Handle);
            }
            catch
            {
            }
        }
    }

    public void ApplyLowEndThrottle(bool enable)
    {
        RendererPoolingEnabled = enable;
        MaxRenderers = enable ? 4 : 16;
        if (enable)
        {
            foreach (var p in GetAll())
            {
                try
                {
                    using var proc = Process.GetProcessById(p.ProcessId);
                    if (p.Kind == "renderer")
                    {
                        try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                    }
                }
                catch
                {
                }
            }
        }
        else
        {
            foreach (var p in GetAll())
            {
                try
                {
                    using var proc = Process.GetProcessById(p.ProcessId);
                    proc.PriorityClass = ProcessPriorityClass.Normal;
                }
                catch
                {
                }
            }
        }
    }

    public IReadOnlyList<SharedProcessInfo> GetAll()
    {
        lock (_lock) return _processes.Values.ToList();
    }
}
