using TLauncher;

// TLauncher.Net — console front-end over TLauncher.Core. Mirrors 4StoryDlg.cpp: patch handshake →
// download/unzip → launch "TClient.exe <loginIp> <loginPort>". No skinned UI, no CTModuleProtector.

var opts = ArgParser.Parse(args);
if (opts.ContainsKey("--help") || opts.ContainsKey("-h")) { PrintUsage(); return 0; }

var s = LauncherConfig.Resolve(
    gameDir: opts.GetValueOrDefault("--game-dir"),
    configPath: opts.GetValueOrDefault("--config"),
    server: opts.GetValueOrDefault("--server"),
    port: opts.GetValueOrDefault("--port"),
    version: opts.GetValueOrDefault("--version"),
    exe: opts.GetValueOrDefault("--exe"));

bool noLaunch = opts.ContainsKey("--no-launch");
int launchTimeout = LauncherConfig.ParseInt(opts.GetValueOrDefault("--launch-timeout"), 0);

Console.WriteLine("=== TLauncher.Net ===");
Console.WriteLine($"  patch server  : {s.Server}:{s.Port}");
Console.WriteLine($"  client version: {s.Version}");
Console.WriteLine($"  game dir      : {s.GameDir}");
Console.WriteLine($"  config        : {(File.Exists(s.ConfigPath) ? s.ConfigPath : "(none — defaults/args)")}");
Console.WriteLine();

PatchManifest manifest;
try
{
    Console.WriteLine($"Connecting to patch server {s.Server}:{s.Port} …");
    manifest = await PatchClient.RequestAsync(s.Server, s.Port, s.Version, CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[error] patch handshake failed: {ex.Message}");
    return 2;
}

Console.WriteLine("CT_NEWPATCH_ACK received:");
Console.WriteLine($"  download base : {(string.IsNullOrEmpty(manifest.FtpUrl) ? "(none)" : manifest.FtpUrl)}");
Console.WriteLine($"  login server  : {manifest.LoginIp}:{manifest.LoginPort}");
Console.WriteLine($"  min beta ver  : {manifest.MinBetaVer}");
Console.WriteLine($"  files to patch: {manifest.Files.Count}");
foreach (var f in manifest.Files)
    Console.WriteLine($"     v{f.Version}  {(string.IsNullOrEmpty(f.Path) ? "" : f.Path + "/")}{f.Name}  ({f.Size:N0} bytes)");
Console.WriteLine();

if (manifest.Files.Count > 0)
{
    Console.WriteLine("Downloading patch files …");
    await Patcher.DownloadAsync(manifest, s.GameDir, new Progress<string>(Console.WriteLine), null, CancellationToken.None);
    Console.WriteLine();
}

string exePath = Path.IsPathRooted(s.Exe) ? s.Exe : Path.Combine(s.GameDir, s.Exe);
Console.WriteLine($"Launch command: \"{exePath}\" {manifest.LoginIp} {manifest.LoginPort}");
if (noLaunch) { Console.WriteLine("(--no-launch: not spawning the client)"); return 0; }

var result = Patcher.Launch(exePath, s.GameDir, manifest.LoginIp, manifest.LoginPort);
Console.WriteLine(result.Message);
switch (result.Status)
{
    case LaunchStatus.NotFound: return 3;
    case LaunchStatus.Failed:
    case LaunchStatus.ElevationDeclined: return 4;
}

if (launchTimeout > 0 && result.Process is { } proc)
{
    using (proc)
    {
        if (proc.WaitForExit(launchTimeout * 1000))
            Console.WriteLine($"Client exited after {launchTimeout}s with code {proc.ExitCode}.");
        else
        {
            Console.WriteLine($"Client still alive after {launchTimeout}s (launched OK); killing it (test mode).");
            try { proc.Kill(entireProcessTree: true); } catch { }
        }
    }
}

return 0;

static void PrintUsage()
{
    Console.WriteLine("""
        TLauncher.Net — 4Story patch + launch (C# console core)

        Usage: TLauncher.Net [options]
          --server <host>       patch server host (default: config.ini [Launcher] address, else 127.0.0.1)
          --port <n>            patch server port  (default: config.ini port, else 3716)
          --version <n>         current client version to report (default: config.ini version, else 0)
          --game-dir <path>     folder with config.ini + the client exe (default: config dir or cwd)
          --config <path>       explicit config.ini path
          --exe <name>          client exe to launch (default: config.ini exe, else TClient.exe)
          --no-launch           do the patch handshake only; print the launch command, don't spawn
          --launch-timeout <s>  after spawning, wait <s>s; if still alive, kill it (test mode)
          --help                this help
        """);
}

/// <summary>Trivial "--key value" / "--flag" argument parser.</summary>
internal static class ArgParser
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-')) continue;
            string key = args[i];
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) d[key] = args[++i];
            else d[key] = "true";
        }
        return d;
    }
}
