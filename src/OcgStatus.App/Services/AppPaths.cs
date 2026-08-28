using System.IO;

namespace OcgStatus.App.Services;

public static class AppPaths
{
    public static string SettingsPath => OcgStatus.Core.AppSettings.DefaultPath;
    public static string SnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OcgStatus", "last-snapshot.json");
    public static string WebView2Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OcgStatus", "WebView2");
    public static string LogsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OcgStatus", "logs");
}
