using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Swifter.Theme;

public enum GlassBackdrop
{
    None,
    Mica,
    Acrylic,
    Tabbed,
    LegacyBlur
}

public static class NativeGlassEngine
{
    private static IntPtr _hookedHwnd;

    public static void Apply(Window window, GlassBackdrop backdrop, bool darkMode, uint acrylicTint = 0xCC202020)
    {
        if (window == null) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            window.SourceInitialized += (s, e) => Apply(window, backdrop, darkMode, acrylicTint);
            return;
        }
        _hookedHwnd = handle;
        DwmApi.SetDarkMode(handle, darkMode);
        DwmApi.SetCornerPreference(handle, DwmApi.DWMWCP_ROUND);
        switch (backdrop)
        {
            case GlassBackdrop.Mica:
                DwmApi.SetBackdrop(handle, DwmApi.DWMSBT_MAINWINDOW);
                ExtendFrame(handle);
                break;
            case GlassBackdrop.Tabbed:
                DwmApi.SetBackdrop(handle, DwmApi.DWMSBT_TABBEDWINDOW);
                ExtendFrame(handle);
                break;
            case GlassBackdrop.Acrylic:
                DwmApi.SetBackdrop(handle, DwmApi.DWMSBT_TRANSIENTWINDOW);
                DwmApi.ApplyAcrylic(handle, acrylicTint);
                break;
            case GlassBackdrop.LegacyBlur:
                ApplyLegacyBlur(handle);
                break;
            case GlassBackdrop.None:
                DwmApi.SetBackdrop(handle, DwmApi.DWMSBT_NONE);
                break;
        }
    }

    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        DwmApi.SetDarkMode(hwnd, dark);
    }

    public static void RefreshTheme(Window window, bool darkMode)
    {
        if (window == null) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        DwmApi.SetDarkMode(handle, darkMode);
        DwmApi.SetBorderColor(handle, darkMode ? 0x003A3A3Au : 0x00D0D0D0u);
    }

    private static void ExtendFrame(IntPtr hwnd)
    {
        var margins = new DwmApi.MARGINS
        {
            cxLeftWidth = -1,
            cxRightWidth = -1,
            cyTopHeight = -1,
            cyBottomHeight = -1
        };
        DwmApi.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    private static void ApplyLegacyBlur(IntPtr hwnd)
    {
        var blur = new DwmApi.DWM_BLURBEHIND
        {
            dwFlags = DwmApi.DWM_BB_ENABLE,
            fEnable = true,
            hRegion = IntPtr.Zero,
            fTransitionOnMaximized = false
        };
        DwmApi.DwmEnableBlurBehindWindow(hwnd, ref blur);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;

    public static void MakeClickThrough(IntPtr hwnd, bool clickThrough)
    {
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex = clickThrough ? ex | WS_EX_LAYERED | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);
    }
}
