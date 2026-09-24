using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Swifter.Core.Extensions;

public sealed class InstalledExtension : INotifyPropertyChanged
{
    private bool _enabled;
    private string _name;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Version { get; set; }
    public string Description { get; set; }
    public string Path { get; set; }
    public ExtensionManifest Manifest { get; set; }
    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }
    public DateTime InstalledUtc { get; set; } = DateTime.UtcNow;
    public event PropertyChangedEventHandler PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class ExtensionHostEngine : INotifyPropertyChanged
{
    public ObservableCollection<InstalledExtension> Extensions { get; } = new ObservableCollection<InstalledExtension>();
    private readonly ManifestParser _parser = new ManifestParser();
    private readonly BackgroundScriptRunner _runner = new BackgroundScriptRunner();
    private readonly string _extensionsRoot;
    private readonly string _registryPath;

    public event Action<InstalledExtension> ExtensionInstalled;
    public event Action<string> ExtensionRemoved;
    public event Action<string, string> ConsoleMessage;
    public event PropertyChangedEventHandler PropertyChanged;

    public ExtensionHostEngine()
    {
        _extensionsRoot = Path.Combine(Program.GetUserDataRoot(), "extensions");
        Directory.CreateDirectory(_extensionsRoot);
        _registryPath = Path.Combine(_extensionsRoot, "registry.json");
        _runner.LogLine += (id, line) => ConsoleMessage?.Invoke(id, line);
        LoadRegistry();
    }

    public async Task LoadInstalledExtensionsAsync()
    {
        await Task.Run(() =>
        {
            foreach (var ext in Extensions.ToList())
            {
                try
                {
                    if (ext.Enabled) ActivateBackground(ext);
                }
                catch
                {
                }
            }
        }).ConfigureAwait(false);
    }

    public InstalledExtension InstallFromDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath)) return null;
        var manifest = _parser.ParseFile(manifestPath);
        if (manifest == null || !manifest.IsValid) return null;

        var target = Path.Combine(_extensionsRoot, SanitizeId(manifest.Name) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        CopyDirectory(directory, target);
        manifest = _parser.ParseFile(Path.Combine(target, "manifest.json"));

        var ext = new InstalledExtension
        {
            Name = manifest.Name,
            Version = manifest.Version,
            Description = manifest.Description,
            Path = target,
            Manifest = manifest,
            Enabled = true
        };
        Extensions.Add(ext);
        SaveRegistry();
        ActivateBackground(ext);
        ExtensionInstalled?.Invoke(ext);
        return ext;
    }

    public bool Uninstall(string extensionId)
    {
        var ext = Extensions.FirstOrDefault(x => x.Id == extensionId);
        if (ext == null) return false;
        _runner.StopServiceWorker(extensionId);
        Extensions.Remove(ext);
        SaveRegistry();
        try
        {
            if (Directory.Exists(ext.Path) && ext.Path.StartsWith(_extensionsRoot, StringComparison.OrdinalIgnoreCase))
                Directory.Delete(ext.Path, true);
        }
        catch
        {
        }
        ExtensionRemoved?.Invoke(extensionId);
        return true;
    }

    public void SetEnabled(string extensionId, bool enabled)
    {
        var ext = Extensions.FirstOrDefault(x => x.Id == extensionId);
        if (ext == null) return;
        ext.Enabled = enabled;
        if (enabled) ActivateBackground(ext);
        else _runner.StopServiceWorker(extensionId);
        SaveRegistry();
    }

    public void Reload(string extensionId)
    {
        var ext = Extensions.FirstOrDefault(x => x.Id == extensionId);
        if (ext == null) return;
        ext.Manifest = _parser.ParseFile(Path.Combine(ext.Path, "manifest.json"));
        _runner.StopServiceWorker(extensionId);
        if (ext.Enabled) ActivateBackground(ext);
    }

    public void ReloadAll()
    {
        foreach (var ext in Extensions.ToList()) Reload(ext.Id);
    }

    private void ActivateBackground(InstalledExtension ext)
    {
        if (ext.Manifest?.Background == null) return;
        if (!ext.Manifest.Background.TryGetValue("background", out var bg)) return;
        foreach (var worker in bg.ServiceWorker)
        {
            var path = Path.Combine(ext.Path, worker);
            if (File.Exists(path))
            {
                _runner.StartServiceWorker(ext.Id, path);
                break;
            }
        }
        foreach (var script in bg.Scripts)
        {
            var path = Path.Combine(ext.Path, script);
            if (File.Exists(path))
            {
                _runner.StartServiceWorker(ext.Id, path);
                break;
            }
        }
    }

    public List<string> GetContentScriptSources(string extensionId, string pageUrl)
    {
        var ext = Extensions.FirstOrDefault(x => x.Id == extensionId && x.Enabled);
        if (ext?.Manifest == null) return new List<string>();
        var files = _parser.MatchContentScripts(ext.Manifest, pageUrl);
        var sources = new List<string>();
        foreach (var f in files)
        {
            var src = _runner.LoadScriptSource(f, ext.Path);
            if (!string.IsNullOrEmpty(src)) sources.Add(src);
        }
        return sources;
    }

    public string BuildContentScriptsForUrl(string pageUrl)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ext in Extensions.Where(e => e.Enabled))
        {
            var sources = GetContentScriptSources(ext.Id, pageUrl);
            if (sources.Count == 0) continue;
            sb.AppendLine("(function(){");
            sb.AppendLine($"try{{window.__ext_{ext.Id}__=true;}}catch(e){{}}");
            foreach (var src in sources)
            {
                sb.AppendLine("(function(){");
                sb.AppendLine(src);
                sb.AppendLine("})();");
            }
            sb.AppendLine("})();");
        }
        return sb.ToString();
    }

    public bool SendRuntimeMessage(string extensionId, string type, JsonElement payload)
    {
        return _runner.SendRuntimeMessage(new ExtensionMessage
        {
            ExtensionId = extensionId,
            Type = type,
            Payload = payload
        });
    }

    public string GetPopupHtml(string extensionId)
    {
        var ext = Extensions.FirstOrDefault(x => x.Id == extensionId);
        if (ext?.Manifest?.Action == null) return null;
        if (!ext.Manifest.Action.TryGetValue("action", out var action) || string.IsNullOrEmpty(action.DefaultPopup)) return null;
        var path = Path.Combine(ext.Path, action.DefaultPopup);
        if (!File.Exists(path)) return null;
        return File.ReadAllText(path);
    }

    public string GetWorkerLog(string extensionId) => _runner.GetLog(extensionId);

    public bool IsWorkerRunning(string extensionId) => _runner.IsRunning(extensionId);

    public IReadOnlyDictionary<string, object> GetBackgroundStatus() => _runner.StatusSnapshot();

    public void Shutdown()
    {
        _runner.StopAll();
        SaveRegistry();
    }

    private void SaveRegistry()
    {
        try
        {
            var data = Extensions.Select(e => new
            {
                e.Id,
                e.Name,
                e.Version,
                e.Description,
                e.Path,
                e.Enabled,
                e.InstalledUtc
            }).ToList();
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_registryPath, json);
        }
        catch
        {
        }
    }

    private void LoadRegistry()
    {
        try
        {
            if (!File.Exists(_registryPath)) return;
            var json = File.ReadAllText(_registryPath);
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var path = item.GetProperty("Path").GetString();
                if (!Directory.Exists(path)) continue;
                var manifest = _parser.ParseFile(Path.Combine(path, "manifest.json"));
                if (manifest == null) continue;
                Extensions.Add(new InstalledExtension
                {
                    Id = item.TryGetProperty("Id", out var id) ? id.GetString() : Guid.NewGuid().ToString("N"),
                    Name = item.TryGetProperty("Name", out var n) ? n.GetString() : manifest.Name,
                    Version = item.TryGetProperty("Version", out var v) ? v.GetString() : manifest.Version,
                    Description = item.TryGetProperty("Description", out var d) ? d.GetString() : manifest.Description,
                    Path = path,
                    Manifest = manifest,
                    Enabled = !item.TryGetProperty("Enabled", out var en) || en.GetBoolean(),
                    InstalledUtc = item.TryGetProperty("InstalledUtc", out var t) && t.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow
                });
            }
        }
        catch
        {
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            File.Copy(file, Path.Combine(dest, rel), true);
        }
    }

    private static string SanitizeId(string name)
    {
        if (string.IsNullOrEmpty(name)) return "ext";
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars);
    }

    public IReadOnlyList<InstalledExtension> GetAll() => Extensions.ToList();

    protected void OnPropertyChanged([CallerMemberName] string n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
