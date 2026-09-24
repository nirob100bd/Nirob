using System.Runtime.InteropServices;
using System.Windows.Media;
using Microsoft.Win32;

namespace Swifter.Theme;

public sealed class AccentColorProvider
{
    private readonly List<Action<Color>> _listeners = new List<Action<Color>>();
    private object _lock = new object();

    public Color CurrentAccent { get; private set; } = Color.FromRgb(0x00, 0x78, 0xD4);
    public Color CurrentAccentDark { get; private set; } = Color.FromRgb(0x00, 0x5A, 0x9E);
    public uint CurrentAccentArgb { get; private set; } = 0xFFD47800;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorizationColor, out bool opaque);

    public AccentColorProvider()
    {
        TryReadSystemAccent();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public void Subscribe(Action<Color> listener)
    {
        lock (_lock) _listeners.Add(listener);
        listener(CurrentAccent);
    }

    public void Unsubscribe(Action<Color> listener)
    {
        lock (_lock) _listeners.Remove(listener);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Color || e.Category == UserPreferenceCategory.General)
        {
            TryReadSystemAccent();
            Broadcast();
        }
    }

    private void TryReadSystemAccent()
    {
        try
        {
            if (DwmGetColorizationColor(out var raw, out _) == 0)
            {
                var a = (byte)((raw >> 24) & 0xFF);
                var r = (byte)((raw >> 16) & 0xFF);
                var g = (byte)((raw >> 8) & 0xFF);
                var b = (byte)(raw & 0xFF);
                if (a == 0) a = 0xFF;
                CurrentAccent = Color.FromRgb(r, g, b);
                CurrentAccentDark = Darken(CurrentAccent, 0.72);
                CurrentAccentArgb = (uint)((a << 24) | (r << 16) | (g << 8) | b);
                return;
            }
        }
        catch
        {
        }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            var val = key?.GetValue("AccentColor") as int?;
            if (val.HasValue)
            {
                var raw = (uint)val.Value;
                var r = (byte)(raw & 0xFF);
                var g = (byte)((raw >> 8) & 0xFF);
                var b = (byte)((raw >> 16) & 0xFF);
                CurrentAccent = Color.FromRgb(r, g, b);
                CurrentAccentDark = Darken(CurrentAccent, 0.72);
                CurrentAccentArgb = 0xFF000000u | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
            }
        }
        catch
        {
        }
    }

    public void SetCustomAccent(Color color)
    {
        CurrentAccent = color;
        CurrentAccentDark = Darken(color, 0.72);
        CurrentAccentArgb = 0xFF000000u | (uint)(color.R << 16) | (uint)(color.G << 8) | color.B;
        Broadcast();
    }

    private void Broadcast()
    {
        Action<Color>[] snap;
        lock (_lock) snap = _listeners.ToArray();
        foreach (var l in snap)
        {
            try { l(CurrentAccent); } catch { }
        }
    }

    public static Color Darken(Color c, double factor)
    {
        return Color.FromRgb(
            (byte)Math.Clamp(c.R * factor, 0, 255),
            (byte)Math.Clamp(c.G * factor, 0, 255),
            (byte)Math.Clamp(c.B * factor, 0, 255));
    }

    public static Color Lighten(Color c, double factor)
    {
        return Color.FromRgb(
            (byte)Math.Clamp(c.R + (255 - c.R) * factor, 0, 255),
            (byte)Math.Clamp(c.G + (255 - c.G) * factor, 0, 255),
            (byte)Math.Clamp(c.B + (255 - c.B) * factor, 0, 255));
    }

    public static Color Mix(Color a, Color b, double t)
    {
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
