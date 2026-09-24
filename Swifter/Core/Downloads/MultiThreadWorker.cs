using System.Buffers;
using System.Collections.Concurrent;

namespace Swifter.Core.Downloads;

public sealed class SegmentJob
{
    public DownloadItem Item { get; set; }
    public DownloadSegment Segment { get; set; }
    public int WorkerIndex { get; set; }
}

public sealed class MultiThreadWorker
{
    private readonly HttpClient _client;
    private readonly ConcurrentQueue<SegmentJob> _queue = new ConcurrentQueue<SegmentJob>();
    private readonly List<Task> _workers = new List<Task>();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    public int WorkerCount { get; set; } = 16;
    public event Action<DownloadItem, DownloadSegment> SegmentCompleted;
    public event Action<DownloadItem, Exception> SegmentFailed;

    public MultiThreadWorker(HttpClient client)
    {
        _client = client;
    }

    public void Enqueue(DownloadItem item)
    {
        foreach (var seg in item.Segments)
        {
            if (seg.Completed) continue;
            _queue.Enqueue(new SegmentJob { Item = item, Segment = seg });
        }
        EnsureWorkers();
    }

    private void EnsureWorkers()
    {
        var target = Math.Clamp(WorkerCount, 1, 16);
        while (_workers.Count < target)
        {
            var index = _workers.Count;
            _workers.Add(Task.Run(() => WorkerLoop(index, _cts.Token)));
        }
    }

    private async Task WorkerLoop(int index, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!_queue.TryDequeue(out var job)) break;
            try
            {
                await ExecuteSegmentAsync(job, token).ConfigureAwait(false);
                SegmentCompleted?.Invoke(job.Item, job.Segment);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SegmentFailed?.Invoke(job.Item, ex);
                if (job.Segment.Received < job.Segment.Remaining)
                    _queue.Enqueue(job);
            }
        }
    }

    private async Task ExecuteSegmentAsync(SegmentJob job, CancellationToken token)
    {
        var seg = job.Segment;
        var item = job.Item;
        if (seg.Completed) return;

        using var req = new HttpRequestMessage(HttpMethod.Get, item.Url);
        if (!string.IsNullOrEmpty(item.Referer)) req.Headers.Referrer = new Uri(item.Referer);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(seg.Start + seg.Received, seg.End);
        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var flags = new FileStreamOptions
        {
            Mode = seg.Received > 0 ? FileMode.Open : FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.ReadWrite,
            Options = FileOptions.Asynchronous
        };
        await using var fs = new FileStream(seg.TempPath, flags);
        if (seg.Received > 0) fs.Seek(seg.Received, SeekOrigin.Begin);

        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            await using var net = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            int read;
            while ((read = await net.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
            {
                token.ThrowIfCancellationRequested();
                await fs.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                seg.Received += read;
                Interlocked.Add(ref item.ReceivedBytes, read);
                if (seg.Completed) break;
            }
            await fs.FlushAsync(token).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public int PendingJobs => _queue.Count;

    public async Task DrainAsync()
    {
        while (!_queue.IsEmpty)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }
    }

    public void Stop()
    {
        try { _cts.Cancel(); } catch { }
    }
}
