using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Http.Headers;

namespace Swifter.Core.Downloads;

public enum DownloadState
{
    Queued,
    Connecting,
    Downloading,
    Paused,
    Stitching,
    Completed,
    Failed,
    Canceled
}

public sealed class DownloadSegment
{
    public int Index { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public long Received { get; set; }
    public string TempPath { get; set; }
    public bool Completed => Start + Received > End;
    public long Remaining => Math.Max(0, End - Start + 1 - Received);
}

public sealed class DownloadItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; }
    public string Referer { get; set; }
    public string FileName { get; set; }
    public string SavePath { get; set; }
    public long TotalBytes { get; set; }
    public long ReceivedBytes;
    public int SegmentCount { get; set; } = 8;
    public DownloadState State { get; set; } = DownloadState.Queued;
    public double SpeedBytesPerSec { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public string ContentType { get; set; }
    public string ETag { get; set; }
    public string LastModified { get; set; }
    public bool ServerSupportsRanges { get; set; }
    public List<DownloadSegment> Segments { get; set; } = new List<DownloadSegment>();
    public long ThrottleBytesPerSec { get; set; }
    public string Error { get; set; }
    public int RetryCount { get; set; }
    public bool IsMedia { get; set; }
    public string MediaType { get; set; }

    public double Percent => TotalBytes > 0 ? Math.Min(100.0, ReceivedBytes * 100.0 / TotalBytes) : 0;

    public TimeSpan? EstimatedRemaining
    {
        get
        {
            if (SpeedBytesPerSec <= 0 || TotalBytes <= 0) return null;
            var remain = TotalBytes - ReceivedBytes;
            if (remain <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(remain / SpeedBytesPerSec);
        }
    }
}

public sealed class SegmentedDownloadEngine : IDisposable
{
    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new ConcurrentDictionary<Guid, CancellationTokenSource>();
    private readonly ConcurrentDictionary<Guid, DownloadItem> _items = new ConcurrentDictionary<Guid, DownloadItem>();
    private readonly SpeedThrottleManager _throttle;
    private readonly FileStitcher _stitcher = new FileStitcher();
    public int MaxSegments { get; set; } = 16;
    public string DownloadDirectory { get; set; }
    public event Action<DownloadItem> ItemUpdated;
    public event Action<DownloadItem> ItemCompleted;
    public event Action<DownloadItem> ItemFailed;
    public event Action<DownloadItem> MediaDetected;

    public SegmentedDownloadEngine(SpeedThrottleManager throttle = null)
    {
        _throttle = throttle ?? new SpeedThrottleManager();
        DownloadDirectory = Path.Combine(Program.GetUserDataRoot(), "downloads");
        Directory.CreateDirectory(DownloadDirectory);
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
    }

    public IReadOnlyList<DownloadItem> GetAll() => _items.Values.ToList();

    public DownloadItem Get(Guid id) => _items.TryGetValue(id, out var item) ? item : null;

    public async Task<DownloadItem> EnqueueAsync(string url, string referer = null, string preferredName = null, bool isMedia = false, string mediaType = null)
    {
        var item = new DownloadItem
        {
            Url = url,
            Referer = referer,
            FileName = SanitizeFileName(preferredName ?? Path.GetFileName(new Uri(url).AbsolutePath) ?? "download.bin"),
            State = DownloadState.Queued,
            StartedUtc = DateTime.UtcNow,
            IsMedia = isMedia,
            MediaType = mediaType
        };
        if (string.IsNullOrWhiteSpace(item.FileName)) item.FileName = "download-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bin";
        item.SavePath = MakeUniquePath(Path.Combine(DownloadDirectory, item.FileName));
        item.FileName = Path.GetFileName(item.SavePath);
        _items[item.Id] = item;
        Raise(item);
        _ = Task.Run(() => RunAsync(item));
        return item;
    }

    private static string MakeUniquePath(string fullPath)
    {
        if (!File.Exists(fullPath)) return fullPath;
        var dir = Path.GetDirectoryName(fullPath) ?? "";
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var ext = Path.GetExtension(fullPath);
        for (var n = 1; n < 10000; n++)
        {
            var candidate = Path.Combine(dir, stem + " (" + n + ")" + ext);
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir, stem + "-" + Guid.NewGuid().ToString("N") + ext);
    }

    public void Pause(Guid id)
    {
        if (_active.TryGetValue(id, out var cts))
        {
            try { cts.Cancel(); } catch { }
        }
        if (_items.TryGetValue(id, out var item))
        {
            item.State = DownloadState.Paused;
            Raise(item);
        }
    }

    public void Resume(Guid id)
    {
        if (!_items.TryGetValue(id, out var item)) return;
        if (item.State == DownloadState.Completed) return;
        item.State = DownloadState.Queued;
        Raise(item);
        _ = Task.Run(() => RunAsync(item));
    }

    public void Cancel(Guid id, bool deletePartial = true)
    {
        if (_active.TryGetValue(id, out var cts))
        {
            try { cts.Cancel(); } catch { }
        }
        if (_items.TryGetValue(id, out var item))
        {
            item.State = DownloadState.Canceled;
            Raise(item);
            if (deletePartial)
            {
                Task.Run(() =>
                {
                    try
                    {
                        foreach (var seg in item.Segments)
                            if (File.Exists(seg.TempPath)) File.Delete(seg.TempPath);
                        var partial = item.SavePath + ".swifterpart";
                        if (File.Exists(partial)) File.Delete(partial);
                    }
                    catch { }
                });
            }
        }
    }

    public void SetThrottle(Guid id, long bytesPerSec)
    {
        if (_items.TryGetValue(id, out var item))
        {
            item.ThrottleBytesPerSec = bytesPerSec;
            _throttle.SetThrottle(id, bytesPerSec);
        }
    }

    private async Task RunAsync(DownloadItem item)
    {
        var cts = new CancellationTokenSource();
        _active[item.Id] = cts;
        var token = cts.Token;
        try
        {
            item.State = DownloadState.Connecting;
            Raise(item);

            using var req = new HttpRequestMessage(HttpMethod.Head, item.Url);
            AttachHeaders(req, item);
            long total = -1;
            var supportsRange = false;
            string etag = null, lastMod = null, contentType = null;
            try
            {
                using var headResp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                total = headResp.Content.Headers.ContentLength ?? -1;
                supportsRange = headResp.Content.Headers.AcceptRanges?.Contains("bytes") == true;
                etag = headResp.Headers.ETag?.ToString();
                lastMod = headResp.Content.Headers.LastModified?.ToString("R");
                contentType = headResp.Content.Headers.ContentType?.MediaType;
            }
            catch
            {
            }

            if (total <= 0 || !supportsRange)
            {
                await DownloadSingleStreamAsync(item, token).ConfigureAwait(false);
            }
            else
            {
                item.TotalBytes = total;
                item.ServerSupportsRanges = true;
                item.ETag = etag;
                item.LastModified = lastMod;
                item.ContentType = contentType;
                item.Segments = BuildSegments(item, total);
                await DownloadSegmentedAsync(item, token).ConfigureAwait(false);
            }

            if (token.IsCancellationRequested && item.State != DownloadState.Canceled)
            {
                item.State = DownloadState.Paused;
                Raise(item);
                return;
            }

            item.State = DownloadState.Stitching;
            Raise(item);
            await _stitcher.StitchAsync(item).ConfigureAwait(false);
            item.State = DownloadState.Completed;
            item.CompletedUtc = DateTime.UtcNow;
            item.SpeedBytesPerSec = 0;
            Raise(item);
            ItemCompleted?.Invoke(item);
        }
        catch (OperationCanceledException)
        {
            if (item.State != DownloadState.Canceled)
            {
                item.State = DownloadState.Paused;
                Raise(item);
            }
        }
        catch (Exception ex)
        {
            item.RetryCount++;
            item.Error = ex.Message;
            if (item.RetryCount < 3)
            {
                item.State = DownloadState.Queued;
                Raise(item);
                await Task.Delay(1000 * item.RetryCount, CancellationToken.None).ConfigureAwait(false);
                _ = Task.Run(() => RunAsync(item));
                return;
            }
            item.State = DownloadState.Failed;
            Raise(item);
            ItemFailed?.Invoke(item);
        }
        finally
        {
            _active.TryRemove(item.Id, out _);
        }
    }

    private void AttachHeaders(HttpRequestMessage req, DownloadItem item)
    {
        if (!string.IsNullOrEmpty(item.Referer)) req.Headers.Referrer = new Uri(item.Referer);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        if (!string.IsNullOrEmpty(item.ETag)) req.Headers.TryAddWithoutValidation("If-None-Match", item.ETag);
    }

    private List<DownloadSegment> BuildSegments(DownloadItem item, long total)
    {
        var count = (int)Math.Clamp(item.SegmentCount, 1, MaxSegments);
        if (total < 1024 * 256) count = 1;
        if (total < 1024 * 1024) count = Math.Min(count, 4);
        var segments = new List<DownloadSegment>();
        var segSize = total / count;
        for (var i = 0; i < count; i++)
        {
            var start = i * segSize;
            var end = i == count - 1 ? total - 1 : start + segSize - 1;
            segments.Add(new DownloadSegment
            {
                Index = i,
                Start = start,
                End = end,
                TempPath = item.SavePath + ".seg" + i
            });
        }
        item.SegmentCount = count;
        return segments;
    }

    private async Task DownloadSegmentedAsync(DownloadItem item, CancellationToken token)
    {
        item.State = DownloadState.Downloading;
        Raise(item);
        var tasks = new List<Task>();
        var speedWindow = new long[20];
        var windowIndex = 0;
        var lastTick = Environment.TickCount64;
        var lastBytes = item.ReceivedBytes;

        var progressCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progressTask = Task.Run(async () =>
        {
            while (!progressCts.Token.IsCancellationRequested)
            {
                try { await Task.Delay(500, progressCts.Token).ConfigureAwait(false); }
                catch { break; }
                var now = Environment.TickCount64;
                var received = Interlocked.Read(ref item.ReceivedBytes);
                var deltaT = Math.Max(1, now - lastTick) / 1000.0;
                item.SpeedBytesPerSec = Math.Max(0, (received - lastBytes) / deltaT);
                lastTick = now;
                lastBytes = received;
                Raise(item);
            }
        }, progressCts.Token);

        foreach (var seg in item.Segments)
        {
            if (seg.Completed) continue;
            tasks.Add(Task.Run(() => RunSegmentAsync(item, seg, token), token));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        try { progressCts.Cancel(); } catch { }
        try { await progressTask.ConfigureAwait(false); } catch { }
        item.ReceivedBytes = item.Segments.Sum(s => s.Received);
        item.SpeedBytesPerSec = 0;
        if (item.Segments.Any(s => !s.Completed)) throw new IOException("Segments incomplete");
    }

    private async Task RunSegmentAsync(DownloadItem item, DownloadSegment seg, CancellationToken token)
    {
        const int maxRetries = 4;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, item.Url);
                AttachHeaders(req, item);
                req.Headers.Range = new RangeHeaderValue(seg.Start + seg.Received, seg.End);
                using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                var existing = 0L;
                if (seg.Received > 0 && File.Exists(seg.TempPath))
                {
                    existing = new FileInfo(seg.TempPath).Length;
                    if (existing != seg.Received)
                    {
                        seg.Received = Math.Min(existing, seg.End - seg.Start + 1);
                    }
                }
                else
                {
                    seg.Received = 0;
                    using (File.Create(seg.TempPath)) { }
                }

                var flags = new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Write,
                    Share = FileShare.ReadWrite,
                    Options = FileOptions.Asynchronous
                };
                await using var fs = new FileStream(seg.TempPath, flags);
                fs.Seek(0, SeekOrigin.End);

                var pool = ArrayPool<byte>.Shared;
                var buffer = pool.Rent(81920);
                try
                {
                    await using var net = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    int read;
                    while ((read = await net.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        await _throttle.WaitAsync(item.Id, read, item.ThrottleBytesPerSec, token).ConfigureAwait(false);
                        await fs.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        seg.Received += read;
                        var newVal = Interlocked.Add(ref item.ReceivedBytes, read);
                    }
                    await fs.FlushAsync(token).ConfigureAwait(false);
                }
                finally
                {
                    pool.Return(buffer);
                }

                if (seg.Completed) return;
                throw new IOException("Segment ended early");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await Task.Delay(400 * (attempt + 1), token).ConfigureAwait(false);
            }
        }
        throw new IOException("Segment " + seg.Index + " failed after retries");
    }

    private async Task DownloadSingleStreamAsync(DownloadItem item, CancellationToken token)
    {
        item.State = DownloadState.Downloading;
        item.SegmentCount = 1;
        item.Segments = new List<DownloadSegment>
        {
            new DownloadSegment { Index = 0, Start = 0, End = long.MaxValue - 1, TempPath = item.SavePath + ".seg0" }
        };
        Raise(item);

        using var req = new HttpRequestMessage(HttpMethod.Get, item.Url);
        AttachHeaders(req, item);
        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        item.TotalBytes = resp.Content.Headers.ContentLength ?? -1;
        item.ContentType = resp.Content.Headers.ContentType?.MediaType;

        var seg = item.Segments[0];
        seg.Received = 0;
        var flags = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous
        };
        await using var fs = new FileStream(seg.TempPath, flags);
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(81920);
        var lastTick = Environment.TickCount64;
        var lastBytes = 0L;
        try
        {
            await using var net = await resp.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            int read;
            while ((read = await net.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
            {
                token.ThrowIfCancellationRequested();
                await _throttle.WaitAsync(item.Id, read, item.ThrottleBytesPerSec, token).ConfigureAwait(false);
                await fs.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                seg.Received += read;
                item.ReceivedBytes = seg.Received;
                var now = Environment.TickCount64;
                if (now - lastTick > 500)
                {
                    item.SpeedBytesPerSec = (seg.Received - lastBytes) / Math.Max(0.001, (now - lastTick) / 1000.0);
                    lastTick = now;
                    lastBytes = seg.Received;
                    Raise(item);
                }
            }
        }
        finally
        {
            pool.Return(buffer);
        }
        item.SpeedBytesPerSec = 0;
        if (item.TotalBytes <= 0) item.TotalBytes = seg.Received;
    }

    public async Task<bool> ResumeBrokenAsync(Guid id)
    {
        if (!_items.TryGetValue(id, out var item)) return false;
        foreach (var seg in item.Segments)
        {
            if (File.Exists(seg.TempPath))
            {
                var len = new FileInfo(seg.TempPath).Length;
                seg.Received = Math.Min(len, Math.Max(0, seg.End - seg.Start + 1));
            }
        }
        item.ReceivedBytes = item.Segments.Sum(s => s.Received);
        if (item.ReceivedBytes >= item.TotalBytes && item.TotalBytes > 0)
        {
            item.State = DownloadState.Stitching;
            await _stitcher.StitchAsync(item).ConfigureAwait(false);
            item.State = DownloadState.Completed;
            Raise(item);
            return true;
        }
        item.State = DownloadState.Queued;
        Raise(item);
        _ = Task.Run(() => RunAsync(item));
        return true;
    }

    private void Raise(DownloadItem item)
    {
        ItemUpdated?.Invoke(item);
    }

    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "download.bin";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim().TrimEnd('.');
        if (result.Length == 0) result = "download.bin";
        if (result.Length > 180)
        {
            var ext = Path.GetExtension(result);
            var stem = Path.GetFileNameWithoutExtension(result);
            result = stem.Substring(0, 160) + ext;
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var kv in _active)
        {
            try { kv.Value.Cancel(); kv.Value.Dispose(); } catch { }
        }
        _client?.Dispose();
        _throttle?.Dispose();
    }
}
