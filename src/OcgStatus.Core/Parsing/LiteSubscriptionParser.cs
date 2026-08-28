using System.Text.Json;
using System.Text.RegularExpressions;

namespace OcgStatus.Core.Parsing;

/// <summary>
/// Parses lite.subscription.get response. Primary shape is the TanStack-Start
/// serialized JS text from https://opencode.ai/_server (see usage-checker.ts).
/// We intentionally mirror that file's strategy so future protocol churn
/// does not require hard-coded $R indices.
/// Fallback: clean JSON (e.g. future official /zen/go/v1/usage shape or hydration JSON).
/// </summary>
public static class LiteSubscriptionParser
{
    public sealed record ParseResult(
        bool UseBalance,
        UsageWindow Rolling,
        UsageWindow Weekly,
        UsageWindow Monthly);

    public static bool TryParse(string text, out ParseResult? result, out string? error)
    {
        result = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "empty response";
            return false;
        }

        if (!text.Contains("rollingUsage", StringComparison.Ordinal))
        {
            // Maybe it's pure JSON without that keyword? Try JSON path first
            if (TryParseJson(text, out var jr) && jr is not null)
            {
                result = jr;
                return true;
            }
            error = "missing rollingUsage";
            return false;
        }

        var rolling = ExtractWindow(text, "rollingUsage", UsageWindowKind.Rolling);
        var weekly = ExtractWindow(text, "weeklyUsage", UsageWindowKind.Weekly);
        var monthly = ExtractWindow(text, "monthlyUsage", UsageWindowKind.Monthly);

        if (rolling is null || weekly is null || monthly is null)
        {
            if (TryParseJson(text, out var jr2) && jr2 is not null)
            {
                result = jr2;
                return true;
            }
            error = $"windows missing (rolling:{rolling is not null} weekly:{weekly is not null} monthly:{monthly is not null})";
            return false;
        }

        var useBalance = ExtractBool(text, "useBalance") ?? false;
        result = new ParseResult(useBalance, rolling, weekly, monthly);
        return true;
    }

    public static bool TryParseJson(string text, out ParseResult? result)
    {
        result = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            // Support envelope {rollingUsage:{...}, weeklyUsage:{...}, monthlyUsage:{...}}
            if (!TryGetWindow(root, "rollingUsage", UsageWindowKind.Rolling, out var r)) return false;
            if (!TryGetWindow(root, "weeklyUsage", UsageWindowKind.Weekly, out var w)) return false;
            if (!TryGetWindow(root, "monthlyUsage", UsageWindowKind.Monthly, out var m)) return false;
            var ub = root.TryGetProperty("useBalance", out var ubEl) && ubEl.ValueKind == JsonValueKind.True;
            if (r is null || w is null || m is null) return false;
            result = new ParseResult(ub, r, w, m);
            return true;
        }
        catch { return false; }
    }

    private static bool TryGetWindow(JsonElement root, string name, UsageWindowKind kind, out UsageWindow? w)
    {
        w = null;
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object) return false;
        double pct = el.TryGetProperty("usagePercent", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : 0;
        int reset = el.TryGetProperty("resetInSec", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0;
        string status = el.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "ok" : "ok";
        long? usage = el.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt64() : null;
        long? limit = el.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number ? l.GetInt64() : null;
        w = new UsageWindow(kind, status, pct, reset, usage, limit);
        return true;
    }

    // Mirrors TypeScript extractWindow exactly
    private static UsageWindow? ExtractWindow(string text, string windowName, UsageWindowKind kind)
    {
        int idx = text.IndexOf(windowName, StringComparison.Ordinal);
        if (idx < 0) return null;
        int end = Math.Min(text.Length, idx + 300);
        var section = text.Substring(idx, end - idx);

        var refMatch = Regex.Match(section, Regex.Escape(windowName) + @":\$R\[(\d+)\]");
        if (refMatch.Success)
        {
            var refIdx = refMatch.Groups[1].Value;
            var defPattern = new Regex(@"\$R\[" + Regex.Escape(refIdx) + @"\]=\{([^}]+)\}", RegexOptions.Singleline);
            var defMatch = defPattern.Match(text);
            if (defMatch.Success)
            {
                var parsed = ParseWindowFields(defMatch.Groups[1].Value, kind);
                if (parsed is not null) return parsed;
            }
        }

        var inlineMatch = Regex.Match(section, Regex.Escape(windowName) + @":(?:\$R\[\d+\]=)?\{([^}]+)\}", RegexOptions.Singleline);
        if (inlineMatch.Success)
        {
            var parsed = ParseWindowFields(inlineMatch.Groups[1].Value, kind);
            if (parsed is not null) return parsed;
        }

        // Do NOT fall back to parsing the raw 300-char section as window fields:
        // that would silently allow "garbage" like `weeklyUsage:{garbage}\nmonthlyUsage:{...}`
        // to pick up the next window's fields via rollover.
        return null;
    }

    private static UsageWindow? ParseWindowFields(string content, UsageWindowKind kind)
    {
        var usageMatch = Regex.Match(content, @"usagePercent\s*:\s*(\d+(?:\.\d+)?)");
        if (!usageMatch.Success) return null;
        if (!double.TryParse(usageMatch.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return null;
        var resetMatch = Regex.Match(content, @"resetInSec\s*:\s*(\d+)");
        int reset = 0;
        if (resetMatch.Success) int.TryParse(resetMatch.Groups[1].Value, out reset);
        var statusMatch = Regex.Match(content, @"status\s*:\s*""([^""]+)""|status\s*:\s*'([^']+)'");
        string status = "ok";
        if (statusMatch.Success) status = statusMatch.Groups[1].Success ? statusMatch.Groups[1].Value : statusMatch.Groups[2].Value;
        // Extract micro-cent fields if present for debugging/optional display
        long? usage = null, limit = null;
        var usageField = Regex.Match(content, @"\busage\s*:\s*(\d+)");
        if (usageField.Success && long.TryParse(usageField.Groups[1].Value, out var uv)) usage = uv;
        var limitField = Regex.Match(content, @"\blimit\s*:\s*(\d+)");
        if (limitField.Success && long.TryParse(limitField.Groups[1].Value, out var lv)) limit = lv;
        return new UsageWindow(kind, status, pct, reset, usage, limit);
    }

    private static bool? ExtractBool(string text, string key)
    {
        var m = Regex.Match(text, Regex.Escape(key) + @"\s*:\s*(true|false)");
        if (!m.Success) return null;
        return m.Groups[1].Value == "true";
    }
}
