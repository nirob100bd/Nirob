using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using Swifter.Theme.CustomControls;

namespace Swifter.Core.Workspace;

public enum TabLayout
{
    HorizontalTop,
    VerticalLeft
}

public enum SplitMode
{
    None,
    Columns2,
    Grid2x2
}

public sealed class BrowserTabState
{
    public Guid TabId { get; set; }
    public string Url { get; set; }
    public string Title { get; set; }
    public string Favicon { get; set; }
    public string GroupId { get; set; }
    public bool IsPinned { get; set; }
    public bool IsContainer { get; set; }
    public string ContainerId { get; set; }
    public DateTime LastActiveUtc { get; set; }
    public bool Hibernated { get; set; }
    public int SplitIndex { get; set; } = -1;
}

public sealed class WorkspaceManager : INotifyPropertyChanged
{
    private TabLayout _layout = TabLayout.HorizontalTop;
    private SplitMode _splitMode = SplitMode.None;
    private bool _panelsVisible = true;
    private Guid _activeTabId;

    public ObservableCollection<TabItemModel> Tabs { get; } = new ObservableCollection<TabItemModel>();
    public ObservableCollection<BrowserTabState> TabStates { get; } = new ObservableCollection<BrowserTabState>();
    public TabLayout Layout
    {
        get => _layout;
        set { _layout = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsVertical)); }
    }
    public bool IsVertical => Layout == TabLayout.VerticalLeft;
    public SplitMode SplitMode
    {
        get => _splitMode;
        set { _splitMode = value; OnPropertyChanged(); }
    }
    public bool PanelsVisible
    {
        get => _panelsVisible;
        set { _panelsVisible = value; OnPropertyChanged(); }
    }
    public Guid ActiveTabId
    {
        get => _activeTabId;
        set { _activeTabId = value; OnPropertyChanged(); }
    }

    public event Action<TabItemModel> TabAdded;
    public event Action<TabItemModel> TabRemoved;
    public event Action<TabItemModel> TabActivated;
    public event Action<TabLayout> LayoutChanged;
    public event Action<SplitMode> SplitChanged;
    public event PropertyChangedEventHandler PropertyChanged;

    public TabItemModel ActiveTab => Tabs.FirstOrDefault(t => t.Id == ActiveTabId);

    public void ToggleLayout()
    {
        Layout = Layout == TabLayout.HorizontalTop ? TabLayout.VerticalLeft : TabLayout.HorizontalTop;
        LayoutChanged?.Invoke(Layout);
    }

    public void SetLayout(TabLayout layout)
    {
        Layout = layout;
        LayoutChanged?.Invoke(layout);
    }

    public void CycleSplit()
    {
        SplitMode = SplitMode switch
        {
            SplitMode.None => SplitMode.Columns2,
            SplitMode.Columns2 => SplitMode.Grid2x2,
            _ => SplitMode.None
        };
        SplitChanged?.Invoke(SplitMode);
    }

    public void SetSplit(SplitMode mode)
    {
        SplitMode = mode;
        SplitChanged?.Invoke(mode);
    }

    public TabItemModel CreateTab(string url = "swifter://newtab", string title = "New Tab")
    {
        var model = new TabItemModel
        {
            Title = title,
            Url = url,
            IsActive = false
        };
        Application.Current?.Dispatcher.Invoke(() =>
        {
            Tabs.Add(model);
            TabStates.Add(new BrowserTabState
            {
                TabId = model.Id,
                Url = url,
                Title = title
            });
        });
        TabAdded?.Invoke(model);
        ActivateTab(model.Id);
        return model;
    }

    public void ActivateTab(Guid id)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var t in Tabs) t.IsActive = t.Id == id;
            var state = TabStates.FirstOrDefault(s => s.TabId == id);
            if (state != null) state.LastActiveUtc = DateTime.UtcNow;
        });
        ActiveTabId = id;
        var tab = Tabs.FirstOrDefault(t => t.Id == id);
        if (tab != null) TabActivated?.Invoke(tab);
    }

    public void CloseTab(Guid id)
    {
        TabItemModel removed = null;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            removed = Tabs.FirstOrDefault(t => t.Id == id);
            if (removed != null)
            {
                Tabs.Remove(removed);
                var state = TabStates.FirstOrDefault(s => s.TabId == id);
                if (state != null) TabStates.Remove(state);
            }
            if (ActiveTabId == id && Tabs.Count > 0)
            {
                var next = Tabs.LastOrDefault(t => t.IsActive) ?? Tabs[Tabs.Count - 1];
                if (next != null)
                {
                    next.IsActive = true;
                    ActiveTabId = next.Id;
                }
            }
        });
        if (removed != null) TabRemoved?.Invoke(removed);
    }

    public void UpdateTab(Guid id, string title = null, string url = null, bool? loading = null, bool? muted = null)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var tab = Tabs.FirstOrDefault(t => t.Id == id);
            if (tab == null) return;
            if (title != null) tab.Title = title;
            if (url != null) tab.Url = url;
            if (loading.HasValue) tab.IsLoading = loading.Value;
            if (muted.HasValue) tab.IsMuted = muted.Value;
            var state = TabStates.FirstOrDefault(s => s.TabId == id);
            if (state != null)
            {
                if (title != null) state.Title = title;
                if (url != null) state.Url = url;
            }
        });
    }

    public TabItemModel GetNextTab(bool forward)
    {
        if (Tabs.Count == 0) return null;
        var index = Tabs.ToList().FindIndex(t => t.Id == ActiveTabId);
        if (index < 0) index = 0;
        if (forward) index = (index + 1) % Tabs.Count;
        else index = (index - 1 + Tabs.Count) % Tabs.Count;
        return Tabs[index];
    }

    public IReadOnlyList<BrowserTabState> GetRestorableStates()
    {
        return TabStates.ToList();
    }

    public void RestoreState(BrowserTabState state)
    {
        var model = new TabItemModel
        {
            Title = state.Title ?? "Restored",
            Url = state.Url ?? "swifter://newtab",
            IsPinned = state.IsPinned,
            GroupId = state.GroupId
        };
        Application.Current?.Dispatcher.Invoke(() => Tabs.Add(model));
        state.TabId = model.Id;
        TabStates.Add(state);
    }

    protected void OnPropertyChanged([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
