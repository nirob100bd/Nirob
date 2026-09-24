using System.IO;
using System.Text;

namespace Swifter.Core.Protocols;

public sealed class CustomSchemeHandler
{
    public const string Scheme = "swifter";
    private readonly ProtocolPageRenderer _renderer = new ProtocolPageRenderer();

    public bool CanHandle(string url)
    {
        return url != null && url.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase);
    }

    public string GetPath(string url)
    {
        if (string.IsNullOrEmpty(url)) return "newtab";
        var trimmed = url.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase)
            ? url.Substring(Scheme.Length + 3)
            : url;
        var q = trimmed.IndexOf('?');
        if (q >= 0) trimmed = trimmed.Substring(0, q);
        trimmed = trimmed.Trim('/');
        return string.IsNullOrEmpty(trimmed) ? "newtab" : trimmed.ToLowerInvariant();
    }

    public Dictionary<string, string> GetQuery(string url)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(url)) return result;
        var q = url.IndexOf('?');
        if (q < 0) return result;
        var query = url.Substring(q + 1);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) result[Uri.UnescapeDataString(pair)] = "";
            else result[Uri.UnescapeDataString(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1));
        }
        return result;
    }

    public ProtocolResponse Handle(string url)
    {
        var path = GetPath(url);
        var query = GetQuery(url);
        try
        {
            return path switch
            {
                "newtab" or "new-tab" or "home" => ProtocolResponse.Html(NewTabPage.Render(query), url),
                "downloads" => ProtocolResponse.Html(DownloadsPage.Render(query), url),
                "history" => ProtocolResponse.Html(HistoryPage.Render(query), url),
                "bookmarks" => ProtocolResponse.Html(BookmarksPage.Render(query), url),
                "settings" => ProtocolResponse.Html(SettingsPage.Render(query), url),
                "shields" => ProtocolResponse.Html(ShieldsPage.Render(query), url),
                "wallpaper" => ServeWallpaper(query),
                "favicon" => ServeAsset(query),
                _ => ProtocolResponse.Html(NotFoundPage(path), url)
            };
        }
        catch (Exception ex)
        {
            return ProtocolResponse.Html(ErrorPage(ex), url);
        }
    }

    private ProtocolResponse ServeWallpaper(Dictionary<string, string> query)
    {
        if (query.TryGetValue("path", out var path) && File.Exists(path))
        {
            var bytes = File.ReadAllBytes(path);
            var mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".gif" => "image/gif",
                _ => "application/octet-stream"
            };
            return new ProtocolResponse { Content = bytes, ContentType = mime };
        }
        var fallback = NewTabPage.DefaultGradientBytes();
        return new ProtocolResponse { Content = fallback, ContentType = "image/png" };
    }

    private ProtocolResponse ServeAsset(Dictionary<string, string> query)
    {
        var name = query.TryGetValue("name", out var n) ? n : "icon";
        var bytes = ProtocolPageRenderer.GetBundledIcon(name);
        return new ProtocolResponse { Content = bytes, ContentType = "image/png" };
    }

    private static string NotFoundPage(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>Not found</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#0f0f12;color:#eee;display:flex;align-items:center;justify-content:center;height:100vh;margin:0}div{text-align:center}h1{font-size:64px;margin:0}a{color:#4C8DFF}</style></head>");
        sb.AppendLine($"<div><h1>404</h1><p>Unknown Swifter page: <code>{System.Net.WebUtility.HtmlEncode(path)}</code></p><p><a href='swifter://newtab'>Go to New Tab</a></p></div></html>");
        return sb.ToString();
    }

    private static string ErrorPage(Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><title>Error</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#1a0f0f;color:#fdd;margin:60px;}</style></head><body>");
        sb.AppendLine("<h1>Swifter internal page error</h1><pre>" + System.Net.WebUtility.HtmlEncode(ex.ToString()) + "</pre>");
        sb.AppendLine("<a style='color:#8ec' href='swifter://newtab'>New Tab</a>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}

public sealed class ProtocolResponse
{
    public byte[] Content { get; set; }
    public string ContentType { get; set; } = "text/html; charset=utf-8";
    public int StatusCode { get; set; } = 200;
    public string Reason { get; set; } = "OK";

    public static ProtocolResponse Html(string html, string url = null)
    {
        return new ProtocolResponse
        {
            Content = Encoding.UTF8.GetBytes(html ?? ""),
            ContentType = "text/html; charset=utf-8"
        };
    }

    public static ProtocolResponse Json(string json)
    {
        return new ProtocolResponse
        {
            Content = Encoding.UTF8.GetBytes(json ?? "{}"),
            ContentType = "application/json; charset=utf-8"
        };
    }

    public static ProtocolResponse NotFound()
    {
        return new ProtocolResponse
        {
            Content = Encoding.UTF8.GetBytes("Not found"),
            StatusCode = 404,
            Reason = "Not Found",
            ContentType = "text/plain"
        };
    }
}
