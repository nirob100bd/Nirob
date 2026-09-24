using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Swifter.Core.Shield;

public sealed class ScriptPattern
{
    public string Name { get; set; }
    public Regex Regex { get; set; }
    public bool Enabled { get; set; } = true;
    public string Replacement { get; set; } = "";
}

public sealed class ScriptBlocker
{
    private readonly ConcurrentBag<ScriptPattern> _patterns = new ConcurrentBag<ScriptPattern>();
    public bool Enabled { get; set; } = true;
    public bool BlockNotificationRequests { get; set; } = true;
    public bool BlockPopupAbuse { get; set; } = true;
    public bool BlockClipboardFlood { get; set; } = true;
    public int BlockedCount;

    public ScriptBlocker()
    {
        AddDefaultPatterns();
    }

    private void AddDefaultPatterns()
    {
        AddPattern("fingerprintjs", new Regex(@"fingerprint2?|fingerprintjs|fingerprintjs2|fpjs|clientjs|canvas-fingerprint|audio-fingerprint", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        AddPattern("cryptojacker", new Regex(@"coinhive|cryptonight|coinimp|webminepool|jsecoin|authedmine|crypto-looter|coinimp|wasmminer|stratum\+tcp", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        AddPattern("keylogger", new Regex(@"onkeyup\s*=.*log|hookedkey|grabkeys|keyhook\.js", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        AddPattern("malware-cdn", new Regex(@"malwarebytes|safebrowsing.*block", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        AddPattern("annoyance-popup", new Regex(@"window\.open\([^)]{200,}\)|\bpopunder\b|popunder\.js|under\.pop", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        AddPattern("clipboard-hijack", new Regex(@"ClipboardItem|clipboard\.writeText\(.*btc|copybtc|clipboard\.oncopy", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        AddPattern("notification-hijack", new Regex(@"Notification\.requestPermission|requestNotificationPermission|push\.subscribe", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        AddPattern("adblock-detect", new Regex(@"adsbygoogle\.js|adblock.*detect|detectAdBlock|blockAdBlock|fuckAdBlock|showAdBlockerOverlay", RegexOptions.IgnoreCase | RegexOptions.Compiled));
        AddPattern("browser-fingerprint-sync", new Regex(@"canvas\.toDataURL\(\).*localStorage|fingerprint.*localStorage\.setItem", RegexOptions.IgnoreCase | RegexOptions.Compiled));
    }

    public void AddPattern(string name, Regex regex)
    {
        _patterns.Add(new ScriptPattern { Name = name, Regex = regex });
    }

    public void AddPattern(string name, string pattern)
    {
        try
        {
            _patterns.Add(new ScriptPattern { Name = name, Regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled) });
        }
        catch
        {
        }
    }

    public bool ShouldBlockUrl(string scriptUrl)
    {
        if (!Enabled || string.IsNullOrEmpty(scriptUrl)) return false;
        foreach (var p in _patterns)
        {
            if (!p.Enabled) continue;
            if (p.Regex.IsMatch(scriptUrl))
            {
                Interlocked.Increment(ref BlockedCount);
                return true;
            }
        }
        return false;
    }

    public string FilterScriptSource(string source)
    {
        if (!Enabled || string.IsNullOrEmpty(source)) return source;
        var result = source;
        foreach (var p in _patterns)
        {
            if (!p.Enabled) continue;
            if (p.Regex.IsMatch(result))
            {
                Interlocked.Increment(ref BlockedCount);
                result = p.Regex.Replace(result, p.Replacement);
            }
        }
        return result;
    }

    public string BuildPageGuardScript()
    {
        return @"
(function(){
if(window.__swifter_guard__)return;window.__swifter_guard__=true;
try{
var open=window.open;
window.open=function(u){
if(!u)return null;
try{
var url=new URL(u,location.href);
if(url.protocol==='javascript:')return null;
}catch(e){}
return open.apply(window,arguments);
};
}catch(e){}
try{
if(" + (BlockNotificationRequests ? "true" : "false") + @"){
var origPerm=Notification&&Notification.requestPermission;
if(origPerm){
Notification.requestPermission=function(cb){
var p=Promise.resolve('denied');
if(typeof cb==='function'){try{cb('denied');}catch(e){}}
return p;
};
}
}catch(e){}
try{
if(" + (BlockClipboardFlood ? "true" : "false") + @"){
var write=navigator.clipboard&&navigator.clipboard.write;
if(write){navigator.clipboard.write=function(){return Promise.resolve();};}
var writeText=navigator.clipboard&&navigator.clipboard.writeText;
if(writeText){
var count=0;
navigator.clipboard.writeText=function(t){
count++;
if(count>3)return Promise.resolve();
return writeText.apply(navigator.clipboard,arguments);
};
}
}catch(e){}
try{
document.addEventListener('copy',function(e){
if(!e.clipboardData)return;
var sel=String(window.getSelection());
if(sel.length>10000){
e.stopImmediatePropagation();
e.preventDefault();
}
},true);
}catch(e){}
})();
";
    }

    public IReadOnlyList<ScriptPattern> GetPatterns() => _patterns.ToList();

    public void SetPatternEnabled(string name, bool enabled)
    {
        foreach (var p in _patterns)
        {
            if (p.Name == name) p.Enabled = enabled;
        }
    }
}
