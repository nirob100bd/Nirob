using System.Text.RegularExpressions;

namespace Swifter.Core.Shield;

public sealed class TrackingParamStripper
{
    private static readonly string[] KnownParams =
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        "utm_cid", "utm_reader", "utm_name", "utm_swu", "utm_viz_id",
        "fbclid", "gclid", "gclsrc", "dclid", "gad_source",
        "msclkid", "mc_cid", "mc_eid", "igshid", "igsh",
        "ref", "ref_src", "ref_url", "_hsenc", "_hsmi", "_hsenc",
        "yclid", "ysclid", "twclid", "ttclid", "li_fat_id",
        "epik", "s_kwcid", "srsltid", "vero_conv", "vero_id",
        "wickedid", "oly_enc_id", "oly_anon_id", "trk", "trkcampaign",
        "spm", "scm", "mkt_tok", "mtm_campaign", "mtm_kwd", "mtm_source",
        "pk_campaign", "pk_kwd", "pk_source", "piwik_campaign", "matomo_campaign",
        "hsCtaTracking", "hsa_cam", "hsa_grp", "hsa_ad", "hsa_src",
        "gdt", "gbraid", "wbraid", "fb_action_ids", "fb_action_types",
        "fb_source", "action_object_map", "action_type_map", "action_ref_map",
        "campaign_id", "ad_id", "adset_id", "placement", "_openstat",
        "spm_referrer", "ncid", "cmpid", "wt.mc_id", "wt.mc_ev",
        "oly_enc", "mc_tracking", "Email", "email_cid"
    };

    private static readonly HashSet<string> KnownSet = new HashSet<string>(KnownParams, StringComparer.OrdinalIgnoreCase);
    private static readonly Regex QueryParamRegex = new Regex(@"([?&])(utm_[a-z]+|fbclid|gclid|msclkid|ref|_hsenc|gclsrc|dclid|igshid|igsh|mc_cid|mc_eid|yclid|twclid|ttclid|srsltid|epik|mkt_tok|trk|spm|pk_campaign|pk_kwd|hsCtaTracking|gbraid|wbraid|ncid)=[^&#]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool Enabled { get; set; } = true;
    public int TotalStripped { get; private set; }

    public string Strip(string url)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(url)) return url;
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("swifter:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return url;

        var stripped = url;
        var questionIndex = stripped.IndexOf('?');
        if (questionIndex < 0) return stripped;

        var fragment = "";
        var hashIndex = stripped.IndexOf('#');
        if (hashIndex >= 0)
        {
            fragment = stripped.Substring(hashIndex);
            stripped = stripped.Substring(0, hashIndex);
        }

        questionIndex = stripped.IndexOf('?');
        if (questionIndex < 0) return stripped + fragment;

        var basePart = stripped.Substring(0, questionIndex);
        var query = stripped.Substring(questionIndex + 1);
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(pairs.Length);
        var removed = 0;
        foreach (var pair in pairs)
        {
            var eq = pair.IndexOf('=');
            var name = eq >= 0 ? pair.Substring(0, eq) : pair;
            if (IsTrackingParam(name)) { removed++; continue; }
            kept.Add(pair);
        }
        if (removed == 0) return url;
        TotalStripped += removed;
        var result = kept.Count > 0 ? basePart + "?" + string.Join("&", kept) : basePart;
        return result + fragment;
    }

    public bool IsTrackingParam(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return KnownSet.Contains(name);
    }

    public string StripAll(IEnumerable<string> urls)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var u in urls) sb.AppendLine(Strip(u));
        return sb.ToString();
    }

    public (string url, int removed) StripDetailed(string url)
    {
        var before = TotalStripped;
        var result = Strip(url);
        return (result, TotalStripped - before);
    }

    public IReadOnlyCollection<string> GetKnownParams() => KnownParams;

    public string SanitizeClipboard(string text)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(text)) return text;
        if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var changed = false;
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    var clean = Strip(parts[i]);
                    if (clean != parts[i]) { parts[i] = clean; changed = true; }
                }
            }
            return changed ? string.Join(" ", parts) : text;
        }
        return QueryParamRegex.Replace(text, m =>
        {
            var prefix = m.Groups[1].Value;
            return prefix == "?" && !text.Contains('?') ? "" : prefix;
        });
    }
}
