using System.Data.SQLite;
using System.Security.Cryptography;
using System.Text;

namespace Swifter.Core.Storage;

public sealed class StoredCookie
{
    public long Id { get; set; }
    public string HostKey { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }
    public string Path { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public bool IsSecure { get; set; }
    public bool IsHttpOnly { get; set; }
}

public sealed class CookieRepository
{
    private readonly DatabaseContext _db;

    public CookieRepository(DatabaseContext db)
    {
        _db = db;
    }

    public void Upsert(StoredCookie cookie)
    {
        if (cookie == null || string.IsNullOrEmpty(cookie.HostKey) || string.IsNullOrEmpty(cookie.Name)) return;
        _db.Execute(@"
INSERT INTO cookies (host_key, name, value, path, expires_utc, is_secure, is_http_only, created_at, updated_at)
VALUES (@h, @n, @v, @p, @e, @s, @ho, @now, @now)
ON CONFLICT(host_key, name, path) DO UPDATE SET
  value = excluded.value,
  expires_utc = excluded.expires_utc,
  is_secure = excluded.is_secure,
  is_http_only = excluded.is_http_only,
  updated_at = excluded.updated_at",
            ("@h", cookie.HostKey),
            ("@n", cookie.Name),
            ("@v", cookie.Value ?? ""),
            ("@p", cookie.Path ?? "/"),
            ("@e", cookie.ExpiresUtc.ToUniversalTime().Ticks),
            ("@s", cookie.IsSecure ? 1 : 0),
            ("@ho", cookie.IsHttpOnly ? 1 : 0),
            ("@now", DateTime.UtcNow.Ticks));
    }

    public void BulkUpsert(IEnumerable<StoredCookie> cookies)
    {
        foreach (var c in cookies) Upsert(c);
    }

    public List<StoredCookie> GetForHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return new List<StoredCookie>();
        return _db.QueryList(
            @"SELECT id, host_key, name, value, path, expires_utc, is_secure, is_http_only
              FROM cookies
              WHERE (host_key = @h OR host_key LIKE '%.' || @h) AND (expires_utc = 0 OR expires_utc > @now)
              ORDER BY length(host_key) DESC",
            Map,
            ("@h", host.Trim().ToLowerInvariant()),
            ("@now", DateTime.UtcNow.Ticks));
    }

    public List<StoredCookie> Search(string query, int limit = 100)
    {
        var q = "%" + query + "%";
        return _db.QueryList(
            @"SELECT id, host_key, name, value, path, expires_utc, is_secure, is_http_only
              FROM cookies WHERE host_key LIKE @q OR name LIKE @q LIMIT @limit",
            Map,
            ("@q", q),
            ("@limit", limit));
    }

    public void Delete(long id)
    {
        _db.Execute("DELETE FROM cookies WHERE id = @id", ("@id", id));
    }

    public void DeleteForHost(string host)
    {
        _db.Execute("DELETE FROM cookies WHERE host_key = @h OR host_key LIKE '%.' || @h", ("@h", host));
    }

    public void ClearAll()
    {
        _db.Execute("DELETE FROM cookies");
    }

    public void ClearExpired()
    {
        _db.Execute("DELETE FROM cookies WHERE expires_utc > 0 AND expires_utc < @now", ("@now", DateTime.UtcNow.Ticks));
    }

    public int Count()
    {
        return Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM cookies"));
    }

    public async Task ExportNetscapeAsync(string filePath, string domainFilter = null)
    {
        var cookies = string.IsNullOrEmpty(domainFilter)
            ? _db.QueryList("SELECT id, host_key, name, value, path, expires_utc, is_secure, is_http_only FROM cookies", Map)
            : _db.QueryList("SELECT id, host_key, name, value, path, expires_utc, is_secure, is_http_only FROM cookies WHERE host_key LIKE @q",
                Map, ("@q", "%" + domainFilter + "%"));

        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        foreach (var c in cookies)
        {
            var includeSub = c.HostKey.StartsWith(".") ? "TRUE" : "FALSE";
            var secure = c.IsSecure ? "TRUE" : "FALSE";
            var expiry = c.ExpiresUtc == default ? "0" : c.ExpiresUtc.ToUniversalTime().Ticks.ToString();
            sb.AppendLine(string.Join("\t", includeSub, c.HostKey, c.Path ?? "/", secure, expiry, c.Name, c.Value ?? ""));
        }
        await File.WriteAllTextAsync(filePath, sb.ToString()).ConfigureAwait(false);
    }

    private static StoredCookie Map(System.Data.IDataReader r)
    {
        return new StoredCookie
        {
            Id = r.GetInt64(0),
            HostKey = r.GetString(1),
            Name = r.GetString(2),
            Value = r.IsDBNull(3) ? "" : r.GetString(3),
            Path = r.IsDBNull(4) ? "/" : r.GetString(4),
            ExpiresUtc = new DateTime(r.GetInt64(5), DateTimeKind.Utc),
            IsSecure = r.GetInt32(6) != 0,
            IsHttpOnly = r.GetInt32(7) != 0
        };
    }
}
