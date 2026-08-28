using System.Text.RegularExpressions;

namespace OcgStatus.Core.Parsing;

/// <summary>
/// Extracts the 64-hex server function id for lite.subscription.get
/// by scanning all JS bundles referenced by the Go page.
/// Mirrors usage-checker.ts extractSubscriptionFnHash.
/// </summary>
public static class LiteSubscriptionHashExtractor
{
    private static readonly Regex RefRe = new(@"(\w+)\s*=\s*createServerReference\(""([0-9a-f]{64})""\)", RegexOptions.Compiled);

    public static string? Extract(string combinedJs, string target = "lite.subscription.get")
    {
        // Build var -> hash map
        var varToHash = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in RefRe.Matches(combinedJs))
        {
            var varName = m.Groups[1].Value;
            var hash = m.Groups[2].Value;
            varToHash[varName] = hash;
        }
        if (varToHash.Count == 0) return null;

        foreach (var (varName, hash) in varToHash)
        {
            var pat = $@"(?:query|action)\(\s*{Regex.Escape(varName)}\s*,\s*[""']{Regex.Escape(target)}[""']";
            if (Regex.IsMatch(combinedJs, pat)) return hash;
        }
        return null;
    }

    public static IReadOnlyList<string> ExtractJsUrls(string html, string baseUrl)
    {
        // Match both src="..." and href="..." /_build/assets/*.js
        var urls = new List<string>();
        foreach (Match m in Regex.Matches(html, @"""/_build/assets/[^""]+\.js"""))
        {
            var raw = m.Value.Trim('"');
            var full = raw.StartsWith("http", StringComparison.Ordinal) ? raw : baseUrl.TrimEnd('/') + raw;
            if (!urls.Contains(full, StringComparer.Ordinal)) urls.Add(full);
        }
        if (urls.Count == 0)
        {
            foreach (Match m in Regex.Matches(html, @"/_build/assets/[^""'\s]+\.js"))
            {
                var full = m.Value.StartsWith("http", StringComparison.Ordinal) ? m.Value : baseUrl.TrimEnd('/') + m.Value;
                if (!urls.Contains(full, StringComparer.Ordinal)) urls.Add(full);
            }
        }
        return urls;
    }
}
