using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Swifter.Core.Storage;

public sealed class CacheEntry
{
    public string Key { get; set; }
    public string Value { get; set; }
    public byte[] Blob { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class CacheManager
{
    private readonly DatabaseContext _db;
    private readonly ConcurrentDictionary<string, DateTime> _memory = new ConcurrentDictionary<string, DateTime>();
    private readonly Timer _cleanup;
    public long MaxMemoryCacheBytes { get; set; } = 64L * 1024 * 1024;
    public long MaxDiskCacheBytes { get; set; } = 512L * 1024 * 1024;
    public string DiskCachePath { get; }

    public CacheManager(DatabaseContext db)
    {
        _db = db;
        DiskCachePath = Path.Combine(Program.GetUserDataRoot(), "cache");
        Directory.CreateDirectory(DiskCachePath);
        _cleanup = new Timer(_ => Cleanup(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
    }

    public void Set(string key, string value, TimeSpan? ttl = null)
    {
        if (string.IsNullOrEmpty(key)) return;
        var expires = DateTime.UtcNow + (ttl ?? TimeSpan.FromHours(6));
        _db.Execute(
            "INSERT INTO cache_entries (key, value, expires_at, created_at) VALUES (@k, @v, @e, @now) ON CONFLICT(key) DO UPDATE SET value = @v, expires_at = @e",
            ("@k", key), ("@v", value ?? ""), ("@e", expires.Ticks), ("@now", DateTime.UtcNow.Ticks));
        _memory[key] = expires;
    }

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_memory.TryGetValue(key, out var exp) && exp < DateTime.UtcNow)
        {
            _memory.TryRemove(key, out _);
            return null;
        }
        var rows = _db.QueryList(
            "SELECT value, expires_at FROM cache_entries WHERE key = @k",
            r => (Value: r.IsDBNull(0) ? null : r.GetString(0), Expires: r.GetInt64(1)),
            ("@k", key));
        var row = rows.FirstOrDefault();
        if (row.Value == null) return null;
        if (new DateTime(row.Expires, DateTimeKind.Utc) < DateTime.UtcNow)
        {
            Remove(key);
            return null;
        }
        return row.Value;
    }

    public void Remove(string key)
    {
        _db.Execute("DELETE FROM cache_entries WHERE key = @k", ("@k", key));
        _memory.TryRemove(key, out _);
    }

    public void Clear()
    {
        _db.Execute("DELETE FROM cache_entries");
        _memory.Clear();
        try
        {
            Directory.Delete(DiskCachePath, true);
            Directory.CreateDirectory(DiskCachePath);
        }
        catch
        {
        }
    }

    public void ClearBrowsingData(bool history = false, bool cache = true, bool cookies = false, bool passwords = false)
    {
        if (cache)
        {
            Clear();
            try
            {
                var tempPath = Path.Combine(Program.GetUserDataRoot(), "temp");
                if (Directory.Exists(tempPath))
                {
                    foreach (var f in Directory.EnumerateFiles(tempPath))
                    {
                        try { File.Delete(f); } catch { }
                    }
                }
            }
            catch
            {
            }
        }
        if (history) _db.Execute("DELETE FROM history");
        if (cookies) _db.Execute("DELETE FROM cookies");
        if (passwords) _db.Execute("DELETE FROM passwords");
    }

    private void Cleanup()
    {
        try
        {
            var now = DateTime.UtcNow.Ticks;
            _db.Execute("DELETE FROM cache_entries WHERE expires_at > 0 AND expires_at < @now", ("@now", now));
            foreach (var kv in _memory.ToArray())
            {
                if (kv.Value < DateTime.UtcNow) _memory.TryRemove(kv.Key, out _);
            }
            EnforceDiskLimit();
        }
        catch
        {
        }
    }

    public void EnforceDiskLimit()
    {
        try
        {
            var files = new List<FileInfo>();
            var dir = new DirectoryInfo(DiskCachePath);
            if (!dir.Exists) return;
            foreach (var f in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                files.Add(f);
            }
            var total = files.Sum(f => f.Length);
            if (total <= MaxDiskCacheBytes) return;
            files.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            foreach (var f in files)
            {
                if (total <= MaxDiskCacheBytes) break;
                try
                {
                    total -= f.Length;
                    f.Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    public long GetDiskCacheSize()
    {
        try
        {
            var dir = new DirectoryInfo(DiskCachePath);
            if (!dir.Exists) return 0;
            return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    public long GetDatabaseSize()
    {
        try
        {
            var f = new FileInfo(Program.GetDatabasePath());
            return f.Exists ? f.Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    public byte[] GetBlob(string key)
    {
        var rows = _db.QueryList(
            "SELECT blob, expires_at FROM cache_entries WHERE key = @k",
            r => (Blob: r.IsDBNull(0) ? null : (byte[])r["blob"], Expires: r.GetInt64(1)),
            ("@k", key));
        var row = rows.FirstOrDefault();
        if (row.Blob == null) return null;
        if (new DateTime(row.Expires, DateTimeKind.Utc) < DateTime.UtcNow)
        {
            Remove(key);
            return null;
        }
        return row.Blob;
    }

    public void SetBlob(string key, byte[] blob, TimeSpan? ttl = null)
    {
        if (string.IsNullOrEmpty(key) || blob == null) return;
        var expires = DateTime.UtcNow + (ttl ?? TimeSpan.FromDays(7));
        _db.Execute(
            "INSERT INTO cache_entries (key, blob, expires_at, created_at) VALUES (@k, @b, @e, @now) ON CONFLICT(key) DO UPDATE SET blob = @b, expires_at = @e",
            ("@k", key), ("@b", blob), ("@e", expires.Ticks), ("@now", DateTime.UtcNow.Ticks));
    }

    public (long DiskBytes, long DbBytes, long HistoryCount) GetUsageSnapshot()
    {
        var historyCount = Convert.ToInt64(_db.Scalar("SELECT COUNT(*) FROM history"));
        return (GetDiskCacheSize(), GetDatabaseSize(), historyCount);
    }

    public void Vacuum()
    {
        try
        {
            _db.Execute("VACUUM");
        }
        catch
        {
        }
    }
}
