using System.Windows;
using System.Windows.Input;

namespace Swifter.Core.Productivity;

public enum GestureAction
{
    None,
    Back,
    Forward,
    NewTab,
    CloseTab,
    Reload,
    OpenLinkNewTab,
    ScrollTop,
    ScrollBottom,
    NextTab,
    PrevTab,
    ShowPalette
}

public sealed class GestureEventArgs : EventArgs
{
    public GestureAction Action { get; set; }
    public string Path { get; set; }
    public string RecognizedAs { get; set; }
}

public sealed class GestureRecognizer
{
    private readonly List<System.Windows.Point> _points = new List<System.Windows.Point>();
    private bool _tracking;
    private DateTime _start;
    public bool Enabled { get; set; } = true;
    public double MinDistance { get; set; } = 60;
    public double MaxDeviation { get; set; } = 70;
    public int MaxPoints { get; set; } = 240;
    public event Action<GestureEventArgs> GestureRecognized;
    public event Action<string> GesturePreview;

    private static readonly Dictionary<string, GestureAction> Map = new Dictionary<string, GestureAction>(StringComparer.OrdinalIgnoreCase)
    {
        ["L"] = GestureAction.Back,
        ["R"] = GestureAction.Forward,
        ["D"] = GestureAction.CloseTab,
        ["U"] = GestureAction.NewTab,
        ["UR"] = GestureAction.OpenLinkNewTab,
        ["DR"] = GestureAction.Reload,
        ["DL"] = GestureAction.ShowPalette,
        ["UD"] = GestureAction.NextTab,
        ["DU"] = GestureAction.PrevTab,
        ["LR"] = GestureAction.ScrollTop,
        ["RL"] = GestureAction.ScrollBottom
    };

    public void OnMouseDown(Point p)
    {
        if (!Enabled) return;
        _tracking = true;
        _points.Clear();
        _points.Add(p);
        _start = DateTime.UtcNow;
    }

    public void OnMouseMove(Point p)
    {
        if (!_tracking || !Enabled) return;
        if (_points.Count >= MaxPoints) return;
        var last = _points[^1];
        if ((p - last).Length < 8) return;
        _points.Add(p);
        var path = BuildPath();
        if (path.Length > 0) GesturePreview?.Invoke(path);
    }

    public void OnMouseUp(Point p)
    {
        if (!_tracking) return;
        _tracking = false;
        if (!Enabled) { _points.Clear(); return; }
        _points.Add(p);
        var elapsed = DateTime.UtcNow - _start;
        if (elapsed > TimeSpan.FromSeconds(2.5)) { _points.Clear(); return; }
        var path = BuildPath();
        _points.Clear();
        if (path.Length == 0) return;
        if (!Map.TryGetValue(path, out var action)) return;
        GestureRecognized?.Invoke(new GestureEventArgs
        {
            Action = action,
            Path = path,
            RecognizedAs = Describe(action)
        });
    }

    public void Cancel()
    {
        _tracking = false;
        _points.Clear();
    }

    private string BuildPath()
    {
        if (_points.Count < 2) return "";
        var start = _points[0];
        var end = _points[^1];
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < MinDistance) return "";

        var maxY = _points.Max(p => Math.Abs(p.Y - start.Y));
        var maxX = _points.Max(p => Math.Abs(p.X - start.X));
        if (maxX > MaxDeviation * 3 && maxY < MinDistance * 0.5)
        {
            return dx > 0 ? "R" : "L";
        }
        if (maxY > MaxDeviation * 2 && maxX < MinDistance * 0.5)
        {
            return dy < 0 ? "U" : "D";
        }

        var sb = new System.Text.StringBuilder();
        var lastDir = '\0';
        for (var i = 1; i < _points.Count; i++)
        {
            var ddx = _points[i].X - _points[i - 1].X;
            var ddy = _points[i].Y - _points[i - 1].Y;
            if (Math.Abs(ddx) < 12 && Math.Abs(ddy) < 12) continue;
            char dir;
            if (Math.Abs(ddx) > Math.Abs(ddy) * 1.4) dir = ddx > 0 ? 'R' : 'L';
            else if (Math.Abs(ddy) > Math.Abs(ddx) * 1.4) dir = ddy > 0 ? 'D' : 'U';
            else continue;
            if (dir != lastDir)
            {
                sb.Append(dir);
                lastDir = dir;
                if (sb.Length >= 3) break;
            }
        }
        var path = sb.ToString();
        if (path.Length == 0) return distance >= MinDistance ? (dx > 0 ? "R" : dx < 0 ? "L" : dy > 0 ? "D" : "U") : "";
        if (path.Length > 3) path = path.Substring(0, 3);
        if (path.Length >= 2)
        {
            var simplified = new System.Text.StringBuilder();
            simplified.Append(path[0]);
            for (var i = 1; i < path.Length; i++)
            {
                if (path[i] != path[i - 1]) simplified.Append(path[i]);
            }
            path = simplified.ToString();
        }
        return path;
    }

    public static string Describe(GestureAction action)
    {
        return action switch
        {
            GestureAction.Back => "← Back",
            GestureAction.Forward => "→ Forward",
            GestureAction.NewTab => "↑ New Tab",
            GestureAction.CloseTab => "↓ Close Tab",
            GestureAction.Reload => "↓→ Reload",
            GestureAction.OpenLinkNewTab => "↑→ Open in New Tab",
            GestureAction.ShowPalette => "↓← Command Palette",
            GestureAction.NextTab => "↑↓ Next Tab",
            GestureAction.PrevTab => "↓↑ Previous Tab",
            GestureAction.ScrollTop => "→← Top",
            GestureAction.ScrollBottom => "←→ Bottom",
            _ => ""
        };
    }

    public IReadOnlyDictionary<string, GestureAction> GetBindings() => Map;

    public void Rebind(string path, GestureAction action)
    {
        if (string.IsNullOrEmpty(path)) return;
        Map[path.ToUpperInvariant()] = action;
    }

    public bool IsTracking => _tracking;
    public IReadOnlyList<Point> CurrentPoints => _points;
}
