using System.Text.Json;

namespace Swifter.Core.Storage;

public sealed class SessionData
{
    public string Key { get; set; } = "default";
    public string Label { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<SessionWindow> Windows { get; set; } = new List<SessionWindow>();
}

public sealed class SessionWindow
{
    public int Index { get; set; }
    public bool IsActive { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public List<SessionTab> Tabs { get; set; } = new List<SessionTab>();
}

public sealed class SessionTab
{
    public string Url { get; set; }
    public string Title { get; set; }
    public bool IsActive { get; set; }
    public string GroupId { get; set; }
    public bool IsPinned { get; set; }
    public string ContainerId { get; set; }
    public int SplitIndex { get; set; } = -1;
}

public sealed class SessionRepository
{
    private readonly DatabaseContext _db;
    private readonly JsonSerializerOptions _json = new JsonSerializerOptions { WriteIndented = false };

    public SessionRepository(DatabaseContext db)
    {
        _db = db;
    }

    public void Save(SessionData session)
    {
        if (session == null) return;
        session.UpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(session, _json);
        var now = DateTime.UtcNow.Ticks;
        var windowCount = session.Windows.Count;
        var tabCount = session.Windows.Sum(w => w.Tabs.Count);
        _db.Execute(@"
INSERT INTO sessions (session_key, label, data_json, window_count, tab_count, created_at, updated_at)
VALUES (@k, @l, @d, @w, @t, @now, @now)
ON CONFLICT(session_key) DO UPDATE SET
  label = excluded.label,
  data_json = excluded.data_json,
  window_count = excluded.window_count,
  tab_count = excluded.tab_count,
  updated_at = excluded.updated_at",
            ("@k", session.Key),
            ("@l", session.Label ?? session.Key),
            ("@d", json),
            ("@w", windowCount),
            ("@t", tabCount),
            ("@now", now));
    }

    public SessionData Load(string key = "default")
    {
        var rows = _db.QueryList(
            "SELECT data_json FROM sessions WHERE session_key = @k",
            r => r.GetString(0),
            ("@k", key));
        var json = rows.FirstOrDefault();
        if (json == null) return null;
        try
        {
            return JsonSerializer.Deserialize<SessionData>(json);
        }
        catch
        {
            return null;
        }
    }

    public List<(string Key, string Label, DateTime UpdatedAt, int Tabs)> ListSessions()
    {
        return _db.QueryList(
            "SELECT session_key, label, updated_at, tab_count FROM sessions ORDER BY updated_at DESC",
            r => (r.GetString(0), r.GetString(1), new DateTime(r.GetInt64(2), DateTimeKind.Utc).ToLocalTime(), r.GetInt32(3)));
    }

    public void Delete(string key)
    {
        _db.Execute("DELETE FROM sessions WHERE session_key = @k", ("@k", key));
    }

    public void CreateSnapshot(string label)
    {
        var session = new SessionData
        {
            Key = "snapshot-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            Label = label
        };
        Save(session);
    }

    public async Task SaveCurrentSessionAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                var data = CollectFromWindow();
                if (data != null) Save(data);
            }).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private SessionData CollectFromWindow()
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app?.MainWindow == null) return null;
            var data = new SessionData { Key = "default", Label = "Session" };
            var window = new SessionWindow
            {
                Index = 0,
                IsActive = true,
                X = (int)app.MainWindow.Left,
                Y = (int)app.MainWindow.Top,
                Width = (int)app.MainWindow.Width,
                Height = (int)app.MainWindow.Height,
                Maximized = app.MainWindow.WindowState == System.Windows.WindowState.Maximized
            };
            data.Windows.Add(window);
            return data;
        }
        catch
        {
            return null;
        }
    }

    public void SaveWindowState(int x, int y, int w, int h, bool maximized, List<SessionTab> tabs)
    {
        var data = Load("default") ?? new SessionData();
        data.Windows.Clear();
        data.Windows.Add(new SessionWindow
        {
            Index = 0,
            IsActive = true,
            X = x,
            Y = y,
            Width = w,
            Height = h,
            Maximized = maximized,
            Tabs = tabs ?? new List<SessionTab>()
        });
        Save(data);
    }

    public int Count()
    {
        return Convert.ToInt32(_db.Scalar("SELECT COUNT(*) FROM sessions"));
    }
}
