using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Swifter.UI.Views;

public partial class PipWindow : Window
{
    private string _sourceUrl;
    private bool _topMost = true;
    private bool _dragging;
    private System.Windows.Point _dragOffset;

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    public PipWindow()
    {
        InitializeComponent();
        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
            Theme.NativeGlassEngine.SetDarkTitleBar(hwnd, true);
        };
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height - 64;
    }

    public void SetVideoElement(object element, string title = null, string sourceUrl = null)
    {
        VideoHost.Content = element;
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Picture in Picture" : title;
        _sourceUrl = sourceUrl;
        Title = "Swifter PiP — " + TitleText.Text;
    }

    public void SetSourceUrl(string url) => _sourceUrl = url;

    private void OnDragStart(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _dragging = true;
            _dragOffset = e.GetPosition(this);
            CaptureMouse();
        }
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
        {
            var pos = e.GetPosition(this);
            Left += pos.X - _dragOffset.X;
            Top += pos.Y - _dragOffset.Y;
        }
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnTopmostToggle(object sender, RoutedEventArgs e)
    {
        _topMost = !_topMost;
        Topmost = _topMost;
        TopmostBtn.Opacity = _topMost ? 1.0 : 0.45;
    }

    private void OnStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
    }

    private void ExecJs(string js)
    {
        try
        {
            var host = VideoHost.Content as FrameworkElement;
            while (host != null)
            {
                if (host is Microsoft.Web.WebView2.Wpf.WebView2 wv)
                {
                    wv.CoreWebView2?.ExecuteScriptAsync(js);
                    return;
                }
                host = VisualTreeHelper.GetParent(host) as FrameworkElement;
            }
        }
        catch
        {
        }
    }

    private void OnBack10(object sender, RoutedEventArgs e) => ExecJs("var v=document.querySelector('video');if(v)v.currentTime-=10;");
    private void OnForward10(object sender, RoutedEventArgs e) => ExecJs("var v=document.querySelector('video');if(v)v.currentTime+=10;");
    private void OnPlayPause(object sender, RoutedEventArgs e) => ExecJs("var v=document.querySelector('video');if(v){v.paused?v.play():v.pause();}");
    private void OnMute(object sender, RoutedEventArgs e) => ExecJs("var v=document.querySelector('video');if(v)v.muted=!v.muted;");
    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ExecJs($"var v=document.querySelector('video');if(v)v.volume={e.NewValue.ToString(System.Globalization.CultureInfo.InvariantCulture)};");

    private void OnOpenOriginal(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_sourceUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _sourceUrl, UseShellExecute = true });
        }
        catch
        {
        }
    }
}
