using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Swifter.Core.Workspace;

public sealed class ContainerProfile : INotifyPropertyChanged
{
    private string _name;
    private string _colorHex;
    private bool _isPrivate;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string ColorHex { get => _colorHex; set { _colorHex = value; OnPropertyChanged(); } }
    public bool IsPrivate { get => _isPrivate; set { _isPrivate = value; OnPropertyChanged(); } }
    public string CookiePartitionKey { get; set; }
    public string UserAgentSuffix { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ContainerProfileManager : INotifyPropertyChanged
{
    private readonly string _storePath;
    private ContainerProfile _active;

    public ObservableCollection<ContainerProfile> Profiles { get; } = new ObservableCollection<ContainerProfile>();
    public ContainerProfile Active
    {
        get => _active;
        set { _active = value; OnPropertyChanged(); }
    }

    public event Action<ContainerProfile> ContainerChanged;
    public event Action<ContainerProfile> ContainerCreated;
    public event PropertyChangedEventHandler PropertyChanged;

    public ContainerProfileManager()
    {
        _storePath = Path.Combine(Program.GetUserDataRoot(), "containers.json");
        Load();
        if (Profiles.Count == 0)
        {
            Profiles.Add(new ContainerProfile { Id = "default", Name = "Default", ColorHex = "#0078D4" });
            Profiles.Add(new ContainerProfile { Id = "personal", Name = "Personal", ColorHex = "#0F7B0F" });
            Profiles.Add(new ContainerProfile { Id = "work", Name = "Work", ColorHex = "#9D5D00" });
            Profiles.Add(new ContainerProfile { Id = "banking", Name = "Banking", ColorHex = "#C42B1C", IsPrivate = true });
            Profiles.Add(new ContainerProfile { Id = "social", Name = "Social", ColorHex = "#8764B8" });
            Save();
        }
        Active = Profiles.FirstOrDefault();
    }

    public ContainerProfile GetOrCreate(string id, string name = null)
    {
        var found = Profiles.FirstOrDefault(p => p.Id == id);
        if (found != null) return found;
        found = new ContainerProfile
        {
            Id = id,
            Name = name ?? id,
            ColorHex = "#0078D4"
        };
        Profiles.Add(found);
        Save();
        return found;
    }

    public ContainerProfile Create(string name, string colorHex)
    {
        var profile = new ContainerProfile
        {
            Name = name,
            ColorHex = colorHex,
            CookiePartitionKey = "swifter-container-" + Guid.NewGuid().ToString("N")
        };
        Profiles.Add(profile);
        Save();
        ContainerCreated?.Invoke(profile);
        return profile;
    }

    public void Remove(string id)
    {
        if (id == "default") return;
        var p = Profiles.FirstOrDefault(x => x.Id == id);
        if (p == null) return;
        Profiles.Remove(p);
        if (Active?.Id == id) Active = Profiles.FirstOrDefault();
        Save();
    }

    public void Activate(string id)
    {
        Active = Profiles.FirstOrDefault(p => p.Id == id);
        if (Active != null) ContainerChanged?.Invoke(Active);
    }

    public Microsoft.Web.WebView2.Core.CoreWebView2Profile ResolveWebViewProfile(Microsoft.Web.WebView2.Core.CoreWebView2Environment env, string containerId)
    {
        if (env == null) return null;
        try
        {
            var profile = GetOrCreate(containerId);
            var safe = new string((profile.Id).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            return env.CreateCoreWebView2Profile(safe);
        }
        catch
        {
            return null;
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return;
            var json = File.ReadAllText(_storePath);
            var list = JsonSerializer.Deserialize<List<ContainerProfile>>(json);
            if (list == null) return;
            Profiles.Clear();
            foreach (var p in list) Profiles.Add(p);
        }
        catch
        {
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Profiles.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storePath, json);
        }
        catch
        {
        }
    }

    public ContainerProfile GetTabContainer(string tabId)
    {
        if (string.IsNullOrEmpty(tabId)) return Active ?? Profiles.FirstOrDefault();
        return Profiles.FirstOrDefault(p => p.Id == tabId) ?? Active ?? Profiles.FirstOrDefault();
    }

    protected void OnPropertyChanged([CallerMemberName] string n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
