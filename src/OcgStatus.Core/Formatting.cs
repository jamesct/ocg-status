namespace OcgStatus.Core;

public static class Formatting
{
    public static string FormatReset(int sec)
    {
        if (sec <= 0) return "即将重置";
        int d = sec / 86_400;
        int h = (sec % 86_400) / 3_600;
        int m = (sec % 3_600) / 60;
        if (d > 0) return $"{d}天 {h}小时";
        if (h > 0) return $"{h}小时 {m}分";
        if (m > 0) return $"{m}分钟";
        return $"{sec}秒";
    }

    public static string FormatRemaining(UsageWindow w)
    {
        return $"{w.RemainingPercent:0.#}% 剩余 · {FormatReset(w.ResetInSec)}";
    }

    public static string ColorKey(double pct)
    {
        if (pct >= 90) return "red";
        if (pct >= 60) return "orange";
        return "green";
    }
}
