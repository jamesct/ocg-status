using System.Text.RegularExpressions;

namespace OcgStatus.Core.Parsing;

/// <summary>
/// Parses lite.subscription.usage responses (/_server with l=2, a=[wrk, window]).
/// Payload shape (TanStack-Start $R serialization):
///   $R[0]={usage:..,limit:..,usagePercent:..,rows:$R[1]=[$R[2]={model:"..",name:"..",cost:..,quotaCost:..,multiplier:..,estimated:!0,contributionPercent:..}, ...]}
/// </summary>
public static class LiteSubscriptionUsageParser
{
    public static bool TryParse(string text, UsageWindowKind window, out ModelBreakdown? breakdown, out string? error)
    {
        breakdown = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("usagePercent", StringComparison.Ordinal))
        {
            error = "missing usagePercent";
            return false;
        }

        var usage = ExtractLong(text, "usage") ?? 0;
        var limit = ExtractLong(text, "limit") ?? 0;
        var pct = ExtractDouble(text, "usagePercent") ?? 0;

        // rows: 展开 $R 引用与内联对象
        var rows = new List<ModelCostRow>();
        int rowsAnchor = text.IndexOf("rows", StringComparison.Ordinal);
        if (rowsAnchor >= 0)
        {
            // 截取 rows 之后的窗口，递归解 $R[n]=[...]
            var section = text[(rowsAnchor + 4)..];
            var arrayBody = ResolveArray(section, text);
            if (arrayBody is not null)
            {
                foreach (Match m in Regex.Matches(arrayBody, @"\{(?<body>[^{}]*)\}"))
                {
                    var body = m.Groups["body"].Value;
                    var model = ExtractString(body, "model");
                    if (model is null) continue;
                    var name = ExtractString(body, "name") ?? model;
                    var cost = ExtractLong(body, "cost") ?? 0;
                    var quotaCost = ExtractLong(body, "quotaCost") ?? cost;
                    var multiplier = ExtractInt(body, "multiplier") ?? 1;
                    var estimated = ExtractBool(body, "estimated") ?? false;
                    var contribution = ExtractDouble(body, "contributionPercent") ?? 0;
                    rows.Add(new ModelCostRow(model, name, cost, quotaCost, multiplier, estimated, contribution));
                }
            }
        }

        breakdown = new ModelBreakdown(window, usage, limit, pct, rows);
        return true;
    }

    /// <summary>从 pos 开始找到 `[ ... ]` 数组（处理 $R[n]=[...] 与内联 [...]）</summary>
    private static string? ResolveArray(string text, string fullText)
    {
        // $R[n]=[...] 引用
        var refMatch = Regex.Match(text, @"\$R\[(?<idx>\d+)\]\s*=\s*\[");
        if (refMatch.Success)
        {
            int open = refMatch.Index + refMatch.Length - 1;
            return TakeBracketed(text, open);
        }
        // 内联 [...]
        int direct = text.IndexOf('[');
        if (direct >= 0 && (direct < 40 || text[..direct].Contains("rows")))
            return TakeBracketed(text, direct);
        return null;
    }

    private static string? TakeBracketed(string text, int open)
    {
        if (open < 0 || open >= text.Length || text[open] != '[') return null;
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '[') depth++;
            else if (text[i] == ']') { depth--; if (depth == 0) return text.Substring(open + 1, i - open - 1); }
        }
        return null;
    }

    private static long? ExtractLong(string text, string key)
    {
        var m = Regex.Match(text, $@"\b{Regex.Escape(key)}\s*:\s*(?<v>-?\d+)");
        if (!m.Success) return null;
        return long.TryParse(m.Groups["v"].Value, out var v) ? v : null;
    }

    private static int? ExtractInt(string text, string key)
    {
        var v = ExtractLong(text, key);
        return v is null ? null : (int)Math.Clamp(v.Value, int.MinValue, int.MaxValue);
    }

    private static double? ExtractDouble(string text, string key)
    {
        var m = Regex.Match(text, $@"\b{Regex.Escape(key)}\s*:\s*(?<v>-?\d+(?:\.\d+)?)");
        if (!m.Success) return null;
        return double.TryParse(m.Groups["v"].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static string? ExtractString(string text, string key)
    {
        var m = Regex.Match(text, $@"\b{Regex.Escape(key)}\s*:\s*""(?<v>[^""]*)""|{Regex.Escape(key)}\s*:\s*'(?<v2>[^']*)'");
        if (!m.Success) return null;
        return m.Groups["v"].Success ? m.Groups["v"].Value : m.Groups["v2"].Value;
    }

    private static bool? ExtractBool(string text, string key)
    {
        var m = Regex.Match(text, $@"\b{Regex.Escape(key)}\s*:\s*(?<v>!0|!1|true|false)");
        if (!m.Success) return null;
        return m.Groups["v"].Value is "!0" or "true";
    }
}