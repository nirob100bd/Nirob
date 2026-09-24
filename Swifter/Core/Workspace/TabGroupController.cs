using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Swifter.Core.Workspace;

public sealed class TabGroup : INotifyPropertyChanged
{
    private string _name;
    private bool _isCollapsed;
    private string _colorHex;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public bool IsCollapsed { get => _isCollapsed; set { _isCollapsed = value; OnPropertyChanged(); } }
    public string ColorHex { get => _colorHex; set { _colorHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(Brush)); } }
    public ObservableCollection<Guid> TabIds { get; } = new ObservableCollection<Guid>();
    public int Depth { get; set; }
    public Guid? ParentGroupId { get; set; }
    public Brush Brush
    {
        get
        {
            if (ThemeManager.TryParseHex(ColorHex ?? "#0078D4", out var c)) return new SolidColorBrush(c);
            return new SolidColorBrush(Colors.SteelBlue);
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class TabGroupController : INotifyPropertyChanged
{
    public ObservableCollection<TabGroup> Groups { get; } = new ObservableCollection<TabGroup>();
    public event Action<TabGroup, Guid> TabAssigned;
    public event Action<TabGroup> GroupRemoved;
    public event PropertyChangedEventHandler PropertyChanged;

    public TabGroup CreateGroup(string name, string colorHex = "#0078D4", Guid? parent = null)
    {
        var group = new TabGroup
        {
            Name = name,
            ColorHex = colorHex,
            ParentGroupId = parent,
            Depth = parent.HasValue ? GetDepth(parent.Value) + 1 : 0
        };
        Groups.Add(group);
        return group;
    }

    private int GetDepth(Guid groupId)
    {
        var g = Groups.FirstOrDefault(x => x.Id == groupId);
        if (g == null) return 0;
        if (g.ParentGroupId.HasValue) return GetDepth(g.ParentGroupId.Value) + 1;
        return 0;
    }

    public void AssignTab(Guid groupId, Guid tabId)
    {
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return;
        foreach (var other in Groups)
        {
            if (other.TabIds.Contains(tabId)) other.TabIds.Remove(tabId);
        }
        group.TabIds.Add(tabId);
        TabAssigned?.Invoke(group, tabId);
    }

    public void UnassignTab(Guid tabId)
    {
        foreach (var g in Groups)
        {
            if (g.TabIds.Contains(tabId)) g.TabIds.Remove(tabId);
        }
    }

    public void RemoveGroup(Guid groupId, bool dissolveTabs = true)
    {
        var group = Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return;
        var children = Groups.Where(g => g.ParentGroupId == groupId).ToList();
        foreach (var child in children)
        {
            child.ParentGroupId = group.ParentGroupId;
            child.Depth = group.Depth;
        }
        if (dissolveTabs) group.TabIds.Clear();
        Groups.Remove(group);
        GroupRemoved?.Invoke(group);
    }

    public void RenameGroup(Guid groupId, string name)
    {
        var g = Groups.FirstOrDefault(x => x.Id == groupId);
        if (g != null) g.Name = name;
    }

    public void ToggleCollapse(Guid groupId)
    {
        var g = Groups.FirstOrDefault(x => x.Id == groupId);
        if (g != null) g.IsCollapsed = !g.IsCollapsed;
    }

    public IReadOnlyList<Guid> GetVisibleTabIds(IEnumerable<Guid> allOrderedIds)
    {
        var result = new List<Guid>();
        var hiddenByCollapsed = new HashSet<Guid>();
        foreach (var g in Groups.Where(x => x.IsCollapsed))
        {
            foreach (var id in g.TabIds) hiddenByCollapsed.Add(id);
        }
        foreach (var id in allOrderedIds)
        {
            if (hiddenByCollapsed.Contains(id)) continue;
            result.Add(id);
        }
        return result;
    }

    public TabGroup GetGroupOfTab(Guid tabId) => Groups.FirstOrDefault(g => g.TabIds.Contains(tabId));

    public void SerializeInto(Dictionary<string, object> store)
    {
        store["groups"] = Groups.Select(g => new
        {
            id = g.Id,
            name = g.Name,
            color = g.ColorHex,
            collapsed = g.IsCollapsed,
            parent = g.ParentGroupId,
            tabs = g.TabIds.ToList()
        }).ToList();
    }

    public void DeserializeFrom(Dictionary<string, object> store)
    {
        Groups.Clear();
        if (!store.TryGetValue("groups", out var raw) || raw is not List<object> list) return;
        foreach (var item in list)
        {
            if (item is not IDictionary<string, object> dict) continue;
            var g = new TabGroup
            {
                Id = dict.TryGetValue("id", out var id) && id is Guid gid ? gid : Guid.NewGuid(),
                Name = dict.TryGetValue("name", out var n) ? n?.ToString() : "Group",
                ColorHex = dict.TryGetValue("color", out var c) ? c?.ToString() : "#0078D4",
                IsCollapsed = dict.TryGetValue("collapsed", out var col) && col is bool b && b,
                ParentGroupId = dict.TryGetValue("parent", out var p) && p is Guid pg ? pg : null
            };
            if (dict.TryGetValue("tabs", out var tabs) && tabs is List<object> tl)
                foreach (var t in tl) g.TabIds.Add(t is Guid tg ? tg : Guid.NewGuid());
            Groups.Add(g);
        }
    }

    public void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
