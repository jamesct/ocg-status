using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using OcgStatus.App.Services;
using OcgStatus.Core;
using ComboBox = System.Windows.Controls.ComboBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WColor = System.Windows.Media.Color;
using WBrush = System.Windows.Media.Brush;
using WinColor = System.Drawing.Color;

namespace OcgStatus.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private bool _loading = true;
    private string _initialPage = "login";
    /// <summary>true = 点过“保存”，关闭后主窗应刷新配置</summary>
    public bool Saved { get; private set; }

    public SettingsWindow(string initialPage = "login")
    {
        InitializeComponent();
        var sp = OcgStatus.App.App.Services!;
        _settings = sp.GetRequiredService<AppSettings>();
        _initialPage = initialPage;

        var a = _settings.Appearance;

        // 登录页
        WsBox.Text = _settings.WorkspaceId;
        AuthBox.Text = _settings.AuthCookie;

        // 刷新页
        IntervalBox.Text = _settings.RefreshIntervalSec.ToString();
        TopmostBox.IsChecked = _settings.AlwaysOnTop;
        AutoStartBox.IsChecked = IsAutoStartEnabled();

        // 外观页
        SelectTag(ThemeBox, a.Theme);
        OpacityBox.Text = Math.Clamp((int)Math.Round(a.Opacity * 100), 1, 100).ToString();
        SelectTag(BgKindBox, a.BackgroundKind);
        BgImagePathBox.Text = a.BackgroundImagePath;
        SolidPreview.Fill = ParseBrush(a.BackgroundColor) ?? System.Windows.Media.Brushes.Transparent;
        Grad1Preview.Fill = ParseBrush(a.BackgroundColor) ?? System.Windows.Media.Brushes.Transparent;
        Grad2Preview.Fill = ParseBrush(a.BackgroundColor2) ?? System.Windows.Media.Brushes.Transparent;
        SelectTag(CornerBox, a.CornerRadius.ToString("0"));
        ShadowBox.IsChecked = a.ShowShadow;
        SelectTag(SizeBox, a.WindowSize);
        CustomWBox.Text = a.CustomWidth.ToString("0");
        CustomHBox.Text = a.CustomHeight.ToString("0");

        // 显示内容页
        ShowProgressBox.IsChecked = a.ShowProgress;
        ShowPercentBox.IsChecked = a.ShowPercent;
        ShowResetBox.IsChecked = a.ShowReset;
        ShowUseBalanceBox.IsChecked = a.ShowUseBalance;
        ShowBdTooltipBox.IsChecked = a.ShowBreakdownTooltip;
        ShowBdDetailBox.IsChecked = a.ShowBreakdownDetail;

        SyncPanels();
        _loading = false;

        // 导航默认页（ShowPage 对空值回退 login，避免右侧空白）
        int idx = _initialPage switch { "appearance" => 2, "display" => 3, "refresh" => 1, _ => 0 };
        NavList.SelectedIndex = idx;
        ShowPage(_initialPage);
    }

    // ---------- 导航 ----------

    private void OnNavChanged(object sender, SelectionChangedEventArgs e)
    {
        ListBoxItem? item = NavList.SelectedItem as ListBoxItem;
        string tag = item?.Tag as string ?? "login";
        if (item is null && NavList.SelectedIndex >= 0 && NavList.SelectedIndex < NavList.Items.Count)
            tag = (NavList.Items[NavList.SelectedIndex] as ListBoxItem)?.Tag as string ?? "login";
        ShowPage(tag);
    }

    private void ShowPage(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) tag = "login";
        LoginPage.Visibility = tag == "login" ? Visibility.Visible : Visibility.Collapsed;
        RefreshPage.Visibility = tag == "refresh" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePage.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        DisplayPage.Visibility = tag == "display" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>供外部切换到指定页（非模态下重复打开时用）</summary>
    public void GoTo(string tag) => ShowPage(string.IsNullOrWhiteSpace(tag) ? "login" : tag);

    // ---------- 外观 ----------

    private void SyncPanels()
    {
        var kind = SelectedTag(BgKindBox);
        SolidPanel.Visibility = kind == "solid" ? Visibility.Visible : Visibility.Collapsed;
        GradientPanel.Visibility = kind == "gradient" ? Visibility.Visible : Visibility.Collapsed;
        ImagePanel.Visibility = kind == "image" ? Visibility.Visible : Visibility.Collapsed;
        CustomSizePanel.Visibility = SelectedTag(SizeBox) == "custom" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>透明度文本框（1–100）：即输即用，实时应用到窗口</summary>
    private void OnOpacityTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if (!int.TryParse(OpacityBox.Text.Trim(), out var v)) return;
        v = Math.Clamp(v, 1, 100);
        var a = _settings.Appearance;
        a.Opacity = v / 100d;
        OpacityLabel.Text = $"{v}%";
        (Owner as MainWindow)?.ApplyAppearance();
    }

    public void OnAnyAppearanceChange(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SyncPanels();
        WriteAppearanceToSettings();
        (Owner as MainWindow)?.ApplyAppearance();
    }

    private void WriteAppearanceToSettings()
    {
        var a = _settings.Appearance;
        a.Theme = SelectedTag(ThemeBox) is { } t and not "" ? t : "system";
        if (int.TryParse(OpacityBox.Text.Trim(), out var ov)) a.Opacity = Math.Clamp(ov, 1, 100) / 100d;
        a.BackgroundKind = SelectedTag(BgKindBox) is { } bk and not "" ? bk : "solid";
        a.BackgroundImagePath = BgImagePathBox.Text.Trim();
        a.CornerRadius = double.TryParse(SelectedTag(CornerBox), out var cr) ? cr : 12;
        a.ShowShadow = ShadowBox.IsChecked == true;
        a.WindowSize = SelectedTag(SizeBox) is { } sz and not "" ? sz : "medium";
        if (double.TryParse(CustomWBox.Text.Trim(), out var cw)) a.CustomWidth = Math.Clamp(cw, 200, 800);
        if (double.TryParse(CustomHBox.Text.Trim(), out var ch)) a.CustomHeight = Math.Clamp(ch, 140, 600);
        a.ShowProgress = ShowProgressBox.IsChecked == true;
        a.ShowPercent = ShowPercentBox.IsChecked == true;
        a.ShowReset = ShowResetBox.IsChecked == true;
        a.ShowUseBalance = ShowUseBalanceBox.IsChecked == true;
        a.ShowBreakdownTooltip = ShowBdTooltipBox.IsChecked == true;
        a.ShowBreakdownDetail = ShowBdDetailBox.IsChecked == true;
    }

    // ---------- 调色盘 ----------

    private void OnPickSolidColor(object sender, RoutedEventArgs e)
    {
        if (PickColor(_settings.Appearance.BackgroundColor, out var hex))
        {
            _settings.Appearance.BackgroundColor = hex;
            SolidPreview.Fill = ParseBrush(hex) ?? System.Windows.Media.Brushes.Transparent;
            (Owner as MainWindow)?.ApplyAppearance();
        }
    }

    private void OnPickGrad1(object sender, RoutedEventArgs e)
    {
        if (PickColor(_settings.Appearance.BackgroundColor, out var hex))
        {
            _settings.Appearance.BackgroundColor = hex;
            Grad1Preview.Fill = ParseBrush(hex) ?? System.Windows.Media.Brushes.Transparent;
            (Owner as MainWindow)?.ApplyAppearance();
        }
    }

    private void OnPickGrad2(object sender, RoutedEventArgs e)
    {
        if (PickColor(_settings.Appearance.BackgroundColor2, out var hex))
        {
            _settings.Appearance.BackgroundColor2 = hex;
            Grad2Preview.Fill = ParseBrush(hex) ?? System.Windows.Media.Brushes.Transparent;
            (Owner as MainWindow)?.ApplyAppearance();
        }
    }

    private bool PickColor(string currentHex, out string hex)
    {
        hex = currentHex;
        using var dlg = new System.Windows.Forms.ColorDialog();
        if (ParseColor(currentHex) is { } cur) dlg.Color = WinColor.FromArgb(cur.A, cur.R, cur.G, cur.B);
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return false;
        var c = dlg.Color;
        hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        return true;
    }

    private static WColor? ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (WColor)System.Windows.Media.ColorConverter.ConvertFromString(hex); } catch { return null; }
    }

    private static WBrush? ParseBrush(string hex) => ParseColor(hex) is { } c ? new SolidColorBrush(c) : null;

    private void OnBrowseImage(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|所有文件 (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true)
        {
            BgImagePathBox.Text = dlg.FileName;
            OnAnyAppearanceChange(this, new RoutedEventArgs());
        }
    }

    // ---------- 登录 ----------

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        var ws = WsBox.Text.Trim();
        var auth = AuthBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ws) || string.IsNullOrWhiteSpace(auth))
        {
            TestResult.Text = "请先填写 Workspace 与 Auth Cookie";
            return;
        }
        TestResult.Text = "测试中…";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
            var probe = new OcgStatus.Core.HttpUsageProvider(new TestLogger(), http, ws, auth);
            var res = await probe.FetchAsync();
            if (res.Kind == OcgStatus.Core.UsageResultKind.Ok && res.Snapshot is not null)
            {
                var s = res.Snapshot;
                TestResult.Text = $"成功：5h {s.Rolling.UsagePercent:0.#}% · 周 {s.Weekly.UsagePercent:0.#}% · 月 {s.Monthly.UsagePercent:0.#}%";
            }
            else
            {
                TestResult.Text = $"失败：{res.Kind} {res.Message}";
            }
        }
        catch (Exception ex) { TestResult.Text = $"异常：{ex.Message}"; }
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show(this, "确定清除本机保存的 Workspace 与 Cookie？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _settings.WorkspaceId = string.Empty;
        _settings.AuthCookie = string.Empty;
        _settings.Save(AppPaths.SettingsPath);
        WsBox.Text = string.Empty;
        AuthBox.Text = string.Empty;
        TestResult.Text = "已清除";
    }

    // ---------- 保存 ----------

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var ws = WsBox.Text.Trim();
        var auth = AuthBox.Text.Trim();
        if (_initialPage == "login" && string.IsNullOrWhiteSpace(ws))
        {
            System.Windows.MessageBox.Show(this, "请填写以 wrk_ 开头的 Workspace ID。", "提示");
            return;
        }
        if (_initialPage == "login" && string.IsNullOrWhiteSpace(auth))
        {
            System.Windows.MessageBox.Show(this, "请粘贴 auth Cookie（Fe26.2**… 的值）。", "提示");
            return;
        }
        if (!string.IsNullOrWhiteSpace(ws) && !ws.StartsWith("wrk_", StringComparison.Ordinal))
        {
            System.Windows.MessageBox.Show(this, "Workspace ID 应以 wrk_ 开头。", "提示");
            return;
        }
        if (!int.TryParse(IntervalBox.Text.Trim(), out var iv))
        {
            System.Windows.MessageBox.Show(this, "刷新间隔必须是数字。", "提示");
            return;
        }
        iv = Math.Clamp(iv, 30, 86400);

        _settings.WorkspaceId = ws;
        _settings.AuthCookie = auth;
        _settings.RefreshIntervalSec = iv;
        _settings.AlwaysOnTop = TopmostBox.IsChecked == true;
        WriteAppearanceToSettings();
        _settings.Save(AppPaths.SettingsPath);
        ApplyAutoStart(AutoStartBox.IsChecked == true);
        Saved = true;
        Close(); // 非模态窗口：不设置 DialogResult（会抛异常），直接关闭即可
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    // ---------- 工具 ----------

    private static void SelectTag(ComboBox box, string tag)
    {
        foreach (var item in box.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag is string t && t == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private static string SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private sealed class TestLogger : Microsoft.Extensions.Logging.ILogger<OcgStatus.Core.HttpUsageProvider>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel l) => false;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel l, Microsoft.Extensions.Logging.EventId id, TState st, Exception? ex, Func<TState, Exception?, string> f) { }
    }

    private static bool IsAutoStartEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
            return k?.GetValue("OcgStatus") is not null;
        }
        catch { return false; }
    }

    private static void ApplyAutoStart(bool enable)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (k is null) return;
            if (enable)
            {
                var exe = Environment.ProcessPath ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(exe)) k.SetValue("OcgStatus", $"\"{exe}\"");
            }
            else k.DeleteValue("OcgStatus", false);
        }
        catch { }
    }
}