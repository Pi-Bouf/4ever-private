namespace TLauncher;

/// <summary>Resolved launcher settings (from config.ini + overrides).</summary>
public sealed record LauncherSettings(string Server, int Port, uint Version, string GameDir, string ConfigPath,
    string Exe, string HomeUrl, string NewsUrl);

/// <summary>
/// Reads the shared <c>config.ini</c> <c>[Launcher]</c> section (the same file the client + C++ launcher
/// use) and resolves the effective launcher settings, applying explicit overrides and the port-0 → 3716
/// default (the docker patchsvr port).
/// </summary>
public static class LauncherConfig
{
    public const int DefaultPatchPort = 3716;

    /// <summary>Minimal INI section reader (case-insensitive keys).</summary>
    public static Dictionary<string, string> ReadSection(string path, string section)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return d;
        string current = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line[^1] == ']') { current = line[1..^1].Trim(); continue; }
            if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            d[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return d;
    }

    public static LauncherSettings Resolve(
        string? gameDir = null, string? configPath = null,
        string? server = null, string? port = null, string? version = null, string? exe = null)
    {
        string cwd = Directory.GetCurrentDirectory();
        string cfg = configPath ?? Path.Combine(gameDir ?? cwd, "config.ini");
        var l = ReadSection(cfg, "Launcher");
        string gdir = gameDir ?? (File.Exists(cfg) ? Path.GetDirectoryName(Path.GetFullPath(cfg))! : cwd);

        string srv = Coalesce(server, Get(l, "address"), "127.0.0.1");
        int prt = ParseInt(Coalesce(port, Get(l, "port"), null), 0);
        if (prt == 0) prt = DefaultPatchPort;
        uint ver = (uint)ParseInt(Coalesce(version, Get(l, "version"), null), 0);
        string ex = Coalesce(exe, Get(l, "exe"), "TClient.exe");

        // News/home URLs: config.ini [Launcher] homeurl/newsurl → url.txt (line 1 = homepage, line 3 = notice,
        // as the C++ ReadURLFile) → the launcher's built-in defaults.
        var url = ReadUrlTxt(gdir);
        string home = Coalesce(Get(l, "homeurl"), url.Home, "http://en.4story.gameforge.com");
        string news = Coalesce(Get(l, "newsurl"), url.News, "http://en.4story.gameforge.com/launcher");

        return new LauncherSettings(srv, prt, ver, gdir, cfg, ex, home, news);
    }

    private static (string? Home, string? News) ReadUrlTxt(string gameDir)
    {
        try
        {
            string p = Path.Combine(gameDir, "url.txt");
            if (!File.Exists(p)) return (null, null);
            var lines = File.ReadAllLines(p);
            string? home = lines.Length > 0 ? lines[0].Trim() : null;
            string? news = lines.Length > 2 ? lines[2].Trim() : null;   // line 3 = notice (C++ ReadURLFile)
            return (string.IsNullOrWhiteSpace(home) ? null : home, string.IsNullOrWhiteSpace(news) ? null : news);
        }
        catch { return (null, null); }
    }

    public static int ParseInt(string? s, int def) => int.TryParse(s, out var v) ? v : def;

    private static string? Get(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : null;
    private static string Coalesce(string? a, string? b, string? c)
        => !string.IsNullOrWhiteSpace(a) ? a : !string.IsNullOrWhiteSpace(b) ? b : (c ?? "");
}
