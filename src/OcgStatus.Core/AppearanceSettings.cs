namespace OcgStatus.Core;

public sealed class AppearanceSettings
{
    // 主题
    public string Theme { get; set; } = "system"; // system | light | dark

    // 透明度（窗口整体）
    public double Opacity { get; set; } = 1.0;

    // 背景：solid | gradient | image
    public string BackgroundKind { get; set; } = "solid";
    public string BackgroundColor { get; set; } = "";   // solid 用（空则用主题默认）
    public string BackgroundColor2 { get; set; } = "";  // gradient 第二色
    public string BackgroundImagePath { get; set; } = "";
    public double BackgroundImageOpacity { get; set; } = 0.8;

    // 圆角 / 阴影
    public double CornerRadius { get; set; } = 12;
    public bool ShowShadow { get; set; } = true;

    // 窗口尺寸：small | medium | large | custom
    public string WindowSize { get; set; } = "medium";
    public double CustomWidth { get; set; } = 360;
    public double CustomHeight { get; set; } = 260;

    // 可见性开关
    public bool ShowProgress { get; set; } = true;
    public bool ShowPercent { get; set; } = true;
    public bool ShowRollingReset { get; set; } = true; // 5 小时剩余重置时间
    public bool ShowReset { get; set; } = true;        // 周 / 月剩余重置时间
    public bool ShowUseBalance { get; set; } = true;
    public bool ShowBreakdownTooltip { get; set; } = true;
    public bool ShowBreakdownDetail { get; set; } = true;
}

public static class WindowSizePresets
{
    public static (double W, double H) Resolve(string size, AppearanceSettings a) => size switch
    {
        "small" => (280, 190),
        "large" => (360, 260),
        "custom" => (a.CustomWidth, a.CustomHeight),
        _ => (320, 210), // medium
    };
}