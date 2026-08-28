using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OcgStatus.App.Services;
using OcgStatus.App.ViewModels;
using OcgStatus.Core;

namespace OcgStatus.App;

public partial class App : System.Windows.Application
{
    public static ServiceProvider? Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();
        var settings = AppSettings.Load(AppPaths.SettingsPath);
        sc.AddSingleton(settings);
        sc.AddLogging(b => b.AddDebug().AddConsole().SetMinimumLevel(LogLevel.Information));
        sc.AddHttpClient<HttpUsageProvider>(c => c.Timeout = TimeSpan.FromSeconds(25));
        sc.AddSingleton<IUsageProvider>(sp =>
        {
            var log = sp.GetRequiredService<ILogger<HttpUsageProvider>>();
            var fac = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>();
            var http = fac.CreateClient(nameof(HttpUsageProvider));
            return new HttpUsageProvider(log, http, settings.WorkspaceId, settings.AuthCookie,
                configReader: () => (settings.WorkspaceId, settings.AuthCookie));
        });
        sc.AddSingleton<MainViewModel>();
        Services = sc.BuildServiceProvider();

        // Fail fast: log startup path without secrets
        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Startup AppData={Path}", AppPaths.SettingsPath);

        // HttpUsageProvider uses no WebView2 profile; kept AppPaths.WebView2Folder for migration only
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (Services is not null)
        {
            (Services.GetService<IUsageProvider>() as IAsyncDisposable)?.DisposeAsync().AsTask().Wait(2000);
            Services.Dispose();
        }
        base.OnExit(e);
    }
}
