using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Swifter.Theme.CustomControls;

public partial class StatusOverlayControl : UserControl, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    private DispatcherTimer _toastTimer;
    private DispatcherTimer _gestureTimer;

    public Brush PillBackground => new SolidColorBrush(App.ThemeManager?.IsDark == true ? Color.FromArgb(0xE6, 0x2A, 0x2A, 0x2A) : Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    public Brush PillBorder => new SolidColorBrush(App.ThemeManager?.IsDark == true ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x30, 0x00, 0x00, 0x00));
    public Brush TitleBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)) : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    public Brush SubtitleBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)) : new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C));

    public StatusOverlayControl()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (s, e) =>
        {
            OnPropertyChanged(nameof(PillBackground));
            OnPropertyChanged(nameof(PillBorder));
            OnPropertyChanged(nameof(TitleBrush));
            OnPropertyChanged(nameof(SubtitleBrush));
        };
    }

    public void ShowToast(string message, string icon = "ℹ", int durationMs = 3200)
    {
        Dispatcher.Invoke(() =>
        {
            ToastIcon.Text = icon;
            ToastText.Text = message;
            ToastPill.Visibility = Visibility.Visible;
            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            _toastTimer.Tick += (s, e) =>
            {
                ToastPill.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
            };
            _toastTimer.Start();
        });
    }

    public void ShowStatus(string title, string detail = "", string icon = "ℹ")
    {
        Dispatcher.Invoke(() =>
        {
            StatusIcon.Text = icon;
            StatusTitle.Text = title;
            StatusDetail.Text = detail;
            StatusPill.Visibility = Visibility.Visible;
        });
    }

    public void HideStatus()
    {
        Dispatcher.Invoke(() => StatusPill.Visibility = Visibility.Collapsed);
    }

    public void ShowSpeed(string text)
    {
        Dispatcher.Invoke(() =>
        {
            SpeedText.Text = text;
            SpeedPill.Visibility = Visibility.Visible;
        });
    }

    public void HideSpeed()
    {
        Dispatcher.Invoke(() => SpeedPill.Visibility = Visibility.Collapsed);
    }

    public void ShowMemory(string text)
    {
        Dispatcher.Invoke(() =>
        {
            MemText.Text = text;
            MemPill.Visibility = Visibility.Visible;
        });
    }

    public void ShowGesture(string gesture)
    {
        Dispatcher.Invoke(() =>
        {
            GestureText.Text = gesture;
            GesturePill.Visibility = Visibility.Visible;
            _gestureTimer?.Stop();
            _gestureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
            _gestureTimer.Tick += (s, e) =>
            {
                GesturePill.Visibility = Visibility.Collapsed;
                _gestureTimer.Stop();
            };
            _gestureTimer.Start();
        });
    }

    public void HideGesture()
    {
        Dispatcher.Invoke(() => GesturePill.Visibility = Visibility.Collapsed);
    }

    protected void OnPropertyChanged([CallerMemberName] string n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
