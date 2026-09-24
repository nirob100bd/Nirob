using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Swifter.Theme.CustomControls;

public partial class AddressBarControl : UserControl, INotifyPropertyChanged
{
    public event EventHandler<string> NavigationRequested;
    public event EventHandler ShieldRequested;
    public event EventHandler StarRequested;
    public event EventHandler ReaderRequested;
    public event EventHandler CopyCleanLinkRequested;
    public event EventHandler MediaDownloadRequested;
    public event EventHandler LensRequested;
    public event PropertyChangedEventHandler PropertyChanged;

    private bool _isReadOnly;
    private string _currentUrl = "";
    private bool _starred;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set { _isReadOnly = value; if (UrlBox != null) UrlBox.IsReadOnly = value; }
    }

    public string CurrentUrl
    {
        get => _currentUrl;
        set { _currentUrl = value ?? ""; OnPropertyChanged(); }
    }

    public Brush FieldBrush => new SolidColorBrush(App.ThemeManager?.IsDark == true ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    public Brush BorderBrushColor => new SolidColorBrush(App.ThemeManager?.IsDark == true ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xD0, 0xD0, 0xD0));
    public Brush GlyphBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)) : new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
    public Brush InputForeground => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)) : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    public Brush AccentBrush => new SolidColorBrush(App.AccentProvider?.CurrentAccent ?? Color.FromRgb(0x00, 0x78, 0xD4));

    public AddressBarControl()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += (s, e) => ApplyTheme();
        if (App.ThemeManager != null) App.ThemeManager.ThemeChanged += (d) => Dispatcher.Invoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        OnPropertyChanged(nameof(FieldBrush));
        OnPropertyChanged(nameof(BorderBrushColor));
        OnPropertyChanged(nameof(GlyphBrush));
        OnPropertyChanged(nameof(InputForeground));
        OnPropertyChanged(nameof(AccentBrush));
    }

    public void SetUrl(string url, bool updateBox = true)
    {
        CurrentUrl = url ?? "";
        if (updateBox && UrlBox != null && !UrlBox.IsKeyboardFocused)
        {
            UrlBox.Text = DisplayUrl(url);
        }
        if (ReaderButton != null)
            ReaderButton.Visibility = IsArticleUrl(url) ? Visibility.Visible : Visibility.Collapsed;
        if (ClearButton != null)
            ClearButton.Visibility = string.IsNullOrEmpty(UrlBox?.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetProgress(double fraction)
    {
        if (ProgressRail == null || ProgressFill == null) return;
        if (fraction <= 0 || fraction >= 1)
        {
            ProgressRail.Visibility = Visibility.Collapsed;
            ProgressFill.Width = 0;
        }
        else
        {
            ProgressRail.Visibility = Visibility.Visible;
            ProgressFill.Width = Math.Max(4, 64 * fraction);
        }
    }

    public void SetShieldState(bool enabled, bool blockedCount = false)
    {
        if (ShieldGlyph == null) return;
        ShieldGlyph.Text = enabled ? "🛡" : "🛡️";
        ShieldGlyph.Foreground = enabled
            ? new SolidColorBrush(Color.FromRgb(0x0F, 0x7B, 0x0F))
            : new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
    }

    public void SetStarred(bool starred)
    {
        _starred = starred;
        if (StarButton != null) StarButton.Content = starred ? "★" : "☆";
    }

    public void FocusAddressBar(bool selectAll = true)
    {
        UrlBox?.Focus();
        if (selectAll) UrlBox?.SelectAll();
    }

    private static string DisplayUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == "swifter") return url;
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path.Length > 40) path = path.Substring(0, 40) + "…";
            return uri.Host + path;
        }
        return url;
    }

    private static bool IsArticleUrl(string url)
    {
        return !string.IsNullOrEmpty(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase);
    }

    private void OnUrlKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var text = UrlBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text))
            {
                UrlBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                NavigationRequested?.Invoke(this, text);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            UrlBox.Text = DisplayUrl(CurrentUrl);
            UrlBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void OnUrlLostFocus(object sender, RoutedEventArgs e)
    {
        if (UrlBox != null) UrlBox.Text = DisplayUrl(CurrentUrl);
    }

    private void OnUrlGotFocus(object sender, RoutedEventArgs e)
    {
        if (UrlBox != null) UrlBox.Text = CurrentUrl;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        UrlBox.Text = "";
        UrlBox.Focus();
    }

    private void OnShieldClick(object sender, RoutedEventArgs e) => ShieldRequested?.Invoke(this, EventArgs.Empty);
    private void OnStarClick(object sender, RoutedEventArgs e) => StarRequested?.Invoke(this, EventArgs.Empty);
    private void OnReaderClick(object sender, RoutedEventArgs e) => ReaderRequested?.Invoke(this, EventArgs.Empty);
    private void OnCopyLinkClick(object sender, RoutedEventArgs e) => CopyCleanLinkRequested?.Invoke(this, EventArgs.Empty);
    private void OnDownloadMediaClick(object sender, RoutedEventArgs e) => MediaDownloadRequested?.Invoke(this, EventArgs.Empty);
    private void OnLensClick(object sender, RoutedEventArgs e) => LensRequested?.Invoke(this, EventArgs.Empty);

    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
