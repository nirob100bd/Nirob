using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Swifter.Theme;

namespace Swifter.UI.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private AppTheme _theme;
    private string _accentHex = "#0078D4";
    private bool _micaEnabled = true;
    private bool _verticalTabs;
    private bool _lowEndMode;
    private bool _hibernationEnabled = true;
    private bool _autoSniff = true;
    private bool _videoOverlay = true;
    private bool _gestureEnabled = true;
    private bool _restoreSession = true;
    private int _memThresholdMb = 200;
    private int _segments = 16;
    private long _globalSpeedLimit;
    private double _tabRadius = 8;
    private string _downloadFolder;
    private string _customWallpaper;
    private bool _isDirty;

    public AppTheme Theme
    {
        get => _theme;
        set { if (Set(ref _theme, value)) { IsDirty = true; ApplyTheme(); } }
    }
    public string AccentHex
    {
        get => _accentHex;
        set { if (Set(ref _accentHex, value)) { IsDirty = true; ApplyAccent(); } }
    }
    public bool MicaEnabled { get => _micaEnabled; set { if (Set(ref _micaEnabled, value)) { IsDirty = true; } } }
    public bool VerticalTabs { get => _verticalTabs; set { if (Set(ref _verticalTabs, value)) IsDirty = true; } }
    public bool LowEndMode { get => _lowEndMode; set { if (Set(ref _lowEndMode, value)) IsDirty = true; } }
    public bool HibernationEnabled { get => _hibernationEnabled; set { if (Set(ref _hibernationEnabled, value)) IsDirty = true; } }
    public bool AutoSniff { get => _autoSniff; set { if (Set(ref _autoSniff, value)) IsDirty = true; } }
    public bool VideoOverlay { get => _videoOverlay; set { if (Set(ref _videoOverlay, value)) IsDirty = true; } }
    public bool GestureEnabled { get => _gestureEnabled; set { if (Set(ref _gestureEnabled, value)) IsDirty = true; } }
    public bool RestoreSession { get => _restoreSession; set { if (Set(ref _restoreSession, value)) IsDirty = true; } }
    public int MemThresholdMb { get => _memThresholdMb; set { if (Set(ref _memThresholdMb, value)) IsDirty = true; } }
    public int Segments { get => _segments; set { if (Set(ref _segments, value)) IsDirty = true; } }
    public long GlobalSpeedLimit { get => _globalSpeedLimit; set { if (Set(ref _globalSpeedLimit, value)) IsDirty = true; } }
    public double TabRadius { get => _tabRadius; set { if (Set(ref _tabRadius, value)) IsDirty = true; } }
    public string DownloadFolder { get => _downloadFolder; set => Set(ref _downloadFolder, value); }
    public string CustomWallpaper { get => _customWallpaper; set { if (Set(ref _customWallpaper, value)) IsDirty = true; } }
    public bool IsDirty { get => _isDirty; set { Set(ref _isDirty, value); OnPropertyChanged(nameof(SaveLabel)); } }
    public string SaveLabel => IsDirty ? "Save changes *" : "Saved";

    public ObservableCollection<HotkeyRow> Hotkeys { get; } = new ObservableCollection<HotkeyRow>();
    public ObservableCollection<ContainerRow> Containers { get; } = new ObservableCollection<ContainerRow>();
    public ObservableCollection<ExtensionRow> Extensions { get; } = new ObservableCollection<ExtensionRow>();

    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ClearDataCommand { get; }
    public ICommand TrimCommand { get; }
    public ICommand PickWallpaperCommand { get; }
    public ICommand NewContainerCommand { get; }

    public event Action ClearDataRequested;
    public event Action TrimRequested;
    public event Action WallpaperRequested;
    public event Action<string> StatusRequested;

    public SettingsViewModel()
    {
        DownloadFolder = Path.Combine(Program.GetUserDataRoot(), "downloads");
        LoadFromSettings();
        SaveCommand = new RelayCommand(_ => Save());
        ResetCommand = new RelayCommand(_ => LoadFromSettings());
        ClearDataCommand = new RelayCommand(_ => ClearDataRequested?.Invoke());
        TrimCommand = new RelayCommand(_ => TrimRequested?.Invoke());
        PickWallpaperCommand = new RelayCommand(_ => WallpaperRequested?.Invoke());
        NewContainerCommand = new RelayCommand(_ =>
        {
            var c = App.Containers?.Create("Container " + (App.Containers.Profiles.Count + 1), "#0078D4");
            if (c != null) Containers.Add(new ContainerRow { Id = c.Id, Name = c.Name, Color = c.ColorHex, IsPrivate = c.IsPrivate });
        });
        LoadHotkeys();
        LoadContainers();
        LoadExtensions();
    }

    public void LoadFromSettings()
    {
        var tm = App.ThemeManager;
        if (tm == null) return;
        _theme = tm.Settings.Theme;
        OnPropertyChanged(nameof(Theme));
        _accentHex = tm.Settings.AccentHex;
        OnPropertyChanged(nameof(AccentHex));
        _micaEnabled = tm.Settings.UseMica;
        OnPropertyChanged(nameof(MicaEnabled));
        _tabRadius = tm.Settings.TabRadius;
        OnPropertyChanged(nameof(TabRadius));
        _customWallpaper = tm.Settings.CustomWallpaperPath;
        OnPropertyChanged(nameof(CustomWallpaper));
        _lowEndMode = Core.Engine.NativeMemoryBridge.IsLowEndCpu();
        OnPropertyChanged(nameof(LowEndMode));
        IsDirty = false;
    }

    public void Save()
    {
        var tm = App.ThemeManager;
        if (tm != null)
        {
            tm.Settings.Theme = Theme;
            tm.Settings.AccentHex = AccentHex;
            tm.Settings.UseMica = MicaEnabled;
            tm.Settings.TabRadius = TabRadius;
            tm.Settings.CustomWallpaperPath = CustomWallpaper ?? "";
            tm.SaveTheme();
            tm.SetTheme(Theme);
            tm.SetAccent(AccentHex);
        }
        if (App.MemoryTrimmer != null) App.MemoryTrimmer.WorkingSetThresholdBytes = (long)MemThresholdMb * 1024 * 1024;
        if (App.SpeedThrottle != null) App.SpeedThrottle.GlobalBytesPerSec = GlobalSpeedLimit;
        if (App.AdBlock != null) App.AdBlock.ShieldEnabled = true;
        if (App.MediaSniffer != null) App.MediaSniffer.Enabled = AutoSniff;
        if (App.MediaOverlay != null) App.MediaOverlay.Enabled = VideoOverlay;
        if (App.Hibernator != null) App.Hibernator.Enabled = HibernationEnabled;
        IsDirty = false;
        StatusRequested?.Invoke("Settings saved");
    }

    private void ApplyTheme()
    {
        App.ThemeManager?.SetTheme(Theme);
    }

    private void ApplyAccent()
    {
        App.ThemeManager?.SetAccent(AccentHex);
    }

    private void LoadHotkeys()
    {
        Hotkeys.Clear();
        var hk = App.Hotkeys;
        if (hk == null) return;
        foreach (var b in hk.Bindings)
        {
            Hotkeys.Add(new HotkeyRow
            {
                Id = b.Id,
                Description = b.Description,
                Gesture = b.Gesture,
                Enabled = b.Enabled
            });
        }
    }

    private void LoadContainers()
    {
        Containers.Clear();
        if (App.Containers == null) return;
        foreach (var c in App.Containers.Profiles)
        {
            Containers.Add(new ContainerRow { Id = c.Id, Name = c.Name, Color = c.ColorHex, IsPrivate = c.IsPrivate });
        }
    }

    private void LoadExtensions()
    {
        Extensions.Clear();
        if (App.ExtensionHost == null) return;
        foreach (var e in App.ExtensionHost.Extensions)
        {
            Extensions.Add(new ExtensionRow
            {
                Id = e.Id,
                Name = e.Name,
                Version = e.Version,
                Enabled = e.Enabled,
                Description = e.Description
            });
        }
    }

    public void SetHotkey(string id, string gesture)
    {
        App.Hotkeys?.SetGesture(id, gesture);
        LoadHotkeys();
    }

    public void ClearData(bool history, bool cache, bool cookies, bool passwords)
    {
        App.Cache?.ClearBrowsingData(history, cache, cookies, passwords);
        StatusRequested?.Invoke("Browsing data cleared");
    }

    public string GetUsageSummary()
    {
        var usage = App.Cache?.GetUsageSnapshot() ?? (0L, 0L, 0L);
        var wsMb = App.MemoryTrimmer?.GetWorkingSetMb() ?? 0;
        return $"Working set {wsMb:0} MB · Disk cache {usage.DiskBytes / (1024 * 1024)} MB · DB {usage.DbBytes / (1024 * 1024)} MB · History {usage.HistoryCount}";
    }

    public void OnPropertyChanged([CallerMemberName] string n = null) => base.OnPropertyChanged(n);
}

public sealed class HotkeyRow : ObservableObject
{
    private string _id;
    private string _description;
    private string _gesture;
    private bool _enabled;
    public string Id { get => _id; set => Set(ref _id, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public string Gesture { get => _gesture; set => Set(ref _gesture, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
}

public sealed class ContainerRow : ObservableObject
{
    private string _id;
    private string _name;
    private string _color;
    private bool _isPrivate;
    public string Id { get => _id; set => Set(ref _id, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    public bool IsPrivate { get => _isPrivate; set => Set(ref _isPrivate, value); }
    public Brush Brush
    {
        get
        {
            if (ThemeManager.TryParseHex(Color ?? "#0078D4", out var c)) return new SolidColorBrush(c);
            return new SolidColorBrush(Colors.SteelBlue);
        }
    }
}

public sealed class ExtensionRow : ObservableObject
{
    private string _id;
    private string _name;
    private string _version;
    private bool _enabled;
    private string _description;
    public string Id { get => _id; set => Set(ref _id, value); }
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Version { get => _version; set => Set(ref _version, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
}
