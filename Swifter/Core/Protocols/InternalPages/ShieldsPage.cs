using System.Text;

namespace Swifter.Core.Protocols.InternalPages;

public static class ShieldsPage
{
    public static string Render(Dictionary<string, string> query)
    {
        query.TryGetValue("host", out var host);
        var sb = new StringBuilder(32768);
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Shields</title><style>");
        sb.AppendLine(ProtocolPageRenderer.BaseCss);
        sb.AppendLine(ShieldsCss);
        sb.AppendLine("</style></head><body class='dark'>");
        sb.AppendLine("<div class='shell'>");
        sb.AppendLine("<nav class='sidebar'><div class='brand'><span class='logo'>S</span><span>Swifter</span></div>");
        sb.AppendLine("<a class='navitem' href='swifter://newtab'><span class='ico'>⌂</span>New Tab</a>");
        sb.AppendLine("<a class='navitem' href='swifter://downloads'><span class='ico'>⬇</span>Downloads</a>");
        sb.AppendLine("<a class='navitem' href='swifter://history'><span class='ico'>🕘</span>History</a>");
        sb.AppendLine("<a class='navitem' href='swifter://bookmarks'><span class='ico'>★</span>Bookmarks</a>");
        sb.AppendLine("<a class='navitem active' href='swifter://shields'><span class='ico'>🛡</span>Shields</a>");
        sb.AppendLine("<a class='navitem' href='swifter://settings'><span class='ico'>⚙</span>Settings</a>");
        sb.AppendLine("<div class='spacer'></div><div class='foot'>swifter://shields</div></nav>");
        sb.AppendLine("<main class='content'>");
        sb.AppendLine("<h1>Shields</h1><div class='sub'>Network interception, cosmetic hiding, fingerprint defense, and WebRTC leak guard</div>");
        sb.AppendLine("<div class='hero'>");
        sb.AppendLine("<div class='hero-icon' id='heroIcon'>🛡</div>");
        sb.AppendLine("<div class='hero-main'>");
        sb.AppendLine("<div class='hero-title'>Shields are <span id='shieldState'>UP</span></div>");
        sb.AppendLine("<div class='hero-sub' id='shieldHost'>" + System.Net.WebUtility.HtmlEncode(host ?? "All sites") + "</div>");
        sb.AppendLine("<div class='row' style='margin-top:12px'>");
        sb.AppendLine("<button class='btn' data-action='shields-toggle'>Toggle for this site</button>");
        sb.AppendLine("<button class='btn ghost' data-action='shields-pause-host'>Pause 5 min</button>");
        sb.AppendLine("<button class='btn ghost' data-action='shields-resume'>Resume</button>");
        sb.AppendLine("</div></div>");
        sb.AppendLine("<div class='ring'><div class='ring-v' id='totalBlocked'>0</div><div class='ring-l'>blocked</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='grid g4' style='margin:16px 0'>");
        sb.AppendLine("<div class='stat'><div class='stat-v' id='sNetwork' style='color:#7CD4FF'>0</div><div class='stat-l'>Network requests</div></div>");
        sb.AppendLine("<div class='stat'><div class='stat-v' id='sCosmetic' style='color:#C792FF'>0</div><div class='stat-l'>Cosmetic elements</div></div>");
        sb.AppendLine("<div class='stat'><div class='stat-v' id='sTrackers' style='color:#FFB86C'>0</div><div class='stat-l'>Tracking params</div></div>");
        sb.AppendLine("<div class='stat'><div class='stat-v' id='sRules' style='color:var(--ok)'>0</div><div class='stat-l'>Filter rules loaded</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='grid g2'>");
        sb.AppendLine("<section class='card'><header class='card-h'>Protections</header><div id='protections'></div></section>");
        sb.AppendLine("<section class='card'><header class='card-h'>Recent blocked</header><div id='blocked'></div><div class='empty' id='blockedEmpty' style='display:block;padding:30px'><div class='big'>✓</div><div>Nothing blocked yet on this page load.</div></div></section>");
        sb.AppendLine("</div>");
        sb.AppendLine("<section class='card'><header class='card-h'>WebRTC Leak Guard</header><div id='webrtc'></div>");
        sb.AppendLine("<div class='row' style='margin-top:10px'><button class='btn ghost sm' data-action='webrtc-scan'>Scan local IPs</button><button class='btn ghost sm' data-action='webrtc-firewall'>Copy firewall rules</button></div>");
        sb.AppendLine("</section>");
        sb.AppendLine("<section class='card'><header class='card-h'>Custom rules</header>");
        sb.AppendLine("<div class='row'><input id='customRule' placeholder='||example-ads.com^ or ##.sponsored' class='grow'/><button class='btn' data-action='rule-add'>Add rule</button></div>");
        sb.AppendLine("<div class='muted' style='margin-top:8px;font-size:12px'>Accepts EasyList syntax — domain anchors, path patterns, and cosmetic selectors.</div>");
        sb.AppendLine("</section>");
        sb.AppendLine("</main></div>");
        sb.AppendLine("<script>");
        sb.AppendLine(ProtocolPageRenderer.BaseJs);
        sb.AppendLine(ShieldsJs);
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private const string ShieldsCss = @"
.hero{display:flex;gap:22px;align-items:center;background:linear-gradient(135deg,color-mix(in srgb,var(--accent) 25%,var(--panel)),var(--panel));border:1px solid var(--border);border-radius:18px;padding:22px 26px;margin-bottom:16px;}
.hero-icon{font-size:52px;filter:drop-shadow(0 8px 20px rgba(0,120,212,.5));}
.hero-main{flex:1;}
.hero-title{font-size:22px;font-weight:700;}
.hero-title span{color:var(--ok);}
.hero-title span.down{color:var(--bad);}
.hero-sub{color:var(--muted);margin-top:4px;font-family:'Cascadia Code',monospace;font-size:12px;}
.ring{width:110px;height:110px;border-radius:50%;border:3px solid color-mix(in srgb,var(--accent) 60%,transparent);display:flex;flex-direction:column;align-items:center;justify-content:center;background:rgba(0,0,0,.25);}
.ring-v{font-size:28px;font-weight:800;}
.ring-l{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.1em;}
.prot{display:flex;justify-content:space-between;align-items:center;padding:10px 0;border-bottom:1px solid var(--border);}
.prot:last-child{border-bottom:0;}
.prot b{font-size:13px;display:block;}
.prot small{color:var(--muted);}
.bl{display:flex;gap:10px;align-items:center;padding:8px 0;border-bottom:1px solid var(--border);font-size:12px;}
.bl:last-child{border-bottom:0;}
.bl .dot{width:8px;height:8px;border-radius:50%;background:var(--bad);flex-shrink:0;}
.bl .u{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:var(--muted);}
.ip{font-family:'Cascadia Code',monospace;background:var(--panel2);border:1px solid var(--border);padding:6px 10px;border-radius:8px;margin:4px 6px 0 0;display:inline-block;font-size:12px;}
.ip.leak{color:var(--bad);border-color:color-mix(in srgb,var(--bad) 50%,transparent);}
.ip.safe{color:var(--ok);border-color:color-mix(in srgb,var(--ok) 50%,transparent);}
";

    private const string ShieldsJs = @"
var PROTECTIONS=[
{id:'adblock',t:'Ad / tracker blocking',s:'EasyList trie + domain matcher',on:true},
{id:'cosmetic',t:'Cosmetic element hiding',s:'Collapses empty slots left by blocked ads',on:true},
{id:'fingerprint',t:'Anti-fingerprinting',s:'Canvas, WebGL, Audio, ClientRect noise',on:true},
{id:'webrtc',t:'WebRTC leak prevention',s:'Relay-only ICE, no non-proxied UDP',on:true},
{id:'paramstrip',t:'Tracking parameter stripping',s:'utm_*, fbclid, gclid, msclkid, ref, _hsenc',on:true},
{id:'scriptblock',t:'Script blocker',s:'Cryptojackers, notification & clipboard hijacks',on:true},
{id:'referrer',t:'Strict referrer policy',s:'Downgrade cross-origin referrers',on:true},
{id:'headers',t:'UA / client hints masking',s:'Uniform fingerprint across sites',on:true}
];
function renderProtections(){
document.getElementById('protections').innerHTML=PROTECTIONS.map(function(p){
return '<div class="prot"><div><b>'+p.t+'</b><small>'+p.s+'</small></div>'+
'<span class="toggle '+(p.on?'on':'')+'" data-action="prot-'+p.id+'" data-id="'+p.id+'"></span></div>';
}).join('');
}
function renderStats(){
var d=window.SWIFTER||{};
var s=d.shieldStats||{};
document.getElementById('sNetwork').textContent=s.network||0;
document.getElementById('sCosmetic').textContent=s.cosmetic||0;
document.getElementById('sTrackers').textContent=s.trackers||0;
document.getElementById('sRules').textContent=s.rules||0;
document.getElementById('totalBlocked').textContent=(s.network||0)+(s.cosmetic||0);
document.getElementById('shieldState').textContent=s.enabled===false?'DOWN':'UP';
document.getElementById('shieldState').className=s.enabled===false?'down':'';
document.getElementById('heroIcon').textContent=s.enabled===false?'⚠':'🛡';
if(d.blockedRecent&&d.blockedRecent.length){
document.getElementById('blockedEmpty').style.display='none';
document.getElementById('blocked').innerHTML=d.blockedRecent.slice(0,14).map(function(b){
return '<div class="bl"><span class="dot"></span><span class="u">'+esc(b.host)+'</span><span class="badge bad">'+esc(b.type)+'</span></div>';
}).join('');
}
if(d.webrtcIps){
document.getElementById('webrtc').innerHTML=d.webrtcIps.map(function(ip){
return '<span class="ip '+(ip.local?'safe':'leak')+'">'+esc(ip.addr)+(ip.local?' · local':' · leak?')+'</span>';
}).join('')||'<span class="muted">No ICE candidates observed yet.</span>';
}
}
function esc(s){var d=document.createElement('div');d.textContent=s==null?'':String(s);return d.innerHTML();}
function render(){renderProtections();renderStats();}
document.addEventListener('click',function(e){
var t=e.target.closest('[data-action^=prot-]');
if(t){
var id=t.getAttribute('data-id');
var p=PROTECTIONS.find(function(x){return x.id===id;});
if(p){p.on=!p.on;t.classList.toggle('on',p.on);command('shield-protection',{id:id,on:p.on});}
}
});
document.addEventListener('swifter-data',function(){renderStats();});
window.addEventListener('load',function(){
render();
requestData('shield-stats');
requestData('webrtc-ips');
setInterval(function(){requestData('shield-stats');},2000);
});
";
}
