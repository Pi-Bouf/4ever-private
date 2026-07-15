using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Unit tests for the spatial grid math and the 3×3 visibility / cell-transition diff (C++ CTMap/CTCell).
/// </summary>
public class GridTests
{
    private static ClientSession Sess(uint id, float x, float z)
    {
        var s = new ClientSession(new FakeClientChannel()) { CharId = id };
        s.Char = new Character { CharId = id, PosX = x, PosZ = z };
        return s;
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(63f, 0)]
    [InlineData(64f, 1)]
    [InlineData(127.9f, 1)]
    [InlineData(128f, 2)]
    [InlineData(3663f, 57)]  // spawn X
    [InlineData(557f, 8)]    // spawn Z
    public void Coord_TruncatesFloatToCellIndex(float world, int expected)
        => Assert.Equal(expected, MapGrid.Coord(world));

    [Fact]
    public void KeyOf_PacksXInLowWord_ZInHighWord()
    {
        // MAKELONG(cellX=57, cellZ=8) = (8 << 16) | 57.
        Assert.Equal((8u << 16) | 57u, MapGrid.KeyOf(3663f, 557f));
        // Same cell → same key; a different cell → a different key.
        Assert.Equal(MapGrid.KeyOf(3663f, 557f), MapGrid.KeyOf(3693f, 557f));
        Assert.NotEqual(MapGrid.KeyOf(3663f, 557f), MapGrid.KeyOf(3840f, 557f));
    }

    [Fact]
    public void Neighbors_SameCell_SeeEachOther_ExcludingSelf()
    {
        var g = new MapGrid(1, 0);
        var a = Sess(1, 3663f, 557f);
        var b = Sess(2, 3670f, 560f); // same cell (57,8)
        g.Add(a); g.Add(b);

        Assert.Equal(new[] { b }, g.Neighbors(a).ToArray());
        Assert.Equal(new[] { a }, g.Neighbors(b).ToArray());
    }

    [Fact]
    public void Neighbors_AdjacentCell_Visible()
    {
        var g = new MapGrid(1, 0);
        var a = Sess(1, 3663f, 557f);         // cell (57,8)
        var b = Sess(2, 3663f + 64f, 557f);   // cell (58,8) — inside the 3×3
        g.Add(a); g.Add(b);

        Assert.Contains(b, g.Neighbors(a));
    }

    [Fact]
    public void Neighbors_ThreeCellsApart_NotVisible()
    {
        var g = new MapGrid(1, 0);
        var a = Sess(1, 3663f, 557f);   // cell (57,8)
        var b = Sess(2, 3840f, 557f);   // cell (60,8) — outside the 3×3
        g.Add(a); g.Add(b);

        Assert.Empty(g.Neighbors(a));
        Assert.Empty(g.Neighbors(b));
    }

    [Fact]
    public void Relocate_WithinCell_ReportsNoChange()
    {
        var g = new MapGrid(1, 0);
        var a = Sess(1, 3663f, 557f);
        g.Add(a);

        var diff = g.Relocate(a, 3693f, 557f); // same cell (57,8)
        Assert.False(diff.CellChanged);
        Assert.Same(CellDiff.None, diff);
    }

    [Fact]
    public void Relocate_AwayFromNeighbour_MarksItLeft()
    {
        var g = new MapGrid(1, 0);
        var a = Sess(1, 3663f, 557f);
        var b = Sess(2, 3663f, 557f); // same cell
        g.Add(a); g.Add(b);

        var diff = g.Relocate(a, 100f, 200f); // far away → b drops out of view
        Assert.True(diff.CellChanged);
        Assert.Equal(new[] { b }, diff.Left.ToArray());
        Assert.Empty(diff.Entered);
        // a is now re-bucketed at the new cell and no longer sees b.
        Assert.Empty(g.Neighbors(a));
    }

    [Fact]
    public void Relocate_TowardDistantPlayer_MarksItEntered()
    {
        var g = new MapGrid(1, 0);
        var a = Sess(1, 3663f, 557f);   // cell (57,8)
        var b = Sess(2, 3840f, 557f);   // cell (60,8) — out of view
        g.Add(a); g.Add(b);
        Assert.Empty(g.Neighbors(a));

        var diff = g.Relocate(a, 3776f, 557f); // cell (59,8) → b (60,8) now in view
        Assert.True(diff.CellChanged);
        Assert.Equal(new[] { b }, diff.Entered.ToArray());
        Assert.Empty(diff.Left);
        Assert.Contains(b, g.Neighbors(a));
    }

    [Fact]
    public void Remove_TakesPlayerOutOfView()
    {
        var g = new MapGrid(1, 0);
        var a = Sess(1, 3663f, 557f);
        var b = Sess(2, 3670f, 560f);
        g.Add(a); g.Add(b);
        g.Remove(b);

        Assert.Empty(g.Neighbors(a));
    }
}
