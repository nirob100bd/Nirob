using System.Text;

namespace Swifter.Core.Protocols.InternalPages;

public static class HistoryPage
{
    public static string Render(Dictionary<string, string> query)
    {
        query.TryGetValue("q", out var q);
        query.TryGetValue("from", out var from);
        query.TryGetValue("to", out var to);
        var sb = new StringBuilder(32768);
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/><title>History</title><style>");
        sb.AppendLine(ProtocolPageRenderer.BaseCss);
        sb.AppendLine(HistoryCss);
        sb.AppendLine("</style></head><body class='dark'>");
        sb.AppendLine("<div class='shell'>");
        sb.AppendLine("<nav class='sidebar'><div class='brand'><span class='logo'>S</span><span>Swifter</span></div>");
        sb.AppendLine("<a class='navitem' href='swifter://newtab'><span class='ico'>⌂</span>New Tab</a>");
        sb.AppendLine("<a class='navitem' href='swifter://downloads'><span class='ico'>⬇</span>Downloads</a>");
        sb.AppendLine("<a class='navitem active' href='swifter://history'><span class='ico'>🕘</span>History</a>");
        sb.AppendLine("<a class='navitem' href='swifter://bookmarks'><span class='ico'>★</span>Bookmarks</a>");
        sb.AppendLine("<a class='navitem' href='swifter://shields'><span class='ico'>🛡</span>Shields</a>");
        sb.AppendLine("<a class='navitem' href='swifter://settings'><span class='ico'>⚙</span>Settings</a>");
        sb.AppendLine("<div class='spacer'></div><div class='foot'>swifter://history</div></nav>");
        sb.AppendLine("<main class='content'>");
        sb.AppendLine("<h1>History</h1><div class='sub' id='summary'>Searchable timeline of every page you visit</div>");
        sb.AppendLine("<div class='searchbox'><input id='q' placeholder='Search history… (title or URL)' value=\"" + System.Net.WebUtility.HtmlEncode(q ?? "") + "\"/></div>");
        sb.AppendLine("<div class='row' style='margin-bottom:14px'>");
        sb.AppendLine("<label class='muted'>From</label><input type='date' id='from' style='width:170px' value=\"" + System.Net.WebUtility.HtmlEncode(from ?? "") + "\"/>");
        sb.AppendLine("<label class='muted'>To</label><input type='date' id='to' style='width:170px' value=\"" + System.Net.WebUtility.HtmlEncode(to ?? "") + "\"/>");
        sb.AppendLine("<button class='btn ghost sm' data-action='hist-apply'>Apply</button>");
        sb.AppendLine("<span class='grow'></span>");
        sb.AppendLine("<button class='btn ghost sm' data-action='hist-export'>Export CSV</button>");
        sb.AppendLine("<button class='btn danger sm' data-action='hist-clear'>Clear All</button>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='card'><div id='chart' class='chart'></div></div>");
        sb.AppendLine("<div class='card' style='padding:0;overflow:hidden'><table><thead><tr><th style='width:170px'>Time</th><th>Title</th><th>URL</th><th style='width:70px'>Visits</th><th style='width:90px'></th></tr></thead><tbody id='rows'></tbody></table>");
        sb.AppendLine("<div id='empty' class='empty' style='display:none'><div class='big'>🕘</div><div>No history entries match your search.</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='row' style='margin-top:12px'><button class='btn ghost sm' data-action='hist-more'>Load more</button><span class='muted' id='count'></span></div>");
        sb.AppendLine("</main></div>");
        sb.AppendLine("<script>");
        sb.AppendLine(ProtocolPageRenderer.BaseJs);
        sb.AppendLine(HistoryJs);
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private const string HistoryCss = @"
.chart{display:flex;align-items:flex-end;gap:4px;height:90px;padding-top:8px;}
.chart .bar{flex:1;background:linear-gradient(180deg,var(--accent),#7C5CFF);border-radius:4px 4px 0 0;min-height:3px;position:relative;}
.chart .bar:hover:after{content:attr(data-tip);position:absolute;bottom:100%;left:50%;transform:translateX(-50%);background:#000;color:#fff;font-size:11px;padding:4px 8px;border-radius:6px;white-space:nowrap;margin-bottom:6px;z-index:3;}
tr .actions{opacity:0;transition:.15s;}
tr:hover .actions{opacity:1;}
";

    private const string HistoryJs = @"
var offset=0;var LIMIT=150;
function esc(s){var d=document.createElement('div');d.textContent=s==null?'':String(s);return d.innerHTML();}
function fmt(t){var d=new Date(t);return d.toLocaleDateString([], {month:'short',day:'numeric'})+' '+d.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit'});}
function query(){
var q=document.getElementById('q').value.trim();
var f=document.getElementById('from').value;var t=document.getElementById('to').value;
command('history-search',{q:q,from:f,to:t,offset:offset,limit:LIMIT});
}
function appendRows(items,reset){
var body=document.getElementById('rows');
if(reset)body.innerHTML='';
items.forEach(function(h){
var tr=document.createElement('tr');
tr.innerHTML='<td class="muted">'+esc(fmt(h.time))+'</td>'+
'<td><a href="#" data-url="'+esc(h.url)+'">'+esc(h.title||h.url)+'</a></td>'+
'<td class="mono muted" style="max-width:340px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">'+esc(h.url)+'</td>'+
'<td>'+h.visits+'</td>'+
'<td class="actions"><button class="btn sm ghost" data-action="hist-delete" data-id="'+h.id+'">Delete</button></td>';
body.appendChild(tr);
});
document.getElementById('empty').style.display=body.children.length?'none':'block';
}
function drawChart(data){
var host=document.getElementById('chart');
if(!data||!data.length){host.innerHTML='';return;}
var max=Math.max.apply(null,data.map(function(d){return d.count;}))||1;
host.innerHTML=data.map(function(d){
var pct=Math.round(d.count*100/max);
return '<div class="bar" style="height:'+Math.max(4,pct)+'%" data-tip="'+esc(d.day)+': '+d.count+'"></div>';
}).join('');
}
function render(){
var d=window.SWIFTER||{};
if(d.history)appendRows(d.history,offset===0);
if(d.historyChart)drawChart(d.historyChart);
if(typeof d.historyTotal==='number')document.getElementById('count').textContent=d.historyTotal+' entries';
if(d.historySummary)document.getElementById('summary').textContent=d.historySummary;
}
document.addEventListener('swifter-data',function(e){
var d=e.detail;
if(d.key==='history'){render();}
});
var t=null;
document.getElementById('q').addEventListener('input',function(){clearTimeout(t);t=setTimeout(function(){offset=0;query();},220);});
document.addEventListener('click',function(e){
var a=e.target.closest('a[data-url]');
if(a){e.preventDefault();navigate(a.getAttribute('data-url'));}
});
window.addEventListener('load',function(){query();});
";
}
