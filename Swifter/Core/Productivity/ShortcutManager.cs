using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Swifter.Core.Productivity;

public sealed class HotkeyBinding : INotifyPropertyChanged
{
    private string _id;
    private string _gesture;
    private string _description;
    private bool _enabled;

    public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
    public string Gesture { get => _gesture; set { _gesture = value; OnPropertyChanged(); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ShortcutManager : INotifyPropertyChanged
{
    public ObservableCollection<HotkeyBinding> Bindings { get; } = new ObservableCollection<HotkeyBinding>();
    private readonly string _path;
    private readonly Dictionary<string, Action> _actions = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, KeyGesture> _parsed = new Dictionary<string, KeyGesture>(StringComparer.OrdinalIgnoreCase);

    public event Action<string> ShortcutTriggered;
    public event Action<string> ConflictDetected;
    public event PropertyChangedEventHandler PropertyChanged;

    public ShortcutManager()
    {
        _path = Path.Combine(Program.GetUserDataRoot(), "hotkeys.json");
        Load();
    }

    public void RegisterDefaults()
    {
        AddDefault("newtab", "Ctrl+T", "New tab");
        AddDefault("newtab2", "Ctrl+N", "New window");
        AddDefault("closetab", "Ctrl+W", "Close tab");
        AddDefault("undotab", "Ctrl+Shift+T", "Reopen closed tab");
        AddDefault("reload", "Ctrl+R", "Reload page");
        AddDefault("hardreload", "Ctrl+Shift+R", "Reload ignoring cache");
        AddDefault("find", "Ctrl+F", "Find in page");
        AddDefault("addressbar", "Ctrl+L", "Focus address bar");
        AddDefault("addressbar2", "Alt+D", "Focus address bar");
        AddDefault("palette", "Ctrl+K", "Command palette");
        AddDefault("palette2", "Ctrl+Shift+P", "Command palette");
        AddDefault("downloads", "Ctrl+J", "Downloads");
        AddDefault("history", "Ctrl+H", "History");
        AddDefault("bookmarks", "Ctrl+Shift+O", "Bookmarks");
        AddDefault("settings", "Ctrl+,", "Settings");
        AddDefault("bookmark", "Ctrl+D", "Bookmark page");
        AddDefault("private", "Ctrl+Shift+N", "New private window");
        AddDefault("nexttab", "Ctrl+Tab", "Next tab");
        AddDefault("prevtab", "Ctrl+Shift+Tab", "Previous tab");
        AddDefault("tab1", "Ctrl+1", "Switch to tab 1");
        AddDefault("tab2", "Ctrl+2", "Switch to tab 2");
        AddDefault("tab3", "Ctrl+3", "Switch to tab 3");
        AddDefault("tab4", "Ctrl+4", "Switch to tab 4");
        AddDefault("tab5", "Ctrl+5", "Switch to tab 5");
        AddDefault("tab6", "Ctrl+6", "Switch to tab 6");
        AddDefault("tab7", "Ctrl+7", "Switch to tab 7");
        AddDefault("tab8", "Ctrl+8", "Switch to tab 8");
        AddDefault("tablast", "Ctrl+9", "Switch to last tab");
        AddDefault("zoomin", "Ctrl+=", "Zoom in");
        AddDefault("zoomout", "Ctrl+-", "Zoom out");
        AddDefault("zoomreset", "Ctrl+0", "Reset zoom");
        AddDefault("reader", "Ctrl+Shift+R", "Reader mode");
        AddDefault("pip", "Alt+Shift+P", "Picture in picture");
        AddDefault("split", "Alt+Shift+S", "Toggle split screen");
        AddDefault("verticaltabs", "Alt+Shift+V", "Toggle vertical tabs");
        AddDefault("panels", "Alt+Shift+E", "Toggle web panels");
        AddDefault("shields", "Alt+Shift+B", "Toggle shields");
        AddDefault("cleardata", "Ctrl+Shift+Delete", "Clear browsing data");
        AddDefault("savepage", "Ctrl+S", "Save page");
        AddDefault("print", "Ctrl+P", "Print");
        AddDefault("viewsource", "Ctrl+U", "View source");
        AddDefault("devtools", "F12", "Developer tools");
        if (Bindings.Count == 0) Save();
        RebuildParsed();
    }

    private void AddDefault(string id, string gesture, string description)
    {
        if (Bindings.Any(b => b.Id == id)) return;
        Bindings.Add(new HotkeyBinding
        {
            Id = id,
            Gesture = gesture,
            Description = description,
            Enabled = true
        });
    }

    public void Bind(string id, Action action)
    {
        _actions[id] = action;
    }

    public void BindMany(Dictionary<string, Action> map)
    {
        foreach (var kv in map) _actions[kv.Key] = kv.Value;
    }

    public bool HandleKey(KeyEventArgs e)
    {
        if (e == null) return false;
        var gesture = FormatGesture(e.Key, Keyboard.Modifiers);
        if (string.IsNullOrEmpty(gesture)) return false;
        foreach (var binding in Bindings)
        {
            if (!binding.Enabled) continue;
            if (!string.Equals(binding.Gesture, gesture, StringComparison.OrdinalIgnoreCase)) continue;
            if (_actions.TryGetValue(binding.Id, out var action))
            {
                ShortcutTriggered?.Invoke(binding.Id);
                action();
                e.Handled = true;
                return true;
            }
            ShortcutTriggered?.Invoke(binding.Id);
            e.Handled = true;
            return true;
        }
        return false;
    }

    public static string FormatGesture(Key key, ModifierKeys modifiers)
    {
        if (key == Key.None) return "";
        if (key >= Key.LeftCtrl && key <= Key.RightAlt) return "";
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        var keyName = key switch
        {
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            _ => key.ToString()
        };
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    public void SetGesture(string id, string gesture)
    {
        var binding = Bindings.FirstOrDefault(b => b.Id == id);
        if (binding == null) return;
        var conflict = Bindings.FirstOrDefault(b => b.Id != id && b.Enabled && string.Equals(b.Gesture, gesture, StringComparison.OrdinalIgnoreCase));
        if (conflict != null) ConflictDetected?.Invoke($"{gesture} already bound to {conflict.Description}");
        binding.Gesture = gesture;
        RebuildParsed();
        Save();
    }

    public void SetEnabled(string id, bool enabled)
    {
        var b = Bindings.FirstOrDefault(x => x.Id == id);
        if (b == null) return;
        b.Enabled = enabled;
        RebuildParsed();
        Save();
    }

    public void ResetDefaults()
    {
        Bindings.Clear();
        RegisterDefaults();
        Save();
    }

    private void RebuildParsed()
    {
        _parsed.Clear();
        foreach (var b in Bindings.Where(x => x.Enabled))
        {
            try
            {
                _parsed[b.Id] = new KeyGesture(
                    (Key)Enum.Parse(typeof(Key), b.Gesture.Split('+').Last().Replace("Ctrl", "Control").Replace(",", "OemComma").Replace("-", "OemMinus").Replace("=", "OemPlus"), true),
                    ModifierKeys.None);
            }
            catch
            {
            }
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var list = System.Text.Json.JsonSerializer.Deserialize<List<HotkeyBinding>>(json);
            if (list == null) return;
            Bindings.Clear();
            foreach (var b in list) Bindings.Add(b);
        }
        catch
        {
        }
    }

    public void Save()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(Bindings.ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
        }
    }

    public IReadOnlyList<HotkeyBinding> GetAll() => Bindings.ToList();

    protected void OnPropertyChanged([CallerMemberName] string n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
