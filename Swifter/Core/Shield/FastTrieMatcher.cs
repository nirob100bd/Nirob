using System.Collections.Concurrent;
using System.Text;

namespace Swifter.Core.Shield;

public sealed class TrieNode
{
    public Dictionary<char, TrieNode> Children;
    public bool IsTerminal;
    public string Payload;
    public int Depth;

    public TrieNode()
    {
        Children = new Dictionary<char, TrieNode>();
    }
}

public sealed class FastTrieMatcher
{
    private readonly TrieNode _root = new TrieNode();
    private readonly object _buildLock = new object();
    private volatile bool _ready;
    private int _ruleCount;
    public bool IsReady => _ready;
    public int RuleCount => _ruleCount;

    public void Build(IEnumerable<string> patterns)
    {
        lock (_buildLock)
        {
            _root.Children.Clear();
            _root.IsTerminal = false;
            _root.Payload = null;
            _ruleCount = 0;
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrEmpty(pattern)) continue;
                Insert(pattern);
                _ruleCount++;
            }
            _ready = true;
        }
    }

    public void Insert(string pattern)
    {
        var node = _root;
        foreach (var ch in pattern)
        {
            if (!node.Children.TryGetValue(ch, out var next))
            {
                next = new TrieNode { Depth = node.Depth + 1 };
                node.Children[ch] = next;
            }
            node = next;
        }
        node.IsTerminal = true;
        node.Payload = pattern;
    }

    public bool ContainsExact(string pattern)
    {
        var node = _root;
        foreach (var ch in pattern)
        {
            if (!node.Children.TryGetValue(ch, out node)) return false;
        }
        return node.IsTerminal;
    }

    public string FindIn(string haystack)
    {
        if (!_ready || string.IsNullOrEmpty(haystack)) return null;
        var length = haystack.Length;
        for (var start = 0; start < length; start++)
        {
            var node = _root;
            for (var i = start; i < length; i++)
            {
                if (!node.Children.TryGetValue(haystack[i], out node)) break;
                if (node.IsTerminal) return node.Payload;
            }
        }
        return null;
    }

    public string FindIn(ReadOnlySpan<char> haystack)
    {
        if (!_ready || haystack.IsEmpty) return null;
        for (var start = 0; start < haystack.Length; start++)
        {
            var node = _root;
            for (var i = start; i < haystack.Length; i++)
            {
                if (!node.Children.TryGetValue(haystack[i], out node)) break;
                if (node.IsTerminal) return node.Payload;
            }
        }
        return null;
    }

    public List<string> FindAllIn(string haystack, int maxResults = 32)
    {
        var results = new List<string>();
        if (!_ready || string.IsNullOrEmpty(haystack)) return results;
        for (var start = 0; start < haystack.Length && results.Count < maxResults; start++)
        {
            var node = _root;
            for (var i = start; i < haystack.Length; i++)
            {
                if (!node.Children.TryGetValue(haystack[i], out node)) break;
                if (node.IsTerminal)
                {
                    results.Add(node.Payload);
                    break;
                }
            }
        }
        return results;
    }

    public void Clear()
    {
        lock (_buildLock)
        {
            _root.Children.Clear();
            _ruleCount = 0;
            _ready = false;
        }
    }
}

public sealed class DomainTrie
{
    private sealed class Node
    {
        public Dictionary<string, Node> Children = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        public bool Terminal;
        public string Rule;
    }

    private readonly Node _root = new Node();
    private volatile bool _ready;

    public bool IsReady => _ready;
    public int Count { get; private set; }

    public void BuildDomains(IEnumerable<KeyValuePair<string, string>> domainToRule)
    {
        _root.Children.Clear();
        Count = 0;
        foreach (var pair in domainToRule)
        {
            InsertDomain(pair.Key, pair.Value);
            Count++;
        }
        _ready = true;
    }

    public void InsertDomain(string domain, string rule)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        domain = domain.Trim().TrimStart('.').ToLowerInvariant();
        var parts = domain.Split(new[] { '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var node = _root;
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (!node.Children.TryGetValue(parts[i], out var next))
            {
                next = new Node();
                node.Children[parts[i]] = next;
            }
            node = next;
        }
        node.Terminal = true;
        node.Rule = rule;
    }

    public string MatchHost(string host)
    {
        if (!_ready || string.IsNullOrWhiteSpace(host)) return null;
        host = host.Trim().ToLowerInvariant();
        var parts = host.Split('.');
        var node = _root;
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (!node.Children.TryGetValue(parts[i], out node)) return null;
            if (node.Terminal) return node.Rule;
        }
        return null;
    }

    public bool IsBlockedHost(string host)
    {
        return MatchHost(host) != null;
    }

    public void Clear()
    {
        _root.Children.Clear();
        Count = 0;
        _ready = false;
    }
}
