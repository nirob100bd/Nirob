using System.Text;

namespace Swifter.Core.Protocols.InternalPages;

public static class DownloadsPage
{
    public static string Render(Dictionary<string, string> query)
    {
        var sb = new StringBuilder(32768);
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Downloads</title><style>");
        sb.AppendLine(ProtocolPageRenderer.BaseCss);
        sb.AppendLine(DownloadsCss);
        sb.AppendLine("</style></head><body class='dark'>");
        sb.AppendLine("<div class='shell'>");
        sb.AppendLine("<nav class='sidebar'><div class='brand'><span class='logo'>S</span><span>Swifter</span></div>");
        sb.AppendLine("<a class='navitem' href='swifter://newtab'><span class='ico'>⌂</span>New Tab</a>");
        sb.AppendLine("<a class='navitem active' href='swifter://downloads'><span class='ico'>⬇</span>Downloads</a>");
        sb.AppendLine("<a class='navitem' href='swifter://history'><span class='ico'>🕘</span>History</a>");
        sb.AppendLine("<a class='navitem' href='swifter://bookmarks'><span class='ico'>★</span>Bookmarks</a>");
        sb.AppendLine("<a class='navitem' href='swifter://shields'><span class='ico'>🛡</span>Shields</a>");
        sb.AppendLine("<a class='navitem' href='swifter://settings'><span class='ico'>⚙</span>Settings</a>");
        sb.AppendLine("<div class='spacer'></div><div class='foot'>swifter://downloads</div></nav>");
        sb.AppendLine("<main class='content'>");
        sb.AppendLine("<h1>Downloads</h1><div class='sub'>IDM-grade segmented transfer engine — up to 16 parallel range streams</div>");
        sb.AppendLine("<div class='toolbar row'>");
        sb.AppendLine("<button class='btn ghost' data-action='dl-pause-all'>⏸ Pause All</button>");
        sb.AppendLine("<button class='btn ghost' data-action='dl-resume-all'>▶ Resume All</button>");
        sb.AppendLine("<button class='btn ghost' data-action='dl-clear-done'>✓ Clear Completed</button>");
        sb.AppendLine("<button class='btn ghost' data-action='dl-open-folder'>📁 Open Folder</button>");
        sb.AppendLine("<span class='grow'></span>");
        sb.AppendLine("<label class='muted'>Global throttle</label>");
        sb.AppendLine("<select id='globalThrottle' data-action='dl-global-throttle'><option value='0'>Unlimited</option><option value='512'>512 KB/s</option><option value='1024'>1 MB/s</option><option value='5120'>5 MB/s</option><option value='10240'>10 MB/s</option></select>");
        sb.AppendLine("<label class='muted'>Segments</label>");
        sb.AppendLine("<select id='segCount' data-action='dl-segments'><option>4</option><option>8</option><option selected>16</option></select>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div id='active'></div>");
        sb.AppendLine("<h2>History</h2>");
        sb.AppendLine("<div id='done'></div>");
        sb.AppendLine("<div id='empty' class='empty' style='display:none'><div class='big'>⬇</div><div>No downloads yet. Media you encounter on the web will appear here.</div></div>");
        sb.AppendLine("</main></div>");
        sb.AppendLine("<script>");
        sb.AppendLine(ProtocolPageRenderer.BaseJs);
        sb.AppendLine(DownloadsJs);
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private const string DownloadsCss = @"
.toolbar{background:var(--panel);border:1px solid var(--border);border-radius:var(--radius);padding:12px 14px;margin-bottom:16px;}
.toolbar select{width:auto;min-width:110px;}
.dl{background:var(--panel);border:1px solid var(--border);border-radius:var(--radius);padding:14px 16px;margin-bottom:12px;}
.dl-top{display:flex;justify-content:space-between;gap:12px;align-items:flex-start;margin-bottom:8px;}
.dl-name{font-weight:600;font-size:14px;word-break:break-all;}
.dl-url{color:var(--muted);font-size:12px;word-break:break-all;margin-top:2px;}
.dl-actions{display:flex;gap:6px;flex-shrink:0;}
.dl-meta{display:flex;gap:14px;flex-wrap:wrap;color:var(--muted);font-size:12px;margin-top:8px;font-variant-numeric:tabular-nums;}
.dl-meta b{color:var(--text);font-weight:600;}
.segs{display:flex;gap:3px;margin-top:8px;height:8px;}
.segs>i{flex:1;background:var(--panel2);border-radius:3px;position:relative;overflow:hidden;border:1px solid var(--border);}
.segs>i:after{content:'';position:absolute;left:0;top:0;bottom:0;width:var(--p,0%);background:linear-gradient(90deg,var(--accent),#7C5CFF);}
.speed{color:#7CD4FF;font-family:'Cascadia Code',monospace;}
.state-downloading{color:#7CD4FF;}
.state-completed{color:var(--ok);}
.state-paused{color:var(--warn);}
.state-failed{color:var(--bad);}
";

    private const string DownloadsJs = @"
function fmtBytes(b){
if(b==null||isNaN(b))return '—';
var u=['B','KB','MB','GB','TB'];var i=0;var v=b;
while(v>=1024&&i<u.length-1){v/=1024;i++;}
return v.toFixed(v>=10||i===0?0:1)+' '+u[i];
}
function fmtEta(sec){
if(sec==null||!isFinite(sec)||sec<0)return '—';
if(sec<60)return Math.ceil(sec)+'s';
if(sec<3600)return Math.floor(sec/60)+'m '+Math.ceil(sec%60)+'s';
return Math.floor(sec/3600)+'h '+Math.floor((sec%3600)/60)+'m';
}
function stateBadge(s){
var map={0:'queued',1:'connecting',2:'downloading',3:'paused',4:'stitching',5:'completed',6:'failed',7:'canceled'};
var name=map[s]||'queued';
var cls=s===5?'ok':s===6?'bad':s===3?'warn':'';
return '<span class="badge '+cls+' state-'+name+'">'+name+'</span>';
}
function renderList(items){
var active=items.filter(function(i){return i.state<5;});
var done=items.filter(function(i){return i.state>=5;});
var ah=document.getElementById('active');var dh=document.getElementById('done');
document.getElementById('empty').style.display=items.length?'none':'block';
ah.innerHTML=active.map(function(d){
var pct=d.total>0?Math.min(100,d.received*100/d.total):0;
var segs='';
if(d.segments&&d.segments.length){
segs='<div class="segs">'+d.segments.map(function(s){
var p=s.total>0?Math.min(100,s.received*100/s.total):0;
return '<i style="--p:'+p.toFixed(1)+'%"></i>';
}).join('')+'</div>';
}
return '<div class="dl"><div class="dl-top"><div><div class="dl-name">'+esc(d.name)+'</div><div class="dl-url">'+esc(d.url)+'</div></div>'+
'<div class="dl-actions">'+
'<button class="btn sm ghost" data-action="dl-pause" data-id="'+d.id+'">⏸</button>'+
'<button class="btn sm ghost" data-action="dl-resume" data-id="'+d.id+'">▶</button>'+
'<button class="btn sm danger" data-action="dl-cancel" data-id="'+d.id+'">✕</button>'+
'</div></div>'+
'<div class="progress"><i style="width:'+pct.toFixed(1)+'%"></i></div>'+
'<div class="dl-meta">'+stateBadge(d.state)+
'<span><b>'+pct.toFixed(1)+'%</b></span>'+
'<span><b>'+fmtBytes(d.received)+'</b> / '+fmtBytes(d.total)+'</span>'+
'<span class="speed">⚡ '+fmtBytes(d.speed)+'/s</span>'+
'<span>ETA <b>'+fmtEta(d.eta)+'</b></span>'+
'<span>Segs <b>'+(d.segments?d.segments.length:d.segCount||1)+'</b></span>'+
'<span>Thr <b>'+(d.throttle?fmtBytes(d.throttle)+'/s':'∞')+'</b></span>'+
'</div>'+segs+'</div>';
}).join('');
dh.innerHTML=done.map(function(d){
var pct=d.state===5?100:(d.total>0?Math.min(100,d.received*100/d.total):0);
return '<div class="dl"><div class="dl-top"><div><div class="dl-name">'+esc(d.name)+'</div><div class="dl-url">'+esc(d.path||d.url)+'</div></div>'+
'<div class="dl-actions">'+stateBadge(d.state)+
'<button class="btn sm ghost" data-action="dl-open" data-id="'+d.id+'">Open</button>'+
'<button class="btn sm ghost" data-action="dl-folder" data-id="'+d.id+'">Folder</button>'+
'</div></div><div class="dl-meta"><span><b>'+fmtBytes(d.total)+'</b></span><span>'+(d.completedAt?new Date(d.completedAt).toLocaleString():'')+'</span></div></div>';
}).join('');
}
function esc(s){var d=document.createElement('div');d.textContent=s==null?'':String(s);return d.innerHTML;}
function render(){
if(window.SWIFTER&&SWIFTER.downloads)renderList(SWIFTER.downloads);
}
document.addEventListener('swifter-data',function(e){if(e.detail.key==='downloads')render();});
window.addEventListener('load',function(){requestData('downloads');setInterval(function(){requestData('downloads');},1000);});
";
}
