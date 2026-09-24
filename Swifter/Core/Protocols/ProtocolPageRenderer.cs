using System.Text;

namespace Swifter.Core.Protocols;

public sealed class ProtocolPageRenderer
{
    public string PageTitle(string title)
    {
        return string.IsNullOrWhiteSpace(title) ? "Swifter" : title + " — Swifter";
    }

    public static string Layout(string title, string body, string activeNav = "", bool dark = true)
    {
        var themeClass = dark ? "dark" : "light";
        var accent = App.AccentProvider != null ? "#" + App.AccentProvider.CurrentAccent.R.ToString("X2") + App.AccentProvider.CurrentAccent.G.ToString("X2") + App.AccentProvider.CurrentAccent.B.ToString("X2") : "#0078D4";
        var sb = new StringBuilder(16384);
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang='en'><head><meta charset='utf-8'/>");
        sb.AppendLine("<meta name='viewport' content='width=device-width,initial-scale=1'/>");
        sb.AppendLine("<title>" + System.Net.WebUtility.HtmlEncode(title) + "</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(BaseCss);
        sb.AppendLine(":root{--accent:" + accent + ";}");
        sb.AppendLine("</style></head>");
        sb.AppendLine($"<body class='{themeClass}'>");
        sb.AppendLine("<div class='shell'>");
        sb.AppendLine(Nav(activeNav));
        sb.AppendLine("<main class='content'>");
        sb.AppendLine(body);
        sb.AppendLine("</main></div>");
        sb.AppendLine("<script>");
        sb.AppendLine(BaseJs);
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private static string Nav(string active)
    {
        var items = new (string Key, string Icon, string Label)[]
        {
            ("newtab", "⌂", "New Tab"),
            ("downloads", "⬇", "Downloads"),
            ("history", "🕘", "History"),
            ("bookmarks", "★", "Bookmarks"),
            ("shields", "🛡", "Shields"),
            ("settings", "⚙", "Settings")
        };
        var sb = new StringBuilder();
        sb.AppendLine("<nav class='sidebar'>");
        sb.AppendLine("<div class='brand'><span class='logo'>S</span><span>Swifter</span></div>");
        foreach (var (key, icon, label) in items)
        {
            var cls = key == active ? "active" : "";
            sb.AppendLine($"<a class='navitem {cls}' href='swifter://{key}'><span class='ico'>{icon}</span>{label}</a>");
        }
        sb.AppendLine("<div class='spacer'></div>");
        sb.AppendLine("<div class='foot'>swifter://internal</div>");
        sb.AppendLine("</nav>");
        return sb.ToString();
    }

    public static string Card(string title, string inner, string extraClass = "")
    {
        return $"<section class='card {extraClass}'><header class='card-h'>{title}</header><div class='card-b'>{inner}</div></section>";
    }

    public static string Stat(string label, string value, string color = "")
    {
        var style = string.IsNullOrEmpty(color) ? "" : $"style='color:{color}'";
        return $"<div class='stat'><div class='stat-v' {style}>{value}</div><div class='stat-l'>{label}</div></div>";
    }

    public static string Btn(string label, string action, string cls = "")
    {
        return $"<button class='btn {cls}' data-action='{action}'>{label}</button>";
    }

    public static byte[] GetBundledIcon(string name)
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = "Swifter.assets." + name + ".png";
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        catch
        {
        }
        return GeneratePngIcon();
    }

    public static byte[] GeneratePngIcon()
    {
        const int size = 32;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                var cx = x - size / 2;
                var cy = y - size / 2;
                var dist = Math.Sqrt(cx * cx + cy * cy);
                if (dist < size / 2 - 1)
                {
                    pixels[i] = 0xD4;
                    pixels[i + 1] = 0x78;
                    pixels[i + 2] = 0x00;
                    pixels[i + 3] = 0xFF;
                }
                else
                {
                    pixels[i + 3] = 0;
                }
            }
        }
        using var ms = new MemoryStream();
        WritePng(ms, size, size, pixels);
        return ms.ToArray();
    }

    private static void WritePng(Stream stream, int width, int height, byte[] rgba)
    {
        using var bw = new BinaryWriter(stream);
        bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(bw, "IHDR", BuildIhdr(width, height));
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }
        var compressed = Compress(raw);
        WriteChunk(bw, "IDAT", compressed);
        WriteChunk(bw, "IEND", Array.Empty<byte>());
    }

    private static byte[] BuildIhdr(int w, int h)
    {
        var data = new byte[13];
        data[0] = (byte)(w >> 24); data[1] = (byte)(w >> 16); data[2] = (byte)(w >> 8); data[3] = (byte)w;
        data[4] = (byte)(h >> 24); data[5] = (byte)(h >> 16); data[6] = (byte)(h >> 8); data[7] = (byte)h;
        data[8] = 8; data[9] = 6; data[10] = 0; data[11] = 0; data[12] = 0;
        return data;
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
        {
            z.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        bw.Write(Swap(data.Length));
        bw.Write(typeBytes);
        bw.Write(data);
        var crc = Crc32(typeBytes, data);
        bw.Write(Swap(crc));
    }

    private static uint Swap(int value)
    {
        return (uint)(((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value >> 8) & 0xFF00) | ((value >> 24) & 0xFF));
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public const string BaseCss = @"
:root{--accent:#0078D4;--bg:#0F0F12;--panel:#17171C;--panel2:#1E1E25;--text:#EDEDED;--muted:#9A9AA5;--border:#2A2A33;--ok:#3FBF6F;--bad:#E25B5B;--warn:#E0A63C;--radius:14px;}
*{box-sizing:border-box;}
html,body{margin:0;padding:0;height:100%;}
body{font-family:'Segoe UI Variable','Segoe UI',system-ui,sans-serif;background:radial-gradient(1200px 600px at 10% -10%,rgba(0,120,212,.18),transparent 60%),radial-gradient(900px 500px at 110% 110%,rgba(120,60,255,.12),transparent 50%),var(--bg);color:var(--text);font-size:14px;}
body.light{--bg:#F4F5F7;--panel:#FFFFFF;--panel2:#F0F1F4;--text:#1B1B1F;--muted:#5E5E6A;--border:#E2E3E8;}
.shell{display:flex;min-height:100vh;}
.sidebar{width:220px;flex:0 0 220px;padding:18px 12px;display:flex;flex-direction:column;gap:4px;border-right:1px solid var(--border);background:color-mix(in srgb,var(--panel) 80%,transparent);backdrop-filter:blur(18px);position:sticky;top:0;height:100vh;}
.brand{display:flex;align-items:center;gap:10px;font-weight:700;font-size:16px;padding:6px 10px 18px;letter-spacing:.02em;}
.logo{width:28px;height:28px;border-radius:9px;background:linear-gradient(135deg,var(--accent),#7C5CFF);display:flex;align-items:center;justify-content:center;color:#fff;font-weight:800;}
.navitem{display:flex;align-items:center;gap:10px;padding:10px 12px;border-radius:10px;color:var(--text);text-decoration:none;transition:.15s;opacity:.85;}
.navitem:hover{background:var(--panel2);opacity:1;}
.navitem.active{background:color-mix(in srgb,var(--accent) 22%,transparent);color:#fff;opacity:1;box-shadow:inset 0 0 0 1px color-mix(in srgb,var(--accent) 55%,transparent);}
.ico{width:18px;text-align:center;}
.spacer{flex:1;}
.foot{font-size:11px;color:var(--muted);padding:8px 12px;}
.content{flex:1;padding:28px 32px 60px;max-width:1200px;}
h1{font-size:28px;margin:0 0 6px;font-weight:700;letter-spacing:-.02em;}
h2{font-size:18px;margin:22px 0 10px;font-weight:600;}
.sub{color:var(--muted);margin-bottom:24px;}
.card{background:color-mix(in srgb,var(--panel) 92%,transparent);border:1px solid var(--border);border-radius:var(--radius);padding:18px 20px;margin-bottom:16px;backdrop-filter:blur(10px);}
.card-h{font-weight:600;font-size:13px;text-transform:uppercase;letter-spacing:.08em;color:var(--muted);margin-bottom:12px;}
.grid{display:grid;gap:14px;}
.g2{grid-template-columns:repeat(2,1fr);}
.g3{grid-template-columns:repeat(3,1fr);}
.g4{grid-template-columns:repeat(4,1fr);}
.stat{background:var(--panel2);border:1px solid var(--border);border-radius:12px;padding:14px 16px;}
.stat-v{font-size:24px;font-weight:700;font-variant-numeric:tabular-nums;}
.stat-l{color:var(--muted);font-size:12px;margin-top:4px;}
.btn{background:var(--accent);color:#fff;border:0;border-radius:10px;padding:9px 16px;font-weight:600;cursor:pointer;font-size:13px;transition:.15s;}
.btn:hover{filter:brightness(1.1);transform:translateY(-1px);}
.btn.ghost{background:var(--panel2);color:var(--text);border:1px solid var(--border);}
.btn.danger{background:var(--bad);}
.btn.sm{padding:5px 10px;font-size:12px;border-radius:8px;}
input,select,textarea{background:var(--panel2);border:1px solid var(--border);color:var(--text);border-radius:10px;padding:9px 12px;font-size:13px;outline:none;width:100%;}
input:focus,select:focus,textarea:focus{border-color:var(--accent);box-shadow:0 0 0 3px color-mix(in srgb,var(--accent) 25%,transparent);}
table{width:100%;border-collapse:collapse;font-size:13px;}
th{text-align:left;color:var(--muted);font-weight:600;padding:8px 10px;border-bottom:1px solid var(--border);font-size:11px;text-transform:uppercase;letter-spacing:.06em;}
td{padding:9px 10px;border-bottom:1px solid var(--border);vertical-align:middle;}
tr:hover td{background:color-mix(in srgb,var(--accent) 7%,transparent);}
a{color:var(--accent);}
.badge{display:inline-block;padding:2px 8px;border-radius:999px;font-size:11px;font-weight:600;background:var(--panel2);border:1px solid var(--border);}
.badge.ok{color:var(--ok);border-color:color-mix(in srgb,var(--ok) 40%,transparent);background:color-mix(in srgb,var(--ok) 12%,transparent);}
.badge.bad{color:var(--bad);border-color:color-mix(in srgb,var(--bad) 40%,transparent);background:color-mix(in srgb,var(--bad) 12%,transparent);}
.badge.warn{color:var(--warn);border-color:color-mix(in srgb,var(--warn) 40%,transparent);background:color-mix(in srgb,var(--warn) 12%,transparent);}
.row{display:flex;gap:10px;align-items:center;flex-wrap:wrap;}
.grow{flex:1;}
.muted{color:var(--muted);}
.mono{font-family:'Cascadia Code',Consolas,monospace;font-size:12px;}
.progress{height:8px;border-radius:99px;background:var(--panel2);overflow:hidden;border:1px solid var(--border);}
.progress>i{display:block;height:100%;background:linear-gradient(90deg,var(--accent),#7C5CFF);border-radius:99px;transition:width .3s;}
.toast{position:fixed;bottom:20px;right:20px;background:var(--panel);border:1px solid var(--border);padding:12px 16px;border-radius:12px;box-shadow:0 12px 40px rgba(0,0,0,.4);opacity:0;transform:translateY(10px);transition:.25s;z-index:99;}
.toast.show{opacity:1;transform:none;}
.toggle{position:relative;width:44px;height:24px;background:var(--panel2);border:1px solid var(--border);border-radius:99px;cursor:pointer;display:inline-block;vertical-align:middle;}
.toggle:after{content:'';position:absolute;top:2px;left:2px;width:18px;height:18px;border-radius:50%;background:var(--muted);transition:.2s;}
.toggle.on{background:color-mix(in srgb,var(--accent) 45%,transparent);border-color:var(--accent);}
.toggle.on:after{left:22px;background:#fff;}
.kbd{background:var(--panel2);border:1px solid var(--border);border-bottom-width:2px;border-radius:6px;padding:1px 6px;font-family:'Cascadia Code',monospace;font-size:11px;}
.empty{text-align:center;padding:60px 20px;color:var(--muted);}
.empty .big{font-size:48px;margin-bottom:12px;}
.searchbox{position:relative;margin-bottom:18px;}
.searchbox input{padding-left:38px;height:44px;border-radius:12px;font-size:14px;}
.searchbox:before{content:'⌕';position:absolute;left:14px;top:50%;transform:translateY(-50%);color:var(--muted);font-size:18px;}
@media(max-width:900px){.sidebar{display:none}.g2,.g3,.g4{grid-template-columns:1fr}.content{padding:18px;}}
";

    public const string BaseJs = @"
function post(msg){try{chrome.webview.postMessage(msg);}catch(e){console.log('post',msg);}}
function toast(text){var t=document.getElementById('__toast');if(!t){t=document.createElement('div');t.id='__toast';t.className='toast';document.body.appendChild(t);}t.textContent=text;t.classList.add('show');clearTimeout(t._h);t._h=setTimeout(function(){t.classList.remove('show');},2600);}
window.addEventListener('message',function(e){
var d=e.data||{};
if(d.type==='swifter-data'){
window.SWIFTER=window.SWIFTER||{};window.SWIFTER[d.key]=d.value;
document.dispatchEvent(new CustomEvent('swifter-data',{detail:d}));
render&&render();
}
});
function requestData(key){post({type:'swifter-request',key:key});}
function navigate(url){post({type:'swifter-navigate',url:url});}
function command(cmd,payload){post({type:'swifter-command',cmd:cmd,payload:payload||{}});}
document.addEventListener('click',function(e){
var el=e.target.closest('[data-action]');
if(!el)return;
var action=el.getAttribute('data-action');
var url=el.getAttribute('data-url');
if(action==='navigate'&&url){navigate(url);return;}
if(action==='open'&&url){navigate(url);return;}
command(action,{url:url,id:el.getAttribute('data-id'),value:el.getAttribute('data-value'),payload:el.getAttribute('data-payload')});
});
document.addEventListener('keydown',function(e){
if((e.ctrlKey||e.metaKey)&&e.key==='k'){e.preventDefault();post({type:'swifter-command',cmd:'palette'});}
});
requestData('init');
";
}
