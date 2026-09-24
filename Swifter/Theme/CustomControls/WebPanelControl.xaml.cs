using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Swifter.Theme.CustomControls;

public class WebPanelItem : INotifyPropertyChanged
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string _title;
    private string _url;
    private string _icon;
    private bool _isActive;

    public string Title { get => _title; set { _title = value; OnPropertyChanged(); } }
    public string Url { get => _url; set { _url = value; OnPropertyChanged(); } }
    public string Icon { get => _icon; set { _icon = value; OnPropertyChanged(); } }
    public bool IsActive { get => _isActive; set { _isActive = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class WebPanelControl : UserControl, INotifyPropertyChanged
{
    public event EventHandler<WebPanelItem> PanelActivated;
    public event EventHandler<WebPanelItem> PanelCloseRequested;
    public event EventHandler AddPanelRequested;
    public event EventHandler CollapseRequested;
    public event PropertyChangedEventHandler PropertyChanged;

    private readonly System.Collections.ObjectModel.ObservableCollection<WebPanelItem> _panels = new();

    public System.Collections.ObjectModel.ObservableCollection<WebPanelItem> Panels => _panels;

    public Brush PanelBackground => new SolidColorBrush(App.ThemeManager?.IsDark == true ? Color.FromRgb(0x24, 0x24, 0x24) : Color.FromRgb(0xF9, 0xF9, 0xF9));
    public Brush PanelBorder => new SolidColorBrush(App.ThemeManager?.IsDark == true ? Color.FromRgb(0x3A, 0x3A, 0x3A) : Color.FromRgb(0xE0, 0xE0, 0xE0));
    public Brush TitleBrush => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)) : new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
    public Brush ChipBackground => App.ThemeManager?.IsDark == true ? new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)) : new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC));

    public WebPanelControl()
    {
        InitializeComponent();
        DataContext = this;
        SeedPresets();
        Loaded += (s, e) => ApplyTheme();
    }

    private void SeedPresets()
    {
        if (_panels.Count > 0) return;
        _panels.Add(new WebPanelItem { Title = "ChatGPT", Icon = "🤖", Url = "https://chatgpt.com" });
        _panels.Add(new WebPanelItem { Title = "WhatsApp", Icon = "💬", Url = "https://web.whatsapp.com" });
        _panels.Add(new WebPanelItem { Title = "X / Twitter", Icon = "𝕏", Url = "https://x.com" });
        _panels.Add(new WebPanelItem { Title = "Spotify", Icon = "🎵", Url = "https://open.spotify.com" });
        _panels.Add(new WebPanelItem { Title = "YouTube", Icon = "▶", Url = "https://m.youtube.com" });
    }

    private void ApplyTheme()
    {
        OnPropertyChanged(nameof(PanelBackground));
        OnPropertyChanged(nameof(PanelBorder));
        OnPropertyChanged(nameof(TitleBrush));
        OnPropertyChanged(nameof(ChipBackground));
    }

    public void SetStatus(string text)
    {
        if (StatusText != null) StatusText.Text = text;
    }

    public void ActivatePanel(WebPanelItem item)
    {
        foreach (var p in _panels) p.IsActive = p.Id == item.Id;
        SetStatus(item.Title + " — " + item.Url);
        PanelActivated?.Invoke(this, item);
    }

    public void AddPanel(string title, string url)
    {
        var item = new WebPanelItem { Title = title, Url = url, Icon = "＋" };
        _panels.Add(item);
        ActivatePanel(item);
    }

    private void OnChatGptClick(object sender, MouseButtonEventArgs e) => ActivateByTitle("ChatGPT");
    private void OnWhatsAppClick(object sender, MouseButtonEventArgs e) => ActivateByTitle("WhatsApp");
    private void OnTwitterClick(object sender, MouseButtonEventArgs e) => ActivateByTitle("X / Twitter");
    private void OnSpotifyClick(object sender, MouseButtonEventArgs e) => ActivateByTitle("Spotify");

    private void ActivateByTitle(string title)
    {
        var item = _panels.FirstOrDefault(p => p.Title == title);
        if (item != null) ActivatePanel(item);
    }

    private void OnAddPanelClick(object sender, RoutedEventArgs e) => AddPanelRequested?.Invoke(this, EventArgs.Empty);
    private void OnCollapseClick(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke(this, EventArgs.Empty);

    protected void OnPropertyChanged([CallerMemberName] string n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
