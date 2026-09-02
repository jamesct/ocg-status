using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OcgStatus.Core.Parsing;

namespace OcgStatus.Core;

/// <summary>
/// Direct HTTP provider: fetches https://opencode.ai/workspace/{ws}/go with a user-supplied Cookie,
/// discovers the current lite.subscription.get server function hash by downloading all JS bundles,
/// then GETs /_server?id={hash}&args=... . Falls back to hydration/script parsing if _server is unavailable.
/// Designed for standalone Linux validation and for a future manual Cookie+Workspace UI.
/// </summary>
public sealed class HttpUsageProvider : IUsageProvider
{
    private readonly ILogger<HttpUsageProvider> _log;
    private readonly HttpClient _http;
    private string _workspaceId;
    private string _cookie; // Full Cookie header value, e.g. "auth=Fe26...; oc_locale=zh"
    private readonly string _baseUrl;
    private readonly Func<(string workspaceId, string cookie)>? _configReader;
    private string? _lastHash;       // lite.subscription.get
    private string? _usageHash;      // lite.subscription.usage
    private string? _combinedJsCache;

    public HttpUsageProvider(
        ILogger<HttpUsageProvider> log,
        HttpClient http,
        string workspaceId,
        string cookie,
        string baseUrl = "https://opencode.ai",
        Func<(string workspaceId, string cookie)>? configReader = null)
    {
        _log = log;
        _http = http;
        _workspaceId = workspaceId;
        _cookie = cookie;
        _baseUrl = baseUrl.TrimEnd('/');
        _configReader = configReader;
    }

    public void UpdateConfig(string workspaceId, string cookie)
    {
        _workspaceId = workspaceId;
        _cookie = cookie;
    }

    /// <summary>
    /// 抓取 single-window 的模型分摊（lite.subscription.usage；hash 与 lite.subscription.get 不同源，需单独解）。
    /// hash 过时时会自动清缓存重扫一次。
    /// </summary>
    public async Task<(bool Ok, ModelBreakdown? Breakdown, string? Error)> FetchBreakdownAsync(UsageWindowKind window, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                if (_configReader is not null)
                {
                    var (ws, ck) = _configReader();
                    _workspaceId = ws;
                    _cookie = ck;
                }
                if (string.IsNullOrWhiteSpace(_cookie) || string.IsNullOrWhiteSpace(_workspaceId))
                    return (false, null, "未配置认证信息");

                if (string.IsNullOrWhiteSpace(_usageHash))
                {
                    var combined = await FetchCombinedJsAsync(ct);
                    var h = LiteSubscriptionHashExtractor.Extract(combined ?? string.Empty, "lite.subscription.usage");
                    if (string.IsNullOrWhiteSpace(h))
                    {
                        if (attempt == 0) { ClearHashCache(); continue; }
                        return (false, null, "未找到 lite.subscription.usage hash");
                    }
                    _usageHash = h;
                }
                var hash = _usageHash;

                var win = window switch
                {
                    UsageWindowKind.Rolling => "rolling",
                    UsageWindowKind.Weekly => "weekly",
                    _ => "monthly",
                };
                var argsJson = JsonSerializer.Serialize(new
                {
                    t = new { t = 9, i = 0, l = 2, a = new object[] { new { t = 1, s = _workspaceId }, new { t = 1, s = win } }, o = 0 },
                    f = 31,
                    m = Array.Empty<object>(),
                });
                var url = $"{_baseUrl}/_server?id={Uri.EscapeDataString(hash)}&args={Uri.EscapeDataString(argsJson)}";
                var text = await GetStringWithHeadersAsync(url, hash, ct);

                var stale = string.IsNullOrWhiteSpace(text)
                    || (text.Contains("\"server-fn:0\"") && text.Length < 300 && !text.Contains("usagePercent", StringComparison.Ordinal));
                if (stale)
                {
                    if (attempt == 0) { ClearHashCache(); continue; }
                    return (false, null, "服务端返回为空（hash 已过期）");
                }

                if (LiteSubscriptionUsageParser.TryParse(text, window, out var bd, out var err))
                    return (true, bd, null);
                return (false, null, err ?? "解析失败");
            }
            catch (Exception ex)
            {
                if (attempt == 0) { ClearHashCache(); continue; }
                return (false, null, ex.Message);
            }
        }
        return (false, null, "未知错误");
    }

    private void ClearHashCache()
    {
        _lastHash = null;
        _usageHash = null;
        _combinedJsCache = null;
    }

    private async Task<string?> FetchCombinedJsAsync(CancellationToken ct)
    {
        if (_combinedJsCache is not null) return _combinedJsCache;
        try
        {
            var goUrl = $"{_baseUrl}/workspace/{Uri.EscapeDataString(_workspaceId)}/go";
            var html = await GetStringAsync(goUrl, ct);
            var jsUrls = LiteSubscriptionHashExtractor.ExtractJsUrls(html, _baseUrl);
            var sb = new System.Text.StringBuilder();
            foreach (var u in jsUrls)
            {
                try
                {
                    var js = await GetStringAsync(u, ct);
                    sb.Append('\n').Append(js);
                }
                catch { }
            }
            _combinedJsCache = sb.ToString();
            return _combinedJsCache;
        }
        catch { return null; }
    }

    /// <inheritdoc />
    public async Task<UsageProviderResult> FetchAsync(CancellationToken ct = default)
    {
        if (_configReader is not null)
        {
            var (ws, ck) = _configReader();
            _workspaceId = ws;
            _cookie = ck;
        }
        if (string.IsNullOrWhiteSpace(_workspaceId) || !IsLikelyWorkspaceId(_workspaceId))
            return UsageProviderResult.Fail(UsageResultKind.LoggedOut, "未配置 workspace，请先填写 wrk_*");

        if (string.IsNullOrWhiteSpace(_cookie))
            return UsageProviderResult.Fail(UsageResultKind.LoggedOut, "未配置 Cookie，请在设置中粘贴 auth Cookie");

        // 最多尝试 2 次：一次 hash 过期 → 清缓存重扫 bundle 换新 hash 再试
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var goUrl = $"{_baseUrl}/workspace/{Uri.EscapeDataString(_workspaceId)}/go";
                var html = await GetStringAsync(goUrl, ct);
                _log.LogInformation("Fetch attempt={Attempt} GET /go {Ms}ms html={Len}", attempt, sw.ElapsedMilliseconds, html.Length);

                if (html.Contains("window.location=\"/auth/authorize\"", StringComparison.Ordinal) ||
                    html.Contains("/auth/authorize", StringComparison.Ordinal) && html.Length < 30000 && !html.Contains("lite.subscription.get", StringComparison.Ordinal))
                {
                    return UsageProviderResult.Fail(UsageResultKind.LoggedOut, "Cookie 已失效或未授权该 workspace，请重新复制 auth Cookie");
                }

                // 官网有时把 usagePercent 内联进 HTML，直接解析（最新数据）
                if (TryParseInlinePayload(html, out var inline))
                {
                    _log.LogInformation("Fetch inline payload parsed in {Ms}ms", sw.ElapsedMilliseconds);
                    return UsageProviderResult.Ok(ToSnapshot(inline!));
                }

                // 解 hash（_combinedJsCache 有缓存直接用；attempt 1 时缓存已被清，会重新下载）
                var combined = await FetchCombinedJsAsync(ct);
                var hash = LiteSubscriptionHashExtractor.Extract(combined ?? string.Empty);
                if (string.IsNullOrWhiteSpace(hash))
                {
                    _log.LogWarning("Fetch attempt={Attempt} hash not found", attempt);
                    if (attempt == 0) { ClearHashCache(); continue; }
                    return UsageProviderResult.Fail(UsageResultKind.ProtocolChanged, "未找到 lite.subscription.get 的 server function");
                }
                _lastHash = hash;
                _log.LogInformation("Fetch attempt={Attempt} hash={Hash}", attempt, hash[..12] + "...");

                var argsJson = JsonSerializer.Serialize(new
                {
                    t = new { t = 9, i = 0, l = 1, a = new object[] { new { t = 1, s = _workspaceId } }, o = 0 },
                    f = 31,
                    m = Array.Empty<object>(),
                });
                var url = $"{_baseUrl}/_server?id={Uri.EscapeDataString(hash)}&args={Uri.EscapeDataString(argsJson)}";
                var serverText = await GetStringWithHeadersAsync(url, hash, ct);
                _log.LogInformation("Fetch attempt={Attempt} _server len={Len} in {Ms}ms", attempt, serverText.Length, sw.ElapsedMilliseconds);

                var stale = string.IsNullOrWhiteSpace(serverText)
                    || (serverText.Contains("\"server-fn:0\"") && serverText.Length < 300 && !serverText.Contains("rollingUsage", StringComparison.Ordinal));
                if (stale)
                {
                    _log.LogWarning("Fetch attempt={Attempt} _server stale (hash 可能已过期)，清缓存重扫", attempt);
                    if (attempt == 0) { ClearHashCache(); continue; }
                    return UsageProviderResult.Fail(UsageResultKind.ProtocolChanged, "服务端返回为空（hash 已过期），请稍后手动刷新");
                }

                if (LiteSubscriptionParser.TryParse(serverText, out var parsed, out var perr))
                    return UsageProviderResult.Ok(ToSnapshot(parsed!));

                _log.LogWarning("Fetch attempt={Attempt} parse failed: {Err}", attempt, perr);
                if (attempt == 0) { ClearHashCache(); continue; }
                return UsageProviderResult.Fail(UsageResultKind.ProtocolChanged, $"解析失败: {perr}");
            }
            catch (HttpRequestException ex)
            {
                _log.LogWarning(ex, "Fetch attempt={Attempt} http error", attempt);
                if (attempt == 0) { ClearHashCache(); continue; }
                return UsageProviderResult.Fail(UsageResultKind.NetworkError, "网络错误，请稍后重试", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "Fetch attempt={Attempt} timeout", attempt);
                if (attempt == 0) { ClearHashCache(); continue; }
                return UsageProviderResult.Fail(UsageResultKind.NetworkError, "请求超时，请稍后重试", ex);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Fetch attempt={Attempt} unexpected", attempt);
                if (attempt == 0) { ClearHashCache(); continue; }
                return UsageProviderResult.Fail(UsageResultKind.UnknownError, "未知错误，请稍后重试", ex);
            }
        }
        return UsageProviderResult.Fail(UsageResultKind.NetworkError, "刷新失败，请稍后重试");
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Cookie", _cookie);
        req.Headers.TryAddWithoutValidation("Accept", "text/html,*/*");
        req.Headers.TryAddWithoutValidation("Referer", $"{_baseUrl}/workspace/{Uri.EscapeDataString(_workspaceId)}/go");
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> GetStringWithHeadersAsync(string url, string hash, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Cookie", _cookie);
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        req.Headers.TryAddWithoutValidation("Referer", $"{_baseUrl}/workspace/{Uri.EscapeDataString(_workspaceId)}/go");
        req.Headers.TryAddWithoutValidation("x-server-id", hash);
        req.Headers.TryAddWithoutValidation("x-server-instance", "server-fn:0");
        req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadAsStringAsync(ct);
    }

    private UsageSnapshot ToSnapshot(LiteSubscriptionParser.ParseResult r) => new(
        WorkspaceId: _workspaceId,
        UseBalance: r.UseBalance,
        Rolling: r.Rolling,
        Weekly: r.Weekly,
        Monthly: r.Monthly,
        FetchedAt: DateTimeOffset.Now);

    private static bool TryParseInlinePayload(string html, out LiteSubscriptionParser.ParseResult? result)
    {
        result = null;
        // The Go page inlines _$HY.r["lite.subscription.get[\"wrk_...\"]"] as a promise placeholder;
        // the actual usage percent is resolved via _server, not inline — but keep hook for future inline.
        // Fall back to generic parser which already handles _server-shaped text if html somehow contains it.
        if (!html.Contains("rollingUsage", StringComparison.Ordinal)) return false;
        return LiteSubscriptionParser.TryParse(html, out result, out _);
    }

    private static bool IsLikelyWorkspaceId(string s)
    {
        if (!s.StartsWith("wrk_", StringComparison.Ordinal)) return false;
        if (s.Length < 10) return false;
        return s.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    }

    private static string ShortUrl(string u)
    {
        var idx = u.LastIndexOf('/');
        return idx >= 0 ? u[(idx + 1)..] : u;
    }
}
