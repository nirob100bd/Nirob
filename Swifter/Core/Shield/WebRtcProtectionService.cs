using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Swifter.Core.Shield;

public sealed class WebRtcProtectionService
{
    private Timer _timer;
    private bool _running;
    public bool BlockNonProxiedUdp { get; set; } = true;
    public bool DisableUdpHostCandidates { get; set; } = true;
    public bool MaskLocalIp { get; set; } = true;
    public List<IPAddress> DiscoveredLocalIps { get; } = new List<IPAddress>();
    public event Action<string> LeakDetected;

    private readonly object _lock = new object();

    public string BuildScript()
    {
        return @"
(function(){
if(window.__swifter_webrtc__)return;window.__swifter_webrtc__=true;
try{
var RTC=window.RTCPeerConnection||window.webkitRTCPeerConnection;
if(!RTC){
try{Object.defineProperty(window,'RTCPeerConnection',{get:function(){return undefined;},configurable:true});}catch(e){}
try{Object.defineProperty(window,'webkitRTCPeerConnection',{get:function(){return undefined;},configurable:true});}catch(e){}
return;
}
var orig=RTC;
var Wrapped=function(cfg,cb){
try{
if(cfg){
cfg.iceTransportPolicy='relay';
cfg.iceCandidatePoolSize=0;
if(Array.isArray(cfg.iceServers)){
cfg.iceServers=cfg.iceServers.filter(function(s){
return !s.urls||String(s.urls).indexOf('stun:')!==0;
});
}
}
}catch(e){}
return new orig(cfg,cb);
};
Wrapped.prototype=orig.prototype;
Object.setPrototypeOf(Wrapped,orig);
window.RTCPeerConnection=Wrapped;
window.webkitRTCPeerConnection=Wrapped;
if(window.RTCSessionDescription){window.RTCSessionDescription=window.RTCSessionDescription;}
if(window.RTCIceCandidate){window.RTCIceCandidate=window.RTCIceCandidate;}
var getStats=Wrapped.prototype.getStats;
if(getStats){
Wrapped.prototype.getStats=function(){
var self=this;
return getStats.apply(self,arguments).then(function(res){
try{
res.forEach(function(v){
if(v&&v.type==='local-candidate'&&v.address){
try{v.address='0.0.0.0';v.ip='0.0.0.0';}catch(e){}
}
});
}catch(e){}
return res;
});
};
}
var og=RTC.prototype.createDataChannel;
if(og){RTC.prototype.createDataChannel=function(){return og.apply(this,arguments);};}
}catch(e){}
try{
var enumerate=window.chrome&&chrome.webstoreRTCPeerConnection;
}catch(e){}
})();
";
    }

    public string BuildLeakDetectorScript()
    {
        return @"
(function(){
try{
var RTC=window.RTCPeerConnection||window.webkitRTCPeerConnection;
if(!RTC)return;
var pc=new RTC({iceServers:[{urls:'stun:stun.l.google.com:19302'}]});
pc.onicecandidate=function(e){
if(!e||!e.candidate||!e.candidate.candidate)return;
var c=e.candidate.candidate;
var m=c.match(/([0-9]{1,3}(\.[0-9]{1,3}){3})/);
if(m&&m[1]&&m[1]!=='0.0.0.0'){
try{window.dispatchEvent(new CustomEvent('swifter-webrtc-leak',{detail:{ip:m[1]}}));}catch(e){}
}
pc.close();
};
pc.createDataChannel('sw');
pc.createOffer(function(s){pc.setLocalDescription(s,function(){});},function(){});
}catch(e){}
})();
";
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _timer = new Timer(_ => Scan(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60));
        RefreshLocalIps();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    public void RefreshLocalIps()
    {
        Task.Run(() =>
        {
            try
            {
                var found = new List<IPAddress>();
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                            found.Add(ua.Address);
                        else if (ua.Address.AddressFamily == AddressFamily.InterNetworkV6 && !ua.Address.IsIPv6LinkLocal)
                            found.Add(ua.Address);
                    }
                }
                lock (_lock)
                {
                    DiscoveredLocalIps.Clear();
                    DiscoveredLocalIps.AddRange(found);
                }
            }
            catch
            {
            }
        });
    }

    private void Scan()
    {
        if (!BlockNonProxiedUdp) return;
        try
        {
            RefreshLocalIps();
        }
        catch
        {
        }
    }

    public string GetSystemFirewallRecommendations()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("netsh advfirewall firewall add rule name=\"Swifter WebRTC UDP Block\" dir=out action=block protocol=udp remoteport=3478,3479,19302,19303,19304,19305,19306,19307,19308,19309,19310,19311,19312");
        sb.AppendLine("netsh advfirewall firewall add rule name=\"Swifter TURN Block\" dir=out action=block protocol=udp remoteport=5349,5060,5061");
        return sb.ToString();
    }

    public IReadOnlyList<IPAddress> GetLocalIpsSnapshot()
    {
        lock (_lock) return DiscoveredLocalIps.ToList();
    }

    public bool IsLikelyVpnActive()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                if (desc.Contains("tun") || desc.Contains("tap") || desc.Contains("wireguard") ||
                    desc.Contains("wintun") || desc.Contains("vpn") || desc.Contains("nordlynx") ||
                    desc.Contains("proton") || desc.Contains("openvpn") || desc.Contains("zerotier"))
                    return true;
            }
        }
        catch
        {
        }
        return false;
    }

    public void ReportLeak(string ip)
    {
        LeakDetected?.Invoke(ip);
    }
}
