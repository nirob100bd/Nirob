using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Swifter.Core.Downloads;
using Swifter.Core.Engine;
using Swifter.Core.Protocols;
using Swifter.Core.Shield;
using Swifter.Core.Storage;
using Swifter.Core.Workspace;
using Swifter.Theme;
using Swifter.Theme.CustomControls;
using Swifter.UI.ViewModels;
using GestureAction = Swifter.Core.Productivity.GestureAction;
using GestureRecognizer = Swifter.Core.Productivity.GestureRecognizer;
using PaletteResult = Swifter.Core.Productivity.PaletteResult;
using ArticleExtractor = Swifter.Core.Productivity.ArticleExtractor;
using GoogleLensIntegration = Swifter.Core.Productivity.GoogleLensIntegration;
using ArticleContent = Swifter.Core.Productivity.ArticleContent;

namespace Swifter.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new MainViewModel();
    private readonly NetworkRequestFilter _networkFilter;
    private readonly CosmeticStyleInjector _cosmeticInjector;
    private readonly SegmentedDownloadEngine _downloadEngine;
    private readonly DownloadsViewModel _downloadsVm;
    private readonly ShieldsViewModel _shieldsVm;
    private readonly SettingsViewModel _settingsVm;
    private readonly HlsStreamSniffer _sniffer;
    private readonly Dictionary<Guid, WebView2> _tabViews = new Dictionary<Guid, WebView2>();
    private readonly Dictionary<Guid, CoreWebView2> _tabCores = new Dictionary<Guid, CoreWebView2>();
    private WebView2 _activeView;
    private bool _webReady;
    private bool _shuttingDown;
    private System.Windows.Point _gestureStart;
    private bool _gestureTracking;
    private bool _verticalLayout;
    private long _lastHeavyTick;
    private string _lensSourceUrl;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        _networkFilter = new NetworkRequestFilter(App.AdBlock);
        _cosmeticInjector = new CosmeticStyleInjector(App.AdBlock);
        _sniffer = App.MediaSniffer ?? new HlsStreamSniffer();
        _downloadEngine = new SegmentedDownloadEngine(App.SpeedThrottle);
        _downloadsVm = new DownloadsViewModel(_downloadEngine);
        _shieldsVm = new ShieldsViewModel(_networkFilter, _cosmeticInjector);
        _settingsVm = new SettingsViewModel();

        TopTabStrip.Tabs = _vm.Tabs;
        VerticalTabStrip.Tabs = _vm.Tabs;
        TopTabStrip.DataContext = this;
        VerticalTabStrip.DataContext = this;
        _vm.NavigateRequested += Navigate;
        _vm.FocusAddressRequested += FocusAddress;
        _vm.FindRequested += ToggleFindBar;
        _vm.ReloadRequested += () => _activeView?.CoreWebView2?.Reload();
        _vm.BackRequested += () => { if (_activeView?.CoreWebView2?.CanGoBack == true) _activeView.CoreWebView2.GoBack(); };
        _vm.ForwardRequested += () => { if (_activeView?.CoreWebView2?.CanGoForward == true) _activeView.CoreWebView2.GoForward(); };
        _vm.Palette.ResultActivated += OnPaletteResult;
        _vm.Palette.Closed += () => PaletteOverlay.Visibility = Visibility.Collapsed;
        PaletteOverlay.Attach(_vm.Palette);
        PaletteOverlay.ResultChosen += r => PaletteOverlay.Visibility = Visibility.Collapsed;

        _shieldsVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ShieldsViewModel.TotalBlocked) || e.PropertyName == nameof(ShieldsViewModel.NetworkBlocked))
                Dispatcher.Invoke(() => BlockedStatus.Text = "🛡 " + _shieldsVm.TotalBlocked);
        };
        _shieldsVm.StateChanged += up =>
        {
            AddressBar.SetShieldState(up);
            Overlay.ShowToast(up ? "Shields enabled" : "Shields disabled", up ? "🛡" : "⚠");
        };
        _shieldsVm.StatusRaised += msg => Overlay.ShowToast(msg);

        NativeGlassEngine.Apply(this, App.ThemeManager?.ActiveBackdrop ?? GlassBackdrop.Mica, App.ThemeManager?.IsDark ?? true);
        if (App.ThemeManager != null) App.ThemeManager.ThemeChanged += dark => Dispatcher.Invoke(() => NativeGlassEngine.RefreshTheme(this, dark));

        Loaded += async (s, e) => await InitializeWebViewAsync();
        CompositionTarget.Rendering += OnFrameRender;

        SetupInput();
    }

    private void SetupInput()
    {
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => _vm.Palette.IsVisible ? _vm.Palette.Close() : OpenPalette()), Key.K, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => NewTab()), Key.T, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => CloseActiveTab()), Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => FocusAddress()), Key.L, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => FocusAddress()), Key.D, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => ToggleFindBar()), Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => Navigate("swifter://history")), Key.H, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => Navigate("swifter://downloads")), Key.J, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => Navigate("swifter://bookmarks")), Key.O, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => Navigate("swifter://settings")), Key.OemComma, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => ReloadHard()), Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => Reload()), Key.R, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => _vm.Workspace.CycleSplit()), Key.S, ModifierKeys.Alt | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => ToggleVerticalTabs()), Key.V, ModifierKeys.Alt | ModifierKeys.Shift));
        InputBindings.Add(new WeakRelayBindingHack(this));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => OpenReader()), Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => _vm.SwitchTab(true)), Key.Tab, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => _vm.SwitchTab(false)), Key.Tab, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => BookmarkPage()), Key.D, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => App.MemoryTrimmer?.TrimNow()), Key.M, ModifierKeys.Control | ModifierKeys.Alt));
    }

    private sealed class WeakRelayBindingHack : KeyBinding
    {
        public WeakRelayBindingHack(Window w) : base(new RelayCommand(_ => { }), Key.E, ModifierKeys.Alt | ModifierKeys.Shift)
        {
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await App.WebViewEnvironment.EnsureControllerAsync(WebView).ConfigureAwait(true);
            ConfigureWebView(WebView, Guid.Empty);
            _activeView = WebView;
            _webReady = true;
            var initial = ResolveStartupUrl();
            Navigate(initial);
            _vm.RefreshPaletteIndex();
        }
        catch (Exception ex)
        {
            MessageBox.Show("WebView2 initialization failed: " + ex.Message, "Swifter", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string ResolveStartupUrl()
    {
        if (App.StartupArgs != null && App.StartupArgs.Count > 0)
        {
            var arg = App.StartupArgs.FirstOrDefault(a => a.StartsWith("http") || a.Contains("://"));
            if (!string.IsNullOrEmpty(arg)) return arg;
        }
        try
        {
            var session = App.Sessions?.Load("default");
            var first = session?.Windows?.FirstOrDefault()?.Tabs?.FirstOrDefault(t => t.IsActive) ??
                        session?.Windows?.FirstOrDefault()?.Tabs?.FirstOrDefault();
            if (first != null && !string.IsNullOrEmpty(first.Url)) return first.Url;
        }
        catch
        {
        }
        return "swifter://newtab";
    }

    private void ConfigureWebView(WebView2 view, Guid tabId)
    {
        view.CoreWebView2.Settings.IsStatusBarEnabled = false;
        view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
        view.CoreWebView2.Settings.IsZoomControlEnabled = false;
        view.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = true;
        view.CoreWebView2.Settings.AreDevToolsEnabled = true;
        view.CoreWebView2.Settings.IsSwipeNavigationEnabled = true;
        view.CoreWebView2.Settings.PasswordAutofillEnabled = true;
        view.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
        view.CoreWebView2.Settings.IsGeneralAutofillEnabled = true;
        view.CoreWebView2.Settings.AreHostObjectsAllowed = true;

        try
        {
            var wv2 = view.CoreWebView2;
            wv2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        }
        catch
        {
        }

        view.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
        view.CoreWebView2.NavigationStarting += (s, e) =>
        {
            var clean = App.ParamStripper?.Strip(e.Uri) ?? e.Uri;
            if (!string.Equals(clean, e.Uri, StringComparison.OrdinalIgnoreCase) && e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                view.CoreWebView2.Navigate(clean);
                return;
            }
            _vm.IsLoading = true;
            _vm.CurrentUrl = clean;
            if (tabId != Guid.Empty) _vm.Workspace.UpdateTab(tabId, url: clean, loading: true);
            AddressBar.SetProgress(0.15);
        };
        view.CoreWebView2.ContentLoading += (s, e) =>
        {
            _vm.Progress = 0.4;
            AddressBar.SetProgress(0.4);
        };
        view.CoreWebView2.NavigationCompleted += async (s, e) =>
        {
            _vm.Progress = e.IsSuccess ? 1 : 0;
            _vm.IsLoading = false;
            AddressBar.SetProgress(e.IsSuccess ? 1 : 0);
            if (tabId != Guid.Empty) _vm.Workspace.UpdateTab(tabId, loading: false);
            if (!e.IsSuccess) return;
            var url = view.CoreWebView2.Source;
            var title = view.CoreWebView2.DocumentTitle;
            if (tabId != Guid.Empty)
            {
                _vm.Workspace.UpdateTab(tabId, title, url);
                App.History?.AddVisit(url, title, "link");
                App.Hibernator?.Touch(tabId);
            }
            _vm.CurrentUrl = url;
            _vm.CurrentTitle = title;
            AddressBar.SetUrl(url);
            StatusLeft.Text = title;
            try
            {
                var host = new Uri(url).Host;
                _shieldsVm.OnHostChanged(host);
                _networkFilter.CurrentHost = host;
                var css = _cosmeticInjector.GetStyleSheet(host);
                if (!string.IsNullOrEmpty(css))
                {
                    await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(_cosmeticInjector.BuildInjectionScript(host));
                }
            }
            catch
            {
            }
        };
        view.CoreWebView2.DocumentTitleChanged += (s, e) =>
        {
            var title = view.CoreWebView2.DocumentTitle;
            _vm.CurrentTitle = title;
            if (tabId != Guid.Empty) _vm.Workspace.UpdateTab(tabId, title: title);
        };
        view.CoreWebView2.SourceChanged += (s, e) =>
        {
            var url = view.CoreWebView2.Source;
            _vm.CurrentUrl = url;
            AddressBar.SetUrl(url);
            if (tabId != Guid.Empty) _vm.Workspace.UpdateTab(tabId, url: url);
        };
        view.CoreWebView2.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            var uri = e.Uri;
            Application.Current.Dispatcher.Invoke(() => NewTab(uri));
        };
        view.CoreWebView2.ContextMenuRequested += (s, e) =>
        {
            try
            {
                if (e.ResourceContext == CoreWebView2WebResourceContext.Image ||
                    (!string.IsNullOrEmpty(e.SourceUri) && LooksLikeImage(e.SourceUri)))
                {
                    _lensSourceUrl = e.SourceUri;
                }
            }
            catch
            {
            }
        };
        view.CoreWebView2.WebMessageReceived += (s, e) => OnWebMessage(tabId, e.WebMessageAsJson);
        view.CoreWebView2.PermissionRequested += (s, e) =>
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Notifications)
            {
                e.State = CoreWebView2PermissionState.Deny;
                return;
            }
            if (e.PermissionKind == CoreWebView2PermissionKind.Geolocation ||
                e.PermissionKind == CoreWebView2PermissionKind.Camera ||
                e.PermissionKind == CoreWebView2PermissionKind.Microphone)
            {
                e.State = CoreWebView2PermissionState.Ask;
                return;
            }
            e.State = CoreWebView2PermissionState.Allow;
        };
        view.CoreWebView2.ProcessFailed += (s, e) =>
        {
            Overlay.ShowToast("Renderer process failed — recovering…", "⚠");
            try { view.CoreWebView2.Reload(); } catch { }
        };
        view.CoreWebView2.ServerCertificateErrorDetected += (s, e) =>
        {
            e.Cancel = true;
            Overlay.ShowToast("Certificate warning for " + new Uri(e.ServerUri).Host, "⚠");
        };

        view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(App.FingerprintProtector?.BuildScript() ?? "");
        view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(App.WebRtcProtection?.BuildScript() ?? "");
        view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(new ScriptBlocker().BuildPageGuardScript());
        view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(App.MediaOverlay?.BuildOverlayScript() ?? "");
        view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(GoogleLensIntegrationHook());
        view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
window.addEventListener('message',function(e){
if(e.data&&e.data.type==='swifter-media-download'){
try{chrome.webview.postMessage(e.data);}catch(err){}
}
});");
    }

    private static bool LooksLikeImage(string url)
    {
        try
        {
            var ext = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".avif";
        }
        catch
        {
            return false;
        }
    }

    private static string GoogleLensIntegrationHook()
    {
        return @"
(function(){
if(window.__swifter_ctx__)return;window.__swifter_ctx__=true;
document.addEventListener('contextmenu',function(e){
var t=e.target;
if(t&&t.tagName==='IMG'&&t.src){
window.__swifter_lens_src__=t.src;
}
},true);
})();";
    }

    private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = e.Request.Uri;
            if (string.IsNullOrEmpty(uri) || !uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

            var host = AdBlockEngine.SafeHost(uri);
            var type = NetworkRequestFilter.MapResourceType(e.ResourceContext);

            if (_shieldsVm.ShieldsUp && App.AdBlock != null && App.AdBlock.ShouldBlock(uri, type, _networkFilter.CurrentHost))
            {
                var env = sender is CoreWebView2 core ? core.Environment : null;
                if (env != null)
                {
                    var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("blocked"));
                    e.Response = env.CreateWebResourceResponse(stream, 403, "Blocked by Swifter", "Content-Type: text/plain\r\nX-Swifter-Blocked: 1");
                    Dispatcher.Invoke(() =>
                    {
                        _shieldsVm.NetworkBlocked = App.AdBlock.Stats.NetworkBlocked;
                        BlockedStatus.Text = "🛡 " + _shieldsVm.TotalBlocked;
                    });
                }
                return;
            }

            _sniffer.InspectResponse(uri, e.Response?.Headers?.GetHeader("Content-Type"), 0, _activeView?.CoreWebView2?.Source);
        }
        catch
        {
        }
    }

    private void OnWebMessage(Guid tabId, string json)
    {
        try
        {
            App.MediaOverlay?.HandleWebMessage(json, asset =>
            {
                Dispatcher.Invoke(() =>
                {
                    Overlay.ShowToast("Media detected: " + (asset.Title ?? asset.Url), "🎬");
                    _downloadEngine.EnqueueAsync(asset.Url, asset.PageUrl, Path.GetFileName(new Uri(asset.Url).AbsolutePath), true, asset.Type);
                });
            });
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();
            if (type == "swifter-lens-target" && doc.RootElement.TryGetProperty("src", out var src))
            {
                _lensSourceUrl = src.GetString();
            }
            else if (type == "swifter-request")
            {
                var key = doc.RootElement.TryGetProperty("key", out var k) ? k.GetString() : "";
                HandleInternalDataRequest(tabId, key);
            }
            else if (type == "swifter-command")
            {
                var cmd = doc.RootElement.TryGetProperty("cmd", out var c) ? c.GetString() : "";
                HandleInternalCommand(cmd, doc.RootElement);
            }
            else if (type == "swifter-navigate" && doc.RootElement.TryGetProperty("url", out var u))
            {
                Navigate(u.GetString());
            }
            else if (type == "swifter-media-download")
            {
                App.MediaOverlay?.HandleWebMessage(json, asset => { });
            }
            else if (type == "swifter-webrtc-leak" && doc.RootElement.TryGetProperty("detail", out var detail))
            {
                App.WebRtcProtection?.ReportLeak(detail.ToString());
                Dispatcher.Invoke(() => Overlay.ShowToast("WebRTC leak candidate detected", "⚠"));
            }
        }
        catch
        {
        }
    }

    private void HandleInternalDataRequest(Guid tabId, string key)
    {
        var view = ResolveView(tabId);
        if (view?.CoreWebView2 == null) return;
        void Send(string k, object v)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new { type = "swifter-data", key = k, value = v });
                view.CoreWebView2.PostWebMessageAsJson(payload);
            }
            catch
            {
            }
        }
        switch (key)
        {
            case "init":
                Send("downloads", BuildDownloadsPayload());
                Send("shield-stats", BuildShieldStatsPayload());
                break;
            case "downloads":
                Send("downloads", BuildDownloadsPayload());
                break;
            case "shield-stats":
                Send("shield-stats", BuildShieldStatsPayload());
                break;
            case "suggestions":
                var suggestions = App.History?.GetSuggestions("", 8)?.Select(h => new { title = h.Title, url = h.Url }).ToList() ?? new List<object>() { };
                Send("suggestions", ((IEnumerable<object>)suggestions).ToList());
                break;
            case "perf":
                Send("perf", BuildPerfPayload());
                break;
            case "webrtc-ips":
                Send("webrtc-ips", BuildWebRtcPayload());
                break;
            case "history":
                Send("history", null);
                break;
        }
    }

    private object BuildDownloadsPayload()
    {
        return _downloadEngine.GetAll().Select(d => new
        {
            id = d.Id,
            name = d.FileName,
            url = d.Url,
            path = d.SavePath,
            received = d.ReceivedBytes,
            total = d.TotalBytes,
            speed = d.SpeedBytesPerSec,
            state = (int)d.State,
            eta = d.EstimatedRemaining?.TotalSeconds,
            segCount = d.SegmentCount,
            throttle = d.ThrottleBytesPerSec,
            completedAt = d.CompletedUtc,
            segments = d.Segments.Select(s => new
            {
                total = s.End - s.Start + 1,
                received = s.Received,
                completed = s.Completed
            }).ToList()
        }).ToList();
    }

    private object BuildShieldStatsPayload()
    {
        var stats = App.AdBlock?.GetStatsSnapshot();
        return new
        {
            network = stats?.NetworkBlocked ?? 0,
            cosmetic = stats?.CosmeticHidden ?? 0,
            trackers = App.ParamStripper?.TotalStripped ?? 0,
            rules = App.AdBlock?.LoadedRuleCount ?? 0,
            enabled = _shieldsVm.ShieldsUp
        };
    }

    private object BuildPerfPayload()
    {
        var usage = App.Cache?.GetUsageSnapshot() ?? (0L, 0L, 0L);
        return new
        {
            workingSet = Math.Round(App.MemoryTrimmer?.GetWorkingSetMb() ?? 0, 1),
            processes = App.MemoryTrimmer == null ? 0 : System.Diagnostics.Process.GetProcessesByName("Swifter").Length,
            cache = Math.Round(usage.DiskBytes / (1024.0 * 1024.0), 1),
            db = Math.Round(usage.DbBytes / (1024.0 * 1024.0), 1)
        };
    }

    private object BuildWebRtcPayload()
    {
        var ips = App.WebRtcProtection?.GetLocalIpsSnapshot() ?? Array.Empty<System.Net.IPAddress>();
        return ips.Select(i => new { addr = i.ToString(), local = System.Net.IPAddress.IsLoopback(i) || IsPrivate(i) }).ToList();
    }

    private static bool IsPrivate(System.Net.IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10 || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] == 192 && bytes[1] == 168);
    }

    private void HandleInternalCommand(string cmd, JsonElement root)
    {
        switch (cmd)
        {
            case "history-search":
                var q = root.TryGetProperty("payload", out var p) && p.TryGetProperty("q", out var qq) ? qq.GetString() : "";
                var results = App.History?.Search(q ?? "", 300) ?? new List<HistoryEntry>();
                PostData("history", results.Select(h => new { id = h.Id, title = h.Title, url = h.Url, time = h.VisitedAt, visits = h.VisitCount }).ToList());
                PostData("historyTotal", results.Count);
                PostData("historySummary", results.Count + " results for '" + (q ?? "") + "'");
                var chart = App.History?.GetDailyCounts(30) ?? new List<(string Day, int Count)>();
                PostData("historyChart", chart.Select(c => new { day = c.Day, count = c.Count }).ToList());
                break;
            case "hist-delete":
                if (root.TryGetProperty("payload", out var dp) && dp.TryGetProperty("id", out var did))
                    App.History?.Delete(did.GetInt64());
                HandleInternalDataRequest(Guid.Empty, "history-search");
                break;
            case "bookmarks-tree":
                PostData("bookmarksTree", BuildBookmarkTree());
                break;
            case "bookmarks-list":
                var folderId = 0L;
                var search = "";
                if (root.TryGetProperty("payload", out var bp))
                {
                    if (bp.TryGetProperty("id", out var fid)) folderId = fid.GetInt64();
                    if (bp.TryGetProperty("q", out var bq)) search = bq.GetString() ?? "";
                }
                var nodes = string.IsNullOrEmpty(search)
                    ? App.Bookmarks?.GetChildren(folderId) ?? new List<BookmarkNode>()
                    : App.Bookmarks?.Search(search) ?? new List<BookmarkNode>();
                PostData("bookmarks", nodes.Where(n => !n.IsFolder).Select(n => new { id = n.Id, title = n.Title, url = n.Url }).ToList());
                break;
            case "bm-open":
                if (root.TryGetProperty("payload", out var op) && op.TryGetProperty("url", out var ou)) Navigate(ou.GetString());
                break;
            case "bm-delete":
                if (root.TryGetProperty("payload", out var bp2) && bp2.TryGetProperty("id", out var bid)) App.Bookmarks?.Delete(bid.GetInt64());
                HandleInternalDataRequest(Guid.Empty, "bookmarks-list");
                HandleInternalDataRequest(Guid.Empty, "bookmarks-tree");
                break;
            case "dl-pause":
                if (root.TryGetProperty("payload", out var pp) && pp.TryGetProperty("id", out var pid) && pid.TryGetGuid(out var pg)) _downloadEngine.Pause(pg);
                break;
            case "dl-resume":
                if (root.TryGetProperty("payload", out var rp) && rp.TryGetProperty("id", out var rid) && rid.TryGetGuid(out var rg)) _downloadEngine.Resume(rg);
                break;
            case "dl-cancel":
                if (root.TryGetProperty("payload", out var cp) && cp.TryGetProperty("id", out var cid) && cid.TryGetGuid(out var cg)) _downloadEngine.Cancel(cg);
                break;
            case "dl-pause-all":
                foreach (var d in _downloadEngine.GetAll().Where(x => x.State == DownloadState.Downloading)) _downloadEngine.Pause(d.Id);
                break;
            case "dl-resume-all":
                foreach (var d in _downloadEngine.GetAll().Where(x => x.State == DownloadState.Paused)) _downloadEngine.Resume(d.Id);
                break;
            case "dl-open-folder":
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _downloadEngine.DownloadDirectory, UseShellExecute = true }); } catch { }
                break;
            case "shields-toggle":
                _shieldsVm.ShieldsUp = !_shieldsVm.ShieldsUp;
                break;
            case "shields-pause-host":
                _networkFilter.PauseHost(_shieldsVm.CurrentHost);
                break;
            case "shields-resume":
                _networkFilter.ResumeHost(_shieldsVm.CurrentHost);
                break;
            case "rule-add":
                if (root.TryGetProperty("payload", out var rlp) && rlp.TryGetProperty("value", out var rv) && !string.IsNullOrWhiteSpace(rv.GetString()))
                    App.AdBlock?.AddCustomRule(rv.GetString());
                break;
            case "trim-now":
                App.MemoryTrimmer?.TrimNow();
                break;
            case "clear-data":
                App.Cache?.ClearBrowsingData(true, true, true, false);
                Overlay.ShowToast("Browsing data cleared", "🧹");
                break;
            case "set-theme":
                if (root.TryGetProperty("payload", out var thp) && thp.TryGetProperty("value", out var thv) && Enum.TryParse<AppTheme>(thv.GetString(), out var th))
                    App.ThemeManager?.SetTheme(th);
                break;
            case "set-accent":
                if (root.TryGetProperty("payload", out var ap) && ap.TryGetProperty("value", out var av))
                    App.ThemeManager?.SetAccent(av.GetString());
                break;
            case "toggle-mica":
                App.ThemeManager?.SetBackdrop(!(App.ThemeManager.Settings.UseMica));
                NativeGlassEngine.Apply(this, App.ThemeManager.ActiveBackdrop, App.ThemeManager.IsDark);
                break;
            case "palette":
                OpenPalette();
                break;
            case "perf":
                HandleInternalDataRequest(Guid.Empty, "perf");
                break;
        }
    }

    private void PostData(string key, object value)
    {
        var view = _activeView;
        if (view?.CoreWebView2 == null) return;
        try
        {
            var payload = JsonSerializer.Serialize(new { type = "swifter-data", key, value });
            view.CoreWebView2.PostWebMessageAsJson(payload);
        }
        catch
        {
        }
    }

    private List<object> BuildBookmarkTree()
    {
        var tree = App.Bookmarks?.GetTree() ?? new List<BookmarkNode>();
        var result = new List<object>();
        void Walk(List<BookmarkNode> nodes, int depth)
        {
            foreach (var n in nodes)
            {
                result.Add(new { id = n.Id, title = n.Title, url = n.Url, folder = n.IsFolder, depth, children = n.Children.Count });
                if (n.Children.Count > 0) Walk(n.Children, depth + 1);
            }
        }
        Walk(tree, 0);
        return result;
    }

    private void HandleInternalDataRequestFull(Guid tabId, string key)
    {
        HandleInternalDataRequest(tabId, key);
    }

    private WebView2 ResolveView(Guid tabId)
    {
        if (tabId == Guid.Empty) return _activeView;
        return _tabViews.TryGetValue(tabId, out var v) ? v : _activeView;
    }

    private void Navigate(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        url = App.ParamStripper?.Strip(url) ?? url;
        _vm.CurrentUrl = url;
        AddressBar.SetUrl(url, updateBox: true);
        StatusLeft.Text = "Loading " + url;

        if (url.StartsWith("swifter://", StringComparison.OrdinalIgnoreCase))
        {
            LoadInternalPage(url);
            return;
        }

        if (_activeView == null || !_webReady)
        {
            _vm.NewTab(url);
            return;
        }

        if (_activeView == WebView && _vm.Workspace.ActiveTabId != Guid.Empty && _tabViews.TryGetValue(_vm.Workspace.ActiveTabId, out var other))
        {
            ActivateTabView(_vm.Workspace.ActiveTabId);
        }
        _activeView.CoreWebView2?.Navigate(url);
    }

    private void LoadInternalPage(string url)
    {
        try
        {
            var handler = new CustomSchemeHandler();
            var response = handler.Handle(url);
            var html = System.Text.Encoding.UTF8.GetString(response.Content);
            if (_activeView?.CoreWebView2 != null)
            {
                _activeView.CoreWebView2.NavigateToString(html);
            }
            else
            {
                WebView.CoreWebView2?.NavigateToString(html);
            }
        }
        catch (Exception ex)
        {
            StatusLeft.Text = "Internal page error: " + ex.Message;
        }
    }

    private async Task AttachTabAsync(Guid tabId, string url)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            var view = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            SingleHostGrid.Children.Add(view);
            _tabViews[tabId] = view;
            try
            {
                await App.WebViewEnvironment.EnsureControllerAsync(view).ConfigureAwait(true);
                ConfigureWebView(view, tabId);
                _tabCores[tabId] = view.CoreWebView2;
                if (url.StartsWith("swifter://"))
                {
                    var response = new CustomSchemeHandler().Handle(url);
                    view.CoreWebView2.NavigateToString(System.Text.Encoding.UTF8.GetString(response.Content));
                }
                else
                {
                    view.CoreWebView2.Navigate(url);
                }
                ActivateTabView(tabId);
            }
            catch (Exception ex)
            {
                Overlay.ShowToast("Tab failed: " + ex.Message, "⚠");
            }
        });
    }

    private void ActivateTabView(Guid tabId)
    {
        if (!_tabViews.TryGetValue(tabId, out var view) && tabId != Guid.Empty) return;
        if (tabId == Guid.Empty) view = WebView;
        foreach (var kv in _tabViews)
        {
            kv.Value.Visibility = kv.Key == tabId ? Visibility.Visible : Visibility.Collapsed;
            kv.Value.IsEnabled = kv.Key == tabId;
        }
        if (tabId == Guid.Empty)
        {
            WebView.Visibility = _tabViews.ContainsKey(tabId) ? Visibility.Collapsed : Visibility.Visible;
        }
        _activeView = view;
        _activeView.Visibility = Visibility.Visible;
        _vm.Workspace.ActivateTab(tabId);
        if (_tabCores.TryGetValue(tabId, out var core))
        {
            AddressBar.SetUrl(core.Source);
            StatusLeft.Text = core.DocumentTitle;
        }
    }

    private void NewTab(string url = "swifter://newtab")
    {
        var tab = _vm.NewTab(url);
        _vm.Workspace.ActivateTab(tab.Id);
        _ = AttachTabAsync(tab.Id, url);
    }

    private void CloseActiveTab()
    {
        var id = _vm.Workspace.ActiveTabId;
        if (id == Guid.Empty) return;
        CloseTabInternal(id);
    }

    private void CloseTabInternal(Guid id)
    {
        if (_tabViews.TryGetValue(id, out var view))
        {
            SingleHostGrid.Children.Remove(view);
            view.Dispose();
            _tabViews.Remove(id);
            _tabCores.Remove(id);
        }
        App.Hibernator?.Forget(id);
        _vm.CloseTab(id);
        var next = _vm.Workspace.ActiveTab;
        if (next != null && _tabViews.TryGetValue(next.Id, out var nv))
        {
            ActivateTabView(next.Id);
        }
        else if (_vm.Tabs.Count == 0)
        {
            _activeView = WebView;
            WebView.Visibility = Visibility.Visible;
        }
    }

    private void OnTabActivated(object sender, TabItemModel tab)
    {
        _vm.Workspace.ActivateTab(tab.Id);
        if (_tabViews.ContainsKey(tab.Id)) ActivateTabView(tab.Id);
        else _ = AttachTabAsync(tab.Id, tab.Url);
    }

    private void OnTabClose(object sender, TabItemModel tab)
    {
        CloseTabInternal(tab.Id);
    }

    private void OnNewTab(object sender, EventArgs e) => NewTab();
    private void OnBack(object sender, EventArgs e) { if (_activeView?.CoreWebView2?.CanGoBack == true) _activeView.CoreWebView2.GoBack(); }
    private void OnForward(object sender, EventArgs e) { if (_activeView?.CoreWebView2?.CanGoForward == true) _activeView.CoreWebView2.GoForward(); }
    private void OnReload(object sender, EventArgs e) => Reload();
    private void OnHome(object sender, EventArgs e) => Navigate("swifter://newtab");

    private void Reload()
    {
        if (_activeView?.CoreWebView2 == null) return;
        if (_activeView.CoreWebView2.Source.StartsWith("swifter://")) LoadInternalPage(_activeView.CoreWebView2.Source);
        else _activeView.CoreWebView2.Reload();
    }

    private void ReloadHard()
    {
        try { _activeView?.CoreWebView2?.Reload(); } catch { }
    }

    private void OnAddressNavigation(object sender, string text) => _vm.OnUrlNavigation(text);

    private void OnShieldClick(object sender, EventArgs e)
    {
        Navigate("swifter://shields?host=" + Uri.EscapeDataString(_shieldsVm.CurrentHost ?? ""));
    }

    private async void OnStarClick(object sender, EventArgs e)
    {
        var url = _vm.CurrentUrl;
        var title = _vm.CurrentTitle;
        if (string.IsNullOrEmpty(url) || !url.StartsWith("http")) return;
        if (App.Bookmarks?.IsBookmarked(url) == true)
        {
            App.Bookmarks.RemoveByUrl(url);
            AddressBar.SetStarred(false);
            Overlay.ShowToast("Bookmark removed", "★");
        }
        else
        {
            App.Bookmarks?.AddBookmark(title, url, 1);
            AddressBar.SetStarred(true);
            Overlay.ShowToast("Bookmarked", "★");
        }
        await Task.CompletedTask;
    }

    private async void OnReaderClick(object sender, EventArgs e)
    {
        await OpenReaderAsync();
    }

    private void OnCopyCleanLink(object sender, EventArgs e)
    {
        var clean = App.ParamStripper?.Strip(_vm.CurrentUrl) ?? _vm.CurrentUrl;
        clean = App.ParamStripper?.SanitizeClipboard(clean) ?? clean;
        try
        {
            Clipboard.SetText(clean);
            Overlay.ShowToast("Clean link copied", "⧉");
        }
        catch
        {
        }
    }

    private async void OnMediaDownloadClick(object sender, EventArgs e)
    {
        var assets = _sniffer.GetAssetsForPage(_vm.CurrentUrl);
        if (assets.Count == 0)
        {
            _activeView?.CoreWebView2?.ExecuteScriptAsync(App.MediaOverlay?.BuildVideoProbeScript() ?? "");
            Overlay.ShowToast("Scanning page for media…", "🎬");
            return;
        }
        var best = assets.OrderByDescending(a => a.SizeBytes).First();
        await _downloadEngine.EnqueueAsync(best.Url, _vm.CurrentUrl, null, true, best.Type);
        Overlay.ShowToast("Downloading " + best.Type + " (" + best.Quality + ")", "⬇");
        Navigate("swifter://downloads");
    }

    private async void OnLensClick(object sender, EventArgs e)
    {
        var lens = new GoogleLensIntegration();
        if (!string.IsNullOrEmpty(_lensSourceUrl) && _lensSourceUrl.StartsWith("http"))
        {
            var url = lens.BuildLensUrl(_lensSourceUrl);
            Navigate(url);
            Overlay.ShowToast("Google Lens search opened", "◫");
        }
        else
        {
            Navigate(lens.BuildLensPasteUrl());
        }
        await Task.CompletedTask;
    }

    private async Task OpenReaderAsync()
    {
        try
        {
            var html = await _activeView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            var decoded = JsonSerializer.Deserialize<string>(html) ?? "";
            var extractor = new ArticleExtractor();
            var article = await Task.Run(() => extractor.ExtractFromHtml(decoded, _vm.CurrentUrl));
            if (article.WordCount < 60)
            {
                Overlay.ShowToast("No readable article found on this page", "📖");
                return;
            }
            var reader = new ReaderWindow(article);
            reader.Show();
        }
        catch (Exception ex)
        {
            Overlay.ShowToast("Reader failed: " + ex.Message, "⚠");
        }
    }

    private void OpenReader() => _ = OpenReaderAsync();

    private void OnMenu(object sender, EventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu();
        void Add(string header, Action action)
        {
            var item = new System.Windows.Controls.MenuItem { Header = header };
            item.Click += (s, e) => action();
            menu.Items.Add(item);
        }
        Add("New Tab", () => NewTab());
        Add("New Window", () => NewTab());
        Add("History", () => Navigate("swifter://history"));
        Add("Bookmarks", () => Navigate("swifter://bookmarks"));
        Add("Downloads", () => Navigate("swifter://downloads"));
        Add("Shields", () => Navigate("swifter://shields"));
        Add("Settings", () => Navigate("swifter://settings"));
        Add("Reader Mode", OpenReader);
        Add("Command Palette", OpenPalette);
        Add("Trim Memory", () => App.MemoryTrimmer?.TrimNow());
        menu.PlacementTarget = MenuNav;
        menu.IsOpen = true;
    }

    private void OpenPalette()
    {
        _vm.RefreshPaletteIndex();
        _vm.Palette.Open();
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteOverlay.ActivatePalette();
    }

    private void OnPaletteClick(object sender, RoutedEventArgs e) => OpenPalette();

    private void OnPaletteResult(PaletteResult result)
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        if (result?.Payload is string action && action.StartsWith("http"))
        {
            Navigate(action);
        }
        else if (result?.Payload is string action2)
        {
            switch (action2)
            {
                case "newtab": NewTab(); break;
                case "closetab": CloseActiveTab(); break;
                case "history": Navigate("swifter://history"); break;
                case "bookmarks": Navigate("swifter://bookmarks"); break;
                case "downloads": Navigate("swifter://downloads"); break;
                case "settings": Navigate("swifter://settings"); break;
                case "shields": Navigate("swifter://shields"); break;
                case "togglevertical": ToggleVerticalTabs(); break;
                case "split2": _vm.Workspace.SetSplit(SplitMode.Columns2); ApplySplitLayout(); break;
                case "togglepanels": TogglePanels(); break;
                case "toggleadblock": _shieldsVm.ShieldsUp = !_shieldsVm.ShieldsUp; break;
                case "trimmemory": App.MemoryTrimmer?.TrimNow(); Overlay.ShowToast("Working set trimmed", "🧠"); break;
                case "pip": _vm.Pip.Toggle(); break;
                case "cleardata": App.Cache?.ClearBrowsingData(true, true, true, false); Overlay.ShowToast("Data cleared", "🧹"); break;
                case "reader": OpenReader(); break;
            }
        }
        else if (result?.Payload is Guid tabId)
        {
            _vm.Workspace.ActivateTab(tabId);
            if (_tabViews.ContainsKey(tabId)) ActivateTabView(tabId);
            else if (_vm.Tabs.FirstOrDefault(t => t.Id == tabId) is TabItemModel tab) _ = AttachTabAsync(tabId, tab.Url);
        }
    }

    private void OnFindClick(object sender, RoutedEventArgs e) => ToggleFindBar();

    private void ToggleFindBar()
    {
        FindBar.Visibility = FindBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (FindBar.Visibility == Visibility.Visible) FindBox.Focus();
    }

    private async void OnFindKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _activeView?.CoreWebView2 == null) return;
        var forward = !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var term = FindBox.Text;
        if (string.IsNullOrEmpty(term)) return;
        if (forward) await _activeView.CoreWebView2.FindAsync(term, CoreWebView2FindOptions.None);
        else await _activeView.CoreWebView2.FindAsync(term, CoreWebView2FindOptions.SearchBackwards);
    }

    private async void OnFindNext(object sender, RoutedEventArgs e)
    {
        if (_activeView?.CoreWebView2 == null) return;
        await _activeView.CoreWebView2.FindAsync(FindBox.Text, CoreWebView2FindOptions.None);
    }

    private async void OnFindPrev(object sender, RoutedEventArgs e)
    {
        if (_activeView?.CoreWebView2 == null) return;
        await _activeView.CoreWebView2.FindAsync(FindBox.Text, CoreWebView2FindOptions.SearchBackwards);
    }

    private void OnFindClose(object sender, RoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Collapsed;
        _activeView?.CoreWebView2?.StopFinding(false);
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleFindBar();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenPalette();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            NewTab();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CloseActiveTab();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusAddress();
            e.Handled = true;
            return;
        }
        if (_vm.Gestures.Enabled) { }
        App.Hotkeys?.HandleKey(e);
    }

    private void FocusAddress() => AddressBar.FocusAddressBar();

    private void OnVerticalLayoutToggled(object sender, bool on)
    {
        _verticalLayout = on;
        VerticalTabRail.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TopTabStrip.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        TopTabStrip.SetVerticalChecked(on);
        VerticalTabStrip.SetVerticalChecked(on);
        _vm.IsVerticalTabs = on;
    }

    private void OnSplitToggled(object sender, bool on)
    {
        _vm.Workspace.SetSplit(on ? SplitMode.Columns2 : SplitMode.None);
        ApplySplitLayout();
    }

    private void ApplySplitLayout()
    {
        var mode = _vm.Workspace.SplitMode;
        SplitHostGrid.Visibility = mode == SplitMode.None ? Visibility.Collapsed : Visibility.Visible;
        SingleHostGrid.Visibility = mode == SplitMode.None ? Visibility.Visible : Visibility.Collapsed;
        TopTabStrip.SetSplitChecked(mode != SplitMode.None);
        VerticalTabStrip.SetSplitChecked(mode != SplitMode.None);
        if (mode == SplitMode.None && _activeView != null)
        {
            _activeView.Visibility = Visibility.Visible;
        }
    }

    private void ToggleVerticalTabs()
    {
        var on = !_verticalLayout;
        OnVerticalLayoutToggled(null, on);
    }

    private void TogglePanels()
    {
        _vm.PanelsVisible = !_vm.PanelsVisible;
        WebPanelHost.Visibility = _vm.PanelsVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPanelCollapse(object sender, EventArgs e) => WebPanelHost.Visibility = Visibility.Collapsed;

    private void OnPanelActivated(object sender, WebPanelItem panel)
    {
        Overlay.ShowToast("Panel: " + panel.Title, "▤");
        _ = AttachFloatingPanel(panel);
    }

    private async Task AttachFloatingPanel(WebPanelItem panel)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            var view = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            PanelWebViewHost.Children.Clear();
            PanelWebViewHost.Children.Add(view);
            PanelWebViewHost.Visibility = Visibility.Visible;
            try
            {
                await App.WebViewEnvironment.EnsureControllerAsync(view).ConfigureAwait(true);
                view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                view.CoreWebView2.Settings.IsStatusBarEnabled = false;
                view.CoreWebView2.Navigate(panel.Url);
            }
            catch (Exception ex)
            {
                Overlay.ShowToast("Panel failed: " + ex.Message, "⚠");
            }
        });
    }

    private void OnAddPanel(object sender, EventArgs e)
    {
        var input = Microsoft.Win32.OpenFileDialog.DefaultExt;
        WebPanel.AddPanel("Custom", "https://example.com");
    }

    private void OnBrowserMouseMove(object sender, MouseEventArgs e)
    {
        if (!_gestureTracking) return;
        _vm.Gestures.OnMouseMove(e.GetPosition(BrowserArea));
    }

    private void OnBrowserMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed)
        {
            _gestureTracking = true;
            _gestureStart = e.GetPosition(BrowserArea);
            _vm.Gestures.OnMouseDown(_gestureStart);
        }
    }

    private void OnBrowserMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_gestureTracking) return;
        _gestureTracking = false;
        _vm.Gestures.OnMouseUp(e.GetPosition(BrowserArea));
    }

    private void OnBrowserRightDown(object sender, MouseButtonEventArgs e)
    {
        _gestureTracking = true;
        _gestureStart = e.GetPosition(BrowserArea);
        _vm.Gestures.OnMouseDown(_gestureStart);
    }

    private async void OnBrowserRightUp(object sender, MouseButtonEventArgs e)
    {
        if (!_gestureTracking) return;
        _gestureTracking = false;
        _vm.Gestures.OnMouseUp(e.GetPosition(BrowserArea));
        await Task.CompletedTask;
    }

    private void BookmarkPage()
    {
        OnStarClick(null, null);
    }

    private async void OnFrameRender(object sender, EventArgs e)
    {
        if (_shuttingDown) return;
        var now = Environment.TickCount64;
        if (now - _lastHeavyTick < 4000) return;
        _lastHeavyTick = now;
        await Dispatcher.InvokeAsync(() =>
        {
            var wsMb = App.MemoryTrimmer?.GetWorkingSetMb() ?? 0;
            MemoryStatus.Text = wsMb.ToString("0") + " MB";
            Overlay.ShowMemory(wsMb.ToString("0") + " MB");
            if (_downloadsVm.ActiveCount > 0)
            {
                SpeedStatus.Text = _downloadsVm.AggregateSpeed;
                Overlay.ShowSpeed(_downloadsVm.AggregateSpeed);
            }
            else
            {
                SpeedStatus.Text = "";
                Overlay.HideSpeed();
            }
            StatusCenter.Text = _downloadsVm.BuildStatusLine();
        });
    }

    private void OnWindowStateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            RootChrome.CornerRadius = new CornerRadius(0);
        }
        else
        {
            RootChrome.CornerRadius = new CornerRadius(8);
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        TopTabStrip.Tabs = _vm.Tabs;
        VerticalTabStrip.Tabs = _vm.Tabs;
        TopTabStrip.NotifyThemeChanged();
        _vm.Gestures.GesturePreview += path => Dispatcher.Invoke(() => Overlay.ShowGesture(GestureRecognizer.Describe(
            Enum.TryParse<Core.Productivity.GestureAction>(
                path switch { "L" => "Back", "R" => "Forward", "D" => "CloseTab", "U" => "NewTab", "UR" => "OpenLinkNewTab", "DR" => "Reload", _ => "None" },
                out var act) ? act : Core.Productivity.GestureAction.None)));
        _vm.Gestures.GestureRecognized += g => Dispatcher.Invoke(() => Overlay.ShowGesture(g.RecognizedAs));
        MemoryStatus.Text = Math.Round(App.MemoryTrimmer?.GetWorkingSetMb() ?? 0) + " MB";
        if (!_vm.Tabs.Any())
        {
            NewTab(App.StartupArgs?.FirstOrDefault() ?? "swifter://newtab");
        }
    }

    private void OnWindowClosing(object sender, CancelEventArgs e)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        try
        {
            var tabs = _vm.Tabs.Select(t => new SessionTab
            {
                Url = t.Url,
                Title = t.Title,
                IsActive = t.IsActive,
                GroupId = t.GroupId,
                IsPinned = t.IsPinned
            }).ToList();
            App.Sessions?.SaveWindowState((int)Left, (int)Top, (int)Width, (int)Height, WindowState == WindowState.Maximized, tabs);
        }
        catch
        {
        }
        try
        {
            _downloadEngine?.Dispose();
            _downloadsVm?.Dispose();
            foreach (var kv in _tabViews)
            {
                try { kv.Value.Dispose(); } catch { }
            }
            WebView?.Dispose();
        }
        catch
        {
        }
    }

    public MainViewModel ViewModel => _vm;
    public ShieldsViewModel Shields => _shieldsVm;
    public SettingsViewModel SettingsVm => _settingsVm;
    public DownloadsViewModel DownloadsVm => _downloadsVm;
}
