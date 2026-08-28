namespace OcgStatus.Core;

public sealed class InMemorySnapshotCache : IUsageSnapshotCache
{
    private UsageProviderResult? _latestResult;
    public UsageSnapshot? Latest => _latestResult?.Snapshot;
    public UsageProviderResult? LatestResult => _latestResult;
    public void Store(UsageProviderResult r) => _latestResult = r;
}
