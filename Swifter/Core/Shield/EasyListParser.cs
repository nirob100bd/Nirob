using System.Text.RegularExpressions;

namespace Swifter.Core.Shield;

public enum RuleType
{
    Block,
    Allow,
    Cosmetic,
    ExceptionCosmetic,
    Network
}

public sealed class EasyListRule
{
    public string Raw { get; set; }
    public RuleType Type { get; set; }
    public string Domain { get; set; }
    public string Pattern { get; set; }
    public string ResourceTypes { get; set; }
    public bool ThirdPartyOnly { get; set; }
    public bool MatchCase { get; set; }
    public string Selector { get; set; }
    public Regex CompiledRegex { get; set; }
    public int OptionsMask { get; set; }
}

public sealed class ParsedFilterSet
{
    public List<EasyListRule> BlockRules { get; } = new List<EasyListRule>();
    public List<EasyListRule> AllowRules { get; } = new List<EasyListRule>();
    public List<EasyListRule> CosmeticRules { get; } = new List<EasyListRule>();
    public List<EasyListRule> ExceptionCosmeticRules { get; } = new List<EasyListRule>();
    public List<string> NetworkPatterns { get; } = new List<string>();
    public Dictionary<string, string> DomainBlocks { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public int TotalRules { get; set; }
}

public sealed class EasyListParser
{
    private static readonly Regex HostAnchor = new Regex(@"\|\|([a-z0-9\-\.]+)\^?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex StartAnchor = new Regex(@"^\|?https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OptionSplit = new Regex(@"\$(.*)$", RegexOptions.Compiled);
    private static readonly Regex DomainSplit = new Regex(@"domain=([^\s,]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CssHide = new Regex(@"##(.+)$", RegexOptions.Compiled);
    private static readonly Regex CssException = new Regex(@"#@#(.+)$", RegexOptions.Compiled);

    public ParsedFilterSet Parse(IEnumerable<string> lines)
    {
        var set = new ParsedFilterSet();
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '!') continue;
            if (line.StartsWith("[Adblock", StringComparison.OrdinalIgnoreCase)) continue;
            ParseLine(line, set);
        }
        set.TotalRules = set.BlockRules.Count + set.AllowRules.Count + set.CosmeticRules.Count + set.NetworkPatterns.Count;
        return set;
    }

    public ParsedFilterSet ParseText(string text)
    {
        return Parse(text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private void ParseLine(string line, ParsedFilterSet set)
    {
        if (CssException.IsMatch(line))
        {
            var m = CssException.Match(line);
            set.ExceptionCosmeticRules.Add(new EasyListRule
            {
                Raw = line,
                Type = RuleType.ExceptionCosmetic,
                Selector = m.Groups[1].Value.Trim()
            });
            return;
        }
        if (CssHide.IsMatch(line))
        {
            var m = CssHide.Match(line);
            set.CosmeticRules.Add(new EasyListRule
            {
                Raw = line,
                Type = RuleType.Cosmetic,
                Selector = m.Groups[1].Value.Trim()
            });
            return;
        }

        var isException = line.StartsWith("@@");
        var body = isException ? line.Substring(2) : line;
        string options = null;
        var optMatch = OptionSplit.Match(body);
        if (optMatch.Success)
        {
            options = optMatch.Groups[1].Value;
            body = body.Substring(0, optMatch.Index);
        }

        var rule = new EasyListRule
        {
            Raw = line,
            Type = isException ? RuleType.Allow : RuleType.Block,
            ThirdPartyOnly = options != null && options.Contains("third-party"),
            ResourceTypes = options
        };

        var hostMatch = HostAnchor.Match(body);
        if (hostMatch.Success)
        {
            rule.Domain = hostMatch.Groups[1].Value.ToLowerInvariant();
            if (!set.DomainBlocks.ContainsKey(rule.Domain) && !isException)
                set.DomainBlocks[rule.Domain] = line;
        }

        var pattern = body;
        if (pattern.StartsWith("||"))
        {
            pattern = pattern.Substring(2);
            pattern = pattern.TrimEnd('^', '|');
            if (pattern.Contains('*') || pattern.Contains('^'))
            {
                set.NetworkPatterns.Add(NormalizePattern(pattern));
                rule.Pattern = NormalizePattern(pattern);
            }
            else
            {
                set.NetworkPatterns.Add(pattern);
                rule.Pattern = pattern;
            }
        }
        else if (pattern.Contains('*') || pattern.Contains('^') || pattern.StartsWith("/") && pattern.EndsWith("/"))
        {
            rule.Pattern = pattern;
            set.NetworkPatterns.Add(pattern);
        }
        else
        {
            var clean = pattern.Trim('|');
            if (clean.Length > 0 && !clean.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                set.NetworkPatterns.Add(clean);
                rule.Pattern = clean;
            }
        }

        if (options != null && DomainSplit.IsMatch(line))
        {
            var dm = DomainSplit.Match(line);
            rule.Domain = dm.Groups[1].Value;
        }

        if (!string.IsNullOrEmpty(rule.Pattern) && (rule.Pattern.Contains('*') || rule.Pattern.Contains("+")))
        {
            try
            {
                rule.CompiledRegex = new Regex(WildcardToRegex(rule.Pattern), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
            }
            catch
            {
                rule.CompiledRegex = null;
            }
        }

        if (isException) set.AllowRules.Add(rule);
        else set.BlockRules.Add(rule);
    }

    public static string NormalizePattern(string pattern)
    {
        return pattern.Replace("^", ".*").Replace("||", "").Replace("|", "");
    }

    public static string WildcardToRegex(string wildcard)
    {
        var sb = new StringBuilder();
        sb.Append('^');
        foreach (var ch in wildcard)
        {
            if (ch == '*') sb.Append(".*");
            else if (ch == '+') sb.Append("+");
            else if (".[]{}()+-#$^|".IndexOf(ch) >= 0) sb.Append('\\').Append(ch);
            else sb.Append(ch);
        }
        sb.Append('$');
        return sb.ToString();
    }

    public static bool LineLooksLikeNetworkRule(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line[0] == '!' || line[0] == '[') return false;
        if (line.Contains("##") || line.Contains("#@#")) return false;
        return true;
    }
}
