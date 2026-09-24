using System.Diagnostics;
using System.Text;

namespace Swifter.Core.Protocols.InternalPages;

public static class NewTabPage
{
    public static string Render(Dictionary<string, string> query)
    {
        var sb = new StringBuilder(32768);
        var wallpaper = ResolveWallpaper();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/><title>New Tab</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(ProtocolPageRenderer.BaseCss);
        sb.AppendLine(NtpCss);
        sb.AppendLine("</style></head><body class='dark'>");
        sb.AppendLine("<div class='ntp' style=\"background-image:url('" + wallpaper + "')\">");
        sb.AppendLine("<div class='ntp-shade'></div>");
        sb.AppendLine("<div class='ntp-inner'>");
        sb.AppendLine("<div class='clock'><div id='time'>--:--</div><div id='date'></div></div>");
        sb.AppendLine("<div class='search'>");
        sb.AppendLine("<form id='searchform' autocomplete='off'>");
        sb.AppendLine("<input id='q' placeholder='Search the web or type a URL' autofocus spellcheck='false'/>");
        sb.AppendLine("<button type='submit' class='btn'>Search</button>");
        sb.AppendLine("</form>");
        sb.AppendLine("<div id='suggest' class='suggest'></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='dial-wrap'>");
        sb.AppendLine("<div class='dial-head'><span>Speed Dial</span><button class='btn ghost sm' data-action='add-dial'>＋ Add</button></div>");
        sb.AppendLine("<div id='dial' class='dial'></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='ntp-foot'>");
        sb.AppendLine("<div id='sys' class='sys'></div>");
        sb.AppendLine("<div class='ntp-actions'>");
        sb.AppendLine("<a class='chip' href='swifter://downloads'>⬇ Downloads</a>");
        sb.AppendLine("<a class='chip' href='swifter://history'>🕘 History</a>");
        sb.AppendLine("<a class='chip' href='swifter://bookmarks'>★ Bookmarks</a>");
        sb.AppendLine("<a class='chip' href='swifter://shields'>🛡 Shields</a>");
        sb.AppendLine("<a class='chip' href='swifter://settings'>⚙ Settings</a>");
        sb.AppendLine("</div></div>");
        sb.AppendLine("</div></div>");
        sb.AppendLine("<script>");
        sb.AppendLine(ProtocolPageRenderer.BaseJs);
        sb.AppendLine(NtpJs);
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private static string ResolveWallpaper()
    {
        try
        {
            var settingsPath = Path.Combine(Program.GetUserDataRoot(), "theme.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("CustomWallpaperPath", out var p))
                {
                    var path = p.GetString();
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        return "swifter://wallpaper?path=" + Uri.EscapeDataString(path);
                }
            }
        }
        catch
        {
        }
        return "swifter://wallpaper?path=default";
    }

    public static byte[] DefaultGradientBytes()
    {
        const int w = 64, h = 36;
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                var t = (double)x / w;
                var u = (double)y / h;
                pixels[i] = (byte)(18 + 40 * t + 20 * u);
                pixels[i + 1] = (byte)(22 + 25 * (1 - t));
                pixels[i + 2] = (byte)(48 + 70 * t * u + 40 * (1 - u));
                pixels[i + 3] = 255;
            }
        }
        using var ms = new MemoryStream();
        var asm = typeof(NewTabPage).Assembly;
        using (var stream = asm.GetManifestResourceStream("Swifter.assets.wallpaper.png"))
        {
            if (stream != null)
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        WriteSimplePng(ms, w, h, pixels);
        return ms.ToArray();
    }

    private static void WriteSimplePng(Stream stream, int width, int height, byte[] rgba)
    {
        using var bw = new BinaryWriter(stream, Encoding.UTF8, true);
        bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        WriteChunk(bw, "IHDR", new byte[]
        {
            (byte)(width >> 24),(byte)(width >> 16),(byte)(width >> 8),(byte)width,
            (byte)(height >> 24),(byte)(height >> 16),(byte)(height >> 8),(byte)height,
            8,6,0,0,0
        });
        var raw = new byte[(width * 4 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (width * 4 + 1)] = 0;
            Buffer.BlockCopy(rgba, y * width * 4, raw, y * (width * 4 + 1) + 1, width * 4);
        }
        using (var ms2 = new MemoryStream())
        {
            using (var z = new System.IO.Compression.DeflateStream(ms2, System.IO.Compression.CompressionLevel.Fastest, true))
            {
                z.Write(raw, 0, raw.Length);
            }
            WriteChunk(bw, "IDAT", ms2.ToArray());
        }
        WriteChunk(bw, "IEND", Array.Empty<byte>());
        bw.Flush();
    }

    private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var len = data.Length;
        bw.Write(new byte[] { (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len });
        bw.Write(typeBytes);
        bw.Write(data);
        uint c = 0xFFFFFFFF;
        foreach (var b in typeBytes) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        c ^= 0xFFFFFFFF;
        bw.Write(new byte[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c });
    }

    private static readonly uint[] CrcTable = BuildCrc();

    private static uint[] BuildCrc()
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

    private const string NtpCss = @"
html,body{height:100%;overflow:hidden;}
.ntp{position:fixed;inset:0;background-size:cover;background-position:center;display:flex;align-items:center;justify-content:center;}
.ntp-shade{position:absolute;inset:0;background:radial-gradient(900px 500px at 50% 20%,rgba(0,0,0,.15),rgba(0,0,0,.55));backdrop-filter:blur(2px);}
.ntp-inner{position:relative;width:min(860px,92vw);display:flex;flex-direction:column;gap:26px;z-index:2;}
.clock{text-align:center;color:#fff;text-shadow:0 6px 30px rgba(0,0,0,.55);}
#time{font-size:76px;font-weight:200;letter-spacing:.06em;font-variant-numeric:tabular-nums;}
#date{font-size:16px;opacity:.85;letter-spacing:.14em;text-transform:uppercase;}
.search{position:relative;}
.search form{display:flex;gap:10px;}
.search input{height:54px;border-radius:16px;font-size:16px;background:rgba(255,255,255,.14);border:1px solid rgba(255,255,255,.28);color:#fff;padding:0 18px;backdrop-filter:blur(18px);box-shadow:0 18px 50px rgba(0,0,0,.35);}
.search input::placeholder{color:rgba(255,255,255,.7);}
.search .btn{height:54px;padding:0 22px;border-radius:16px;}
.suggest{position:absolute;left:0;right:66px;top:62px;background:rgba(18,18,24,.95);border:1px solid rgba(255,255,255,.14);border-radius:14px;overflow:hidden;display:none;z-index:5;box-shadow:0 20px 50px rgba(0,0,0,.5);}
.suggest.open{display:block;}
.suggest .s{padding:12px 16px;cursor:pointer;display:flex;gap:10px;align-items:center;color:#eee;}
.suggest .s:hover,.suggest .s.sel{background:rgba(0,120,212,.3);}
.dial-wrap{background:rgba(12,12,18,.55);border:1px solid rgba(255,255,255,.14);border-radius:18px;padding:16px;backdrop-filter:blur(18px);}
.dial-head{display:flex;justify-content:space-between;align-items:center;color:#ddd;font-size:12px;text-transform:uppercase;letter-spacing:.1em;margin-bottom:12px;}
.dial{display:grid;grid-template-columns:repeat(auto-fill,minmax(120px,1fr));gap:12px;}
.tile{background:rgba(255,255,255,.08);border:1px solid rgba(255,255,255,.12);border-radius:14px;padding:14px 10px;text-align:center;cursor:pointer;transition:.15s;color:#fff;text-decoration:none;}
.tile:hover{transform:translateY(-3px);background:rgba(255,255,255,.14);box-shadow:0 12px 30px rgba(0,0,0,.35);}
.tile .ic{width:38px;height:38px;margin:0 auto 8px;border-radius:12px;display:flex;align-items:center;justify-content:center;font-size:18px;font-weight:700;color:#fff;background:linear-gradient(135deg,var(--accent),#7C5CFF);}
.tile .tl{font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.tile .rm{position:absolute;top:4px;right:6px;font-size:11px;opacity:0;color:#f88;cursor:pointer;}
.tile:hover .rm{opacity:1;}
.ntp-foot{display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap;color:#ddd;}
.sys{font-size:12px;font-family:'Cascadia Code',monospace;background:rgba(0,0,0,.4);padding:8px 12px;border-radius:10px;border:1px solid rgba(255,255,255,.12);}
.ntp-actions{display:flex;gap:8px;flex-wrap:wrap;}
.chip{background:rgba(255,255,255,.1);border:1px solid rgba(255,255,255,.16);color:#fff;text-decoration:none;padding:7px 12px;border-radius:99px;font-size:12px;backdrop-filter:blur(10px);}
.chip:hover{background:rgba(255,255,255,.2);}
";

    private const string NtpJs = @"
var DEFAULT_DIAL=[
{t:'YouTube',u:'https://www.youtube.com',c:'#FF0000'},
{t:'Google',u:'https://www.google.com',c:'#4285F4'},
{t:'Wikipedia',u:'https://wikipedia.org',c:'#636466'},
{t:'Reddit',u:'https://reddit.com',c:'#FF4500'},
{t:'GitHub',u:'https://github.com',c:'#333'},
{t:'X',u:'https://x.com',c:'#111'},
{t:'ChatGPT',u:'https://chatgpt.com',c:'#10A37F'},
{t:'Netflix',u:'https://netflix.com',c:'#E50914'},
{t:'Amazon',u:'https://amazon.com',c:'#FF9900'},
{t:'Spotify',u:'https://open.spotify.com',c:'#1DB954'},
{t:'Gmail',u:'https://mail.google.com',c:'#EA4335'},
{t:'Maps',u:'https://maps.google.com',c:'#34A853'}
];
function getDial(){try{var d=JSON.parse(localStorage.getItem('swifter-dial')||'null');return Array.isArray(d)&&d.length?d:DEFAULT_DIAL.slice();}catch(e){return DEFAULT_DIAL.slice();}}
function saveDial(d){try{localStorage.setItem('swifter-dial',JSON.stringify(d));}catch(e){}}
function renderDial(){
var d=getDial();var host=document.getElementById('dial');
host.innerHTML='';
d.forEach(function(item,i){
var a=document.createElement('a');a.className='tile';a.href='#';a.setAttribute('data-url',item.u);
var letter=(item.t||'?').charAt(0).toUpperCase();
a.innerHTML='<div class="ic" style="background:linear-gradient(135deg,'+(item.c||'#0078D4')+',#7C5CFF)">'+letter+'</div><div class="tl">'+item.t+'</div><span class="rm" title="Remove">✕</span>';
a.addEventListener('click',function(e){
if(e.target.classList.contains('rm')){e.preventDefault();e.stopPropagation();var dd=getDial();dd.splice(i,1);saveDial(dd);renderDial();return;}
e.preventDefault();navigate(item.u);
});
host.appendChild(a);
});
}
function tick(){
var now=new Date();
document.getElementById('time').textContent=now.toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'});
document.getElementById('date').textContent=now.toLocaleDateString([],{weekday:'long',month:'long',day:'numeric'});
}
function sysTick(){
var m=performance.memory?Math.round(performance.memory.usedJSHeapSize/1048576)+' MB JS':navigator.hardwareConcurrency+' threads · online '+(navigator.onLine?'yes':'no');
document.getElementById('sys').textContent='⚡ '+m+' · '+new Date().toLocaleTimeString();
}
function initSearch(){
var form=document.getElementById('searchform');var q=document.getElementById('q');var box=document.getElementById('suggest');
form.addEventListener('submit',function(e){e.preventDefault();var v=q.value.trim();if(!v)return;
if(/^https?:\/\//i.test(v)||/^[\w-]+(\.[\w-]+)+/.test(v)&&!v.includes(' ')){navigate(v.startsWith('http')?v:'https://'+v);}
else{navigate('https://www.google.com/search?q='+encodeURIComponent(v));}
});
var sel=-1;
q.addEventListener('input',function(){
var v=q.value.trim();sel=-1;
if(!v){box.classList.remove('open');box.innerHTML='';return;}
if(window.SWIFTER&&SWIFTER.suggestions&&SWIFTER.suggestions.length){
box.innerHTML=SWIFTER.suggestions.map(function(s,i){return '<div class="s" data-i="'+i+'" data-url="'+s.url+'"><span>⌕</span><span>'+s.title+'</span></div>';}).join('');
box.classList.add('open');
box.querySelectorAll('.s').forEach(function(el){el.addEventListener('click',function(){navigate(el.getAttribute('data-url'));});});
}else{box.classList.remove('open');}
});
q.addEventListener('keydown',function(e){
var items=box.querySelectorAll('.s');
if(e.key==='ArrowDown'){e.preventDefault();sel=Math.min(items.length-1,sel+1);}
else if(e.key==='ArrowUp'){e.preventDefault();sel=Math.max(0,sel-1);}
else if(e.key==='Enter'&&sel>=0&&items[sel]){e.preventDefault();navigate(items[sel].getAttribute('data-url'));return;}
items.forEach(function(el,i){el.classList.toggle('sel',i===sel);});
});
}
function addDialog(){
var u=prompt('Speed dial URL','https://');
if(!u)return;var t=prompt('Label',u.replace(/^https?:\/\//,'').split('/')[0]);
var d=getDial();d.push({t:t||u,u:u,c:'#0078D4'});saveDial(d);renderDial();
}
document.addEventListener('click',function(e){
var el=e.target.closest('[data-action]');
if(el&&el.getAttribute('data-action')==='add-dial'){e.preventDefault();addDialog();}
});
document.addEventListener('swifter-data',function(e){
if(e.detail&&e.detail.key==='suggestions'){}
});
function render(){}
window.addEventListener('load',function(){renderDial();initSearch();tick();sysTick();setInterval(tick,1000);setInterval(sysTick,4000);requestData('suggestions');requestData('sysmetrics');});
";
}
