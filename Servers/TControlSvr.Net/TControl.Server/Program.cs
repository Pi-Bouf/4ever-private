using Serilog;
using TControl.Server;
using TControl.Server.Ops;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders(); // Serilog is the sole sink (avoids duplicate console lines)
builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.Configure<ControlServerOptions>(builder.Configuration.GetSection("Control"));

// OS-specific ops behind interfaces; cross-platform no-op defaults (see PORT_STATUS.md). A Windows
// implementation can be registered here instead to enable real service control / platform metrics.
builder.Services.AddSingleton<IServiceController, NoOpServiceController>();
builder.Services.AddSingleton<IPlatformMonitor, NoOpPlatformMonitor>();
builder.Services.AddSingleton<IFileDeploy, NoOpFileDeploy>();

builder.Services.AddHostedService<ControlWorker>();

var host = builder.Build();
host.Run();
