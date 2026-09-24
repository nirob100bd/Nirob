using System.Text.Json;

namespace Swifter.Core.Extensions;

public sealed class ExtensionManifest
{
    public string ManifestVersion { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Version { get; set; }
    public string DefaultLocale { get; set; }
    public Dictionary<string, ExtensionAction> Action { get; set; } = new Dictionary<string, ExtensionAction>();
    public List<ExtensionContentScript> ContentScripts { get; set; } = new List<ExtensionContentScript>();
    public Dictionary<string, ExtensionBackground> Background { get; set; } = new Dictionary<string, ExtensionBackground>();
    public List<string> Permissions { get; set; } = new List<string>();
    public List<string> HostPermissions { get; set; } = new List<string>();
    public List<string> OptionalPermissions { get; set; } = new List<string>();
    public Dictionary<string, ExtensionCommand> Commands { get; set; } = new Dictionary<string, ExtensionCommand>();
    public List<ExtensionWebAccessible> WebAccessibleResources { get; set; } = new List<ExtensionWebAccessible>();
    public string IconsPath { get; set; }
    public bool IsValid => ManifestVersion == "3" && !string.IsNullOrEmpty(Name);
    public List<string> ParseErrors { get; set; } = new List<string>();
}

public sealed class ExtensionAction
{
    public string DefaultTitle { get; set; }
    public string DefaultPopup { get; set; }
    public string DefaultIcon { get; set; }
}

public sealed class ExtensionContentScript
{
    public List<string> Matches { get; set; } = new List<string>();
    public List<string> Js { get; set; } = new List<string>();
    public List<string> Css { get; set; } = new List<string>();
    public bool RunAtDocumentStart { get; set; }
    public bool RunAtDocumentEnd { get; set; }
    public bool AllFrames { get; set; }
    public string World { get; set; } = "ISOLATED";
}

public sealed class ExtensionBackground
{
    public List<string> ServiceWorker { get; set; } = new List<string>();
    public List<string> Scripts { get; set; } = new List<string>();
    public string Type { get; set; } = "module";
}

public sealed class ExtensionCommand
{
    public string Description { get; set; }
    public Dictionary<string, string> SuggestedKey { get; set; } = new Dictionary<string, string>();
}

public sealed class ExtensionWebAccessible
{
    public List<string> Resources { get; set; } = new List<string>();
    public List<string> Matches { get; set; } = new List<string>();
    public bool UseDynamicUrl { get; set; }
}

public sealed class ManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExtensionManifest ParseFile(string manifestPath)
    {
        if (!File.Exists(manifestPath)) return null;
        var text = File.ReadAllText(manifestPath);
        return Parse(text);
    }

    public ExtensionManifest Parse(string json)
    {
        var manifest = new ExtensionManifest();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            var root = doc.RootElement;
            manifest.ManifestVersion = GetString(root, "manifest_version");
            manifest.Name = ResolveMessage(root, "name");
            manifest.Description = ResolveMessage(root, "description");
            manifest.Version = GetString(root, "version");
            manifest.DefaultLocale = GetString(root, "default_locale");

            if (root.TryGetProperty("action", out var action) && action.ValueKind == JsonValueKind.Object)
            {
                var a = new ExtensionAction
                {
                    DefaultTitle = GetString(action, "default_title"),
                    DefaultPopup = GetString(action, "default_popup")
                };
                if (action.TryGetProperty("default_icon", out var icon))
                {
                    a.DefaultIcon = icon.ValueKind == JsonValueKind.String
                        ? icon.GetString()
                        : icon.EnumerateObject().OrderByDescending(p => int.TryParse(p.Name, out var n) ? n : 0).Select(p => p.Value.GetString()).FirstOrDefault();
                }
                manifest.Action["action"] = a;
            }

            if (root.TryGetProperty("content_scripts", out var cs) && cs.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in cs.EnumerateArray())
                {
                    var script = new ExtensionContentScript
                    {
                        Matches = GetStringArray(item, "matches"),
                        Js = GetStringArray(item, "js"),
                        Css = GetStringArray(item, "css"),
                        AllFrames = GetBool(item, "all_frames"),
                        World = GetString(item, "world") ?? "ISOLATED"
                    };
                    var runAt = GetString(item, "run_at") ?? "document_idle";
                    script.RunAtDocumentStart = runAt == "document_start";
                    script.RunAtDocumentEnd = runAt == "document_end";
                    if (script.Js.Count > 0 || script.Matches.Count > 0) manifest.ContentScripts.Add(script);
                }
            }

            if (root.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.Object)
            {
                var b = new ExtensionBackground { Type = GetString(bg, "type") ?? "module" };
                if (bg.TryGetProperty("service_worker", out var sw) && sw.ValueKind == JsonValueKind.String)
                    b.ServiceWorker.Add(sw.GetString());
                if (bg.TryGetProperty("scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Array)
                    b.Scripts = scripts.EnumerateArray().Select(x => x.GetString()).Where(x => x != null).ToList();
                manifest.Background["background"] = b;
            }

            manifest.Permissions = GetStringArray(root, "permissions");
            manifest.HostPermissions = GetStringArray(root, "host_permissions");
            manifest.OptionalPermissions = GetStringArray(root, "optional_permissions");

            if (root.TryGetProperty("commands", out var cmds) && cmds.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in cmds.EnumerateObject())
                {
                    var c = new ExtensionCommand { Description = GetString(prop.Value, "description") };
                    if (prop.Value.TryGetProperty("suggested_key", out var keys) && keys.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var k in keys.EnumerateObject())
                            c.SuggestedKey[k.Name] = k.Value.GetString();
                    }
                    manifest.Commands[prop.Name] = c;
                }
            }

            if (root.TryGetProperty("web_accessible_resources", out var war) && war.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in war.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        manifest.WebAccessibleResources.Add(new ExtensionWebAccessible { Resources = new List<string> { item.GetString() } });
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        manifest.WebAccessibleResources.Add(new ExtensionWebAccessible
                        {
                            Resources = GetStringArray(item, "resources"),
                            Matches = GetStringArray(item, "matches"),
                            UseDynamicUrl = GetBool(item, "use_dynamic_url")
                        });
                    }
                }
            }

            if (root.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Object)
            {
                manifest.IconsPath = icons.EnumerateObject().OrderByDescending(p => int.TryParse(p.Name, out var n) ? n : 0).Select(p => p.Value.GetString()).FirstOrDefault();
            }

            if (manifest.ManifestVersion != "3")
                manifest.ParseErrors.Add("Only Manifest V3 is supported");
            if (string.IsNullOrEmpty(manifest.Name))
                manifest.ParseErrors.Add("Missing name");
        }
        catch (JsonException ex)
        {
            manifest.ParseErrors.Add(ex.Message);
        }
        return manifest;
    }

    private static string GetString(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static bool GetBool(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true"));
    }

    private static List<string> GetStringArray(JsonElement el, string prop)
    {
        var list = new List<string>();
        if (!el.TryGetProperty(prop, out var v)) return list;
        if (v.ValueKind == JsonValueKind.Array)
            list.AddRange(v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()));
        else if (v.ValueKind == JsonValueKind.String)
            list.Add(v.GetString());
        return list;
    }

    private static string ResolveMessage(JsonElement root, string prop)
    {
        var value = GetString(root, prop);
        if (value == null) return null;
        if (!value.StartsWith("__") || !value.EndsWith("__")) return value;
        var key = value.Trim('_');
        try
        {
            var locale = GetString(root, "default_locale") ?? "en";
            var localesDir = null as string;
            return "__" + key + "__";
        }
        catch
        {
        }
        return value;
    }

    public List<string> MatchContentScripts(ExtensionManifest manifest, string url)
    {
        var result = new List<string>();
        if (manifest?.ContentScripts == null) return result;
        foreach (var cs in manifest.ContentScripts)
        {
            if (cs.Matches.Any(m => MatchPattern(m, url)))
                result.AddRange(cs.Js);
        }
        return result;
    }

    public static bool MatchPattern(string pattern, string url)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(url)) return false;
        if (pattern == "<all_urls>") return url.StartsWith("http") || url.StartsWith("file");
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(url, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
