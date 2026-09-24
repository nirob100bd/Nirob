using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Swifter.Core.Downloads;

public sealed class MediaAsset
{
    public string Url { get; set; }
    public string Type { get; set; }
    public string Quality { get; set; }
    public string Title { get; set; }
    public string PageUrl { get; set; }
    public long SizeBytes { get; set; }
    public bool IsLive { get; set; }
    public bool IsAd { get; set; }
    public DateTime DetectedUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
}

public sealed class HlsStreamSniffer
{
    private readonly ConcurrentDictionary<string, MediaAsset> _assets = new ConcurrentDictionary<string, MediaAsset>(StringComparer.OrdinalIgnoreCase);
    public bool Enabled { get; set; } = true;
    public bool DetectAds { get; set; } = true;
    public event Action<MediaAsset> AssetDetected;

    private static readonly string[] MediaExtensions =
    {
        ".m3u8", ".mpd", ".mp4", ".m4s", ".webm", ".mp3", ".m4a", ".aac", ".ogg", ".wav", ".flac", ".mov", ".mkv", ".ts"
    };

    private static readonly string[] MediaContentTypes =
    {
        "application/vnd.apple.mpegurl",
        "application/x-mpegurl",
        "audio/mpegurl",
        "application/mpegurl",
        "application/dash+xml",
        "video/mp4",
        "video/webm",
        "video/quicktime",
        "video/x-matroska",
        "audio/mpeg",
        "audio/mp4",
        "audio/aac",
        "audio/ogg",
        "audio/wav",
        "application/octet-stream"
    };

    private static readonly string[] AdMarkers =
    {
        "/ads/", "/ad/", "ads.", "advert", "vast", "vmss", "preroll", "midroll", "doubleclick",
        "googleads", "adserver", "adsystem", "paid-content", "sponsored", "beacon", "tracking"
    };

    public void InspectResponse(string url, string contentType, long contentLength, string pageUrl)
    {
        if (!Enabled || string.IsNullOrEmpty(url)) return;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

        var isMedia = LooksLikeMedia(url, contentType);
        if (!isMedia) return;

        var asset = new MediaAsset
        {
            Url = url,
            Type = ResolveType(url, contentType),
            SizeBytes = contentLength,
            PageUrl = pageUrl,
            Quality = ExtractQuality(url),
            IsAd = DetectAds && LooksLikeAd(url),
            IsLive = url.Contains("m3u8") && (url.Contains("live") || url.Contains("manifest"))
        };
        if (_assets.TryAdd(url, asset)) AssetDetected?.Invoke(asset);
    }

    public void InspectResponseBody(string url, string body, string pageUrl)
    {
        if (!Enabled || string.IsNullOrEmpty(body)) return;
        if (body.Length > 5_000_000) return;
        if (!url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase) && !body.Contains("#EXTM3U")) return;

        foreach (Match m in Regex.Matches(body, @"https?://[^\s""'<>]+\.m3u8[^\s""'<>]*"))
        {
            var child = m.Value.TrimEnd('\\', '"', '\'');
            var asset = new MediaAsset
            {
                Url = child,
                Type = "hls",
                PageUrl = pageUrl,
                Quality = ExtractQuality(child),
                IsAd = LooksLikeAd(child)
            };
            if (_assets.TryAdd(child, asset)) AssetDetected?.Invoke(asset);
        }
        foreach (Match m in Regex.Matches(body, @"#EXT-X-STREAM-INF:([^\r\n]+)\r?\n([^\r\n]+)"))
        {
            var resolution = Regex.Match(m.Groups[1].Value, @"RESOLUTION=(\d+x\d+)");
            var line = m.Groups[2].Value.Trim();
            var absolute = ResolveUrl(url, line);
            if (string.IsNullOrEmpty(absolute)) continue;
            var asset = new MediaAsset
            {
                Url = absolute,
                Type = "hls",
                Quality = resolution.Success ? resolution.Groups[1].Value : "unknown",
                PageUrl = pageUrl
            };
            if (_assets.TryAdd(absolute, asset)) AssetDetected?.Invoke(asset);
        }
    }

    public static bool LooksLikeMedia(string url, string contentType)
    {
        try
        {
            var path = new Uri(url).AbsolutePath.ToLowerInvariant();
            foreach (var ext in MediaExtensions)
            {
                if (path.EndsWith(ext)) return true;
            }
        }
        catch
        {
        }
        if (!string.IsNullOrEmpty(contentType))
        {
            var ct = contentType.ToLowerInvariant();
            foreach (var mc in MediaContentTypes)
            {
                if (ct.StartsWith(mc)) return true;
            }
            if (ct.Contains("mpegurl") || ct.Contains("dash+xml")) return true;
        }
        if (url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase)) return true;
        if (url.Contains("/manifest.mpd", StringComparison.OrdinalIgnoreCase)) return true;
        if (url.Contains("format=m3u8", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool LooksLikeAd(string url)
    {
        var u = url.ToLowerInvariant();
        foreach (var marker in AdMarkers)
        {
            if (u.Contains(marker)) return true;
        }
        return false;
    }

    private static string ResolveType(string url, string contentType)
    {
        var u = url.ToLowerInvariant();
        if (u.Contains(".m3u8") || (contentType?.Contains("mpegurl") ?? false)) return "hls";
        if (u.Contains(".mpd") || (contentType?.Contains("dash") ?? false)) return "dash";
        if (u.Contains(".mp4")) return "mp4";
        if (u.Contains(".webm")) return "webm";
        if (u.Contains(".mp3")) return "mp3";
        if (u.Contains(".m4a") || u.Contains(".aac")) return "audio";
        if (u.Contains(".mov")) return "mov";
        if (u.Contains(".mkv")) return "mkv";
        if (u.Contains(".ts")) return "ts";
        return contentType?.Split(';')[0] ?? "unknown";
    }

    private static string ExtractQuality(string url)
    {
        var m = Regex.Match(url, @"(\d{3,4})p", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value + "p";
        m = Regex.Match(url, @"(\d{3,4})x(\d{3,4})", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[0].Value;
        m = Regex.Match(url, @"_(\d{3,4})kbps", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value + "kbps";
        if (url.Contains("1080", StringComparison.OrdinalIgnoreCase)) return "1080p";
        if (url.Contains("720", StringComparison.OrdinalIgnoreCase)) return "720p";
        if (url.Contains("480", StringComparison.OrdinalIgnoreCase)) return "480p";
        if (url.Contains("360", StringComparison.OrdinalIgnoreCase)) return "360p";
        if (url.Contains("audio", StringComparison.OrdinalIgnoreCase)) return "audio";
        return "auto";
    }

    private static string ResolveUrl(string baseManifestUrl, string relative)
    {
        try
        {
            if (relative.StartsWith("http")) return relative;
            var baseUrl = new Uri(baseManifestUrl);
            return new Uri(baseUrl, relative).ToString();
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<MediaAsset> GetAssets(bool includeAds = false)
    {
        return _assets.Values
            .Where(a => includeAds || !a.IsAd)
            .OrderByDescending(a => a.DetectedUtc)
            .ToList();
    }

    public IReadOnlyList<MediaAsset> GetAssetsForPage(string pageUrl)
    {
        return _assets.Values
            .Where(a => string.Equals(a.PageUrl, pageUrl, StringComparison.OrdinalIgnoreCase) || (pageUrl != null && a.PageUrl != null && new Uri(a.PageUrl).Host == new Uri(pageUrl).Host))
            .OrderByDescending(a => a.DetectedUtc)
            .ToList();
    }

    public MediaAsset GetAsset(string url) => _assets.TryGetValue(url, out var a) ? a : null;

    public void Clear()
    {
        _assets.Clear();
    }

    public async Task<string> ResolveHlsVariantsAsync(string manifestUrl, HttpClient client)
    {
        try
        {
            var text = await client.GetStringAsync(manifestUrl).ConfigureAwait(false);
            var variants = new List<(double bandwidth, string url)>();
            foreach (Match m in Regex.Matches(text, @"#EXT-X-STREAM-INF:([^\r\n]+)\r?\n([^\r\n]+)"))
            {
                var bw = Regex.Match(m.Groups[1].Value, @"BANDWIDTH=(\d+)");
                var line = m.Groups[2].Value.Trim();
                var absolute = ResolveUrl(manifestUrl, line);
                if (absolute == null) continue;
                var bandwidth = bw.Success ? double.Parse(bw.Groups[1].Value) : 0;
                variants.Add((bandwidth, absolute));
            }
            if (variants.Count == 0) return manifestUrl;
            return variants.OrderByDescending(v => v.bandwidth).First().url;
        }
        catch
        {
            return manifestUrl;
        }
    }
}
