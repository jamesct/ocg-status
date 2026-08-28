namespace OcgStatus.Core;

public interface IUsageProvider
{
    Task<UsageProviderResult> FetchAsync(CancellationToken ct = default);
}

public interface IUsageSnapshotCache
{
    UsageSnapshot? Latest { get; }
    UsageProviderResult? LatestResult { get; }
    void Store(UsageProviderResult result);
}
