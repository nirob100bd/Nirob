using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Swifter.Theme;

public enum AppTheme
{
    Light,
    Dark,
    System
}

public sealed class ThemeSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string AccentHex { get; set; } = "#0078D4";
    public bool UseMica { get; set; } = true;
    public double TabRadius { get; set; } = 8;
    public double UiScale { get; set; } = 1.0;
    public bool ReduceMotion { get; set; } = false;
    public string CustomWallpaperPath { get; set; } = "";
    public int NewTabLayout { get; set; } = 0;
}

public sealed class ThemeManager
{
    private readonly AccentColorProvider _accent;
    private string SettingsPath => Path.Combine(Program.GetUserDataRoot(), "theme.json");

    public ThemeSettings Settings { get; private set; } = new ThemeSettings();
    public bool IsDark { get; private set; }
    public GlassBackdrop ActiveBackdrop { get; private set; } = GlassBackdrop.Mica;
    public event Action<bool> ThemeChanged;

    public ThemeManager(AccentColorProvider accent)
    {
        _accent = accent;
    }

    public void LoadTheme()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<ThemeSettings>(json) ?? new ThemeSettings();
            }
        }
        catch
        {
            Settings = new ThemeSettings();
        }
        ResolveAndApply();
    }

    public void SaveTheme()
    {
        try
        {
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
        }
    }

    public void SetTheme(AppTheme theme)
    {
        Settings.Theme = theme;
        SaveTheme();
        ResolveAndApply();
    }

    public void SetAccent(string hex)
    {
        Settings.AccentHex = hex;
        SaveTheme();
        if (TryParseHex(hex, out var color)) _accent.SetCustomAccent(color);
        ApplyResources();
        ThemeChanged?.Invoke(IsDark);
    }

    public void SetBackdrop(bool mica)
    {
        Settings.UseMica = mica;
        ActiveBackdrop = mica ? GlassBackdrop.Mica : GlassBackdrop.None;
        SaveTheme();
    }

    private void ResolveAndApply()
    {
        IsDark = Settings.Theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark()
        };
        ActiveBackdrop = Settings.UseMica ? GlassBackdrop.Mica : GlassBackdrop.None;
        if (TryParseHex(Settings.AccentHex, out var accent)) _accent.SetCustomAccent(accent);
        ApplyResources();
        ThemeChanged?.Invoke(IsDark);
    }

    private void ApplyResources()
    {
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher.Invoke(() =>
        {
            var res = app.Resources;
            res["AccentBrush"] = new SolidColorBrush(_accent.CurrentAccent);
            res["WindowBgBrush"] = new SolidColorBrush(IsDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
            res["TabSurfaceBrush"] = new SolidColorBrush(IsDark ? Color.FromRgb(0x2B, 0x2B, 0x2B) : Color.FromRgb(0xE8, 0xE8, 0xE8));
            res["TabActiveBrush"] = new SolidColorBrush(IsDark ? Color.FromRgb(0x32, 0x32, 0x32) : Color.FromRgb(0xFF, 0xFF, 0xFF));
            res["TextPrimaryBrush"] = new SolidColorBrush(IsDark ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x1A, 0x1A, 0x1A));
            res["TextSecondaryBrush"] = new SolidColorBrush(IsDark ? Color.FromRgb(0xA0, 0xA0, 0xA0) : Color.FromRgb(0x5C, 0x5C, 0x5C));
            res["BorderBrushLight"] = new SolidColorBrush(IsDark ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xD0, 0xD0, 0xD0));
            res["GlassBrush"] = new SolidColorBrush(IsDark ? Color.FromArgb(0xCC, 0x1C, 0x1C, 0x1C) : Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
            foreach (Window w in app.Windows)
            {
                NativeGlassEngine.RefreshTheme(w, IsDark);
            }
        });
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int i) return i == 0;
        }
        catch
        {
        }
        return false;
    }

    public static bool TryParseHex(string hex, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            color = Color.FromRgb((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
            return true;
        }
        if (hex.Length == 8 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
        {
            color = Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
            return true;
        }
        return false;
    }

    public string ToHex(Color c)
    {
        return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
    }
}
