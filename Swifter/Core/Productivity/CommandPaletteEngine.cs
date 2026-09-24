using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Swifter.Core.Productivity;

public sealed class PaletteResult : INotifyPropertyChanged
{
    private string _title;
    private string _subtitle;
    private string _icon;
    private string _category;
    private object _payload;
    private double _score;

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Subtitle { get => _subtitle; set { _subtitle = value; OnPropertyChanged(); } }
    public string Icon { get => _icon; set { _icon = value; OnPropertyChanged(); } }
    public string Category { get => _category; set { _category = value; OnPropertyChanged(); } }
    public object Payload { get => _payload; set { _payload = value; OnPropertyChanged(); } }
    public double Score { get => _score; set { _score = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Func<object, bool> _canExecute;

    public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object parameter) => _execute(parameter);
}

public sealed class CommandPaletteEngine : INotifyPropertyChanged
{
    private string _query = "";
    private int _selectedIndex;
    private bool _isVisible;

    public ObservableCollection<PaletteResult> Results { get; } = new ObservableCollection<PaletteResult>();
    public string Query
    {
        get => _query;
        set { _query = value ?? ""; OnPropertyChanged(); Refresh(); }
    }
    public int SelectedIndex
    {
        get => _selectedIndex;
        set { _selectedIndex = Math.Max(0, Math.Min(value, Results.Count - 1)); OnPropertyChanged(); }
    }
    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); }
    }

    public event Action<PaletteResult> ResultActivated;
    public event Action Closed;
    public event PropertyChangedEventHandler PropertyChanged;

    private readonly List<PaletteResult> _index = new List<PaletteResult>();
    private readonly object _lock = new object();

    public CommandPaletteEngine()
    {
        RegisterDefaultActions();
    }

    public void RegisterAction(string title, string icon, string category, Action execute, string subtitle = null)
    {
        lock (_lock)
        {
            _index.Add(new PaletteResult
            {
                Title = title,
                Icon = icon,
                Category = category,
                Subtitle = subtitle ?? category,
                Payload = execute
            });
        }
        if (IsVisible) Refresh();
    }

    private void RegisterDefaultActions()
    {
        RegisterAction("New Tab", "＋", "Browser", () => Request("newtab"), "Ctrl+T");
        RegisterAction("Close Tab", "✕", "Browser", () => Request("closetab"), "Ctrl+W");
        RegisterAction("Reopen Closed Tab", "↺", "Browser", () => Request("undoclosetab"), "Ctrl+Shift+T");
        RegisterAction("New Window", "▣", "Browser", () => Request("newwindow"), "Ctrl+N");
        RegisterAction("New Private Window", "🕶", "Browser", () => Request("newprivate"), "Ctrl+Shift+N");
        RegisterAction("Split Screen Left/Right", "◫", "Layout", () => Request("split2"), "Alt+Shift+S");
        RegisterAction("Toggle Vertical Tabs", "☰", "Layout", () => Request("togglevertical"), "Alt+Shift+V");
        RegisterAction("Toggle Web Panels", "▤", "Layout", () => Request("togglepanels"), "");
        RegisterAction("History", "🕘", "Page", () => Request("history"), "Ctrl+H");
        RegisterAction("Bookmarks Manager", "★", "Page", () => Request("bookmarks"), "Ctrl+Shift+O");
        RegisterAction("Downloads", "⬇", "Page", () => Request("downloads"), "Ctrl+J");
        RegisterAction("Settings", "⚙", "Page", () => Request("settings"), "");
        RegisterAction("Shields Dashboard", "🛡", "Page", () => Request("shields"), "");
        RegisterAction("Reader Mode", "📖", "Page", () => Request("reader"), "Ctrl+Shift+R");
        RegisterAction("Picture in Picture", "⧉", "Media", () => Request("pip"), "Alt+Shift+P");
        RegisterAction("Reload Ignoring Cache", "⟳", "Browser", () => Request("hardreload"), "Ctrl+Shift+R");
        RegisterAction("Clear Browsing Data", "🧹", "Privacy", () => Request("cleardata"), "Ctrl+Shift+Del");
        RegisterAction("Mute Tab", "🔇", "Browser", () => Request("mutetab"), "");
        RegisterAction("Duplicate Tab", "⧉", "Browser", () => Request("duplicate"), "Alt+Shift+D");
        RegisterAction("Hibernate Background Tabs", "💤", "Performance", () => Request("hibernateall"), "");
        RegisterAction("Trim Working Set", "🧠", "Performance", () => Request("trimmemory"), "");
        RegisterAction("Toggle Ad Block", "🛡", "Privacy", () => Request("toggleadblock"), "");
        RegisterAction("Command Palette", "⌘", "Browser", () => Request("palette"), "Ctrl+K");
        RegisterAction("Google Lens", "◫", "Tools", () => Request("lens"), "");
        RegisterAction("Export Bookmarks", "📤", "Tools", () => Request("exportbookmarks"), "");
    }

    private void Request(string action)
    {
        ResultActivated?.Invoke(new PaletteResult { Title = action, Category = "action", Payload = action });
    }

    public void IndexHistory(IEnumerable<(string Title, string Url)> items)
    {
        lock (_lock)
        {
            _index.RemoveAll(r => r.Category == "History");
            foreach (var (title, url) in items.Take(300))
            {
                _index.Add(new PaletteResult
                {
                    Title = string.IsNullOrWhiteSpace(title) ? url : title,
                    Subtitle = url,
                    Icon = "🕘",
                    Category = "History",
                    Payload = url
                });
            }
        }
    }

    public void IndexBookmarks(IEnumerable<(string Title, string Url)> items)
    {
        lock (_lock)
        {
            _index.RemoveAll(r => r.Category == "Bookmarks");
            foreach (var (title, url) in items.Take(300))
            {
                _index.Add(new PaletteResult
                {
                    Title = string.IsNullOrWhiteSpace(title) ? url : title,
                    Subtitle = url,
                    Icon = "★",
                    Category = "Bookmarks",
                    Payload = url
                });
            }
        }
    }

    public void IndexTabs(IEnumerable<(string Title, string Url, Guid TabId)> items)
    {
        lock (_lock)
        {
            _index.RemoveAll(r => r.Category == "Tabs");
            foreach (var (title, url, tabId) in items)
            {
                _index.Add(new PaletteResult
                {
                    Title = title ?? url,
                    Subtitle = url,
                    Icon = "⧉",
                    Category = "Tabs",
                    Payload = tabId
                });
            }
        }
    }

    public void Refresh()
    {
        var query = _query.Trim();
        List<PaletteResult> snapshot;
        lock (_lock) snapshot = _index.ToList();

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Results.Clear();
            if (string.IsNullOrEmpty(query))
            {
                foreach (var r in snapshot.Where(s => s.Category == "Browser" || s.Category == "Page").Take(12))
                    Results.Add(r);
            }
            else
            {
                var scored = snapshot
                    .Select(r => (Result: r, Score: Score(r, query)))
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Result.Category)
                    .Take(30);
                foreach (var (result, score) in scored)
                {
                    result.Score = score;
                    Results.Add(result);
                }
            }
            SelectedIndex = 0;
        });
    }

    private static double Score(PaletteResult r, string query)
    {
        var q = query.ToLowerInvariant();
        var title = (r.Title ?? "").ToLowerInvariant();
        var subtitle = (r.Subtitle ?? "").ToLowerInvariant();
        if (title == q) return 1000;
        if (title.StartsWith(q)) return 500;
        if (title.Contains(q)) return 300;
        if (subtitle.StartsWith(q)) return 220;
        if (subtitle.Contains(q)) return 180;
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = words.Count(w => title.Contains(w) || subtitle.Contains(w));
        if (hits == words.Length && words.Length > 0) return 150;
        return FuzzyScore(title, q);
    }

    private static double FuzzyScore(string text, string pattern)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern)) return 0;
        var ti = 0;
        var matched = 0;
        for (var pi = 0; pi < pattern.Length; pi++)
        {
            var found = text.IndexOf(pattern[pi], ti);
            if (found < 0) return 0;
            matched++;
            ti = found + 1;
        }
        return matched * 10.0 / Math.Max(1, pattern.Length);
    }

    public void ActivateSelected()
    {
        if (Results.Count == 0) return;
        var item = Results[Math.Clamp(SelectedIndex, 0, Results.Count - 1)];
        ResultActivated?.Invoke(item);
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;
        SelectedIndex = (SelectedIndex + delta + Results.Count) % Results.Count;
    }

    public void Open()
    {
        IsVisible = true;
        Query = "";
        Refresh();
    }

    public void Close()
    {
        IsVisible = false;
        Closed?.Invoke();
    }

    protected void OnPropertyChanged([CallerMemberName] string n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
