using System.Diagnostics;

namespace TBot;

/// <summary>
/// <c>--Bot:Scenario=stress</c>: many bots in one process, each on its own thread and account, ramped in at
/// <see cref="BotConfig.StressRampPerSec"/>. Every bot runs the normal <see cref="BotRunner"/> flow (account →
/// login → create char → enter the map → walk). A status line every 5 s shows how many bots are at each stage,
/// alongside the map server's RSS and CPU (sampled through <c>docker exec</c>); the end summary gives login/enter
/// latency percentiles, kicks and failures grouped by message.
/// </summary>
public static class StressTest
{
    private sealed class Slot
    {
        public required BotRunner Runner;
        public required string Account;
        public string? Error;
    }

    public static async Task<int> RunAsync(BotConfig cfg, CancellationToken ct)
    {
        if (!cfg.CreateAccount || cfg.GlobalConnectionString.Length == 0)
        {
            Console.Error.WriteLine("the stress scenario needs --Bot:CreateAccount=true and --Bot:GlobalConnectionString");
            return 2;
        }

        int n = cfg.StressCount;
        var slots = new Slot[n];
        var threads = new Thread[n];
        var started = Stopwatch.StartNew();
        Console.WriteLine($"[stress] {n} bots, prefix '{cfg.StressPrefix}', {cfg.StressRampPerSec}/s ramp, " +
                          $"walk {cfg.MoveDurationSec}s every {cfg.MoveTickMs} ms, radius 20..{cfg.StressMaxRadius}");

        // Create every account up front. Besides keeping DB inserts out of the ramp, it keeps each bot on its own
        // thread: BotRunner is blocking after its one await (the account insert), and a bot resuming from that await
        // on a thread-pool thread would hold it for the whole walk and starve the pool (the ramp crawled to ~1.5/s).
        var prov = new AccountProvisioner(cfg.GlobalConnectionString);
        int created = 0;
        for (int i = 0; i < n; i++)
            if (await prov.EnsureAccountAsync(ForBot(cfg, i).Account, cfg.Password, ct)) created++;
        Console.WriteLine($"[stress] accounts ready ({created} created, {n - created} existing) in {started.Elapsed.TotalSeconds:0.0}s");
        started.Restart();

        using var statusCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sampler = new ServerSampler(cfg.StressDockerContainer);
        var status = StatusLoopAsync(slots, sampler, started, statusCts.Token);

        for (int i = 0; i < n && !ct.IsCancellationRequested; i++)
        {
            var botCfg = ForBot(cfg, i);
            var slot = new Slot { Runner = new BotRunner(botCfg), Account = botCfg.Account };
            slots[i] = slot;
            // Small stacks: the bots are blocking loops with shallow call depth, and there can be hundreds.
            threads[i] = new Thread(() =>
            {
                try { slot.Runner.RunAsync(ct).GetAwaiter().GetResult(); }
                catch (Exception ex) { slot.Error = $"{slot.Runner.Stage}: {ex.GetType().Name}: {ex.Message}"; }
            }, 256 * 1024) { IsBackground = true, Name = botCfg.Account };
            threads[i].Start();
            await Task.Delay(TimeSpan.FromSeconds(1 / cfg.StressRampPerSec), CancellationToken.None);
        }

        foreach (var t in threads) t?.Join();
        statusCts.Cancel();
        try { await status; } catch (OperationCanceledException) { }

        return PrintSummary(slots.Where(s => s is not null).ToArray(), sampler, started.Elapsed);
    }

    /// <summary>One bot's config: its own account and character name, and a walk radius spread over the crowd.</summary>
    private static BotConfig ForBot(BotConfig c, int i)
    {
        var b = c.Copy();
        b.Account = $"{c.StressPrefix}{i + 1:D3}";
        b.CharName = "Str" + Capitalize(c.StressPrefix) + Letters(i);
        b.CharSlot = 0;
        b.LogTag = $"[{b.Account}]";
        b.Quiet = true;
        b.CreateAccount = false;   // provisioned up front by RunAsync
        b.Scenario = "";
        b.MoveRadius = c.StressMaxRadius <= 20 ? 20 : 20 + (i * 37) % (c.StressMaxRadius - 20);
        return b;
    }

    // Names are letters only (digits are refused), and the filter also refuses names containing "gm" (reported as
    // DupName) or "bot" — so no 'g' and no 'o'.
    private const string NameLetters = "abcdefhijklmnpqrstuvwxyz";

    private static string Letters(int i)
    {
        Span<char> s = stackalloc char[3];
        for (int k = 2; k >= 0; k--) { s[k] = NameLetters[i % NameLetters.Length]; i /= NameLetters.Length; }
        return new string(s);
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static async Task StatusLoopAsync(Slot[] slots, ServerSampler sampler, Stopwatch started, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(ct))
        {
            var live = slots.Where(s => s is not null).ToArray();
            int walking = live.Count(s => s.Runner.Stage == "walking" && s.Error is null && !s.Runner.Kicked);
            int connecting = live.Count(s => s.Runner.Stage is "account" or "login" or "enter" && s.Error is null);
            int done = live.Count(s => s.Runner.Stage == "done" && !s.Runner.Kicked);
            int kicked = live.Count(s => s.Runner.Kicked);
            int failed = live.Count(s => s.Error is not null);
            var srv = await sampler.SampleAsync();
            Console.WriteLine($"[{started.Elapsed:mm\\:ss}] started {live.Length,4} | connecting {connecting,4} | in world {walking,4} " +
                              $"| done {done,4} | kicked {kicked,3} | failed {failed,3} | map {srv}");
        }
    }

    private static int PrintSummary(Slot[] slots, ServerSampler sampler, TimeSpan elapsed)
    {
        var ok = slots.Where(s => s.Error is null && !s.Runner.Kicked && s.Runner.Stage == "done").ToArray();
        var entered = slots.Where(s => s.Runner.EnterMs > 0).ToArray();
        Console.WriteLine();
        Console.WriteLine($"===== stress summary ({elapsed:mm\\:ss}) =====");
        Console.WriteLine($"bots {slots.Length} | completed walk {ok.Length} | kicked {slots.Count(s => s.Runner.Kicked)} " +
                          $"| failed {slots.Count(s => s.Error is not null)}");
        Console.WriteLine($"login ms  {Percentiles(slots.Where(s => s.Runner.LoginMs > 0).Select(s => s.Runner.LoginMs))}");
        Console.WriteLine($"enter ms  {Percentiles(entered.Select(s => s.Runner.EnterMs))}");
        Console.WriteLine($"moves sent {slots.Sum(s => s.Runner.MovesSent)}");
        Console.WriteLine($"map peak  {sampler.PeakSummary}");
        foreach (var g in slots.Where(s => s.Error is not null).GroupBy(s => s.Error).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  {g.Count(),4} × {g.Key}   (e.g. {g.First().Account})");
        foreach (var s in slots.Where(s => s.Runner.Kicked).Take(10))
            Console.WriteLine($"  kicked: {s.Account} after {s.Runner.MovesSent} moves — {s.Runner.KickReason}");
        return ok.Length == slots.Length ? 0 : 1;
    }

    private static string Percentiles(IEnumerable<double> values)
    {
        var v = values.OrderBy(x => x).ToArray();
        if (v.Length == 0) return "n/a";
        double P(double p) => v[Math.Min(v.Length - 1, (int)(p * v.Length))];
        return $"p50 {P(0.50):0} | p95 {P(0.95):0} | max {v[^1]:0}  (n={v.Length})";
    }

    /// <summary>Reads the map server's RSS and CPU from <c>/proc/1</c> inside its container.</summary>
    private sealed class ServerSampler(string container)
    {
        private long _lastTicks = -1;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private TimeSpan _lastAt;
        private long _peakRssKb;
        private double _peakCpu;

        public string PeakSummary => container.Length == 0 ? "not sampled" : $"RSS {_peakRssKb / 1024} MB | CPU {_peakCpu:0.00} core";

        public async Task<string> SampleAsync()
        {
            if (container.Length == 0) return "not sampled";
            try
            {
                var psi = new ProcessStartInfo("docker", $"exec {container} sh -c \"grep VmRSS /proc/1/status; cat /proc/1/stat\"")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                using var p = Process.Start(psi)!;
                string output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                long rssKb = long.Parse(lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
                // /proc/<pid>/stat: fields after the ")" — utime and stime are fields 14 and 15 (clock ticks, 100 Hz).
                var f = lines[1][(lines[1].LastIndexOf(')') + 2)..].Split(' ');
                long ticks = long.Parse(f[11]) + long.Parse(f[12]);
                var now = _clock.Elapsed;
                string cpu = "  -  ";
                if (_lastTicks >= 0)
                {
                    double cores = (ticks - _lastTicks) / 100.0 / (now - _lastAt).TotalSeconds;
                    _peakCpu = Math.Max(_peakCpu, cores);
                    cpu = $"{cores:0.00}";
                }
                _lastTicks = ticks; _lastAt = now;
                _peakRssKb = Math.Max(_peakRssKb, rssKb);
                return $"RSS {rssKb / 1024,4} MB, CPU {cpu} core";
            }
            catch (Exception ex) { return $"sample failed ({ex.GetType().Name})"; }
        }
    }
}
