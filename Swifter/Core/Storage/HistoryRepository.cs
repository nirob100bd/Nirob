using System.Data.SQLite;

namespace Swifter.Core.Storage;

public sealed class HistoryEntry
{
    public long Id { get; set; }
    public string Url { get; set; }
    public string Title { get; set; }
    public DateTime VisitedAt { get; set; }
    public int VisitCount { get; set; }
    public int TypedCount { get; set; }
    public string Transition { get; set; }
    public bool Hidden { get; set; }
}

public sealed class HistoryRepository
{
    private readonly DatabaseContext _db;
    public event Action<HistoryEntry> EntryAdded;

    public HistoryRepository(DatabaseContext db)
    {
        _db = db;
    }

    public void AddVisit(string url, string title, string transition = "link")
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (url.StartsWith("swifter://") || url.StartsWith("about:")) return;
        var now = DateTime.UtcNow.Ticks;
        Task.Run(() =>
        {
            try
            {
                _db.Execute(@"
INSERT INTO history (url, title, visited_at, visit_count, typed_count, transition)
VALUES (@url, @title, @now,
  COALESCE((SELECT visit_count FROM history WHERE url = @url), 0) + 1,
  CASE WHEN @transition = 'typed' THEN COALESCE((SELECT typed_count FROM history WHERE url = @url), 0) + 1
       ELSE COALESCE((SELECT typed_count FROM history WHERE url = @url), 0) END,
  @transition)
ON CONFLICT(url) DO UPDATE SET
  title = COALESCE(excluded.title, history.title),
  visited_at = excluded.visited_at,
  visit_count = history.visit_count + 1,
  transition = excluded.transition",
                    ("@url", url),
                    ("@title", title ?? ""),
                    ("@now", now),
                    ("@transition", transition ?? "link"));
                EntryAdded?.Invoke(new HistoryEntry
                {
                    Url = url,
                    Title = title,
                    VisitedAt = DateTime.UtcNow,
                    Transition = transition
                });
            }
            catch
            {
            }
        });
    }

    public List<HistoryEntry> Search(string query, int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(query))
            return GetRecent(limit);
        var q = "%" + query.Trim() + "%";
        return _db.QueryList(
            @"SELECT id, url, title, visited_at, visit_count, typed_count, transition, hidden
              FROM history WHERE hidden = 0 AND (url LIKE @q OR title LIKE @q)
              ORDER BY visit_count DESC, visited_at DESC LIMIT @limit",
            Map,
            ("@q", q),
            ("@limit", limit));
    }

    public List<HistoryEntry> GetRecent(int limit = 200, DateTime? since = null)
    {
        var sinceTicks = (since ?? DateTime.MinValue).ToUniversalTime().Ticks;
        return _db.QueryList(
            @"SELECT id, url, title, visited_at, visit_count, typed_count, transition, hidden
              FROM history WHERE hidden = 0 AND visited_at >= @since
              ORDER BY visited_at DESC LIMIT @limit",
            Map,
            ("@since", sinceTicks),
            ("@limit", limit));
    }

    public List<HistoryEntry> GetRange(DateTime from, DateTime to)
    {
        return _db.QueryList(
            @"SELECT id, url, title, visited_at, visit_count, typed_count, transition, hidden
              FROM history WHERE visited_at >= @from AND visited_at < @to AND hidden = 0
              ORDER BY visited_at DESC LIMIT 2000",
            Map,
            ("@from", from.ToUniversalTime().Ticks),
            ("@to", to.ToUniversalTime().Ticks));
    }

    public List<HistoryEntry> GetSuggestions(string prefix, int limit = 8)
    {
        var q = prefix.Trim() + "%";
        var likeQ = "%" + prefix.Trim() + "%";
        var results = _db.QueryList(
            @"SELECT id, url, title, visited_at, visit_count, typed_count, transition, hidden
              FROM history WHERE hidden = 0 AND (url LIKE @q OR title LIKE @like)
              ORDER BY visit_count DESC, visited_at DESC LIMIT @limit",
            Map,
            ("@q", q),
            ("@like", likeQ),
            ("@limit", limit));
        return results;
    }

    public void Delete(long id)
    {
        _db.Execute("DELETE FROM history WHERE id = @id", ("@id", id));
    }

    public void DeleteRange(DateTime from, DateTime to)
    {
        _db.Execute("DELETE FROM history WHERE visited_at >= @from AND visited_at < @to",
            ("@from", from.ToUniversalTime().Ticks),
            ("@to", to.ToUniversalTime().Ticks));
    }

    public void ClearAll()
    {
        _db.Execute("DELETE FROM history");
    }

    public void HideUrl(string url)
    {
        _db.Execute("UPDATE history SET hidden = 1 WHERE url = @url", ("@url", url));
    }

    public int Count()
    {
        var result = _db.Scalar("SELECT COUNT(*) FROM history WHERE hidden = 0");
        return Convert.ToInt32(result);
    }

    public List<(string Day, int Count)> GetDailyCounts(int days = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days).Ticks;
        var rows = _db.QueryList(
            @"SELECT substr(datetime(visited_at / 10000000 - 62135596800, 'unixepoch'), 1, 10) AS day, COUNT(*) AS c
              FROM history WHERE visited_at >= @cutoff AND hidden = 0
              GROUP BY day ORDER BY day",
            r => (r.GetString(0), r.GetInt32(1)),
            ("@cutoff", cutoff));
        return rows;
    }

    private static HistoryEntry Map(System.Data.IDataReader r)
    {
        return new HistoryEntry
        {
            Id = r.GetInt64(0),
            Url = r.GetString(1),
            Title = r.IsDBNull(2) ? "" : r.GetString(2),
            VisitedAt = new DateTime(r.GetInt64(3), DateTimeKind.Utc).ToLocalTime(),
            VisitCount = r.GetInt32(4),
            TypedCount = r.GetInt32(5),
            Transition = r.IsDBNull(6) ? "" : r.GetString(6),
            Hidden = r.GetInt32(7) != 0
        };
    }
}
