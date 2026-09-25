using TMap.Data;
using Xunit;
using Xunit.Abstractions;

namespace TMap.Data.Tests;

/// <summary>
/// The map runs DB-free by design (the server catches connection failures and synthesizes defaults), so
/// there is nothing to assert without a live database. These tests exercise the real query path only when
/// <c>TMAP_TEST_GAME</c> points at a game DB; otherwise they no-op, matching the sibling ports.
/// <para>
/// A skipped run is <b>logged</b>, not silent: without the env var these assert nothing, and a green suite
/// would otherwise read as "the DB path is verified" when it was never entered. To run them against the
/// compose stack (see the repo-root <c>docker-compose.yml</c>):
/// </para>
/// <code>
/// $env:TMAP_TEST_GAME = "Server=localhost,11433;Database=TGame_gsp;User ID=sa;Password=$env:SA_PASSWORD;TrustServerCertificate=True;Encrypt=False"
/// dotnet test TMap.Data.Tests
/// </code>
/// </summary>
public class DataSmokeTests
{
    private readonly ITestOutputHelper _out;
    public DataSmokeTests(ITestOutputHelper output) => _out = output;

    private static string? GameCs => Environment.GetEnvironmentVariable("TMAP_TEST_GAME");

    /// <summary>True when a live game DB is configured; logs the skip reason when it is not.</summary>
    private bool Configured([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (!string.IsNullOrWhiteSpace(GameCs)) return true;
        _out.WriteLine($"TMAP_TEST_GAME not set — skipping {caller} (asserted nothing).");
        return false;
    }

    [Fact]
    public async Task Ping_Succeeds_WhenConfigured()
    {
        if (!Configured()) return;
        var db = new GameDatabase(GameCs!);
        await db.PingAsync();
    }

    [Fact]
    public async Task LoadChar_ReturnsNull_ForUnknownChar_WhenConfigured()
    {
        if (!Configured()) return;
        var db = new GameDatabase(GameCs!);
        var row = await db.LoadCharAsync(0xFFFFFFF0);
        Assert.Null(row);
    }

    [Fact]
    public async Task LoadTemplates_PopulatesItemAndMagicCharts_WhenConfigured()
    {
        if (!Configured()) return;
        var db = new GameDatabase(GameCs!);
        var store = await db.LoadTemplatesAsync();
        Assert.True(store.HasItems, "TITEMCHART should yield at least one item template.");
        // Every loaded item template has a 4-element revision table (fRevision/fMRevision/fAtRate/fMAtRate).
        Assert.All(store.Items.Values, t => Assert.Equal(4, t.Revision.Length));
        _out.WriteLine($"Loaded {store.Items.Count} item templates.");
    }

    /// <summary>
    /// The AI-script load against the real baseline. Pinned to the shape actually in
    /// <c>TGame_gsp</c> (47 <c>TAICHART</c> rows / 18 commands / 6 conditions → 2 scripts), because that
    /// shape is what falsified the guess that "aggressive = binds SetHost under AT_ENTER": both
    /// live scripts bind it, so the real discriminator is the <c>AT_ENTERLB</c> engagement chain. If a
    /// migration ever changes the chart, this is the test that should make you re-read the scripts.
    /// </summary>
    [Fact]
    public async Task LoadTemplates_PopulatesAiScripts_WhenConfigured()
    {
        if (!Configured()) return;
        var store = await new GameDatabase(GameCs!).LoadTemplatesAsync();

        Assert.Equal(2, store.AiScripts.Count);
        Assert.True(store.AiScripts.ContainsKey(1) && store.AiScripts.ContainsKey(2));
        Assert.Equal(47, store.AiScripts.Values.Sum(s => s.BindingCount));   // every TAICHART row bound

        // Script 1 engages on sight, script 2 only fights back — the live split (3192 vs 344 monsters).
        Assert.True(store.AiScripts[1].IsAggressive);
        Assert.False(store.AiScripts[2].IsAggressive);

        // Both bind SetHost under AT_ENTER, so the discarded heuristic cannot tell them apart.
        Assert.All(store.AiScripts.Values, s => Assert.True(s.Binds(AiTrigger.Enter, AiCommandKind.SetHost)));

        // The 6 TAICONCHART rows landed on their commands.
        Assert.Equal(6, store.AiScripts.Values
            .SelectMany(s => s.ByTrigger.SelectMany(b => b.Values).SelectMany(l => l))
            .Select(b => b.Command).Distinct().Sum(c => c.Conditions.Count));

        // Every monster template resolves a script (all live rows carry a non-zero bAIType).
        Assert.NotEmpty(store.MonsterTemplates);
        Assert.All(store.MonsterTemplates.Values, t => Assert.NotNull(store.AiScriptFor(t.AiType)));

        _out.WriteLine($"Loaded {store.AiScripts.Count} AI scripts over {store.MonsterTemplates.Count} monster templates.");
    }
}
