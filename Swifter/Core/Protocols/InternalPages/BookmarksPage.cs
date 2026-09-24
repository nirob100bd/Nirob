using System.Text;

namespace Swifter.Core.Protocols.InternalPages;

public static class BookmarksPage
{
    public static string Render(Dictionary<string, string> query)
    {
        var sb = new StringBuilder(32768);
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Bookmarks</title><style>");
        sb.AppendLine(ProtocolPageRenderer.BaseCss);
        sb.AppendLine(BookmarksCss);
        sb.AppendLine("</style></head><body class='dark'>");
        sb.AppendLine("<div class='shell'>");
        sb.AppendLine("<nav class='sidebar'><div class='brand'><span class='logo'>S</span><span>Swifter</span></div>");
        sb.AppendLine("<a class='navitem' href='swifter://newtab'><span class='ico'>⌂</span>New Tab</a>");
        sb.AppendLine("<a class='navitem' href='swifter://downloads'><span class='ico'>⬇</span>Downloads</a>");
        sb.AppendLine("<a class='navitem' href='swifter://history'><span class='ico'>🕘</span>History</a>");
        sb.AppendLine("<a class='navitem active' href='swifter://bookmarks'><span class='ico'>★</span>Bookmarks</a>");
        sb.AppendLine("<a class='navitem' href='swifter://shields'><span class='ico'>🛡</span>Shields</a>");
        sb.AppendLine("<a class='navitem' href='swifter://settings'><span class='ico'>⚙</span>Settings</a>");
        sb.AppendLine("<div class='spacer'></div><div class='foot'>swifter://bookmarks</div></nav>");
        sb.AppendLine("<main class='content'>");
        sb.AppendLine("<h1>Bookmarks</h1><div class='sub'>Folder tree manager with search, drag targets, and export</div>");
        sb.AppendLine("<div class='row' style='margin-bottom:14px'>");
        sb.AppendLine("<div class='searchbox grow' style='margin:0'><input id='q' placeholder='Search bookmarks…'/></div>");
        sb.AppendLine("<button class='btn' data-action='bm-new-folder'>＋ New Folder</button>");
        sb.AppendLine("<button class='btn ghost' data-action='bm-import'>Import</button>");
        sb.AppendLine("<button class='btn ghost' data-action='bm-export'>Export</button>");
        sb.AppendLine("</div>");
        sb.AppendLine("<div class='bm-layout'>");
        sb.AppendLine("<div class='card tree' id='tree'></div>");
        sb.AppendLine("<div class='card list'>");
        sb.AppendLine("<div class='row' style='margin-bottom:10px'><b id='folderName'>Bookmarks Bar</b><span class='grow'></span><span class='muted' id='bmCount'></span></div>");
        sb.AppendLine("<div id='items'></div>");
        sb.AppendLine("<div id='empty' class='empty' style='display:none'><div class='big'>★</div><div>This folder is empty. Use ★ in the address bar to add pages.</div></div>");
        sb.AppendLine("</div></div>");
        sb.AppendLine("</main></div>");
        sb.AppendLine("<script>");
        sb.AppendLine(ProtocolPageRenderer.BaseJs);
        sb.AppendLine(BookmarksJs);
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    private const string BookmarksCss = @"
.bm-layout{display:grid;grid-template-columns:260px 1fr;gap:14px;align-items:start;}
.tree{padding:10px;max-height:70vh;overflow:auto;}
.fnode{padding:8px 10px;border-radius:8px;cursor:pointer;display:flex;gap:8px;align-items:center;font-size:13px;}
.fnode:hover{background:var(--panel2);}
.fnode.active{background:color-mix(in srgb,var(--accent) 22%,transparent);color:#fff;}
.fnode .cnt{margin-left:auto;color:var(--muted);font-size:11px;}
.fnode.child{margin-left:18px;}
.fnode.grandchild{margin-left:36px;}
.bm{display:flex;align-items:center;gap:10px;padding:10px 8px;border-radius:10px;border-bottom:1px solid var(--border);}
.bm:last-child{border-bottom:0;}
.bm:hover{background:var(--panel2);}
.bm .ic{width:26px;height:26px;border-radius:7px;background:linear-gradient(135deg,var(--accent),#7C5CFF);display:flex;align-items:center;justify-content:center;font-weight:700;color:#fff;font-size:12px;flex-shrink:0;}
.bm .t{flex:1;min-width:0;}
.bm .t b{display:block;font-size:13px;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.bm .t span{display:block;font-size:11px;color:var(--muted);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
.bm .ops{display:flex;gap:6px;opacity:0;transition:.15s;}
.bm:hover .ops{opacity:1;}
@media(max-width:860px){.bm-layout{grid-template-columns:1fr;}}
";

    private const string BookmarksJs = @"
var currentFolder=0;var treeCache=[];
function esc(s){var d=document.createElement('div');d.textContent=s==null?'':String(s);return d.innerHTML();}
function drawTree(nodes,depth){
var host=document.getElementById('tree');
nodes.forEach(function(n){
var div=document.createElement('div');
div.className='fnode '+(n.id===currentFolder?'active ':'')+(depth===1?'child':depth>=2?'grandchild':'');
div.innerHTML='<span>'+(n.folder?'📁':'★')+'</span><span>'+esc(n.title)+'</span><span class="cnt">'+(n.children?n.children.length:'')+'</span>';
div.addEventListener('click',function(){currentFolder=n.id;document.getElementById('folderName').textContent=n.title;command('bookmarks-open-folder',{id:n.id});refresh();});
host.appendChild(div);
if(n.children&&n.children.some(function(c){return c.folder;})){
drawTree(n.children.filter(function(c){return c.folder;}),depth+1);
}
});
}
function refresh(){
var host=document.getElementById('tree');host.innerHTML='';
command('bookmarks-tree',{});
command('bookmarks-list',{id:currentFolder,q:document.getElementById('q').value});
}
function render(){
var d=window.SWIFTER||{};
if(d.bookmarksTree){
treeCache=d.bookmarksTree;
var host=document.getElementById('tree');host.innerHTML='';
drawTree(treeCache,0);
}
if(d.bookmarks){
var host=document.getElementById('items');
host.innerHTML=d.bookmarks.map(function(b){
return '<div class="bm"><div class="ic">'+esc((b.title||'?').charAt(0).toUpperCase())+'</div>'+
'<div class="t"><b>'+esc(b.title)+'</b><span>'+esc(b.url)+'</span></div>'+
'<div class="ops">'+
'<button class="btn sm ghost" data-action="bm-open" data-url="'+esc(b.url)+'">Open</button>'+
'<button class="btn sm ghost" data-action="bm-rename" data-id="'+b.id+'">Rename</button>'+
'<button class="btn sm danger" data-action="bm-delete" data-id="'+b.id+'">Delete</button>'+
'</div></div>';
}).join('');
document.getElementById('empty').style.display=d.bookmarks.length?'none':'block';
document.getElementById('bmCount').textContent=d.bookmarks.length+' items';
}
}
document.addEventListener('swifter-data',function(){render();});
var t=null;
document.getElementById('q').addEventListener('input',function(){clearTimeout(t);t=setTimeout(function(){command('bookmarks-list',{id:currentFolder,q:document.getElementById('q').value});},200);});
window.addEventListener('load',function(){refresh();});
";
}
