using System.Text.Json;

namespace Swifter.Core.Downloads;

public sealed class MediaOverlayInjector
{
    public bool Enabled { get; set; } = true;
    public bool AutoDetect { get; set; } = true;
    public event Action<MediaAsset> DownloadRequested;

    public string BuildOverlayScript()
    {
        return @"
(function(){
if(window.__swifter_overlay__)return;window.__swifter_overlay__=true;
function css(){
var s=document.createElement('style');
s.textContent=''+
'.sw_dl_root{position:fixed;z-index:2147483646;font-family:Segoe UI,sans-serif;pointer-events:auto;}'+
'.sw_dl_btn{display:flex;align-items:center;gap:6px;background:linear-gradient(135deg,rgba(20,20,25,.88),rgba(35,35,45,.92));color:#fff;border:1px solid rgba(255,255,255,.22);border-radius:10px;padding:7px 12px;font-size:12.5px;font-weight:600;cursor:pointer;backdrop-filter:blur(14px);box-shadow:0 6px 24px rgba(0,0,0,.45);user-select:none;transition:transform .15s ease,box-shadow .15s ease;}'+
'.sw_dl_btn:hover{transform:translateY(-1px);box-shadow:0 10px 28px rgba(0,0,0,.55);border-color:rgba(120,180,255,.65);}'+
'.sw_dl_menu{position:absolute;top:38px;left:0;min-width:220px;background:rgba(18,18,24,.96);border:1px solid rgba(255,255,255,.18);border-radius:12px;padding:6px;display:none;box-shadow:0 14px 40px rgba(0,0,0,.6);backdrop-filter:blur(16px);}'+
'.sw_dl_menu.open{display:block;}'+
'.sw_dl_item{display:flex;justify-content:space-between;align-items:center;gap:14px;color:#eaeaea;font-size:12px;padding:8px 10px;border-radius:8px;cursor:pointer;}'+
'.sw_dl_item:hover{background:rgba(90,140,255,.25);}'+
'.sw_dl_tag{font-size:10px;background:rgba(0,120,212,.35);color:#9fd0ff;border-radius:6px;padding:2px 6px;}'+
'.sw_dl_close{position:absolute;top:-10px;right:-10px;width:20px;height:20px;border-radius:50%;background:#c42b1c;color:#fff;font-size:11px;display:flex;align-items:center;justify-content:center;cursor:pointer;border:1px solid rgba(255,255,255,.4);}'+
'video::-webkit-media-controls{opacity:.25;transition:opacity .2s;}'+
'video:hover::-webkit-media-controls{opacity:1;}';
document.head.appendChild(s);
}
function el(tag,cls,txt){var e=document.createElement(tag);if(cls)e.className=cls;if(txt)e.textContent=txt;return e;}
function attach(v){
if(!v||v.__sw_dl__)return;v.__sw_dl__;
try{
if(v.mozHasAudio||v.dataset&&v.dataset.swSkip)return;
}catch(e){}
var root=el('div','sw_dl_root');
var btn=el('div','sw_dl_btn');
btn.innerHTML='⬇ <span>Download</span>';
var menu=el('div','sw_dl_menu');
var close=el('div','sw_dl_close','×');
root.appendChild(btn);root.appendChild(menu);root.appendChild(close);
document.body.appendChild(root);
function place(){
var r=v.getBoundingClientRect();
var top=Math.max(4,r.top+window.scrollY+8);
var left=Math.max(4,r.left+window.scrollX+r.width-170);
root.style.top=top+'px';root.style.left=left+'px';
}
place();
window.addEventListener('scroll',place,{passive:true});
window.addEventListener('resize',place);
var hideTimer=null;
function scheduleHide(){hideTimer=setTimeout(function(){root.style.display='none';},2600);}
function show(){clearTimeout(hideTimer);root.style.display='block';place();}
scheduleHide();
v.addEventListener('play',function(){root.style.display='block';place();scheduleHide();});
v.addEventListener('pause',show);
v.addEventListener('mouseenter',function(){root.style.display='block';place();clearTimeout(hideTimer);});
v.addEventListener('mouseleave',scheduleHide);
btn.addEventListener('mouseenter',function(){clearTimeout(hideTimer);root.style.display='block';});
btn.addEventListener('click',function(e){
e.stopPropagation();
menu.classList.toggle('open');
if(menu.classList.contains('open')){
menu.innerHTML='';
var src=v.currentSrc||v.src||'';
var title=(document.title||'video').substring(0,80);
function add(label,kind,extra){
var it=el('div','sw_dl_item');
var l=el('span',null,label);
var t=el('span','sw_dl_tag',kind);
it.appendChild(l);it.appendChild(t);
it.addEventListener('click',function(){
try{
window.chrome&&chrome.webview&&chrome.webview.postMessage({type:'swifter-media-download',url:src,page:location.href,title:title,kind:kind,quality:extra||'auto'});
}catch(e){}
menu.classList.remove('open');
});
menu.appendChild(it);
}
if(src){
add('Best quality (auto)','auto');
add('Video source','video',(v.videoHeight?v.videoHeight+'p':''));
if(v.audioTracks&&v.audioTracks.length)add('Audio only','audio');
add('Current source URL','raw');
}
add('Save page media list','list');
}
});
close.addEventListener('click',function(e){e.stopPropagation();root.remove();});
}
function scan(){
var vids=document.querySelectorAll('video');
for(var i=0;i<vids.length;i++){try{attach(vids[i]);}catch(e){}}
}
css();
scan();
setInterval(scan,2500);
var mo=new MutationObserver(function(){scan();});
mo.observe(document.documentElement,{childList:true,subtree:true});
window.__swifter_overlay_scan__=scan;
try{
window.chrome&&chrome.webview&&chrome.webview.addEventListener('message',function(e){
if(e&&e.data&&e.data.type==='swifter-media-assets'){
var assets=e.data.assets||[];
scan();
}
});
}catch(e){}
})();
";
    }

    public string BuildVideoProbeScript()
    {
        return @"
(function(){
try{
var vids=document.querySelectorAll('video');
var out=[];
for(var i=0;i<vids.length;i++){
var v=vids[i];
out.push({src:v.currentSrc||v.src||'',w:v.videoWidth||0,h:v.videoHeight||0,dur:v.duration||0,title:document.title});
}
window.chrome&&chrome.webview&&chrome.webview.postMessage({type:'swifter-video-probe',videos:out,page:location.href});
}catch(e){}
})();
";
    }

    public void HandleWebMessage(string json, Action<MediaAsset> onAsset)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)) return;
            var typeStr = type.GetString();
            if (typeStr == "swifter-media-download")
            {
                var asset = new MediaAsset
                {
                    Url = root.TryGetProperty("url", out var u) ? u.GetString() : null,
                    PageUrl = root.TryGetProperty("page", out var p) ? p.GetString() : null,
                    Title = root.TryGetProperty("title", out var t) ? t.GetString() : null,
                    Type = root.TryGetProperty("kind", out var k) ? k.GetString() : "video",
                    Quality = root.TryGetProperty("quality", out var q) ? q.GetString() : "auto"
                };
                if (!string.IsNullOrEmpty(asset.Url)) onAsset?.Invoke(asset);
                DownloadRequested?.Invoke(asset);
            }
            else if (typeStr == "swifter-video-probe")
            {
                if (root.TryGetProperty("videos", out var videos) && videos.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in videos.EnumerateArray())
                    {
                        var src = v.TryGetProperty("src", out var s) ? s.GetString() : null;
                        if (string.IsNullOrEmpty(src)) continue;
                        var h = v.TryGetProperty("h", out var hh) ? hh.GetInt32() : 0;
                        var asset = new MediaAsset
                        {
                            Url = src,
                            Type = src.Contains(".m3u8") ? "hls" : "mp4",
                            Quality = h > 0 ? h + "p" : "auto",
                            PageUrl = v.TryGetProperty("page", out var pg) ? pg.GetString() : null
                        };
                        onAsset?.Invoke(asset);
                    }
                }
            }
        }
        catch
        {
        }
    }
}
