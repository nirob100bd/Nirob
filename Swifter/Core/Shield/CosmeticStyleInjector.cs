using System.Collections.Concurrent;
using System.Text;

namespace Swifter.Core.Shield;

public sealed class CosmeticStyleInjector
{
    private readonly AdBlockEngine _engine;
    public bool Enabled { get; set; } = true;
    public bool CollapseEmptyFrames { get; set; } = true;
    public bool HideCookieBanners { get; set; } = true;
    private readonly ConcurrentDictionary<string, string> _hostCss = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public CosmeticStyleInjector(AdBlockEngine engine)
    {
        _engine = engine;
    }

    public string GetStyleSheet(string host)
    {
        if (!Enabled) return "";
        var key = host ?? "";
        if (_hostCss.TryGetValue(key, out var cached)) return cached;
        var sb = new StringBuilder(16384);
        sb.AppendLine(":root{--swifter-shields:1;}");
        if (_engine != null) sb.Append(_engine.BuildCosmeticStyleSheet(host));
        if (HideCookieBanners)
        {
            sb.AppendLine("[class*='cookie'] [class*='banner'],[id*='cookie'][class*='consent'],[class*='CookieBanner'],[id*='onetrust'],[class*='ot-sdk'],[id*='CybotCookiebotDialog'],[class*='cc-window'],[aria-label*='cookie'],[id*='hs-eu-cookie-confirmation'],[class*='consent-container'],[class*='gdpr']{display:none!important;}");
        }
        if (CollapseEmptyFrames)
        {
            sb.AppendLine("iframe[width='0'],iframe[height='0'],iframe[style*='display:none'],div[style*='visibility:hidden'][style*='height:0']{display:none!important;}");
            sb.AppendLine("body>ins,body>aside[id*='ad']{display:none!important;}");
            sb.AppendLine("[data-ad-slot],[data-ad-unit],[data-google-ad-slot]{display:none!important;}");
            sb.AppendLine("div:empty:not([class]):not([id]):not([style]):has(ins),div[id*='-ad-'],div[class*='-ad-']{display:none!important;}");
        }
        sb.AppendLine(".swifter-blocked-spacer{min-height:0!important;height:0!important;overflow:hidden!important;}");
        var css = sb.ToString();
        if (_hostCss.Count > 512) _hostCss.Clear();
        _hostCss[key] = css;
        return css;
    }

    public string BuildInjectionScript(string host)
    {
        if (!Enabled) return "";
        var css = GetStyleSheet(host);
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(css));
        return @"
(function(){
try{
var k='__swifter_cosmetic__';
if(window[k])return;
window[k]=true;
var css=decodeURIComponent(escape(atob('" + b64 + @"')));
var s=document.createElement('style');
s.id='swifter-cosmetic-style';
s.type='text/css';
s.appendChild(document.createTextNode(css));
(document.head||document.documentElement).appendChild(s);
var mo=new MutationObserver(function(muts){
for(var i=0;i<muts.length;i++){
var m=muts[i];
for(var j=0;j<m.addedNodes.length;j++){
var n=m.addedNodes[j];
if(n.nodeType!==1)continue;
try{
if(n.matches&&n.matches('[id^=div-gpt-ad],[id^=aswift_],ins.adsbygoogle,.adsbox,.ad-banner,.sponsored-post')){n.style.setProperty('display','none','important');}
}catch(e){}
}
}
});
mo.observe(document.documentElement,{childList:true,subtree:true});
}catch(e){}
})();
";
    }

    public string BuildFrameCollapserScript()
    {
        return @"
(function(){
try{
var s=document.createElement('style');
s.textContent='[id^=""google_ads""]:not([data-visible]),[id^=""div-gpt-ad""]:not([src]):not([data-visible]){display:none!important;}';
(document.head||document.documentElement).appendChild(s);
}catch(e){}
})();
";
    }

    public void InvalidateHost(string host)
    {
        if (!string.IsNullOrEmpty(host)) _hostCss.TryRemove(host, out _);
    }

    public void InvalidateAll()
    {
        _hostCss.Clear();
    }
}
