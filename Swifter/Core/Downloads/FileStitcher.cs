using System.Buffers;

namespace Swifter.Core.Downloads;

public sealed class FileStitcher
{
    public int BufferSize { get; set; } = 1 << 20;
    public event Action<DownloadItem, double> ProgressChanged;

    public async Task StitchAsync(DownloadItem item)
    {
        if (item.Segments.Count == 1 && !File.Exists(item.Segments[0].TempPath))
            throw new FileNotFoundException("Missing segment", item.Segments[0].TempPath);

        foreach (var seg in item.Segments)
        {
            if (!File.Exists(seg.TempPath))
                throw new FileNotFoundException("Missing segment " + seg.Index, seg.TempPath);
        }

        var finalPath = item.SavePath;
        var dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(finalPath))
        {
            finalPath = MakeUniquePath(finalPath);
            item.SavePath = finalPath;
        }

        var flags = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        };

        await using var output = new FileStream(finalPath, flags);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long written = 0;
        var total = item.TotalBytes > 0 ? item.TotalBytes : item.Segments.Sum(s => s.End - s.Start + 1);
        try
        {
            for (var i = 0; i < item.Segments.Count; i++)
            {
                var seg = item.Segments[i];
                await using var input = new FileStream(seg.TempPath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    written += read;
                    if (total > 0 && (written & 0xFFFFF) < read)
                        ProgressChanged?.Invoke(item, Math.Min(1.0, written / (double)total));
                }
            }
            await output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (total > 0 && written != total)
            throw new IOException($"Stitched size {written} != expected {total}");

        CleanupSegments(item);
    }

    public async Task<long> StitchToAsync(DownloadItem item, Stream destination)
    {
        long written = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            foreach (var seg in item.Segments)
            {
                if (!File.Exists(seg.TempPath)) continue;
                await using var input = new FileStream(seg.TempPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                    written += read;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return written;
    }

    public void CleanupSegments(DownloadItem item)
    {
        foreach (var seg in item.Segments)
        {
            try
            {
                if (File.Exists(seg.TempPath)) File.Delete(seg.TempPath);
            }
            catch
            {
            }
        }
        var part = item.SavePath + ".swifterpart";
        try
        {
            if (File.Exists(part)) File.Delete(part);
        }
        catch
        {
        }
    }

    public static string MakeUniquePath(string path)
    {
        var dir = Path.GetDirectoryName(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir ?? "", $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(dir ?? "", $"{stem}-{Guid.NewGuid():N}{ext}");
    }

    public async Task<bool> ValidateAsync(DownloadItem item)
    {
        if (!File.Exists(item.SavePath)) return false;
        var info = new FileInfo(item.SavePath);
        if (item.TotalBytes > 0 && info.Length != item.TotalBytes) return false;
        if (info.Length == 0) return false;
        await Task.CompletedTask;
        return true;
    }
}
