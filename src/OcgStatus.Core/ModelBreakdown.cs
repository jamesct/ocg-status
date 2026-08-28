using System.Text.Json.Serialization;

namespace OcgStatus.Core;

public sealed record ModelCostRow(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("cost")] long Cost,
    [property: JsonPropertyName("quotaCost")] long QuotaCost,
    [property: JsonPropertyName("multiplier")] int Multiplier,
    [property: JsonPropertyName("estimated")] bool Estimated,
    [property: JsonPropertyName("contributionPercent")] double ContributionPercent);

public sealed record ModelBreakdown(
    UsageWindowKind Window,
    long Usage,
    long Limit,
    double UsagePercent,
    IReadOnlyList<ModelCostRow> Rows);

public static class ModelCostRowExtensions
{
    /// <summary>micro-cents → 美元（quotaCost 单位是 micro-cents；1 美元 = 100_000_000 micro-cents）</summary>
    public static double CostInUsd(this long microCents) => microCents / 100_000_000d;
}