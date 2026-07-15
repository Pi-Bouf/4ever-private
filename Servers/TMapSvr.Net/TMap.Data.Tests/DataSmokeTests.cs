using TMap.Data;
using Xunit;

namespace TMap.Data.Tests;

/// <summary>
/// The map runs DB-free by design (the server catches connection failures and synthesizes defaults), so
/// there is nothing to assert without a live database. These tests exercise the real query path only when
/// <c>TMAP_TEST_GAME</c> points at a game DB; otherwise they no-op, matching the sibling ports.
/// </summary>
public class DataSmokeTests
{
    private static string? GameCs => Environment.GetEnvironmentVariable("TMAP_TEST_GAME");

    [Fact]
    public async Task Ping_Succeeds_WhenConfigured()
    {
        if (string.IsNullOrWhiteSpace(GameCs)) return; // no-op unless a game DB is configured
        var db = new GameDatabase(GameCs);
        await db.PingAsync();
    }

    [Fact]
    public async Task LoadChar_ReturnsNull_ForUnknownChar_WhenConfigured()
    {
        if (string.IsNullOrWhiteSpace(GameCs)) return;
        var db = new GameDatabase(GameCs);
        var row = await db.LoadCharAsync(0xFFFFFFF0);
        Assert.Null(row);
    }

    [Fact]
    public async Task LoadTemplates_PopulatesItemAndMagicCharts_WhenConfigured()
    {
        if (string.IsNullOrWhiteSpace(GameCs)) return;
        var db = new GameDatabase(GameCs);
        var store = await db.LoadTemplatesAsync();
        Assert.True(store.HasItems, "TITEMCHART should yield at least one item template.");
        // Every loaded item template has a 4-element revision table (fRevision/fMRevision/fAtRate/fMAtRate).
        Assert.All(store.Items.Values, t => Assert.Equal(4, t.Revision.Length));
    }
}
