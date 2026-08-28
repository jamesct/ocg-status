using System.Text.Json.Serialization;

namespace OcgStatus.Core;

public sealed record UsageWindow(
    [property: JsonPropertyName("kind")] UsageWindowKind Kind,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("usagePercent")] double UsagePercent,
    [property: JsonPropertyName("resetInSec")] int ResetInSec,
    [property: JsonPropertyName("usage")] long? Usage = null,
    [property: JsonPropertyName("limit")] long? Limit = null)
{
    public double RemainingPercent => Math.Max(0, 100 - UsagePercent);

    public bool IsRateLimited =>
        string.Equals(Status, "rate-limited", StringComparison.OrdinalIgnoreCase);

    public static string DisplayName(UsageWindowKind kind) => kind switch
    {
        UsageWindowKind.Rolling => "5 小时额度",
        UsageWindowKind.Weekly => "本周额度",
        UsageWindowKind.Monthly => "本月额度",
        _ => kind.ToString(),
    };
}
