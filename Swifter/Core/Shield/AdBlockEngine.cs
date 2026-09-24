using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Swifter.Core.Shield;

public sealed class BlockStats
{
    public long TotalBlocked;
    public long NetworkBlocked;
    public long CosmeticHidden;
    public long TrackerStripped;
    public string LastHost;
}

public sealed class AdBlockEngine
{
    private readonly FastTrieMatcher _patternTrie = new FastTrieMatcher();
    private readonly DomainTrie _domainTrie = new DomainTrie();
    private readonly EasyListParser _parser = new EasyListParser();
    private ParsedFilterSet _filters = new ParsedFilterSet();
    private readonly ConcurrentDictionary<string, bool> _allowCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _blockCache = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
    public BlockStats Stats { get; } = new BlockStats();
    public bool ShieldEnabled { get; set; } = true;
    public bool AggressiveMode { get; set; } = false;
    public event Action<string, string> RequestBlocked;

    private static readonly string[] BuiltInNetworkRules =
    {
        "||doubleclick.net^",
        "||googleadservices.com^",
        "||googlesyndication.com^",
        "||google-analytics.com^",
        "||googletagmanager.com^",
        "||googletagservices.com^",
        "||adservice.google.com^",
        "||facebook.net^",
        "||facebook.com/tr^",
        "||connect.facebook.net^",
        "||scorecardresearch.com^",
        "||quantserve.com^",
        "||adnxs.com^",
        "||pubmatic.com^",
        "||rubiconproject.com^",
        "||openx.net^",
        "||criteo.com^",
        "||criteo.net^",
        "||taboola.com^",
        "||outbrain.com^",
        "||zergnet.com^",
        "||hotjar.com^",
        "||mixpanel.com^",
        "||segment.io^",
        "||amplitude.com^",
        "||branch.io^",
        "||appsflyer.com^",
        "||adjust.com^",
        "||branch.io^",
        "||newrelic.com^",
        "||nr-data.net^",
        "||clarity.ms^",
        "||mouseflow.com^",
        "||fullstory.com^",
        "||sentry.io^",
        "||bugsnag.com^",
        "||crazyegg.com^",
        "||optimizely.com^",
        "||vwo.com^",
        "||abtasty.com^",
        "||matomo.org^",
        "||piwik.pro^",
        "||adcolony.com^",
        "||unityads.unity3d.com^",
        "||applovin.com^",
        "||vungle.com^",
        "||ironsrc.com^",
        "||startappservice.com^",
        "||inmobi.com^",
        "||moatads.com^",
        "||adsrvr.org^",
        "||demdex.net^",
        "||everesttech.net^",
        "||omtrdc.net^",
        "||bluekai.com^",
        "||exelator.com^",
        "||rlcdn.com^",
        "||bidswitch.net^",
        "||smartadserver.com^",
        "||yieldmo.com",
        "||media.net^",
        "||mgid.com^",
        "||revcontent.com^",
        "||propellerads.com^",
        "||popads.net^",
        "||adsterra.com^",
        "||trafficjunky.com^",
        "||exosrv.com^",
        "||juicyads.com^",
        "||pornotraffic.net^",
        "||hotlog.ru^",
        "||yandex.ru/an^",
        "||mc.yandex.ru^",
        "||bat.bing.com^",
        "||snap.licdn.com^",
        "||ads.linkedin.com^",
        "||pinimg.com/ct^",
        "||tiktok.com/i18n/pixel^",
        "||analytics.tiktok.com^",
        "||ads-twitter.com^",
        "||static.ads-twitter.com^",
        "||ads.yahoo.com^",
        "||adtech.yahooinc.com^",
        "||advertising.com^",
        "||33across.com^",
        "||media.net^",
        "||sharethrough.com^",
        "||seedtag.com^",
        "||coveo.com^",
        "||klaviyo.com/track^",
        "||mailchimp.com/track^",
        "||hubspot.com/collect^",
        "||_hsenc^"
    };

    private static readonly string[] BuiltInCosmeticRules =
    {
        "###ad-banner",
        "###ad-container",
        "###ad-slot",
        "###ads-container",
        "##.ad-banner",
        "##.ad-container",
        "##.ad-slot",
        "##.adsbox",
        "##.ads-wrapper",
        "##.advertisement",
        "##.advert",
        "##.ad-wrapper",
        "##.adhesive-ad",
        "##.sponsored-post",
        "##.sponsor",
        "##.taboola",
        "##.OUTBRAIN",
        "##.trc_related_container",
        "##.ad-insertion",
        "##[id^=google_ads]",
        "##[id^=div-gpt-ad]",
        "##[id^=aswift_]",
        "##ins.adsbygoogle",
        "##.adsbygoogle",
        "##.ad-frame",
        "##.ad-overlay",
        "##.popup-ad",
        "##.interstitial",
        "##.adstitial",
        "##iframe[src*='doubleclick']",
        "##iframe[src*='googlesyndication']",
        "##iframe[src*='adservice']",
        "##.cookie-banner",
        "##.cookie-consent",
        "##.gdpr-banner",
        "##.newsletter-popup",
        "##.email-signup",
        "##.megabanner",
        "##.banner-ad",
        "##.rectangle-ad",
        "##.native-ad",
        "##.ad-card",
        "##aside .ad",
        "##header .ad",
        "##footer .ad"
    };

    public AdBlockEngine()
    {
    }

    public int LoadedRuleCount => _patternTrie.RuleCount + _domainTrie.Count;

    public async Task LoadRulesAsync()
    {
        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var lines = new List<string>(BuiltInNetworkRules);
            var cosmetic = new List<string>(BuiltInCosmeticRules);
            var localRulesPath = Path.Combine(Program.GetUserDataRoot(), "filters.txt");
            if (File.Exists(localRulesPath))
            {
                var extra = await File.ReadAllLinesAsync(localRulesPath).ConfigureAwait(false);
                lines.AddRange(extra);
            }
            var downloaded = await TryDownloadEasyListAsync().ConfigureAwait(false);
            if (downloaded != null) lines.AddRange(downloaded);

            var set = _parser.Parse(lines);
            foreach (var c in cosmetic)
                set.CosmeticRules.Add(new EasyListRule { Raw = c, Type = RuleType.Cosmetic, Selector = c.Contains("##") ? c.Substring(c.IndexOf("##") + 2) : c });
            _filters = set;

            _domainTrie.BuildDomains(set.DomainBlocks.Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value)));
            var patterns = new HashSet<string>(set.NetworkPatterns);
            foreach (var r in set.BlockRules)
                if (!string.IsNullOrEmpty(r.Pattern)) patterns.Add(r.Pattern);
            foreach (var a in set.AllowRules)
                if (!string.IsNullOrEmpty(a.Pattern)) patterns.Add(a.Pattern);
            _patternTrie.Build(patterns);
            _allowCache.Clear();
            _blockCache.Clear();
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<List<string>> TryDownloadEasyListAsync()
    {
        try
        {
            var cachePath = Path.Combine(Program.GetUserDataRoot(), "cache", "easylist.snapshot");
            if (File.Exists(cachePath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath);
                if (age < TimeSpan.FromHours(24))
                    return (await File.ReadAllLinesAsync(cachePath).ConfigureAwait(false)).ToList();
            }
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Swifter/1.0");
            var text = await client.GetStringAsync("https://easylist.to/easylist/easylist.txt").ConfigureAwait(false);
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            await File.WriteAllLinesAsync(cachePath, lines).ConfigureAwait(false);
            return lines;
        }
        catch
        {
            var cachePath = Path.Combine(Program.GetUserDataRoot(), "cache", "easylist.snapshot");
            try
            {
                if (File.Exists(cachePath)) return (await File.ReadAllLinesAsync(cachePath).ConfigureAwait(false)).ToList();
            }
            catch
            {
            }
            return null;
        }
    }

    public bool ShouldBlock(string url, string resourceType = "script", string initiator = null)
    {
        if (!ShieldEnabled) return false;
        if (string.IsNullOrEmpty(url)) return false;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        var key = resourceType + "|" + url;
        if (_blockCache.TryGetValue(key, out var cached)) return cached;
        var blocked = Evaluate(url, resourceType, initiator);
        if (_blockCache.Count > 200000) _blockCache.Clear();
        _blockCache[key] = blocked;
        if (blocked)
        {
            Interlocked.Increment(ref Stats.NetworkBlocked);
            Stats.LastHost = SafeHost(url);
            RequestBlocked?.Invoke(SafeHost(url), resourceType);
        }
        return blocked;
    }

    private bool Evaluate(string url, string resourceType, string initiator)
    {
        var host = SafeHost(url);
        if (string.IsNullOrEmpty(host)) return false;

        foreach (var allow in _filters.AllowRules)
        {
            if (MatchesAllow(allow, url, host, initiator)) return false;
        }

        var domainRule = _domainTrie.MatchHost(host);
        if (domainRule != null)
        {
            if (MatchesTypeFilter(_filters.AllowRules, url, host, resourceType)) return false;
            return true;
        }

        var trieHit = _patternTrie.FindIn(url);
        if (trieHit != null)
        {
            if (MatchesTypeFilter(_filters.AllowRules, url, host, resourceType)) return false;
            return true;
        }

        foreach (var rule in _filters.BlockRules)
        {
            if (rule.CompiledRegex != null && rule.CompiledRegex.IsMatch(url))
            {
                if (MatchesTypeFilter(_filters.AllowRules, url, host, resourceType)) return false;
                return true;
            }
        }

        if (AggressiveMode && LooksLikeAdUrl(url, host)) return true;
        return false;
    }

    private static bool MatchesAllow(EasyListRule allow, string url, string host, string initiator)
    {
        if (!string.IsNullOrEmpty(allow.Pattern) && url.Contains(allow.Pattern, StringComparison.OrdinalIgnoreCase)) return true;
        if (allow.CompiledRegex != null && allow.CompiledRegex.IsMatch(url)) return true;
        if (!string.IsNullOrEmpty(allow.Domain) && (host.EndsWith("." + allow.Domain, StringComparison.OrdinalIgnoreCase) || host.Equals(allow.Domain, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }

    private static bool MatchesTypeFilter(List<EasyListRule> allows, string url, string host, string resourceType)
    {
        foreach (var a in allows)
        {
            if (!string.IsNullOrEmpty(a.ResourceTypes) && a.ResourceTypes.Contains(resourceType, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(a.Pattern) || url.Contains(a.Pattern, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static bool LooksLikeAdUrl(string url, string host)
    {
        var u = url.ToLowerInvariant();
        return u.Contains("/ads/") || u.Contains("/ad/") || u.Contains("advert") || u.Contains("bannerid=") ||
               u.Contains("clickspot") || u.Contains("popunder") || u.Contains("track?ad") ||
               host.StartsWith("ad.", StringComparison.Ordinal) || host.StartsWith("ads.", StringComparison.Ordinal);
    }

    public static string SafeHost(string url)
    {
        try
        {
            return new Uri(url).Host;
        }
        catch
        {
            return "";
        }
    }

    public bool IsCosmeticHostBlocked(string host)
    {
        return ShieldEnabled && (!string.IsNullOrEmpty(host) && _domainTrie.IsBlockedHost(host));
    }

    public string BuildCosmeticStyleSheet(string host)
    {
        if (!ShieldEnabled) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("html,body{--swifter-blocked:1;}");
        foreach (var rule in _filters.CosmeticRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Selector)) continue;
            if (rule.Selector.Contains(",") || rule.Selector.Length > 500) continue;
            try
            {
                sb.Append(rule.Selector).Append("{display:none!important;visibility:hidden!important;opacity:0!important;pointer-events:none!important;}\n");
            }
            catch
            {
            }
        }
        sb.AppendLine("ins.adsbygoogle{display:none!important;}");
        sb.AppendLine("[id^='div-gpt-ad'],[id^=google_ads],[id^=aswift_]{display:none!important;}");
        sb.AppendLine(".ad-banner,.adsbox,.sponsored-post,.OUTBRAIN,.taboola{display:none!important;}");
        return sb.ToString();
    }

    public string GetCosmeticCss()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var rule in _filters.CosmeticRules)
        {
            if (!string.IsNullOrWhiteSpace(rule.Selector) && rule.Selector.Length < 300 && !rule.Selector.Contains("{"))
                sb.Append(rule.Selector).Append("{display:none!important;}\n");
        }
        return sb.ToString();
    }

    public IReadOnlyList<EasyListRule> GetCosmeticRules() => _filters.CosmeticRules;

    public void AddCustomRule(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return;
        var parsed = _parser.Parse(new[] { rule });
        foreach (var b in parsed.BlockRules)
        {
            _filters.BlockRules.Add(b);
            if (!string.IsNullOrEmpty(b.Domain)) _domainTrie.InsertDomain(b.Domain, b.Raw);
            if (!string.IsNullOrEmpty(b.Pattern)) _patternTrie.Insert(b.Pattern);
        }
        foreach (var c in parsed.CosmeticRules) _filters.CosmeticRules.Add(c);
        var path = Path.Combine(Program.GetUserDataRoot(), "filters.txt");
        try { File.AppendAllText(path, rule + Environment.NewLine); } catch { }
    }

    public BlockStats GetStatsSnapshot()
    {
        return new BlockStats
        {
            TotalBlocked = Interlocked.Read(ref Stats.NetworkBlocked) + Interlocked.Read(ref Stats.CosmeticHidden),
            NetworkBlocked = Interlocked.Read(ref Stats.NetworkBlocked),
            CosmeticHidden = Interlocked.Read(ref Stats.CosmeticHidden),
            TrackerStripped = Interlocked.Read(ref Stats.TrackerStripped),
            LastHost = Stats.LastHost
        };
    }
}
