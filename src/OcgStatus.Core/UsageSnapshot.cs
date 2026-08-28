using System.Text.Json.Serialization;

namespace OcgStatus.Core;

public sealed record UsageSnapshot(
    [property: JsonPropertyName("workspaceId")] string WorkspaceId,
    [property: JsonPropertyName("useBalance")] bool UseBalance,
    [property: JsonPropertyName("rollingUsage")] UsageWindow Rolling,
    [property: JsonPropertyName("weeklyUsage")] UsageWindow Weekly,
    [property: JsonPropertyName("monthlyUsage")] UsageWindow Monthly,
    [property: JsonPropertyName("fetchedAt")] DateTimeOffset FetchedAt)
{
    public IReadOnlyList<UsageWindow> Windows => [Rolling, Weekly, Monthly];

    public double MaxUsagePercent => Windows.Max(w => w.UsagePercent);
}
