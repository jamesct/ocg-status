using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using OcgStatus.App.Services;
using OcgStatus.Core;

namespace OcgStatus.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainViewModel> _log;
    private readonly AppSettings _settings;
    private readonly IUsageProvider _provider;
    private readonly InMemorySnapshotCache _cache = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _countdownTimer;

    private bool _isRefreshing;
    private string _statusText = "初始化…";
    private string? _errorText;
    private string? _transientHint;
    private UsageSnapshot? _snapshot;
    private bool _isLoggedOut;
    private bool _needsProtocolUpdate;
    private readonly Dictionary<UsageWindowKind, ModelBreakdown> _breakdowns = new();

    public MainViewModel(ILogger<MainViewModel> log, AppSettings settings, IUsageProvider provider)
    {
        _log = log;
        _settings = settings;
        _provider = provider;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(settings.RefreshIntervalSec, 30, 86400)) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => RaiseCountdown();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private void RaiseCountdown()
    {
        Raise(nameof(RollingResetText));
        Raise(nameof(WeeklyResetText));
        Raise(nameof(MonthlyResetText));
        Raise(nameof(FetchedAtText));
    }

    public UsageSnapshot? Snapshot { get => _snapshot; private set { _snapshot = value; Raise(nameof(Snapshot)); Raise(nameof(HasSnapshot)); Raise(nameof(RollingResetText)); Raise(nameof(WeeklyResetText)); Raise(nameof(MonthlyResetText)); Raise(nameof(FetchedAtText)); Raise(nameof(UseBalanceText)); } }
    public bool HasSnapshot => Snapshot is not null;
    public bool IsRefreshing { get => _isRefreshing; private set { _isRefreshing = value; Raise(nameof(IsRefreshing)); } }
    public string StatusText { get => _statusText; private set { _statusText = value; Raise(nameof(StatusText)); } }
    public string? ErrorText { get => _errorText; private set { _errorText = value; Raise(nameof(ErrorText)); Raise(nameof(HasError)); } }
    public string? TransientHint { get => _transientHint; private set { _transientHint = value; Raise(nameof(TransientHint)); Raise(nameof(HasTransientHint)); } }
    public bool HasTransientHint => !string.IsNullOrWhiteSpace(TransientHint);
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsLoggedOut { get => _isLoggedOut; private set { _isLoggedOut = value; Raise(nameof(IsLoggedOut)); } }
    public bool NeedsProtocolUpdate { get => _needsProtocolUpdate; private set { _needsProtocolUpdate = value; Raise(nameof(NeedsProtocolUpdate)); } }

    public string RollingResetText => Snapshot is null ? "—" : Formatting.FormatReset(Snapshot.Rolling.ResetInSec);
    public string WeeklyResetText => Snapshot is null ? "—" : Formatting.FormatReset(Snapshot.Weekly.ResetInSec);
    public string MonthlyResetText => Snapshot is null ? "—" : Formatting.FormatReset(Snapshot.Monthly.ResetInSec);
    public string FetchedAtText => Snapshot is null ? string.Empty : $"更新于 {Snapshot.FetchedAt:HH:mm:ss}";
    public string UseBalanceText => Snapshot is null ? string.Empty : $"余额接续：{(Snapshot.UseBalance ? "已开启" : "关闭")}";

    public AppSettings Settings => _settings;

    /// <summary>立即返回缓存的模型分摊（未拉取过则为空）</summary>
    public ModelBreakdown? BreakdownFor(UsageWindowKind w) => _breakdowns.TryGetValue(w, out var b) ? b : null;

    /// <summary>主刷新成功后同步刷新三窗口模型分摊（后台并行，失败静默）</summary>
    private async Task RefreshBreakdownsAsync()
    {
        if (_provider is not HttpUsageProvider http) return;
        var ct = CancellationToken.None;
        var tasks = new[]
        {
            FetchBreakdownAsync(http, UsageWindowKind.Rolling, ct),
            FetchBreakdownAsync(http, UsageWindowKind.Weekly, ct),
            FetchBreakdownAsync(http, UsageWindowKind.Monthly, ct),
        };
        await Task.WhenAll(tasks);
    }

    private async Task FetchBreakdownAsync(HttpUsageProvider http, UsageWindowKind w, CancellationToken ct)
    {
        var (ok, bd, err) = await http.FetchBreakdownAsync(w, ct);
        if (ok && bd is not null)
        {
            _breakdowns[w] = bd;
            Raise(nameof(BreakdownFor));
        }
        else
        {
            _log.LogDebug("Breakdown {Window} failed: {Err}", w, err);
        }
    }

    private static UsageSnapshot? TryLoadSnapshot()
    {
        try
        {
            var p = AppPaths.SnapshotPath;
            if (!File.Exists(p)) return null;
            var json = File.ReadAllText(p);
            return JsonSerializer.Deserialize<UsageSnapshot>(json);
        }
        catch { return null; }
    }

    private static void PersistSnapshot(UsageSnapshot s)
    {
        try
        {
            var p = AppPaths.SnapshotPath;
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, JsonSerializer.Serialize(s));
        }
        catch { }
    }

    public void Start()
    {
        // 打开即展示：优先加载上次落盘的快照，不等待网络
        var cached = TryLoadSnapshot();
        if (cached is not null)
        {
            Snapshot = cached;
            StatusText = FetchedAtText;
        }
        _countdownTimer.Start();
        _refreshTimer.Start();
        // 后台默默刷新，不把标题刷成“加载中/刷新中”覆盖已展示的数据
        _ = RefreshAsync(quietIfHasSnapshot: HasSnapshot);
    }

    public void Stop()
    {
        _refreshTimer.Stop();
        _countdownTimer.Stop();
    }

    public void UpdateRefreshInterval(int sec)
    {
        sec = Math.Clamp(sec, 30, 86400);
        _settings.RefreshIntervalSec = sec;
        _settings.Save(OcgStatus.App.Services.AppPaths.SettingsPath);
        _refreshTimer.Interval = TimeSpan.FromSeconds(sec);
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    public Task RefreshAsync() => RefreshAsync(quietIfHasSnapshot: false);

    private async Task RefreshAsync(bool quietIfHasSnapshot)
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        var hadSnapshot = HasSnapshot;
        ErrorText = null;
        TransientHint = null;
        IsLoggedOut = false;
        NeedsProtocolUpdate = false;
        // 有旧数据时不在标题栏闪“刷新中…”，保持“更新于 HH:mm:ss”让刷新在后台进行
        if (!hadSnapshot || !quietIfHasSnapshot)
            StatusText = hadSnapshot ? "刷新中…" : "加载中…";
        try
        {
            var res = await _provider.FetchAsync();
            if (res.Kind == UsageResultKind.Ok && res.Snapshot is not null)
            {
                _cache.Store(res);
                Snapshot = res.Snapshot;
                PersistSnapshot(res.Snapshot);
                StatusText = FetchedAtText;
                _log.LogInformation("Usage ok rolling={Rolling} weekly={Weekly} monthly={Monthly}", res.Snapshot.Rolling.UsagePercent, res.Snapshot.Weekly.UsagePercent, res.Snapshot.Monthly.UsagePercent);
                _ = RefreshBreakdownsAsync(); // 后台带出模型分摊，不阻塞主刷新
                return;
            }

            // Keep last snapshot visible (stale) while surfacing error
            switch (res.Kind)
            {
                case UsageResultKind.LoggedOut:
                    IsLoggedOut = true;
                    ErrorText = res.Message ?? "登录已失效，请重新登录";
                    StatusText = ErrorText;
                    break;
                case UsageResultKind.NotSubscribed:
                    ErrorText = res.Message ?? "当前 workspace 未订阅 OpenCode Go";
                    StatusText = ErrorText;
                    break;
                case UsageResultKind.ProtocolChanged:
                    NeedsProtocolUpdate = true;
                    if (HasSnapshot)
                    {
                        TransientHint = "网络波动，已保留上次数据，稍后自动重试";
                        StatusText = FetchedAtText;
                    }
                    else
                    {
                        ErrorText = res.Message ?? "官网响应格式已变化";
                        StatusText = ErrorText;
                    }
                    break;
                default:
                    if (HasSnapshot)
                    {
                        TransientHint = res.Message ?? "网络波动，已保留上次数据，稍后自动重试";
                        StatusText = FetchedAtText;
                    }
                    else
                    {
                        ErrorText = res.Message ?? "刷新失败，稍后重试";
                        StatusText = ErrorText;
                    }
                    break;
            }
            if (res.Exception is not null) _log.LogWarning(res.Exception, "Refresh failed: {Kind}", res.Kind);
        }
        catch (Exception ex)
        {
            ErrorText = "刷新异常，请稍后重试";
            StatusText = HasSnapshot ? $"{FetchedAtText} · {ErrorText}" : ErrorText;
            _log.LogWarning(ex, "Refresh exception");
        }
        finally
        {
            IsRefreshing = false;
        }
    }
}
