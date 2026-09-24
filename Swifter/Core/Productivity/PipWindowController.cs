using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Swifter.Core.Productivity;

public sealed class PipWindowController
{
    private Window _pipWindow;
    private FrameworkElement _videoHost;
    private bool _topMost = true;
    private double _restoreWidth;
    private double _restoreHeight;
    private WindowState _restoreState;

    public bool IsOpen => _pipWindow != null && _pipWindow.IsVisible;
    public bool TopMost
    {
        get => _topMost;
        set
        {
            _topMost = value;
            if (_pipWindow != null) _pipWindow.Topmost = value;
        }
    }

    public event Action Opened;
    public event Action Closed;
    public event Action<string> VideoUrlDetected;

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    public void OpenOverVideo(FrameworkElement videoElement, string videoUrl = null)
    {
        if (videoElement == null) return;
        _videoHost = videoElement;
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_pipWindow != null)
            {
                _pipWindow.Activate();
                return;
            }
            _pipWindow = new Window
            {
                Title = "Swifter — Picture in Picture",
                Width = 420,
                Height = 260,
                MinWidth = 240,
                MinHeight = 150,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = false,
                Topmost = _topMost,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Background = System.Windows.Media.Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = SystemParameters.WorkArea.Right - 440,
                Top = SystemParameters.WorkArea.Bottom - 300
            };
            _pipWindow.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(_pipWindow).Handle;
                var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
                Theme.NativeGlassEngine.SetDarkTitleBar(hwnd, true);
            };
            _pipWindow.Closing += (s, e) =>
            {
                Closed?.Invoke();
                _pipWindow = null;
                _videoHost = null;
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var contentHost = new ContentControl { Content = videoElement };
            Grid.SetRow(contentHost, 0);
            grid.Children.Add(contentHost);

            var bar = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xE0, 0x10, 0x10, 0x14)),
                Padding = new Thickness(8, 4, 8, 4)
            };
            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var back = MakePipButton("⏮", (s, e) => EvalMedia("document.querySelector('video')&&document.querySelector('video').currentTime-=10"));
            var fwd = MakePipButton("⏭", (s, e) => EvalMedia("document.querySelector('video')&&document.querySelector('video').currentTime+=10"));
            var play = MakePipButton("⏯", (s, e) => EvalMedia("var v=document.querySelector('video');if(v){v.paused?v.play():v.pause();}"));
            var close = MakePipButton("✕", (s, e) => _pipWindow?.Close());
            Grid.SetColumn(back, 0);
            var spacer = new TextBlock { Text = videoUrl == null ? "Picture in Picture" : Shorten(videoUrl), Foreground = System.Windows.Media.Brushes.White, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(spacer, 1);
            Grid.SetColumn(play, 2);
            Grid.SetColumn(close, 3);
            barGrid.Children.Add(back);
            barGrid.Children.Add(spacer);
            barGrid.Children.Add(play);
            barGrid.Children.Add(close);
            bar.Child = barGrid;
            Grid.SetRow(bar, 1);
            grid.Children.Add(bar);

            _pipWindow.Content = grid;
            _pipWindow.Show();
            if (!string.IsNullOrEmpty(videoUrl)) VideoUrlDetected?.Invoke(videoUrl);
            Opened?.Invoke();
        });
    }

    private static string Shorten(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            if (string.IsNullOrEmpty(name)) name = uri.Host;
            return name.Length > 42 ? name.Substring(0, 42) + "…" : name;
        }
        catch
        {
            return url.Length > 42 ? url.Substring(0, 42) + "…" : url;
        }
    }

    private Button MakePipButton(string text, RoutedEventHandler handler)
    {
        var b = new Button
        {
            Content = text,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = System.Windows.Media.Brushes.White,
            Padding = new Thickness(8, 2, 8, 2),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 13
        };
        b.Click += handler;
        return b;
    }

    private void EvalMedia(string js)
    {
        try
        {
            var host = _videoHost;
            while (host != null)
            {
                if (host is Microsoft.Web.WebView2.Wpf.WebView2 wv)
                {
                    wv.CoreWebView2?.ExecuteScriptAsync(js);
                    return;
                }
                host = System.Windows.Media.VisualTreeHelper.GetParent(host) as FrameworkElement;
            }
        }
        catch
        {
        }
    }

    public void Toggle()
    {
        if (IsOpen) _pipWindow?.Close();
        else OpenOverVideo(_videoHost);
    }

    public void MoveTo(double left, double top)
    {
        if (_pipWindow == null) return;
        _pipWindow.Left = left;
        _pipWindow.Top = top;
    }

    public void SetSize(double width, double height)
    {
        if (_pipWindow == null) return;
        _pipWindow.Width = Math.Max(200, width);
        _pipWindow.Height = Math.Max(140, height);
    }

    public void Close()
    {
        _pipWindow?.Close();
        _pipWindow = null;
    }

    public void SnapToCorner()
    {
        if (_pipWindow == null) return;
        _pipWindow.Left = SystemParameters.WorkArea.Right - _pipWindow.Width - 20;
        _pipWindow.Top = SystemParameters.WorkArea.Bottom - _pipWindow.Height - 60;
    }
}
