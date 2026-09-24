using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Swifter.Theme.CustomControls;

namespace Swifter.UI.ViewModels;

public sealed class TabViewModel : ObservableObject
{
    private TabItemModel _model;
    private bool _canGoBack;
    private bool _canGoForward;
    private bool _isAudible;
    private bool _isMuted;
    private bool _isPinned;
    private bool _isReading;
    private double _zoom = 1.0;
    private string _certificateState = "none";
    private Guid _containerId;
    private string _faviconGlyph = "◉";

    public TabItemModel Model
    {
        get => _model;
        set { _model = value; OnPropertyChanged(); OnPropertyChanged(nameof(Id)); OnPropertyChanged(nameof(Title)); }
    }

    public Guid Id => _model?.Id ?? Guid.Empty;
    public string Title => _model?.Title ?? "New Tab";
    public string Url => _model?.Url ?? "swifter://newtab";
    public bool CanGoBack { get => _canGoBack; set => Set(ref _canGoBack, value); }
    public bool CanGoForward { get => _canGoForward; set => Set(ref _canGoForward, value); }
    public bool IsAudible { get => _isAudible; set { Set(ref _isAudible, value); OnPropertyChanged(nameof(AudioIcon)); } }
    public bool IsMuted { get => _isMuted; set { Set(ref _isMuted, value); _model.IsMuted = value; OnPropertyChanged(nameof(AudioIcon)); } }
    public bool IsPinned { get => _isPinned; set { Set(ref _isPinned, value); _model.IsPinned = value; } }
    public bool IsReading { get => _isReading; set => Set(ref _isReading, value); }
    public double Zoom { get => _zoom; set => Set(ref _zoom, value); }
    public string CertificateState { get => _certificateState; set { Set(ref _certificateState, value); OnPropertyChanged(nameof(CertificateIcon)); } }
    public Guid ContainerId { get => _containerId; set => Set(ref _containerId, value); }
    public string FaviconGlyph { get => _faviconGlyph; set => Set(ref _faviconGlyph, value); }
    public string AudioIcon => IsMuted ? "🔇" : IsAudible ? "🔊" : "";
    public string CertificateIcon => CertificateState switch
    {
        "secure" => "🔒",
        "insecure" => "⚠",
        "internal" => "🛡",
        _ => ""
    };
    public string DisplayUrl
    {
        get
        {
            var url = Url;
            if (url.StartsWith("swifter://")) return url;
            try
            {
                var uri = new Uri(url);
                return uri.Host + uri.AbsolutePath.TrimEnd('/');
            }
            catch
            {
                return url;
            }
        }
    }

    public TabViewModel(TabItemModel model = null)
    {
        _model = model ?? new TabItemModel();
    }

    public void UpdateTitle(string title)
    {
        _model.Title = title;
        OnPropertyChanged(nameof(Title));
    }

    public void UpdateUrl(string url)
    {
        _model.Url = url;
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(DisplayUrl));
        CertificateState = url.StartsWith("https://") ? "secure"
            : url.StartsWith("http://") ? "insecure"
            : url.StartsWith("swifter://") || url.StartsWith("about:") ? "internal"
            : "none";
    }

    public void SetLoading(bool loading)
    {
        _model.IsLoading = loading;
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    public void TogglePin()
    {
        IsPinned = !IsPinned;
    }

    public void ToggleZoom(double delta = 0.1)
    {
        Zoom = Math.Clamp(Math.Round(Zoom + delta, 2), 0.25, 5.0);
    }

    public void ResetZoom()
    {
        Zoom = 1.0;
    }
}

public sealed class TabCollectionViewModel : ObservableObject
{
    private TabViewModel _active;

    public ObservableCollection<TabViewModel> Tabs { get; } = new ObservableCollection<TabViewModel>();
    public TabViewModel Active
    {
        get => _active;
        set
        {
            if (Set(ref _active, value) && value != null)
            {
                foreach (var t in Tabs) t.Model.IsActive = t == value;
                ActiveChanged?.Invoke(value);
            }
        }
    }

    public event Action<TabViewModel> ActiveChanged;
    public event Action<TabViewModel> TabClosed;

    public TabViewModel Add(string url = "swifter://newtab", string title = "New Tab")
    {
        var model = new TabItemModel { Url = url, Title = title };
        var vm = new TabViewModel(model);
        Tabs.Add(vm);
        Active = vm;
        return vm;
    }

    public void Close(TabViewModel tab)
    {
        if (tab == null) return;
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        TabClosed?.Invoke(tab);
        if (Active == tab)
        {
            Active = Tabs.Count == 0 ? null : Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }
    }

    public void Close(Guid id)
    {
        Close(Tabs.FirstOrDefault(t => t.Id == id));
    }

    public TabViewModel Next(bool forward)
    {
        if (Tabs.Count == 0) return null;
        var index = Active == null ? 0 : Tabs.IndexOf(Active);
        index = forward ? (index + 1) % Tabs.Count : (index - 1 + Tabs.Count) % Tabs.Count;
        Active = Tabs[index];
        return Active;
    }

    public IReadOnlyList<TabViewModel> HibernationCandidates(TimeSpan idleThreshold)
    {
        var cutoff = DateTime.UtcNow - idleThreshold;
        return Tabs.Where(t => t != Active).ToList();
    }
}
