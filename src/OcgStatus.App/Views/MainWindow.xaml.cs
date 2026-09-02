using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using OcgStatus.App.Services;
using OcgStatus.App.ViewModels;
using OcgStatus.Core;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using ToolTip = System.Windows.Controls.ToolTip;

namespace OcgStatus.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _settings;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _reallyClosing;

    public MainWindow()
    {
        InitializeComponent();

        var sp = OcgStatus.App.App.Services
                 ?? throw new InvalidOperationException("App.Services not initialized");
        _vm = sp.GetRequiredService<MainViewModel>();
        _settings = sp.GetRequiredService<AppSettings>();
        DataContext = _vm;
        _vm.PropertyChanged += OnVmChanged;

        if (_settings.WindowLeft is { } wl && _settings.WindowTop is { } wt && !double.IsNaN(wl) && !double.IsNaN(wt))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = wl;
            Top = wt;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Topmost = _settings.AlwaysOnTop;
        Loaded += (_, _) => Init();
    }

    private void Init()
    {
        InitTray();
        ApplyAppearance();
        EnsureOnScreen();
        _vm.Start();
        RefreshUi();
    }

    private void InitTray()
    {
        if (_tray is not null) return;
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Visible = true,
            Text = "OcgStatus",
            Icon = LoadAppIcon(),
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示", null, (_, _) => ShowFromTray());
        menu.Items.Add("刷新", null, async (_, _) => await _vm.RefreshAsync());
        menu.Items.Add("登录", null, (_, _) => OpenSettings("login"));
        menu.Items.Add("设置", null, (_, _) => OpenSettings());
        menu.Items.Add("重置窗口位置", null, (_, _) => ResetPositionToCenter());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ReallyClose());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private static System.Drawing.Icon? LoadAppIcon()
    {
        try
        {
            var sri = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ocg.ico"));
            if (sri is not null)
                using (var s = sri.Stream) return new System.Drawing.Icon(s);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    private void ShowFromTray()
    {
        EnsureOnScreen();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void ResetPositionToCenter()
    {
        var wa = SystemParameters.WorkArea;
        var (w, h) = WindowSizePresets.Resolve(_settings.Appearance.WindowSize, _settings.Appearance);
        if (!double.IsNaN(Width) && Width > 0) w = Width;
        if (!double.IsNaN(Height) && Height > 0) h = Height;

        Left = Math.Max(wa.Left, wa.Left + (wa.Width - w) / 2);
        Top = Math.Max(wa.Top, wa.Top + (wa.Height - h) / 2);
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.Save(AppPaths.SettingsPath);

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ReallyClose()
    {
        _reallyClosing = true;
        Close();
    }

    // ---------- 外观应用 ----------

    public void ApplyAppearance()
    {
        var a = _settings.Appearance;

        // 主题
        ApplyTheme(a.Theme);

        // 窗口尺寸
        var (w, h) = WindowSizePresets.Resolve(a.WindowSize, a);
        Width = w;
        Height = h;

        // 透明度
        Opacity = Math.Clamp(a.Opacity, 0.5, 1.0);

        // 圆角
        MainCard.CornerRadius = new CornerRadius(Math.Clamp(a.CornerRadius, 0, 16));

        // 阴影：已按要求移除（圆角+直角混阴影效果差）
        MainCard.Effect = null;

        // 背景
        ApplyBackground(a);

        // 可见性开关
        RollingBar.Visibility = a.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        WeeklyBar.Visibility = a.ShowProgress ? Visibility.Visible : Visibility.Collapsed;
        MonthlyBar.Visibility = a.ShowProgress ? Visibility.Visible : Visibility.Collapsed;

        RollingPct.Visibility = a.ShowPercent ? Visibility.Visible : Visibility.Collapsed;
        WeeklyPct.Visibility = a.ShowPercent ? Visibility.Visible : Visibility.Collapsed;
        MonthlyPct.Visibility = a.ShowPercent ? Visibility.Visible : Visibility.Collapsed;

        RollingReset.Visibility = a.ShowRollingReset ? Visibility.Visible : Visibility.Collapsed;
        WeeklyReset.Visibility = a.ShowReset ? Visibility.Visible : Visibility.Collapsed;
        MonthlyReset.Visibility = a.ShowReset ? Visibility.Visible : Visibility.Collapsed;

        FooterLeft.Visibility = a.ShowUseBalance ? Visibility.Visible : Visibility.Collapsed;

        // 模型分摊开关：悬浮提示 & 点击展开
        foreach (var (row, kind) in new (FrameworkElement, UsageWindowKind)[]
        {
            (RollingRow, UsageWindowKind.Rolling),
            (WeeklyRow, UsageWindowKind.Weekly),
            (MonthlyRow, UsageWindowKind.Monthly),
        })
        {
            row.ToolTip = a.ShowBreakdownTooltip ? BuildTooltip(kind) : null;
            row.Cursor = a.ShowBreakdownDetail || a.ShowBreakdownTooltip ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
        }
    }

    private string _appliedTheme = "";

    private void ApplyTheme(string theme)
    {
        bool isDark = theme == "dark";
        if (theme == "system")
        {
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                isDark = k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { }
        }
        var key = isDark ? "dark" : "light";
        if (_appliedTheme == key) return; // 主题未变：不触发全局资源重建
        _appliedTheme = key;
        var uri = isDark
            ? new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Themes/Light.xaml", UriKind.Absolute);
        try
        {
            var r = new ResourceDictionary { Source = uri };
            System.Windows.Application.Current.Resources.MergedDictionaries.Clear();
            System.Windows.Application.Current.Resources.MergedDictionaries.Add(r);
        }
        catch { }
    }

    private void ApplyBackground(AppearanceSettings a)
    {
        var themeBg = TryGetBrush("ThemeWindowBackground") ?? System.Windows.Media.Brushes.White;
        switch (a.BackgroundKind)
        {
            case "image" when !string.IsNullOrWhiteSpace(a.BackgroundImagePath) && File.Exists(a.BackgroundImagePath):
                try
                {
                    var img = new BitmapImage(new Uri(a.BackgroundImagePath, UriKind.Absolute));
                    // 图片不透明度不再单独可调：由全局透明度统一控制（窗口 Opacity）
                    MainCard.Background = new ImageBrush(img) { Opacity = 1.0 };
                }
                catch { MainCard.Background = themeBg; }
                break;
            case "gradient":
                var c1 = ParseColor(a.BackgroundColor) ?? ((SolidColorBrush)themeBg).Color;
                var c2 = ParseColor(a.BackgroundColor2) ?? ShiftBrightness(c1, 0.12);
                MainCard.Background = new LinearGradientBrush(c1, c2, 90);
                break;
            default:
                MainCard.Background = ParseColor(a.BackgroundColor) is { } c ? new SolidColorBrush(c) : themeBg;
                break;
        }
    }

    private static Color? ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex); } catch { return null; }
    }

    private static Color ShiftBrightness(Color c, double delta)
    {
        var f = 1 + delta;
        return Color.FromRgb(
            (byte)Math.Clamp(c.R * f, 0, 255),
            (byte)Math.Clamp(c.G * f, 0, 255),
            (byte)Math.Clamp(c.B * f, 0, 255));
    }

    private Brush? TryGetBrush(string key)
    {
        try { return System.Windows.Application.Current.TryFindResource(key) as Brush; } catch { return null; }
    }

    // ---------- 模型分摊 ----------

    private ToolTip? BuildTooltip(UsageWindowKind kind)
    {
        var s = _vm.Snapshot;
        if (s is null) return null;
        var w = kind switch
        {
            UsageWindowKind.Rolling => s.Rolling,
            UsageWindowKind.Weekly => s.Weekly,
            _ => s.Monthly,
        };
        var limitUsd = kind switch
        {
            UsageWindowKind.Rolling => 12,
            UsageWindowKind.Weekly => 30,
            _ => 60,
        };

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{UsageWindow.DisplayName(kind)} · 已用 {w.UsagePercent:0.#}% (限额 ${limitUsd})");
        sb.AppendLine($"重置剩余：{Formatting.FormatReset(w.ResetInSec)}" + (w.IsRateLimited ? " · 已限流" : string.Empty));

        var bd = _vm.BreakdownFor(kind);
        if (bd is not null && bd.Rows.Count > 0)
        {
            sb.AppendLine("模型分摊：");
            foreach (var r in bd.Rows.Take(8))
                sb.AppendLine($"  {r.Name} · ${r.QuotaCost.CostInUsd():0.00} · {r.ContributionPercent:0.#}%");
            if (bd.Rows.Count > 8) sb.AppendLine($"  … 共 {bd.Rows.Count} 个模型");
        }
        return new ToolTip { Content = sb.ToString().TrimEnd(), Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
    }

    private UsageWindowKind? _openBreakdownKind;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_SIZE = 0xF000;
    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;
    private const int ResizeBorder = 6;

    private int GetResizeDirection(System.Windows.Point p)
    {
        bool left = p.X <= ResizeBorder;
        bool right = p.X >= ActualWidth - ResizeBorder;
        bool top = p.Y <= ResizeBorder;
        bool bottom = p.Y >= ActualHeight - ResizeBorder;

        if (top && left) return WMSZ_TOPLEFT;
        if (top && right) return WMSZ_TOPRIGHT;
        if (bottom && left) return WMSZ_BOTTOMLEFT;
        if (bottom && right) return WMSZ_BOTTOMRIGHT;
        if (left) return WMSZ_LEFT;
        if (right) return WMSZ_RIGHT;
        if (top) return WMSZ_TOP;
        if (bottom) return WMSZ_BOTTOM;
        return 0;
    }

    private void OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        var dir = GetResizeDirection(p);
        Cursor = dir switch
        {
            WMSZ_LEFT or WMSZ_RIGHT => System.Windows.Input.Cursors.SizeWE,
            WMSZ_TOP or WMSZ_BOTTOM => System.Windows.Input.Cursors.SizeNS,
            WMSZ_TOPLEFT or WMSZ_BOTTOMRIGHT => System.Windows.Input.Cursors.SizeNWSE,
            WMSZ_TOPRIGHT or WMSZ_BOTTOMLEFT => System.Windows.Input.Cursors.SizeNESW,
            _ => System.Windows.Input.Cursors.Arrow,
        };
    }

    /// <summary>点击窗口边缘拖拽缩放或点击非行位置关闭模型展开面板</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            var p = e.GetPosition(this);
            var dir = GetResizeDirection(p);
            if (dir != 0)
            {
                if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
                {
                    e.Handled = true;
                    SendMessage(source.Handle, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + dir), IntPtr.Zero);
                    return;
                }
            }
        }

        if (BreakdownPanel.Visibility != Visibility.Visible) return;
        // 命中行不做处理（行的 OnRowClick 负责切换）；点空白关闭
        var hit = System.Windows.Media.VisualTreeHelper.HitTest(this, e.GetPosition(this))?.VisualHit;
        if (IsWithin(hit, RollingRow) || IsWithin(hit, WeeklyRow) || IsWithin(hit, MonthlyRow)
            || IsWithin(hit, BreakdownPanel)) return;
        CloseBreakdown();
    }

    private static bool IsWithin(DependencyObject? hit, DependencyObject? root)
    {
        for (var cur = hit; cur is not null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur))
            if (ReferenceEquals(cur, root)) return true;
        return false;
    }

    private void OnRowMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // ToolTip 在 ApplyAppearance 时已设置；这里确保展开面板不跟随悬浮切换
    }

    private void OnRowMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) { }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        var a = _settings.Appearance;
        if (!a.ShowBreakdownDetail) return;
        UsageWindowKind kind;
        if (ReferenceEquals(sender, RollingRow)) kind = UsageWindowKind.Rolling;
        else if (ReferenceEquals(sender, WeeklyRow)) kind = UsageWindowKind.Weekly;
        else kind = UsageWindowKind.Monthly;
        ToggleBreakdown(kind);
    }

    private void ToggleBreakdown(UsageWindowKind kind)
    {
        if (BreakdownPanel.Visibility == Visibility.Visible && _openBreakdownKind == kind)
            { CloseBreakdown(); return; }
        var bd = _vm.BreakdownFor(kind);
        if (bd is null) return;
        BreakdownTitle.Text = $"{UsageWindow.DisplayName(kind)} 模型分摊";
        BreakdownList.Children.Clear();
        foreach (var r in bd.Rows)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBlock { Text = r.Name, FontSize = 11, Foreground = TryGetBrush("ThemeWindowForeground"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var val = new TextBlock
            {
                Text = $"${r.QuotaCost.CostInUsd():0.00} · {r.ContributionPercent:0.#}%",
                FontSize = 11,
                Foreground = TryGetBrush("ThemeMutedForeground"),
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(val, 1);
            row.Children.Add(name);
            row.Children.Add(val);
            BreakdownList.Children.Add(row);
        }
        _openBreakdownKind = kind;
        BreakdownPanel.Visibility = Visibility.Visible;
    }

    private void CloseBreakdown()
    {
        BreakdownPanel.Visibility = Visibility.Collapsed;
        _openBreakdownKind = null;
    }

    private void OnBreakdownClose(object sender, RoutedEventArgs e)
    {
        BreakdownPanel.Visibility = Visibility.Collapsed;
        _openBreakdownKind = null;
    }

    // ---------- 状态 UI ----------

    private void OnVmChanged(object? _, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(RefreshUi);
    }

    private void RebuildTooltips()
    {
        if (!_settings.Appearance.ShowBreakdownTooltip) return;
        RollingRow.ToolTip = BuildTooltip(UsageWindowKind.Rolling);
        WeeklyRow.ToolTip = BuildTooltip(UsageWindowKind.Weekly);
        MonthlyRow.ToolTip = BuildTooltip(UsageWindowKind.Monthly);
    }

    private void RefreshUi()
    {
        var s = _vm.Snapshot;
        if (s is not null)
        {
            LoadedPanel.Visibility = Visibility.Visible;
            EmptyPanel.Visibility = Visibility.Collapsed;
            var r = s.Rolling; var w = s.Weekly; var m = s.Monthly;
            RollingPct.Text = $"{r.UsagePercent:0.#}% 已用";
            WeeklyPct.Text = $"{w.UsagePercent:0.#}% 已用";
            MonthlyPct.Text = $"{m.UsagePercent:0.#}% 已用";
            RollingBar.Value = Math.Clamp(r.UsagePercent, 0, 100);
            WeeklyBar.Value = Math.Clamp(w.UsagePercent, 0, 100);
            MonthlyBar.Value = Math.Clamp(m.UsagePercent, 0, 100);
            RollingBar.Foreground = BrushFor(r.UsagePercent);
            WeeklyBar.Foreground = BrushFor(w.UsagePercent);
            MonthlyBar.Foreground = BrushFor(m.UsagePercent);
            RollingReset.Text = $"重置剩余 {Formatting.FormatReset(r.ResetInSec)}" + (r.IsRateLimited ? " · 已限流" : string.Empty);
            WeeklyReset.Text = $"重置剩余 {Formatting.FormatReset(w.ResetInSec)}" + (w.IsRateLimited ? " · 已限流" : string.Empty);
            MonthlyReset.Text = $"重置剩余 {Formatting.FormatReset(m.ResetInSec)}" + (m.IsRateLimited ? " · 已限流" : string.Empty);
            FooterLeft.Text = _vm.UseBalanceText;
            FooterRight.Text = _vm.FetchedAtText;
            // 标题不显示时间：仅在有非静默状态（刷新/错误）时显示短文本
            if (_vm.IsRefreshing)
            {
                StatusDot.Text = "刷新中…";
                StatusDot.Visibility = Visibility.Visible;
            }
            else
            {
                StatusDot.Visibility = Visibility.Collapsed;
            }
            if (_vm.HasTransientHint)
            {
                StaleHint.Text = _vm.TransientHint ?? string.Empty;
                StaleHint.Visibility = Visibility.Visible;
            }
            else
            {
                StaleHint.Visibility = Visibility.Collapsed;
            }
            if (_tray is not null) _tray.Text = $"OcgStatus {r.UsagePercent:0.#}% / {w.UsagePercent:0.#}% / {m.UsagePercent:0.#}%".Trim();
            RebuildTooltips();
        }
        else
        {
            LoadedPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            StaleHint.Visibility = Visibility.Collapsed;
            BreakdownPanel.Visibility = Visibility.Collapsed;
            if (_vm.IsLoggedOut)
            {
                EmptyText.Text = _vm.ErrorText ?? "未配置 Cookie/Workspace，请去设置中填写";
                LoginCta.Content = "去设置";
                LoginCta.Visibility = Visibility.Visible;
            }
            else if (_vm.HasError)
            {
                EmptyText.Text = _vm.ErrorText ?? "加载失败";
                LoginCta.Content = "去设置";
                LoginCta.Visibility = Visibility.Visible;
            }
            else
            {
                EmptyText.Text = _vm.StatusText;
                LoginCta.Content = "去设置";
                LoginCta.Visibility = string.IsNullOrWhiteSpace(_settings.WorkspaceId) || string.IsNullOrWhiteSpace(_settings.AuthCookie) ? Visibility.Visible : Visibility.Collapsed;
            }
            FooterLeft.Text = _vm.HasError ? (_vm.ErrorText ?? string.Empty) : string.Empty;
            FooterRight.Text = string.Empty;
            StatusDot.Visibility = Visibility.Collapsed;
        }
        Title = s is null ? "OcgStatus" : $"OcgStatus {s.Rolling.UsagePercent:0.#}% · {s.Weekly.UsagePercent:0.#}% · {s.Monthly.UsagePercent:0.#}%";
    }

    private static System.Windows.Media.Brush BrushFor(double pct)
    {
        if (pct >= 90) return new SolidColorBrush(Color.FromRgb(0xE5, 0x3E, 0x3E));
        if (pct >= 60) return new SolidColorBrush(Color.FromRgb(0xDD, 0x6B, 0x20));
        return new SolidColorBrush(Color.FromRgb(0x38, 0xA1, 0x69));
    }

    // ---------- 窗口行为 ----------

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
        if (e.ClickCount == 2) ToggleCompact();
    }

    private void ToggleCompact()
    {
        _settings.CompactMode = !_settings.CompactMode;
        _settings.Save(AppPaths.SettingsPath);
        ApplyAppearance();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureOnScreen();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsLoaded && sizeInfo.NewSize.Width >= 200 && sizeInfo.NewSize.Height >= 160)
        {
            if (Math.Abs(sizeInfo.NewSize.Width - sizeInfo.PreviousSize.Width) > 0.5 ||
                Math.Abs(sizeInfo.NewSize.Height - sizeInfo.PreviousSize.Height) > 0.5)
            {
                _settings.Appearance.WindowSize = "custom";
                _settings.Appearance.CustomWidth = Math.Round(sizeInfo.NewSize.Width);
                _settings.Appearance.CustomHeight = Math.Round(sizeInfo.NewSize.Height);
                _settings.Save(AppPaths.SettingsPath);
            }
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.Save(AppPaths.SettingsPath);
        }
    }

    private void EnsureOnScreen()
    {
        var wa = SystemParameters.WorkArea;
        var (w, h) = WindowSizePresets.Resolve(_settings.Appearance.WindowSize, _settings.Appearance);
        if (!double.IsNaN(Width) && Width > 0) w = Width;
        if (!double.IsNaN(Height) && Height > 0) h = Height;

        if (double.IsNaN(Left) || Left < wa.Left - 20 || Left > wa.Right - 60)
        {
            Left = Math.Max(wa.Left + 20, wa.Right - w - 40);
        }
        if (double.IsNaN(Top) || Top < wa.Top - 20 || Top > wa.Bottom - 60)
        {
            Top = Math.Max(wa.Top + 20, wa.Top + 80);
        }

        if (Left + w > wa.Right) Left = Math.Max(wa.Left, wa.Right - w);
        if (Top + h > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - h);
        if (Left < wa.Left) Left = wa.Left;
        if (Top < wa.Top) Top = wa.Top;
    }

    private void OnResetPositionClick(object sender, RoutedEventArgs e) => ResetPositionToCenter();

    private void OnMenuClick(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is not null)
        {
            MenuButton.ContextMenu.PlacementTarget = MenuButton;
            MenuButton.ContextMenu.IsOpen = true;
        }
        e.Handled = true;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await _vm.RefreshAsync();
    private void OnLoginClick(object sender, RoutedEventArgs e) => OpenSettings("login");
    private void OnSettingsClick(object sender, RoutedEventArgs e) => OpenSettings("");
    private void OnMinimizeClick(object sender, RoutedEventArgs e) => Hide();
    private void OnCloseClick(object sender, RoutedEventArgs e) => ReallyClose();

    private SettingsWindow? _settingsWin;

    private void OpenSettings(string initialPage = "")
    {
        // 非模态：允许操作主程序（拖动窗口）；已打开则只激活，避免重复实例
        if (_settingsWin is { IsLoaded: true })
        {
            _settingsWin.GoTo(string.IsNullOrWhiteSpace(initialPage) ? "login" : initialPage);
            _settingsWin.Activate();
            return;
        }
        var w = new SettingsWindow(initialPage);
        w.Owner = this;
        w.Closed += (_, _) =>
        {
            _settingsWin = null;
            if (w.Saved)
            {
                Topmost = _settings.AlwaysOnTop;
                _vm.UpdateRefreshInterval(_settings.RefreshIntervalSec);
                ApplyAppearance();
                _ = _vm.RefreshAsync();
            }
        };
        _settingsWin = w;
        w.Show();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_reallyClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _vm.Stop();
        _tray?.Dispose();
        _tray = null;
    }
}