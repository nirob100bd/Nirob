using System.Collections.Concurrent;
using Microsoft.Web.WebView2.Core;

namespace Swifter.Core.Shield;

public sealed class BlockedRequestRecord
{
    public string Url { get; set; }
    public string Host { get; set; }
    public string ResourceType { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class NetworkRequestFilter
{
    private readonly AdBlockEngine _engine;
    private readonly ConcurrentQueue<BlockedRequestRecord> _recent = new ConcurrentQueue<BlockedRequestRecord>();
    private const int MaxRecent = 500;
    public bool GlobalEnabled { get; set; } = true;
    public HashSet<string> PausedHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public event Action<BlockedRequestRecord> Blocked;

    public NetworkRequestFilter(AdBlockEngine engine)
    {
        _engine = engine;
    }

    public void Attach(CoreWebView2 core)
    {
        if (core == null) return;
        core.WebResourceRequested += OnWebResourceRequested;
    }

    public void Detach(CoreWebView2 core)
    {
        if (core == null) return;
        core.WebResourceRequested -= OnWebResourceRequested;
    }

    private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            if (!GlobalEnabled || e.Request == null) return;
            var uri = e.Request.Uri;
            if (string.IsNullOrEmpty(uri) || !uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
            var host = AdBlockEngine.SafeHost(uri);
            if (string.IsNullOrEmpty(host)) return;
            if (PausedHosts.Contains(host)) return;
            if (PausedHosts.Contains(CurrentHost)) return;

            var type = MapResourceType(e.ResourceContext);
            if (!_engine.ShouldBlock(uri, type, CurrentHost)) return;

            var record = new BlockedRequestRecord
            {
                Url = uri,
                Host = host,
                ResourceType = type,
                Timestamp = DateTime.UtcNow
            };
            _recent.Enqueue(record);
            while (_recent.Count > MaxRecent && _recent.TryDequeue(out _)) { }
            Blocked?.Invoke(record);

            e.Response = CreateBlockedResponse(e.GetDeferral, core, e.Request);
        }
        catch
        {
        }
    }

    public string CurrentHost { get; set; } = "";

    private static CoreWebView2WebResourceResponse CreateBlockedResponse(Func<CoreWebView2Deferral> getDeferral, CoreWebView2 core, CoreWebView2WebResourceRequest request)
    {
        try
        {
            var env = core.Environment;
            var stream = new MemoryStream(EncodingBytes());
            return env.CreateWebResourceResponse(stream, 403, "Blocked by Swifter Shield", "Content-Type: text/plain\r\nX-Swifter-Blocked: 1");
        }
        catch
        {
            return null;
        }
    }

    private static byte[] EncodingBytes()
    {
        return System.Text.Encoding.UTF8.GetBytes("blocked");
    }

    public static string MapResourceType(CoreWebView2WebResourceContext context)
    {
        return context switch
        {
            CoreWebView2WebResourceContext.Document => "document",
            CoreWebView2WebResourceContext.Script => "script",
            CoreWebView2WebResourceContext.Image => "image",
            CoreWebView2WebResourceContext.Stylesheet => "stylesheet",
            CoreWebView2WebResourceContext.Font => "font",
            CoreWebView2WebResourceContext.Media => "media",
            CoreWebView2WebResourceContext.XmlHttpRequest => "xhr",
            CoreWebView2WebResourceContext.Fetch => "fetch",
            CoreWebView2WebResourceContext.SubResource => "subresource",
            CoreWebView2WebResourceContext.WebSocket => "websocket",
            _ => "other"
        };
    }

    public IReadOnlyList<BlockedRequestRecord> GetRecent(int count = 50)
    {
        return _recent.Reverse().Take(count).ToList();
    }

    public void ClearRecent()
    {
        while (_recent.TryDequeue(out _)) { }
    }

    public void PauseHost(string host)
    {
        if (!string.IsNullOrEmpty(host)) PausedHosts.Add(host.ToLowerInvariant());
    }

    public void ResumeHost(string host)
    {
        if (!string.IsNullOrEmpty(host)) PausedHosts.Remove(host.ToLowerInvariant());
    }
}
