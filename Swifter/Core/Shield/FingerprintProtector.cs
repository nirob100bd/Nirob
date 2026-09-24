using System.Text;

namespace Swifter.Core.Shield;

public sealed class FingerprintProtector
{
    public bool CanvasNoise { get; set; } = true;
    public bool AudioNoise { get; set; } = true;
    public bool WebGLSpoof { get; set; } = true;
    public bool NavigatorMask { get; set; } = true;
    public bool ClientRectNoise { get; set; } = true;
    public bool BlockFontEnumeration { get; set; } = true;
    public bool SpoofWebGlVendor { get; set; } = true;

    public string BuildScript()
    {
        var sb = new StringBuilder(8192);
        sb.AppendLine("(function(){");
        sb.AppendLine("if(window.__swifter_fp__)return;window.__swifter_fp__=true;");
        if (CanvasNoise) AppendCanvasNoise(sb);
        if (AudioNoise) AppendAudioNoise(sb);
        if (WebGLSpoof) AppendWebGl(sb);
        if (NavigatorMask) AppendNavigator(sb);
        if (ClientRectNoise) AppendClientRects(sb);
        if (BlockFontEnumeration) AppendFonts(sb);
        sb.AppendLine("})();");
        return sb.ToString();
    }

    private static void AppendCanvasNoise(StringBuilder sb)
    {
        sb.AppendLine(@"
(function(){
var toDataURL=HTMLCanvasElement.prototype.toDataURL;
var toBlob=HTMLCanvasElement.prototype.toBlob;
var getImageData=CanvasRenderingContext2D.prototype.getImageData;
function noise(ctx,w,h){
try{
var d=ctx.getImageData(0,0,Math.min(w,16),Math.min(h,16));
if(d&&d.data&&d.data.length>11){
var s=Date.now()&255;
d.data[8]=(d.data[8]+ (s&3))&255;
d.data[9]=(d.data[9]+ ((s>>2)&3))&255;
d.data[10]=(d.data[10]+ ((s>>4)&3))&255;
ctx.putImageData(d,0,0);
}
}catch(e){}
}
HTMLCanvasElement.prototype.toDataURL=function(type,quality){
try{
var ctx=this.getContext('2d');
if(ctx)noise(ctx,this.width,this.height);
}catch(e){}
return toDataURL.call(this,type,quality);
};
HTMLCanvasElement.prototype.toBlob=function(cb,type,quality){
try{
var ctx=this.getContext('2d');
if(ctx)noise(ctx,this.width,this.height);
}catch(e){}
return toBlob.call(this,cb,type,quality);
};
CanvasRenderingContext2D.prototype.getImageData=function(x,y,w,h){
var d=getImageData.call(this,x,y,w,h);
try{
if(d&&d.data&&d.data.length>11){
var salt=(window.__swifter_salt__||12345);
d.data[(x*4+8)%d.data.length]=(d.data[(x*4+8)%d.data.length]^(salt&1));
}
}catch(e){}
return d;
};
Object.defineProperty(navigator,'webdriver',{get:function(){return false;}});
})();
");
    }

    private static void AppendAudioNoise(StringBuilder sb)
    {
        sb.AppendLine(@"
(function(){
try{
var ac=window.AudioContext||window.webkitAudioContext;
if(!ac)return;
var proto=ac.prototype;
var orig=proto.createAnalyser;
var origOsc=proto.createOscillator;
proto.createAnalyser=function(){
var a=orig.call(this);
var g=a.getFloatFrequencyData;
var f=a.getFloatTimeDomainData;
a.getFloatFrequencyData=function(arr){
g.call(this,arr);
if(arr&&arr.length>4){arr[3]=arr[3]+((Date.now()&7)-3)*0.00001;}
};
a.getFloatTimeDomainData=function(arr){
f.call(this,arr);
if(arr&&arr.length>6){arr[5]=arr[5]+((Date.now()&3)-1)*0.000001;}
};
return a;
};
proto.createOscillator=function(){
var o=origOsc.call(this);
try{
var g=o.detune;
if(g){var origVal=g.value;Object.defineProperty(g,'value',{get:function(){return origVal+((Date.now()&1)?0.01:-0.01);},set:function(v){origVal=v;}});}
}catch(e){}
return o;
};
}catch(e){}
})();
");
    }

    private static void AppendWebGl(StringBuilder sb)
    {
        sb.AppendLine(@"
(function(){
try{
var patch=function(proto){
if(!proto)return;
var getParameter=proto.getParameter;
var unmasked=null;
proto.getParameter=function(p){
try{
if(p===37445)return 'Google Inc. (Intel)';
if(p===37446)return 'ANGLE (Intel, Intel(R) UHD Graphics 630 Direct3D11 vs_5_0 ps_5_0, D3D11)';
if(p===7937)return 'WebKit WebGL';
if(p===7936)return 'WebKit WebGL';
}catch(e){}
return getParameter.call(this,p);
};
};
patch(window.WebGLRenderingContext&&WebGLRenderingContext.prototype);
patch(window.WebGL2RenderingContext&&WebGL2RenderingContext.prototype);
var dbg=window.WebGLRenderingContext&&WebGLRenderingContext.prototype.getExtension;
if(dbg){
window.WebGLRenderingContext.prototype.getExtension=function(n){
var r=dbg.call(this,n);
if(r&&n==='WEBGL_debug_renderer_info'){
return {UNMASKED_VENDOR_WEBGL:37445,UNMASKED_RENDERER_WEBGL:37446};
}
return r;
};
}
}catch(e){}
})();
");
    }

    private static void AppendNavigator(StringBuilder sb)
    {
        sb.AppendLine(@"
(function(){
try{
var props={
platform:'Win32',
vendor:'Google Inc.',
language:'en-US',
languages:['en-US','en'],
maxTouchPoints:0,
deviceMemory:8,
hardwareConcurrency:" + Math.Clamp(Environment.ProcessorCount, 2, 16) + @",
appVersion:navigator.appVersion
};
for(var k in props){
try{
Object.defineProperty(navigator,k,{get:function(){return props[k];},configurable:true});
}catch(e){}
}
var origBR=navigator.brave;
try{
Object.defineProperty(navigator,'webdriver',{get:function(){return false;}});
}catch(e){}
try{
var origQuery=navigator.permissions&&navigator.permissions.query;
if(origQuery){
navigator.permissions.query=function(d){
return Promise.resolve({state:'prompt',status:'prompt',onchange:null});
};
}
}catch(e){}
}catch(e){}
})();
");
    }

    private static void AppendClientRects(StringBuilder sb)
    {
        sb.AppendLine(@"
(function(){
try{
var rects=Element.prototype.getClientRects;
Element.prototype.getClientRects=function(){
var r=rects.call(this);
try{
if(r&&r.length>0&&r[0]){
var o={0:{x:r[0].x,y:r[0].y,width:r[0].width,height:r[0].height,top:r[0].top,left:r[0].left,right:r[0].right,bottom:r[0].bottom}};
return new Proxy(r,{get:function(t,p){
if(p==='length')return t.length;
if(t[p]&&typeof t[p]==='object'&&'width' in t[p]){
var w=t[p].width;
var jitter=((Date.now()&1)?0.0001:-0.0001);
return new Proxy(t[p],{get:function(tt,pp){
if(pp==='width'||pp==='x'||pp==='left')return tt[pp]+jitter;
return tt[pp];
}});
}
return t[p];
}});
}
}catch(e){}
return r;
};
}catch(e){}
})();
");
    }

    private static void AppendFonts(StringBuilder sb)
    {
        sb.AppendLine(@"
(function(){
try{
if(document.fonts&&document.fonts.check){
var orig=document.fonts.check;
document.fonts.check=function(){return false;};
}
var origMeasure=CanvasRenderingContext2D.prototype.measureText;
CanvasRenderingContext2D.prototype.measureText=function(t){
var m=origMeasure.call(this,t);
try{
Object.defineProperty(m,'width',{value:m.width+0.0000001});
}catch(e){}
return m;
};
}catch(e){}
})();
");
    }

    public string BuildHeaderPatchScript()
    {
        return @"
(function(){
try{
window.__swifter_ua='" + EscapeForJs(BuildSpoofedUa()) + @"';
var orig=XMLHttpRequest.prototype.open;
XMLHttpRequest.prototype.open=function(m,u){
this.setRequestHeader&&this.setRequestHeader('X-Swifter-UA','1');
return orig.apply(this,arguments);
};
}catch(e){}
})();
";
    }

    public string BuildSpoofedUa()
    {
        return "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";
    }

    public Dictionary<string, string> GetHeaderOverrides()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = BuildSpoofedUa(),
            ["Accept-Language"] = "en-US,en;q=0.9",
            ["Sec-CH-UA"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\", \"Microsoft Edge\";v=\"131\"",
            ["Sec-CH-UA-Mobile"] = "?0",
            ["Sec-CH-UA-Platform"] = "\"Windows\""
        };
    }

    public void ApplyHeaderOverrides(Microsoft.Web.WebView2.Core.CoreWebView2 core)
    {
        if (core == null) return;
        try
        {
            foreach (var kv in GetHeaderOverrides())
            {
                core.WebResourceRequested += (s, e) =>
                {
                    try
                    {
                        if (e.Request.Headers.Contains("X-Swifter-UA")) return;
                        e.Request.Headers.SetHeader(kv.Key, kv.Value);
                        e.Request.Headers.SetHeader("X-Swifter-UA", "1");
                    }
                    catch
                    {
                    }
                };
            }
        }
        catch
        {
        }
    }
}

public static class JsUtil
{
    public static string EscapeForJs(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r").Replace("<", "\\u003c");
    }
}
