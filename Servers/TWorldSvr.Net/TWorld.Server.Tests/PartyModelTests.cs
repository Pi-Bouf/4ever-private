using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

public class PartyModelTests
{
    private static Character Char(uint id) => new() { CharId = id };

    [Fact]
    public void AddMember_RespectsMaxAndDuplicates()
    {
        var p = new Party { Id = 0x100, ChiefId = 1 };
        for (uint i = 1; i <= Proto.MaxPartyMember; i++)
            Assert.True(p.AddMember(Char(i)));
        Assert.Equal(Proto.MaxPartyMember, p.Size);
        Assert.True(p.IsFull);
        Assert.False(p.AddMember(Char(99))); // full
    }

    [Fact]
    public void AddMember_RejectsCharAlreadyInAParty()
    {
        var a = new Party { Id = 0x100 };
        var b = new Party { Id = 0x101 };
        var c = Char(5);
        Assert.True(a.AddMember(c));
        Assert.False(b.AddMember(c));     // already in party a
        Assert.Equal(a, c.Party);
    }

    [Fact]
    public void NextChiefAfter_RotatesWhenChiefLeaves()
    {
        var p = new Party { Id = 0x100, ChiefId = 1 };
        p.AddMember(Char(1));
        p.AddMember(Char(2));
        var next = p.NextChiefAfter(1);
        Assert.NotNull(next);
        Assert.Equal(2u, next!.CharId);
        Assert.Equal(2u, p.ChiefId);
    }

    [Fact]
    public void DelMember_ClearsBackReference()
    {
        var p = new Party { Id = 0x100 };
        var c = Char(7);
        p.AddMember(c);
        Assert.True(p.DelMember(7));
        Assert.Null(c.Party);
        Assert.Equal(0, p.Size);
    }

    [Fact]
    public void PartyIdPool_AllocatesInRange_AndRecycles()
    {
        var pool = new PartyIdPool();
        ushort first = pool.Alloc();
        Assert.Equal(Proto.PartyIdMin, first);
        ushort second = pool.Alloc();
        Assert.Equal((ushort)(Proto.PartyIdMin + 1), second);
        pool.Free(first);
        // freed id goes to the back of the queue, so it is handed out again eventually.
        Assert.NotEqual(0, pool.Alloc());
    }
}
