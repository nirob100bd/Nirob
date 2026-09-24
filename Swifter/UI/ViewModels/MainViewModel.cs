using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Swifter.Core.Workspace;
using Swifter.Theme.CustomControls;

namespace Swifter.UI.ViewModels;

public class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Func<object, bool> _can;
    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public RelayCommand(Action<object> execute, Func<object, bool> can = null)
    {
        _execute = execute;
        _can = can;
    }
    public bool CanExecute(object parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object parameter) => _execute(parameter);
}

public sealed class MainViewModel : ObservableObject
{
    private string _currentUrl = "swifter://newtab";
    private string _currentTitle = "New Tab";
    private string _statusText = "Ready";
    private double _progress;
    private bool _isVerticalTabs;
    private bool _panelsVisible = true;
    private bool _shieldsEnabled = true;
    private bool _isLoading;
    private string _findQuery = "";
    private int _blockedCount;
    private SplitMode _splitMode = SplitMode.None;

    public ObservableCollection<TabItemModel> Tabs { get; } = new ObservableCollection<TabItemModel>();
    public WorkspaceManager Workspace { get; } = new WorkspaceManager();
    public GridSplitViewController SplitController { get; } = new GridSplitViewController();
    public TabGroupController Groups { get; } = new TabGroupController();
    public Core.Productivity.CommandPaletteEngine Palette { get; } = new Core.Productivity.CommandPaletteEngine();
    public Core.Productivity.GestureRecognizer Gestures { get; } = new Core.Productivity.GestureRecognizer();
    public Core.Productivity.PipWindowController Pip { get; } = new Core.Productivity.PipWindowController();
    public string CurrentUrl { get => _currentUrl; set => Set(ref _currentUrl, value); }
    public string CurrentTitle { get => _currentTitle; set => Set(ref _currentTitle, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public double Progress { get => _progress; set => Set(ref _progress, value); }
    public bool IsVerticalTabs { get => _isVerticalTabs; set => Set(ref _isVerticalTabs, value); }
    public bool PanelsVisible { get => _panelsVisible; set => Set(ref _panelsVisible, value); }
    public bool ShieldsEnabled { get => _shieldsEnabled; set => Set(ref _shieldsEnabled, value); }
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
    public string FindQuery { get => _findQuery; set => Set(ref _findQuery, value); }
    public int BlockedCount { get => _blockedCount; set => Set(ref _blockedCount, value); }
    public SplitMode SplitMode { get => _splitMode; set => Set(ref _splitMode, value); }

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand TogglePaletteCommand { get; }
    public ICommand ToggleSplitCommand { get; }
    public ICommand ToggleVerticalTabsCommand { get; }
    public ICommand TogglePanelsCommand { get; }
    public ICommand ToggleShieldsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenHistoryCommand { get; }
    public ICommand OpenBookmarksCommand { get; }
    public ICommand OpenDownloadsCommand { get; }
    public ICommand ClearDataCommand { get; }
    public ICommand TrimMemoryCommand { get; }
    public ICommand ReaderCommand { get; }
    public ICommand PipCommand { get; }
    public ICommand AddressFocusCommand { get; }
    public ICommand FindCommand { get; }
    public ICommand NextTabCommand { get; }
    public ICommand PrevTabCommand { get; }
    public ICommand ToggleFindCommand { get; }

    public event Action<string> NavigateRequested;
    public event Action ReloadRequested;
    public event Action BackRequested;
    public event Action ForwardRequested;
    public event Action FindRequested;
    public event Action FocusAddressRequested;

    public MainViewModel()
    {
        Workspace.TabAdded += t => App.Current?.Dispatcher.Invoke(() => { if (!Tabs.Contains(t)) Tabs.Add(t); });
        Workspace.TabRemoved += t => App.Current?.Dispatcher.Invoke(() => Tabs.Remove(t));
        Workspace.TabActivated += t => SyncActiveTab(t);
        Workspace.LayoutChanged += l => IsVerticalTabs = l == TabLayout.VerticalLeft;
        Workspace.SplitChanged += m => { SplitMode = m; SplitController.SetMode(m); OnPropertyChanged(nameof(SplitMode)); };

        NewTabCommand = new RelayCommand(_ => NewTab());
        CloseTabCommand = new RelayCommand(_ => CloseActiveTab(), _ => Tabs.Count > 0);
        ReloadCommand = new RelayCommand(_ => ReloadRequested?.Invoke());
        BackCommand = new RelayCommand(_ => BackRequested?.Invoke());
        ForwardCommand = new RelayCommand(_ => ForwardRequested?.Invoke());
        TogglePaletteCommand = new RelayCommand(_ => Palette.IsVisible ? Palette.Close() : Palette.Open());
        ToggleSplitCommand = new RelayCommand(_ => Workspace.CycleSplit());
        ToggleVerticalTabsCommand = new RelayCommand(_ => Workspace.ToggleLayout());
        TogglePanelsCommand = new RelayCommand(_ => PanelsVisible = !PanelsVisible);
        ToggleShieldsCommand = new RelayCommand(_ => ShieldsEnabled = !ShieldsEnabled);
        OpenSettingsCommand = new RelayCommand(_ => Navigate("swifter://settings"));
        OpenHistoryCommand = new RelayCommand(_ => Navigate("swifter://history"));
        OpenBookmarksCommand = new RelayCommand(_ => Navigate("swifter://bookmarks"));
        OpenDownloadsCommand = new RelayCommand(_ => Navigate("swifter://downloads"));
        ClearDataCommand = new RelayCommand(_ => { App.Cache?.ClearBrowsingData(history: true, cache: true, cookies: true); StatusText = "Browsing data cleared"; });
        TrimMemoryCommand = new RelayCommand(_ => App.MemoryTrimmer?.TrimNow());
        ReaderCommand = new RelayCommand(_ => FindRequested?.Invoke());
        PipCommand = new RelayCommand(_ => Pip.Toggle());
        AddressFocusCommand = new RelayCommand(_ => FocusAddressRequested?.Invoke());
        FindCommand = new RelayCommand(_ => FindRequested?.Invoke());
        ToggleFindCommand = new RelayCommand(_ => FindRequested?.Invoke());
        NextTabCommand = new RelayCommand(_ => SwitchTab(true));
        PrevTabCommand = new RelayCommand(_ => SwitchTab(false));

        Gestures.GestureRecognized += g => HandleGesture(g.Action);
        Palette.ResultActivated += r => HandlePaletteResult(r);
    }

    public TabItemModel NewTab(string url = "swifter://newtab")
    {
        var tab = Workspace.CreateTab(url, url == "swifter://newtab" ? "New Tab" : url);
        NavigateRequested?.Invoke(url);
        return tab;
    }

    public void CloseTab(Guid id)
    {
        Workspace.CloseTab(id);
        var active = Workspace.ActiveTab;
        if (active != null) NavigateRequested?.Invoke(active.Url);
    }

    private void CloseActiveTab()
    {
        if (Workspace.ActiveTabId != Guid.Empty) CloseTab(Workspace.ActiveTabId);
    }

    private void SyncActiveTab(TabItemModel tab)
    {
        if (tab == null) return;
        CurrentUrl = tab.Url;
        CurrentTitle = tab.Title;
        IsLoading = tab.IsLoading;
        App.Hibernator?.Touch(tab.Id);
    }

    private void Navigate(string url)
    {
        var clean = App.ParamStripper?.Strip(url) ?? url;
        NavigateRequested?.Invoke(clean);
    }

    private void HandleGesture(Core.Productivity.GestureAction action)
    {
        switch (action)
        {
            case Core.Productivity.GestureAction.Back: BackRequested?.Invoke(); break;
            case Core.Productivity.GestureAction.Forward: ForwardRequested?.Invoke(); break;
            case Core.Productivity.GestureAction.NewTab: NewTab(); break;
            case Core.Productivity.GestureAction.CloseTab: CloseActiveTab(); break;
            case Core.Productivity.GestureAction.Reload: ReloadRequested?.Invoke(); break;
            case Core.Productivity.GestureAction.ShowPalette: Palette.Open(); break;
            case Core.Productivity.GestureAction.OpenLinkNewTab: NewTab(); break;
            case Core.Productivity.GestureAction.NextTab: SwitchTab(true); break;
            case Core.Productivity.GestureAction.PrevTab: SwitchTab(false); break;
        }
    }

    public void SwitchTab(bool forward)
    {
        var next = Workspace.GetNextTab(forward);
        if (next != null)
        {
            Workspace.ActivateTab(next.Id);
            NavigateRequested?.Invoke(next.Url);
        }
    }

    public void SwitchTabByIndex(int index)
    {
        if (index < 0 || index >= Tabs.Count) return;
        var tab = Tabs[index];
        Workspace.ActivateTab(tab.Id);
        NavigateRequested?.Invoke(tab.Url);
    }

    private void HandlePaletteResult(Core.Productivity.PaletteResult result)
    {
        if (result == null) return;
        Palette.Close();
        if (result.Payload is string action)
        {
            switch (action)
            {
                case "newtab": NewTab(); break;
                case "closetab": CloseActiveTab(); break;
                case "newwindow": NewTab(); break;
                case "history": Navigate("swifter://history"); break;
                case "bookmarks": Navigate("swifter://bookmarks"); break;
                case "downloads": Navigate("swifter://downloads"); break;
                case "settings": Navigate("swifter://settings"); break;
                case "shields": Navigate("swifter://shields"); break;
                case "togglevertical": Workspace.ToggleLayout(); break;
                case "split2": Workspace.SetSplit(SplitMode.Columns2); break;
                case "togglepanels": PanelsVisible = !PanelsVisible; break;
                case "toggleadblock": ShieldsEnabled = !ShieldsEnabled; break;
                case "trimmemory": App.MemoryTrimmer?.TrimNow(); break;
                case "hibernateall": StatusText = "Background tabs hibernated"; break;
                case "pip": Pip.Toggle(); break;
                case "palette": Palette.Open(); break;
                case "cleardata": ClearDataCommand.Execute(null); break;
                default:
                    if (action.StartsWith("http") || action.StartsWith("swifter://")) Navigate(action);
                    break;
            }
        }
        else if (result.Payload is Guid tabId)
        {
            Workspace.ActivateTab(tabId);
            var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab != null) NavigateRequested?.Invoke(tab.Url);
        }
        else if (result.Payload is string url && (url.StartsWith("http") || url.StartsWith("swifter://")))
        {
            Navigate(url);
        }
    }

    public void OnUrlNavigation(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        string url;
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("swifter://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            url = input;
        }
        else if (Uri.TryCreate(input, UriKind.Absolute, out _) && input.Contains('.') && !input.Contains(' '))
        {
            url = "https://" + input;
        }
        else
        {
            url = "https://www.google.com/search?q=" + Uri.EscapeDataString(input);
        }
        var clean = App.ParamStripper?.Strip(url) ?? url;
        if (clean.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            App.History?.AddVisit(clean, clean, "typed");
        }
        NavigateRequested?.Invoke(clean);
    }

    public void UpdateProgress(double fraction)
    {
        Progress = fraction;
        IsLoading = fraction > 0 && fraction < 1;
    }

    public IReadOnlyList<TabItemModel> GetPaletteTabs() => Tabs.Select(t => (t.Title, t.Url, t.Id)).ToList();

    public void RefreshPaletteIndex()
    {
        Palette.IndexTabs(Tabs.Select(t => (t.Title, t.Url, t.Id)));
        try
        {
            var history = App.History?.GetSuggestions("", 100)?.Select(h => (h.Title, h.Url)) ?? Enumerable.Empty<(string, string)>();
            Palette.IndexHistory(history);
        }
        catch
        {
        }
        try
        {
            var bookmarks = App.Bookmarks?.Search("", 100)?.Select(b => (b.Title, b.Url)) ?? Enumerable.Empty<(string, string)>();
            Palette.IndexBookmarks(bookmarks);
        }
        catch
        {
        }
    }
}
