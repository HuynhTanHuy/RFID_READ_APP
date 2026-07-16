using CareHR.RfidGateway.Api;
using CareHR.RfidGateway.Configuration;
using CareHR.RfidGateway.Reader;
using CareHR.RfidGateway.Sdk;
using CareHR.RfidGateway.Services;
using CareHR.RfidGateway.UI;
using CareHR.RfidGateway.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace CareHR.RfidGateway;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Environment.ContentRootPath = AppContext.BaseDirectory;

        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logDir, "gateway-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                shared: true)
            .CreateLogger();

        builder.Services.AddSerilog();
        builder.Services.Configure<GatewayOptions>(
            builder.Configuration.GetSection(GatewayOptions.SectionName));

        builder.Services.AddSingleton<GatewayOptionsAccessor>();
        builder.Services.AddSingleton<AppSettingsStore>();
        builder.Services.AddSingleton<GatewayState>();
        builder.Services.AddSingleton<TagDebouncer>();
        builder.Services.AddSingleton<UhfPrimeSdk>();
        builder.Services.AddSingleton<RfidReader>();
        builder.Services.AddHttpClient<CareHrApiClient>();
        builder.Services.AddSingleton<RfidGatewayWorker>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<RfidGatewayWorker>());

        builder.Services.AddTransient<SettingsForm>();
        builder.Services.AddTransient<StatusForm>();

        using var host = builder.Build();

        var bound = host.Services.GetRequiredService<IOptions<GatewayOptions>>().Value;
        var accessor = host.Services.GetRequiredService<GatewayOptionsAccessor>();
        accessor.Replace(bound);

        try
        {
            AutoStartHelper.SetEnabled(bound.AutoStartWithWindows);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to apply AutoStart setting");
        }

        host.Start();

        try
        {
            Application.Run(new TrayAppContext(host));
        }
        finally
        {
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Log.CloseAndFlush();
        }
    }
}
