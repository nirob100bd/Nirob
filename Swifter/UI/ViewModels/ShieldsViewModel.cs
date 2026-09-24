using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Swifter.Core.Shield;
using Swifter.Theme;

namespace Swifter.UI.ViewModels;

public sealed class BlockedEntry : ObservableObject
{
    private string _url;
    private string _host;
    private string _resourceType;
    private DateTime _timestamp;

    public string Url { get => _url; set => Set(ref _url, value); }
    public string Host { get => _host; set => Set(ref _host, value); }
    public string ResourceType { get => _resourceType; set => Set(ref _resourceType, value); }
    public DateTime Timestamp { get => _timestamp; set => Set(ref _timestamp, value); }
}

public sealed class ProtectionToggle : ObservableObject
{
    private string _id;
    private string _title;
    private string _description;
    private bool _enabled;

    public string Id { get => _id; set => Set(ref _id, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Description { get => _description; set => Set(ref _description, value); }
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public Brush DotBrush
    {
        get
        {
            if (ThemeManager.TryParseHex(Enabled ? "#0F7B0F" : "#C42B1C", out var c)) return new SolidColorBrush(c);
            return new SolidColorBrush(Colors.Gray);
        }
    }
}

public sealed class ShieldsViewModel : ObservableObject
{
    private bool _shieldsUp = true;
    private string _currentHost = "";
    private long _networkBlocked;
    private long _cosmeticHidden;
    private long _paramsStripped;
    private int _ruleCount;
    private string _webRtcStatus = "Guard active";
    private string _fingerprintStatus = "Noise injected";

    private readonly NetworkRequestFilter _filter;
    private readonly CosmeticStyleInjector _cosmetic;

    public bool ShieldsUp
    {
        get => _shieldsUp;
        set
        {
            if (Set(ref _shieldsUp, value))
            {
                if (App.AdBlock != null) App.AdBlock.ShieldEnabled = value;
                if (_filter != null) _filter.GlobalEnabled = value;
                OnPropertyChanged(nameof(StateLabel));
                OnPropertyChanged(nameof(StateBrush));
                OnPropertyChanged(nameof(StateColor));
                StateChanged?.Invoke(value);
            }
        }
    }
    public string StateLabel => ShieldsUp ? "Shields are UP" : "Shields are DOWN";
    public string StateColor => ShieldsUp ? "#0F7B0F" : "#C42B1C";
    public Brush StateBrush
    {
        get
        {
            if (ThemeManager.TryParseHex(StateColor, out var c)) return new SolidColorBrush(c);
            return Brushes.Gray;
        }
    }
    public string CurrentHost { get => _currentHost; set { Set(ref _currentHost, value); OnPropertyChanged(nameof(HostLabel)); } }
    public string HostLabel => string.IsNullOrEmpty(CurrentHost) ? "All sites" : CurrentHost;
    public long NetworkBlocked { get => _networkBlocked; set => Set(ref _networkBlocked, value); }
    public long CosmeticHidden { get => _cosmeticHidden; set => Set(ref _cosmeticHidden, value); }
    public long ParamsStripped { get => _paramsStripped; set => Set(ref _paramsStripped, value); }
    public int RuleCount { get => _ruleCount; set => Set(ref _ruleCount, value); }
    public long TotalBlocked => NetworkBlocked + CosmeticHidden;
    public string WebRtcStatus { get => _webRtcStatus; set => Set(ref _webRtcStatus, value); }
    public string FingerprintStatus { get => _fingerprintStatus; set => Set(ref _fingerprintStatus, value); }

    public ObservableCollection<BlockedEntry> RecentBlocked { get; } = new ObservableCollection<BlockedEntry>();
    public ObservableCollection<ProtectionToggle> Protections { get; } = new ObservableCollection<ProtectionToggle>();

    public ICommand ToggleCommand { get; }
    public ICommand PauseHostCommand { get; }
    public ICommand ResumeHostCommand { get; }
    public ICommand ScanWebRtcCommand { get; }
    public ICommand AddRuleCommand { get; }

    public event Action<bool> StateChanged;
    public event Action<string> HostPaused;
    public event Action HostResumed;
    public event Action<string> RuleAdded;
    public event Action<string> StatusRaised;

    public ShieldsViewModel(NetworkRequestFilter filter, CosmeticStyleInjector cosmetic)
    {
        _filter = filter;
        _cosmetic = cosmetic;
        ToggleCommand = new RelayCommand(_ => ShieldsUp = !ShieldsUp);
        PauseHostCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrEmpty(CurrentHost)) return;
            _filter?.PauseHost(CurrentHost);
            HostPaused?.Invoke(CurrentHost);
            StatusRaised?.Invoke("Paused shields for " + CurrentHost);
        });
        ResumeHostCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(CurrentHost)) _filter?.ResumeHost(CurrentHost);
            HostResumed?.Invoke();
            StatusRaised?.Invoke("Shields resumed");
        });
        ScanWebRtcCommand = new RelayCommand(_ =>
        {
            var ips = App.WebRtcProtection?.GetLocalIpsSnapshot() ?? (IReadOnlyList<System.Net.IPAddress>)Array.Empty<System.Net.IPAddress>();
            WebRtcStatus = ips.Count == 0 ? "No local IPs exposed" : string.Join(", ", ips.Select(i => i.ToString()));
        });
        AddRuleCommand = new RelayCommand(o =>
        {
            if (o is string rule && !string.IsNullOrWhiteSpace(rule))
            {
                App.AdBlock?.AddCustomRule(rule);
                RuleAdded?.Invoke(rule);
                StatusRaised?.Invoke("Rule added");
            }
        });

        SeedProtections();
        if (_filter != null) _filter.Blocked += OnBlocked;
    }

    private void SeedProtections()
    {
        Protections.Add(new ProtectionToggle { Id = "adblock", Title = "Ad / tracker blocking", Description = "EasyList trie + domain matcher", Enabled = true });
        Protections.Add(new ProtectionToggle { Id = "cosmetic", Title = "Cosmetic element hiding", Description = "Collapses empty slots", Enabled = true });
        Protections.Add(new ProtectionToggle { Id = "fingerprint", Title = "Anti-fingerprinting", Description = "Canvas / WebGL / Audio noise", Enabled = true });
        Protections.Add(new ProtectionToggle { Id = "webrtc", Title = "WebRTC leak prevention", Description = "Relay-only ICE candidates", Enabled = true });
        Protections.Add(new ProtectionToggle { Id = "paramstrip", Title = "Tracking parameter stripping", Description = "utm_*, fbclid, gclid, ref", Enabled = true });
        Protections.Add(new ProtectionToggle { Id = "scriptblock", Title = "Script blocker", Description = "Cryptojackers & hijacks", Enabled = true });
        Protections.Add(new ProtectionToggle { Id = "headers", Title = "UA / client-hints masking", Description = "Uniform fingerprint", Enabled = true });
    }

    private void OnBlocked(BlockedRequestRecord record)
    {
        if (record == null) return;
        var entry = new BlockedEntry
        {
            Url = record.Url,
            Host = record.Host,
            ResourceType = record.ResourceType,
            Timestamp = record.Timestamp
        };
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            RecentBlocked.Insert(0, entry);
            while (RecentBlocked.Count > 100) RecentBlocked.RemoveAt(RecentBlocked.Count - 1);
        });
    }

    public void OnHostChanged(string host)
    {
        CurrentHost = host;
        var filter = App.AdBlock;
        if (filter != null)
        {
            ShieldsUp = filter.ShieldEnabled;
            var stats = filter.GetStatsSnapshot();
            NetworkBlocked = stats.NetworkBlocked;
            CosmeticHidden = stats.CosmeticHidden;
            RuleCount = filter.LoadedRuleCount;
            OnPropertyChanged(nameof(TotalBlocked));
        }
        var paramStripper = App.ParamStripper;
        if (paramStripper != null) ParamsStripped = paramStripper.TotalStripped;
    }

    public void RefreshStats()
    {
        var stats = App.AdBlock?.GetStatsSnapshot();
        if (stats == null) return;
        NetworkBlocked = stats.NetworkBlocked;
        CosmeticHidden = stats.CosmeticHidden;
        ParamsStripped = App.ParamStripper?.TotalStripped ?? 0;
        RuleCount = App.AdBlock?.LoadedRuleCount ?? 0;
        OnPropertyChanged(nameof(TotalBlocked));
        FingerprintStatus = App.FingerprintProtector != null ? "Noise active" : "Inactive";
        WebRtcStatus = App.WebRtcProtection != null ? (App.WebRtcProtection.BlockNonProxiedUdp ? "Guard active" : "Disabled") : "Inactive";
    }

    public void SetProtection(string id, bool enabled)
    {
        var p = Protections.FirstOrDefault(x => x.Id == id);
        if (p != null) p.Enabled = enabled;
        switch (id)
        {
            case "adblock":
                if (App.AdBlock != null) App.AdBlock.ShieldEnabled = enabled;
                ShieldsUp = enabled;
                break;
            case "cosmetic":
                if (_cosmetic != null) _cosmetic.Enabled = enabled;
                break;
            case "fingerprint":
                if (App.FingerprintProtector != null)
                {
                    App.FingerprintProtector.CanvasNoise = enabled;
                    App.FingerprintProtector.AudioNoise = enabled;
                    App.FingerprintProtector.WebGLSpoof = enabled;
                }
                break;
            case "webrtc":
                if (App.WebRtcProtection != null) App.WebRtcProtection.BlockNonProxiedUdp = enabled;
                break;
            case "paramstrip":
                if (App.ParamStripper != null) App.ParamStripper.Enabled = enabled;
                break;
        }
    }

    public string GetHostWebRtcSummary()
    {
        var ips = App.WebRtcProtection?.GetLocalIpsSnapshot();
        if (ips == null || ips.Count == 0) return "No candidates";
        var vpn = App.WebRtcProtection.IsLikelyVpnActive();
        return $"{ips.Count} local IPs · VPN detected: {(vpn ? "yes" : "no")} · {(App.WebRtcProtection.BlockNonProxiedUdp ? "relay-only" : "open UDP")}";
    }
}
