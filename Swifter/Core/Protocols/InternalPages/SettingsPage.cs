using System.Text;

namespace Swifter.Core.Protocols.InternalPages;

public static class SettingsPage
{
    public static string Render(Dictionary<string, string> query)
    {
        query.TryGetValue("tab", out var tab);
        var sb = new StringBuilder(40960);
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Settings</title><style>");
        sb.AppendLine(ProtocolPageRenderer.BaseCss);
        sb.AppendLine(SettingsCss);
        sb.AppendLine("</style></head><body class='dark'>");
        sb.AppendLine("<div class='shell'>");
        sb.AppendLine("<nav class='sidebar'><div class='brand'><span class='logo'>S</span><span>Swifter</span></div>");
        sb.AppendLine("<a class='navitem' href='swifter://newtab'><span class='ico'>⌂</span>New Tab</a>");
        sb.AppendLine("<a class='navitem' href='swifter://downloads'><span class='ico'>⬇</span>Downloads</a>");
        sb.AppendLine("<a class='navitem' href='swifter://history'><span class='ico'>🕘</span>History</a>");
        sb.AppendLine("<a class='navitem' href='swifter://bookmarks'><span class='ico'>★</span>Bookmarks</a>");
        sb.AppendLine("<a class='navitem' href='swifter://shields'><span class='ico'>🛡</span>Shields</a>");
        sb.AppendLine("<a class='navitem active' href='swifter://settings'><span class='ico'>⚙</span>Settings</a>");
        sb.AppendLine("<div class='spacer'></div><div class='foot'>swifter://settings</div></nav>");
        sb.AppendLine("<main class='content'>");
        sb.AppendLine("<h1>Settings</h1><div class='sub'>Control panel for appearance, performance, privacy, and profiles</div>");
        sb.AppendLine("<div class='tabs' id='tabs'>");
        sb.AppendLine(TabBtn("appearance", "🎨 Appearance", tab));
        sb.AppendLine(TabBtn("performance", "⚡ Performance", tab));
        sb.AppendLine(TabBtn("privacy", "🔒 Privacy", tab));
        sb.AppendLine(TabBtn("downloads", "⬇ Downloads", tab));
        sb.AppendLine(TabBtn("shortcuts", "⌨ Shortcuts", tab));
        sb.AppendLine(TabBtn("containers", "📦 Profiles", tab));
        sb.AppendLine(TabBtn("extensions", "🧩 Extensions", tab));
        sb.AppendLine(TabBtn("about", "ℹ About", tab));
        sb.AppendLine("</div>");
        sb.AppendLine("<div id='panels'>");
        sb.AppendLine(AppearancePanel());
        sb.AppendLine(PerformancePanel());
        sb.AppendLine(PrivacyPanel());
        sb.AppendLine(DownloadsPanel());
        sb.AppendLine(ShortcutsPanel());
        sb.AppendLine(ContainersPanel());
        sb.AppendLine(ExtensionsPanel());
        sb.AppendLine(AboutPanel());
        sb.AppendLine("</div>");
        sb.AppendLine("</main></div>");
        sb.AppendLine("<script>");
        sb.AppendLine(ProtocolPageRenderer.BaseJs);
        sb.AppendLine(SettingsJs);
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private static string TabBtn(string key, string label, string active)
    {
        var cls = (active ?? "appearance") == key ? "active" : "";
        return $"<button class='tab {cls}' data-tab='{key}'>{label}</button>";
    }

    private static string Row(string title, string control, string sub = "")
    {
        var subHtml = string.IsNullOrEmpty(sub) ? "" : $"<div class='set-sub'>{sub}</div>";
        return $"<div class='set'><div class='set-t'><b>{title}</b>{subHtml}</div><div class='set-c'>{control}</div></div>";
    }

    private static string Toggle(string action, bool on, string id = "")
    {
        return $"<span class='toggle {(on ? "on" : "")}' data-action='{action}' id='{id}'></span>";
    }

    private static string AppearancePanel()
    {
        var theme = App.ThemeManager?.Settings.Theme ?? Swifter.Theme.AppTheme.System;
        var accent = App.ThemeManager?.Settings.AccentHex ?? "#0078D4";
        var mica = App.ThemeManager?.Settings.UseMica != false;
        var sb = new StringBuilder();
        sb.AppendLine("<section class='panel' data-panel='appearance'>");
        sb.AppendLine("<h2>Appearance</h2>");
        sb.AppendLine("<div class='card'>");
        sb.AppendLine(Row("Theme", $"<select data-action='set-theme'><option value='System' {(theme == Swifter.Theme.AppTheme.System ? "selected" : "")}>System</option><option value='Light' {(theme == Swifter.Theme.AppTheme.Light ? "selected" : "")}>Light</option><option value='Dark' {(theme == Swifter.Theme.AppTheme.Dark ? "selected" : "")}>Dark</option></select>", "Follow Windows or force a palette"));
        sb.AppendLine(Row("Accent color", $"<input type='color' value='{accent}' data-action='set-accent' style='width:60px;height:36px;padding:2px'/>", "Used across UI, tabs, and internal pages"));
        sb.AppendLine(Row("Mica backdrop", Toggle("toggle-mica", mica), "Native Windows 11 material behind the window"));
        sb.AppendLine(Row("Tab radius", "<input type='range' min='0' max='16' value='" + (App.ThemeManager?.Settings.TabRadius ?? 8) + "' data-action='set-radius'/>", "Corner roundness of tab strip items"));
        sb.AppendLine(Row("Vertical tab sidebar", Toggle("toggle-vertical", App.ThemeManager?.Settings.NewTabLayout == 1), "Dock tabs to the left edge"));
        sb.AppendLine(Row("Custom wallpaper", "<button class='btn ghost sm' data-action='pick-wallpaper'>Choose image…</button>", "Background for swifter://newtab"));
        sb.AppendLine("</div>");
        sb.AppendLine("<h2>Startup</h2><div class='card'>");
        sb.AppendLine(Row("Restore last session", Toggle("toggle-restore", true), "Reopen tabs from previous exit"));
        sb.AppendLine(Row("Open links from previous session", "<select><option selected>Ask</option><option>Always</option><option>Never</option></select>", ""));
        sb.AppendLine("</div></section>");
        return sb.ToString();
    }

    private static string PerformancePanel()
    {
        var cores = Environment.ProcessorCount;
        var lowEnd = cores <= 2;
        var sb = new StringBuilder();
        sb.AppendLine("<section class='panel' data-panel='performance'>");
        sb.AppendLine("<h2>Performance</h2><div class='card'>");
        sb.AppendLine(Row("Low-end CPU throttle", Toggle("toggle-lowend", lowEnd), $"Auto-detected ({cores} logical processors). Limits renderer pool and raster threads."));
        sb.AppendLine(Row("Memory trim threshold", "<select data-action='set-memthreshold'><option value='100'>100 MB</option><option value='200' selected>200 MB</option><option value='400'>400 MB</option><option value='800'>800 MB</option></select>", "Working set size that triggers kernel trim"));
        sb.AppendLine(Row("Tab hibernation", Toggle("toggle-hibernate", true), "Freeze background tabs idle for 5+ minutes"));
        sb.AppendLine(Row("Hibernate after", "<select><option>2 min</option><option selected>5 min</option><option>15 min</option><option>30 min</option></select>", ""));
        sb.AppendLine(Row("Shared renderer processes", Toggle("toggle-sharing", true), "Single environment factory with pooled Chromium processes"));
        sb.AppendLine(Row("Trim now", "<button class='btn ghost' data-action='trim-now'>🧠 Trim working set</button>", "Calls SetProcessWorkingSetSize + EmptyWorkingSet immediately"));
        sb.AppendLine("</div>");
        sb.AppendLine("<h2>Resources</h2><div class='card' id='perfStats'>");
        sb.AppendLine("<div class='grid g4' id='perfGrid'>" + ProtocolPageRenderer.Stat("Working set", "—") + ProtocolPageRenderer.Stat("Shared processes", "—") + ProtocolPageRenderer.Stat("Cache on disk", "—") + ProtocolPageRenderer.Stat("Database", "—") + "</div>");
        sb.AppendLine("</div></section>");
        return sb.ToString();
    }

    private static string PrivacyPanel()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section class='panel' data-panel='privacy'>");
        sb.AppendLine("<h2>Privacy & Shields</h2><div class='card'>");
        sb.AppendLine(Row("Ad / tracker block", Toggle("toggle-adblock", App.AdBlock?.ShieldEnabled != false), "EasyList + built-in domain trie engine"));
        sb.AppendLine(Row("Aggressive cosmetic hiding", Toggle("toggle-aggressive", App.AdBlock?.AggressiveMode == true), "Heuristic pattern matching for unknown ad networks"));
        sb.AppendLine(Row("Tracking parameter strip", Toggle("toggle-utm", App.ParamStripper?.Enabled != false), "Removes utm_*, fbclid, gclid, msclkid, ref, _hsenc on navigation & clipboard"));
        sb.AppendLine(Row("Canvas fingerprint noise", Toggle("toggle-canvas", App.FingerprintProtector?.CanvasNoise != false), "Perturbs toDataURL / getImageData reads"));
        sb.AppendLine(Row("Audio fingerprint noise", Toggle("toggle-audio", App.FingerprintProtector?.AudioNoise != false), "Offsets AnalyserNode frequency/time data"));
        sb.AppendLine(Row("WebGL vendor spoof", Toggle("toggle-webgl", App.FingerprintProtector?.WebGLSpoof != false), "Masks unmasked vendor/renderer strings"));
        sb.AppendLine(Row("WebRTC leak guard", Toggle("toggle-webrtc", true), "Forces relay-only ICE candidates, blocks STUN UDP"));
        sb.AppendLine(Row("Script blocker", Toggle("toggle-scripts", true), "Blocks cryptojackers, notification hijackers, clipboard hijacks"));
        sb.AppendLine(Row("Clear browsing data", "<button class='btn danger' data-action='clear-data'>Clear now…</button>", "History, cache, cookies, passwords"));
        sb.AppendLine("</div></section>");
        return sb.ToString();
    }

    private static string DownloadsPanel()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section class='panel' data-panel='downloads'>");
        sb.AppendLine("<h2>Downloads</h2><div class='card'>");
        sb.AppendLine(Row("Download folder", "<input value='" + System.Net.WebUtility.HtmlEncode(Path.Combine(Program.GetUserDataRoot(), "downloads")) + "' readonly/>", ""));
        sb.AppendLine(Row("Concurrent segments", "<select data-action='set-segments'><option>4</option><option>8</option><option selected>16</option></select>", "Per-file parallel HTTP range streams"));
        sb.AppendLine(Row("Global speed limit", "<select data-action='set-speed'><option value='0'>Unlimited</option><option value='1024'>1 MB/s</option><option value='5120'>5 MB/s</option><option value='10240'>10 MB/s</option></select>", ""));
        sb.AppendLine(Row("Auto-sniff media", Toggle("toggle-sniff", true), "Detect .m3u8/.mpd/.mp4/.mp3 on the wire"));
        sb.AppendLine(Row("Video overlay button", Toggle("toggle-overlay", true), "Floating download glass button over HTML5 video"));
        sb.AppendLine(Row("Ask where to save", Toggle("toggle-ask", false), ""));
        sb.AppendLine("</div></section>");
        return sb.ToString();
    }

    private static string ShortcutsPanel()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section class='panel' data-panel='shortcuts'>");
        sb.AppendLine("<h2>Keyboard Shortcuts</h2><div class='card'><table><thead><tr><th>Action</th><th>Gesture</th><th style='width:80px'>Enabled</th></tr></thead><tbody id='shortcuts'>");
        var hotkeys = App.Hotkeys;
        if (hotkeys != null)
        {
            foreach (var b in hotkeys.Bindings)
            {
                sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(b.Description ?? b.Id)}</td><td><span class='kbd'>{System.Net.WebUtility.HtmlEncode(b.Gesture)}</span></td><td>{Toggle("noop", b.Enabled)}</td></tr>");
            }
        }
        sb.AppendLine("</tbody></table>");
        sb.AppendLine("<div class='row' style='margin-top:12px'><button class='btn ghost sm' data-action='hotkeys-reset'>Reset defaults</button></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<h2>Mouse Gestures</h2><div class='card'>");
        sb.AppendLine(Row("Enable gestures", Toggle("toggle-gestures", true), "Right-click drag to navigate"));
        sb.AppendLine(Row("Left drag", "<span class='badge'>Back</span>", ""));
        sb.AppendLine(Row("Right drag", "<span class='badge'>Forward</span>", ""));
        sb.AppendLine(Row("Down drag", "<span class='badge'>Close tab</span>", ""));
        sb.AppendLine(Row("Up drag", "<span class='badge'>New tab</span>", ""));
        sb.AppendLine(Row("Up-right drag", "<span class='badge'>Open link new tab</span>", ""));
        sb.AppendLine("</div></section>");
        return sb.ToString();
    }

    private static string ContainersPanel()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section class='panel' data-panel='containers'>");
        sb.AppendLine("<h2>Container Profiles</h2>");
        sb.AppendLine("<div class='sub'>Isolated CoreWebView2Profile instances with independent cookie jars and local storage</div>");
        sb.AppendLine("<div class='card'><div id='containers' class='grid g3'>");
        if (App.Containers != null)
        {
            foreach (var p in App.Containers.Profiles)
            {
                sb.AppendLine($"<div class='stat'><div class='stat-v' style='color:{System.Net.WebUtility.HtmlEncode(p.ColorHex)}'>{System.Net.WebUtility.HtmlEncode(p.Name)}</div><div class='stat-l'>{(p.IsPrivate ? "Private · " : "")}{System.Net.WebUtility.HtmlEncode(p.Id)}</div></div>");
            }
        }
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='row' style='margin-top:14px'><button class='btn' data-action='container-new'>＋ New container</button></div>");
        sb.AppendLine("</div></section>");
        return sb.ToString();
    }

    private static string ExtensionsPanel()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<section class='panel' data-panel='extensions'>");
        sb.AppendLine("<h2>Web Extensions (Manifest V3)</h2>");
        sb.AppendLine("<div class='sub'>Compatibility host for background service workers, content scripts, and popups</div>");
        sb.AppendLine("<div class='card'><div id='extList' class='empty'><div class='big'>🧩</div><div>No extensions installed. Load an unpacked MV3 extension to begin.</div></div>");
        sb.AppendLine("<div class='row' style='margin-top:14px'><button class='btn' data-action='ext-load'>Load unpacked…</button><button class='btn ghost' data-action='ext-reload'>Reload all</button></div>");
        sb.AppendLine("</div></section>");
        return sb.ToString();
    }

    private static string AboutPanel()
    {
        var version = Program.GetVersionString();
        var sb = new StringBuilder();
        sb.AppendLine("<section class='panel' data-panel='about'>");
        sb.AppendLine("<h2>About Swifter</h2><div class='card'>");
        sb.AppendLine("<div class='grid g3'>");
        sb.AppendLine(ProtocolPageRenderer.Stat("Version", version));
        sb.AppendLine(ProtocolPageRenderer.Stat("Engine", "WebView2 / Chromium"));
        sb.AppendLine(ProtocolPageRenderer.Stat("Runtime", ".NET 8 WPF"));
        sb.AppendLine(ProtocolPageRenderer.Stat("OS", Environment.OSVersion.VersionString));
        sb.AppendLine(ProtocolPageRenderer.Stat("CPU threads", Environment.ProcessorCount.ToString()));
        sb.AppendLine(ProtocolPageRenderer.Stat("User data", Program.GetUserDataRoot()));
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='row' style='margin-top:16px'>");
        sb.AppendLine("<button class='btn ghost' data-action='open-datadir'>Open data directory</button>");
        sb.AppendLine("<button class='btn ghost' data-action='export-logs'>Export logs</button>");
        sb.AppendLine("<a class='btn ghost' href='https://github.com' target='_blank'>Repository</a>");
        sb.AppendLine("</div>");
        sb.AppendLine("<p class='muted' style='margin-top:18px'>Swifter is an ultra-optimized Windows 11 browser built on Microsoft WebView2 with IDM-grade downloads, EasyList shields, container isolation, and native Mica chrome.</p>");
        sb.AppendLine("</div></section>");
        return sb.ToString();
    }

    private const string SettingsCss = @"
.tabs{display:flex;gap:6px;flex-wrap:wrap;margin-bottom:18px;border-bottom:1px solid var(--border);padding-bottom:10px;}
.tab{background:transparent;border:1px solid transparent;color:var(--muted);padding:8px 14px;border-radius:10px;cursor:pointer;font-size:13px;font-weight:600;}
.tab:hover{background:var(--panel2);color:var(--text);}
.tab.active{background:color-mix(in srgb,var(--accent) 20%,transparent);color:#fff;border-color:color-mix(in srgb,var(--accent) 45%,transparent);}
.panel{display:none;animation:fade .2s ease;}
.panel.active{display:block;}
@keyframes fade{from{opacity:0;transform:translateY(6px);}to{opacity:1;transform:none;}}
.set{display:flex;justify-content:space-between;gap:20px;align-items:center;padding:14px 0;border-bottom:1px solid var(--border);}
.set:last-child{border-bottom:0;}
.set-t b{font-size:14px;}
.set-sub{color:var(--muted);font-size:12px;margin-top:3px;max-width:520px;}
.set-c{flex-shrink:0;min-width:140px;display:flex;justify-content:flex-end;align-items:center;}
.set-c input[type=range]{width:180px;}
input[type=color]{border:none;background:transparent;cursor:pointer;}
";

    private const string SettingsJs = @"
function showTab(key){
document.querySelectorAll('.tab').forEach(function(t){t.classList.toggle('active',t.getAttribute('data-tab')===key);});
document.querySelectorAll('.panel').forEach(function(p){p.classList.toggle('active',p.getAttribute('data-panel')===key);});
history.replaceState(null,'','swifter://settings?tab='+key);
}
document.addEventListener('click',function(e){
var tab=e.target.closest('.tab');
if(tab){showTab(tab.getAttribute('data-tab'));return;}
var tog=e.target.closest('.toggle');
if(tog&&!tog.getAttribute('data-action')!=='noop'){
if(tog.getAttribute('data-action')&&tog.getAttribute('data-action')!=='noop'){
tog.classList.toggle('on');
command(tog.getAttribute('data-action'),{on:tog.classList.contains('on')});
}
}
});
document.addEventListener('change',function(e){
var el=e.target.closest('[data-action]');
if(el&&(el.tagName==='SELECT'||el.tagName==='INPUT')){
command(el.getAttribute('data-action'),{value:el.value});
}
});
function renderPerf(){
if(!window.SWIFTER||!SWIFTER.perf)return;
var p=SWIFTER.perf;
var cells=document.querySelectorAll('#perfGrid .stat-v');
if(cells.length>=4){
cells[0].textContent=p.workingSet+' MB';
cells[1].textContent=p.processes;
cells[2].textContent=p.cache+' MB';
cells[3].textContent=p.db+' MB';
}
}
document.addEventListener('swifter-data',function(e){if(e.detail.key==='perf')renderPerf();});
window.addEventListener('load',function(){
var m=location.search.match(/tab=([a-z]+)/);
showTab(m?m[1]:'appearance');
requestData('perf');
setInterval(function(){requestData('perf');},3000);
});
";
}
