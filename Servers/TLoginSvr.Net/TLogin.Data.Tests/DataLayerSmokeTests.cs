using TLogin.Data;
using Xunit;
using Xunit.Abstractions;

namespace TLogin.Data.Tests;

/// <summary>
/// Integration smoke tests against a live SQL Server with the restored baselines. They run only when
/// <c>TLOGIN_TEST_GLOBAL</c> (and optionally <c>TLOGIN_TEST_GAME</c>) connection strings are set; otherwise
/// they no-op so the suite stays green without a database. With docker-compose up, point them at
/// <c>Server=localhost,11433;...</c>.
/// </summary>
public class DataLayerSmokeTests
{
    private readonly ITestOutputHelper _out;
    public DataLayerSmokeTests(ITestOutputHelper output) => _out = output;

    private static string? Global => Environment.GetEnvironmentVariable("TLOGIN_TEST_GLOBAL");
    private static string? Game => Environment.GetEnvironmentVariable("TLOGIN_TEST_GAME");

    private bool Skip()
    {
        if (string.IsNullOrWhiteSpace(Global))
        {
            _out.WriteLine("TLOGIN_TEST_GLOBAL not set — skipping DB smoke test.");
            return true;
        }
        return false;
    }

    [Fact]
    public async Task GetNation_ReturnsNonZero()
    {
        if (Skip()) return;
        var db = new GlobalDatabase(Global!);
        byte nation = await db.GetNationAsync();
        _out.WriteLine($"Nation = {nation}");
        Assert.True(nation > 0, "TGetNation should return a configured nation.");
    }

    [Fact]
    public async Task LoadGroups_DoesNotThrow_AndGameDbsConnect()
    {
        if (Skip()) return;
        var db = new GlobalDatabase(Global!);
        var groups = await db.LoadGroupsAsync();
        _out.WriteLine($"Loaded {groups.Count} group(s).");
        Assert.NotNull(groups);

        if (!string.IsNullOrWhiteSpace(Game) && groups.Count > 0)
        {
            var game = new GameDatabase(Game!);
            // A harmless lookup against a non-existent user must execute without throwing.
            var chars = await game.CharListAsync(0);
            Assert.NotNull(chars);
        }
    }

    [Fact]
    public async Task CheckIp_Executes()
    {
        if (Skip()) return;
        var db = new GlobalDatabase(Global!);
        int result = await db.CheckIpAsync("127.0.0.1");
        _out.WriteLine($"TCheckIP(127.0.0.1) = {result}");
        Assert.InRange(result, 0, 255);
    }

    [Fact]
    public async Task VeteranChart_Loads()
    {
        if (Skip()) return;
        var db = new GlobalDatabase(Global!);
        var vet = await db.LoadVeteranChartAsync();
        _out.WriteLine($"Veteran tiers: {vet.Count}");
        Assert.NotNull(vet);
    }
}
