using Microsoft.Extensions.Configuration;
using TBot;

// Config: appsettings.json (Bot section) with command-line overrides, e.g.:
//   dotnet run -- --Bot:Account=test2 --Bot:CreateAccount=true --Bot:LoginHost=192.168.1.37
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddCommandLine(args)
    .Build();

var cfg = config.GetSection("Bot").Get<BotConfig>() ?? new BotConfig();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    await new BotRunner(cfg).RunAsync(cts.Token);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[TBot] {ex.GetType().Name}: {ex.Message}");
    return 1;
}
