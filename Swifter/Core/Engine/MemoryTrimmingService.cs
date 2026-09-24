using System.Diagnostics;

namespace Swifter.Core.Engine;

public sealed class MemoryTrimmingService
{
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new object();
    private bool _trimming;
    public long WorkingSetThresholdBytes { get; set; } = 200L * 1024 * 1024;
    public int IntervalSeconds { get; set; } = 15;
    public event Action<long> WorkingSetTrimmed;

    public MemoryTrimmingService()
    {
        _timer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _timer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(IntervalSeconds));
    }

    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnTick(object state)
    {
        if (_trimming) return;
        lock (_gate)
        {
            if (_trimming) return;
            _trimming = true;
        }
        Task.Run(() =>
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                proc.Refresh();
                var ws = proc.WorkingSet64;
                if (ws > WorkingSetThresholdBytes)
                {
                    var freed = NativeMemoryBridge.TrimWorkingSet();
                    proc.Refresh();
                    WorkingSetTrimmed?.Invoke(ws - proc.WorkingSet64);
                }
            }
            catch
            {
            }
            finally
            {
                lock (_gate) _trimming = false;
            }
        });
    }

    public void TrimNow()
    {
        Task.Run(() =>
        {
            try { NativeMemoryBridge.TrimWorkingSet(); } catch { }
        });
    }

    public long GetWorkingSetBytes()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            proc.Refresh();
            return proc.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    public double GetWorkingSetMb()
    {
        return GetWorkingSetBytes() / (1024.0 * 1024.0);
    }
}
