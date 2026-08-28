namespace OcgStatus.Core;

public enum UsageResultKind
{
    Ok,
    NotSubscribed,
    LoggedOut,
    NetworkError,
    ProtocolChanged,
    UnknownError,
}

public sealed record UsageProviderResult(
    UsageResultKind Kind,
    UsageSnapshot? Snapshot,
    string? Message,
    Exception? Exception)
{
    public static UsageProviderResult Ok(UsageSnapshot s) =>
        new(UsageResultKind.Ok, s, null, null);

    public static UsageProviderResult Fail(UsageResultKind kind, string msg, Exception? ex = null) =>
        new(kind, null, msg, ex);
}
