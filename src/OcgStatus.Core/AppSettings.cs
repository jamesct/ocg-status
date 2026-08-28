using System.Text.Json;

namespace OcgStatus.Core;

public sealed class AppSettings
{
    public string WorkspaceId { get; set; } = "";
    public int RefreshIntervalSec { get; set; } = 300;
    public bool AlwaysOnTop { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    /// <summary>Manual auth cookie for opencode.ai (full Cookie header value, contains auth=...).</summary>
    public string AuthCookie { get; set; } = "";
    public AppearanceSettings Appearance { get; set; } = new();
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            var json = File.ReadAllText(path);
            var s = JsonSerializer.Deserialize<AppSettings>(json);
            return s is null ? new AppSettings() : Sanitize(s);
        }
        catch { return new AppSettings(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OcgStatus", "settings.json");

    private static AppSettings Sanitize(AppSettings s)
    {
        s.RefreshIntervalSec = Math.Clamp(s.RefreshIntervalSec, 30, 86400);
        return s;
    }
}
