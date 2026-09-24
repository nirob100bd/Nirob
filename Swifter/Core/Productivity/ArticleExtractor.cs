using System.Text;
using System.Text.RegularExpressions;

namespace Swifter.Core.Productivity;

public sealed class ArticleContent
{
    public string Title { get; set; }
    public string Byline { get; set; }
    public string SiteName { get; set; }
    public string Html { get; set; }
    public string TextContent { get; set; }
    public string LeadImageUrl { get; set; }
    public string Excerpt { get; set; }
    public string Url { get; set; }
    public double Score { get; set; }
    public int WordCount { get; set; }
    public DateTime ExtractedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ArticleExtractor
{
    private static readonly string[] BlockTags =
    {
        "div", "section", "article", "main", "aside", "header", "footer", "nav", "form", "figure", "blockquote", "ul", "ol", "table"
    };

    private static readonly string[] NegativeHints =
    {
        "comment", "meta", "footer", "foot", "masthead", "sidebar", "aside", "promo", "banner", "ad-", "-ad",
        "related", "recommend", "share", "social", "newsletter", "subscribe", "popup", "modal", "cookie",
        "widget", "menu", "nav", "breadcrumb", "tag-", "author-box", "more-stories"
    };

    private static readonly string[] PositiveHints =
    {
        "article", "story", "content", "post", "entry", "main", "body", "text", "articlebody", "post-content"
    };

    private static readonly Regex ScriptStrip = new Regex(@"<script[\s\S]*?</script>|<style[\s\S]*?</style>|<noscript[\s\S]*?</noscript>|<!--[\s\S]*?-->", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TagStrip = new Regex(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new Regex(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex SentenceSplit = new Regex(@"(?<=[.!?])\s+", RegexOptions.Compiled);

    public ArticleContent ExtractFromHtml(string html, string pageUrl = null)
    {
        if (string.IsNullOrWhiteSpace(html)) return new ArticleContent();
        html = ScriptStrip.Replace(html, "");
        var candidates = ScoreCandidates(html);
        var best = candidates.OrderByDescending(c => c.Score).FirstOrDefault();
        if (best == null)
        {
            return new ArticleContent
            {
                Html = "<p>" + WebDecode(StripTags(html)).Substring(0, Math.Min(4000, StripTags(html).Length)) + "</p>",
                TextContent = StripTags(html),
                Url = pageUrl
            };
        }

        var rawHtml = best.Html;
        var text = CleanText(StripTags(rawHtml));
        var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return new ArticleContent
        {
            Title = best.Title,
            Html = SanitizeArticleHtml(rawHtml),
            TextContent = text,
            WordCount = words.Length,
            Score = best.Score,
            Excerpt = BuildExcerpt(text),
            Byline = best.Byline,
            SiteName = best.SiteName,
            LeadImageUrl = best.Image,
            Url = pageUrl
        };
    }

    private sealed class Candidate
    {
        public string Html { get; set; }
        public double Score { get; set; }
        public string Title { get; set; }
        public string Byline { get; set; }
        public string SiteName { get; set; }
        public string Image { get; set; }
    }

    private List<Candidate> ScoreCandidates(string html)
    {
        var results = new List<Candidate>();
        var titleMatch = Regex.Match(html, @"<title[^>]*>([\s\S]*?)</title>", RegexOptions.IgnoreCase);
        var docTitle = titleMatch.Success ? WebDecode(titleMatch.Groups[1].Value.Trim()) : null;

        var h1Match = Regex.Match(html, @"<h1[^>]*>([\s\S]*?)</h1>", RegexOptions.IgnoreCase);
        var h1 = h1Match.Success ? CleanText(h1Match.Groups[1].Value) : null;

        var siteMatch = Regex.Match(html, @"<meta[^>]+property=[""']og:site_name[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        if (!siteMatch.Success)
            siteMatch = Regex.Match(html, @"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:site_name[""']", RegexOptions.IgnoreCase);
        var site = siteMatch.Success ? siteMatch.Groups[1].Value : null;

        var imageMatch = Regex.Match(html, @"<meta[^>]+property=[""']og:image[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        var image = imageMatch.Success ? imageMatch.Groups[1].Value : null;

        var authorMatch = Regex.Match(html, @"<meta[^>]+name=[""']author[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
        var author = authorMatch.Success ? authorMatch.Groups[1].Value : null;

        foreach (var tag in BlockTags)
        {
            var matches = Regex.Matches(html, $@"<{tag}(\s[^>]*)?>([\s\S]*?)</{tag}>", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                if (results.Count > 80) break;
                var attrs = m.Groups[1].Value ?? "";
                var inner = m.Groups[2].Value;
                var plain = CleanText(StripTags(inner));
                if (plain.Length < 250) continue;
                var words = plain.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length < 50) continue;

                var score = 0.0;
                score += Math.Min(120, words.Length * 0.15);
                var linkTextLength = Regex.Matches(inner, @"<a\s[^>]*>([\s\S]*?)</a>", RegexOptions.IgnoreCase)
                    .Cast<Match>().Sum(mm => CleanText(StripTags(mm.Groups[1].Value)).Length);
                var linkDensity = plain.Length > 0 ? (double)linkTextLength / plain.Length : 0;
                score -= linkDensity * 150;

                var attrLower = attrs.ToLowerInvariant();
                foreach (var p in PositiveHints)
                    if (attrLower.Contains(p)) score += 45;
                foreach (var n in NegativeHints)
                    if (attrLower.Contains(n)) score -= 70;

                if (tag == "article" || tag == "main") score += 60;
                if (tag == "aside" || tag == "nav" || tag == "footer" || tag == "header") score -= 120;

                var pCount = Regex.Matches(inner, "<p[\\s>]", RegexOptions.IgnoreCase).Count;
                score += pCount * 6;
                var hCount = Regex.Matches(inner, "<h[1-6][\\s>]", RegexOptions.IgnoreCase).Count;
                score += hCount * 4;
                var imgCount = Regex.Matches(inner, "<img[\\s>]", RegexOptions.IgnoreCase).Count;
                score += Math.Min(30, imgCount * 8);

                if (inner.Contains("articleBody", StringComparison.OrdinalIgnoreCase)) score += 80;
                if (inner.Contains("schema.org", StringComparison.OrdinalIgnoreCase)) score += 40;

                results.Add(new Candidate
                {
                    Html = m.Value,
                    Score = score,
                    Title = h1 ?? docTitle,
                    Byline = author,
                    SiteName = site,
                    Image = image
                });
            }
        }
        return results;
    }

    public string BuildReaderHtml(ArticleContent article, ReaderTheme theme, ReaderTypography typography)
    {
        var css = BuildReaderCss(theme, typography);
        var title = WebEncode(article.Title ?? "Reader Mode");
        var byline = string.IsNullOrEmpty(article.Byline) ? "" : $"<div class=\"byline\">{WebEncode(article.Byline)}</div>";
        var site = string.IsNullOrEmpty(article.SiteName) ? "" : $"<div class=\"sitename\">{WebEncode(article.SiteName)}</div>";
        var lead = string.IsNullOrEmpty(article.LeadImageUrl) ? "" : $"<img class=\"lead\" src=\"{WebEncode(article.LeadImageUrl)}\" alt=\"\"/>";
        var body = string.IsNullOrEmpty(article.Html) ? $"<p>{WebEncode(article.TextContent ?? "")}</p>" : article.Html;
        return $@"<!DOCTYPE html>
<html><head><meta charset=""utf-8""/><meta name=""viewport"" content=""width=device-width,initial-scale=1""/>
<title>{title}</title>
<style>{css}</style>
</head>
<body>
<article>
{site}
<h1 class=""title"">{title}</h1>
{byline}
{lead}
<div class=""content"">{body}</div>
</article>
<script>
function collectText(){{var e=document.querySelector('.content');return e?e.innerText:'';}}
function swifterSpeak(){{
try{{
var t=collectText();
if(!t)return;
var u=new SpeechSynthesisUtterance(t);
u.rate=1.0;u.pitch=1.0;
speechSynthesis.cancel();
speechSynthesis.speak(u);
window.chrome&&chrome.webview&&chrome.webview.postMessage({{type:'swifter-tts-started'}});
}}catch(e){{}}
}}
function swifterPauseTts(){{try{{speechSynthesis.pause();}}catch(e){{}}}}
function swifterResumeTts(){{try{{speechSynthesis.resume();}}catch(e){{}}}}
function swifterStopTts(){{try{{speechSynthesis.cancel();window.chrome&&chrome.webview&&chrome.webview.postMessage({{type:'swifter-tts-stopped'}});}}catch(e){{}}}}
window.addEventListener('keydown',function(e){{
if(e.ctrlKey&&e.key==='Enter'){{e.preventDefault();swifterSpeak();}}
if(e.key==='Escape'){{swifterStopTts();}}
}});
</script>
</body></html>";
    }

    private static string BuildReaderCss(ReaderTheme theme, ReaderTypography t)
    {
        var (bg, fg, muted) = theme switch
        {
            ReaderTheme.Dark => ("#141414", "#E8E6E3", "#9A9691"),
            ReaderTheme.Sepia => ("#F4ECD8", "#3B2F1E", "#7A6A4F"),
            ReaderTheme.Black => ("#000000", "#D9D9D9", "#8A8A8A"),
            _ => ("#FAFAF8", "#222222", "#666666")
        };
        var size = Math.Clamp(t.FontSize, 14, 28);
        var line = Math.Clamp(t.LineHeight, 1.2, 2.4);
        var family = t.Font switch
        {
            ReaderFont.Serif => "Georgia, 'Times New Roman', serif",
            ReaderFont.Mono => "'Cascadia Code', Consolas, monospace",
            _ => "'Segoe UI', -apple-system, sans-serif"
        };
        var width = Math.Clamp(t.ContentWidth, 560, 1100);
        return $@"
:root{{color-scheme:{(theme == ReaderTheme.Light ? "light" : "dark")};}}
*{{box-sizing:border-box;}}
html,body{{margin:0;padding:0;background:{bg};color:{fg};font-family:{family};font-size:{size}px;line-height:{line};}}
article{{max-width:{width}px;margin:0 auto;padding:48px 28px 120px;}}
.title{{font-size:{size + 14}px;line-height:1.25;margin:8px 0 10px;font-weight:700;letter-spacing:-0.02em;}}
.byline{{color:{muted};font-size:{Math.Max(12, size - 2)}px;margin-bottom:18px;}}
.sitename{{color:{muted};font-size:12px;text-transform:uppercase;letter-spacing:0.14em;margin-bottom:6px;}}
.lead{{width:100%;border-radius:12px;margin:18px 0;}}
.content p{{margin:0 0 1.1em;}}
.content h2{{font-size:{size + 6}px;margin:1.6em 0 0.6em;}}
.content h3{{font-size:{size + 3}px;margin:1.4em 0 0.5em;}}
.content a{{color:#4C8DFF;}}
.content img{{max-width:100%;height:auto;border-radius:10px;}}
.content blockquote{{border-left:3px solid {muted};margin:1.2em 0;padding:0.2em 0 0.2em 1em;color:{muted};font-style:italic;}}
.content pre{{background:{(theme == ReaderTheme.Light ? "#F0EEE9" : "#1E1E1E")};padding:14px;border-radius:10px;overflow:auto;font-size:{Math.Max(12, size - 2)}px;}}
.content code{{font-family:'Cascadia Code',Consolas,monospace;font-size:0.94em;}}
.toolbar{{position:fixed;right:18px;bottom:18px;display:flex;gap:8px;background:{(theme == ReaderTheme.Light ? "#FFFFFFEE" : "#222222EE")};border:1px solid {muted}44;border-radius:14px;padding:8px;backdrop-filter:blur(10px);}}
";
    }

    public string BuildExcerpt(string text, int maxLen = 280)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sentences = SentenceSplit.Split(text);
        var sb = new StringBuilder();
        foreach (var s in sentences)
        {
            if (sb.Length + s.Length + 1 > maxLen) break;
            sb.Append(s.Trim()).Append(' ');
        }
        var result = sb.ToString().Trim();
        if (result.Length == 0 || (result.Length < maxLen && text.Length <= maxLen)) return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "…";
        if (!result.EndsWith('.') && !result.EndsWith('!') && !result.EndsWith('?')) result += "…";
        return result;
    }

    public static string StripTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var noTags = TagStrip.Replace(html, " ");
        noTags = WebDecode(noTags);
        return Whitespace.Replace(noTags, " ").Trim();
    }

    public static string CleanText(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var decoded = WebDecode(html);
        decoded = Regex.Replace(decoded, @"&nbsp;?", " ", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"&#\d+;", " ");
        return Whitespace.Replace(decoded, " ").Trim();
    }

    public static string WebDecode(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return System.Net.WebUtility.HtmlDecode(s);
    }

    public static string WebEncode(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return System.Net.WebUtility.HtmlEncode(s);
    }

    private static string SanitizeArticleHtml(string html)
    {
        html = ScriptStrip.Replace(html, "");
        html = Regex.Replace(html, @"<iframe[\s\S]*?</iframe>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @" on[a-z]+=""[^""]*""", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @" on[a-z]+='[^']*'", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"javascript:[^'""]+", "#", RegexOptions.IgnoreCase);
        return html;
    }

    public async Task<ArticleContent> ExtractFromUrlAsync(string url, HttpClient client = null)
    {
        var ownsClient = client == null;
        client ??= new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        try
        {
            var html = await client.GetStringAsync(url).ConfigureAwait(false);
            return await Task.Run(() => ExtractFromHtml(html, url)).ConfigureAwait(false);
        }
        finally
        {
            if (ownsClient) client.Dispose();
        }
    }
}

public enum ReaderTheme
{
    Light,
    Dark,
    Sepia,
    Black
}

public enum ReaderFont
{
    Sans,
    Serif,
    Mono
}

public sealed class ReaderTypography
{
    public ReaderFont Font { get; set; } = ReaderFont.Sans;
    public int FontSize { get; set; } = 18;
    public double LineHeight { get; set; } = 1.7;
    public int ContentWidth { get; set; } = 720;
    public ReaderTheme Theme { get; set; } = ReaderTheme.Light;
}
