using LimitIO.Core.Runtime;
using LimitIO.Core.Storage;
using LimitIO.Core.Time;
using LimitIO.Service.Accounting;
using LimitIO.Service.Capture;
using LimitIO.Service.Enforcement;
using LimitIO.Service.Ipc;
using LimitIO.Service.Reset;
using LimitIO.Service.Worker;
using Microsoft.Extensions.Hosting;
using Serilog;

Directory.CreateDirectory(Paths.LogDirectory);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(Paths.LogDirectory, "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders();
    builder.Services.AddSerilog();

    builder.Services.AddWindowsService(options => options.ServiceName = "LimitIOService");

    builder.Services.AddSingleton<IClock>(SystemClock.Instance);
    builder.Services.AddSingleton(_ => new ConfigStore(Paths.ConfigFilePath));
    builder.Services.AddSingleton(_ => new UsageStore(Paths.UsageDatabasePath));
    builder.Services.AddSingleton<LimitIO.Service.Configuration.ConfigCache>();

    builder.Services.AddSingleton<GraceManager>();
    builder.Services.AddSingleton<LimitEnforcer>();
    builder.Services.AddSingleton<UsageAccountant>();
    builder.Services.AddSingleton<PacketCaptureEngine>();

    builder.Services.AddSingleton<AuthSessionManager>();
    builder.Services.AddSingleton<RequestRouter>();
    builder.Services.AddSingleton<NamedPipeServer>();

    builder.Services.AddHostedService<EnforcementCoordinatorService>();
    builder.Services.AddHostedService<UsageRetentionService>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "LimitIO.Service terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
