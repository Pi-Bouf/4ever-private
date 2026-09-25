using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// CS_MONMOVE — client-authoritative monster movement. The server validates and applies; it does not walk
/// hosted monsters itself. The rules that matter: only the monster's <b>host</b> may move it, the batch is
/// per-monster (one bad entry skips only itself), and the relay excludes the sender.
/// </summary>
public class MonMoveTests
{
    private static TemplateStore Store()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Formulas[8] = new FormulaRow(200, 0f, 0f);
        t.Formulas[19] = new FormulaRow(100, 0f, 0f);
        t.Classes[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[1] = new StatSeed(1, 1, 1, 1, 1, 1);
        return t;
    }

    private static Character Hero(uint id = 1)
        => new() { CharId = id, Name = "Hero", Class = 1, Race = 1, Hp = 200, Mp = 100 };

    /// <summary>One-monster CS_MONMOVE_REQ as the client sends it (CSHandler.cpp:945-962).</summary>
    private static byte[] MonMoveReq(uint monId, float x, float y, float z, byte action = 3,
                                     byte objType = 2, ushort pitch = 0, ushort dir = 100,
                                     byte mouseDir = 1, byte keyDir = 1)
    {
        var w = new PacketWriter(Msg.CS_MONMOVE_REQ);
        w.WriteUInt16(1);                 // wMonCount
        w.WriteUInt32(monId);
        w.WriteByte(objType);
        w.WriteByte(1);                   // bChannel
        w.WriteUInt16(0);                 // wMapID
        w.WriteFloat(x); w.WriteFloat(y); w.WriteFloat(z);
        w.WriteUInt16(pitch); w.WriteUInt16(dir);
        w.WriteByte(mouseDir); w.WriteByte(keyDir); w.WriteByte(action);
        return w.ToArray();
    }

    private static Monster Mob(uint id, uint hostId, float x, float z)
        => new()
        {
            Id = id, ChartId = 1, Level = 1, Hp = 100, MaxHp = 100,
            PosX = x, PosY = 0f, PosZ = z, StartX = x, StartZ = z,
            Channel = 1, MapId = 0, HostId = hostId, Status = 1,
        };

    // ---- the fix: a hosted monster actually moves ----

    [Fact]
    public async Task HostMovesItsMonster()
    {
        var h = new MapTestHarness(Store());
        var (s, _) = await h.EnterAsync(1, 1, 1, preSeeded: Hero());
        var mon = Mob(50, hostId: 1, x: 3663f, z: 557f);
        h.State.AddMonster(mon);

        await h.Service.DispatchClientAsync(s, MonMoveReq(50, 3700f, 0f, 600f));

        Assert.Equal(3700f, mon.PosX);
        Assert.Equal(600f, mon.PosZ);
        Assert.Equal(3, mon.Action);
        Assert.Equal(100, mon.Dir);
    }

    [Fact]
    public async Task NonHostCannotMoveIt()
    {
        var h = new MapTestHarness(Store());
        var (s, _) = await h.EnterAsync(1, 1, 1, preSeeded: Hero());
        var mon = Mob(50, hostId: 999, x: 3663f, z: 557f);   // hosted by someone else
        h.State.AddMonster(mon);

        await h.Service.DispatchClientAsync(s, MonMoveReq(50, 3700f, 0f, 600f));

        Assert.Equal(3663f, mon.PosX);   // untouched — this is the anti-cheat boundary
        Assert.Equal(557f, mon.PosZ);
    }

    [Fact]
    public async Task UnknownMonsterIsIgnored()
    {
        var h = new MapTestHarness(Store());
        var (s, client) = await h.EnterAsync(1, 1, 1, preSeeded: Hero());
        client.Clear();

        await h.Service.DispatchClientAsync(s, MonMoveReq(4242, 3700f, 0f, 600f));

        Assert.False(client.Has(Msg.CS_MONMOVE_ACK));
    }

    [Fact]
    public async Task NonPositiveCoordinatesAreRejected()
    {
        var h = new MapTestHarness(Store());
        var (s, _) = await h.EnterAsync(1, 1, 1, preSeeded: Hero());
        var mon = Mob(50, hostId: 1, x: 3663f, z: 557f);
        h.State.AddMonster(mon);

        await h.Service.DispatchClientAsync(s, MonMoveReq(50, 0f, 0f, 600f));

        Assert.Equal(3663f, mon.PosX);
    }

    [Fact]
    public async Task ACorpseInTheDeadAnimationIsSkipped()
    {
        var h = new MapTestHarness(Store());
        var (s, _) = await h.EnterAsync(1, 1, 1, preSeeded: Hero());
        var mon = Mob(50, hostId: 1, x: 3663f, z: 557f);
        mon.Action = 7;   // TA_DEAD
        h.State.AddMonster(mon);

        await h.Service.DispatchClientAsync(s, MonMoveReq(50, 3700f, 0f, 600f));

        Assert.Equal(3663f, mon.PosX);
    }

    // ---- the relay ----

    [Fact]
    public async Task RelayExcludesTheSenderAndReachesOthers()
    {
        var h = new MapTestHarness(Store());
        var (s, hostClient) = await h.EnterAsync(1, 1, 1, preSeeded: Hero());
        var (_, otherClient) = await h.EnterAsync(2, 2, 2, name: "Other", preSeeded: Hero(2));
        var mon = Mob(50, hostId: 1, x: 3663f, z: 557f);
        h.State.AddMonster(mon);
        hostClient.Clear(); otherClient.Clear();

        await h.Service.DispatchClientAsync(s, MonMoveReq(50, 3700f, 0f, 600f));

        Assert.False(hostClient.Has(Msg.CS_MONMOVE_ACK));   // the host drove it; it already knows
        Assert.True(otherClient.Has(Msg.CS_MONMOVE_ACK));
    }

    [Fact]
    public async Task RelayIsByteExact()
    {
        var h = new MapTestHarness(Store());
        var (s, _) = await h.EnterAsync(1, 1, 1, preSeeded: Hero());
        var (_, otherClient) = await h.EnterAsync(2, 2, 2, name: "Other", preSeeded: Hero(2));
        var mon = Mob(50, hostId: 1, x: 3663f, z: 557f);
        h.State.AddMonster(mon);
        otherClient.Clear();

        await h.Service.DispatchClientAsync(s, MonMoveReq(50, 3700f, 12f, 600f, action: 9, dir: 250));

        var ack = otherClient.Last(Msg.CS_MONMOVE_ACK);
        Assert.NotNull(ack);
        // wMonCount(2) + one block: dwMonID(4) bType(1) fPos(12) wPitch(2) wDIR(2) bMouse(1) bKey(1) bAction(1)
        Assert.Equal(PacketHeader.Size + 2 + 24, ack!.Length);
        var r = new PacketReader(ack);
        Assert.Equal(1, r.ReadUInt16());
        Assert.Equal(50u, r.ReadUInt32());
        Assert.Equal(2, r.ReadByte());          // OT_MON
        Assert.Equal(3700f, r.ReadFloat());
        Assert.Equal(12f, r.ReadFloat());
        Assert.Equal(600f, r.ReadFloat());
        r.ReadUInt16();                          // wPitch
        Assert.Equal(250, r.ReadUInt16());
        r.ReadByte(); r.ReadByte();
        Assert.Equal(9, r.ReadByte());           // TA_FOLLOW
    }

    // ---- the leash, which only runs because the position now advances ----

    [Fact]
    public async Task DraggingABattleMonsterPastTheLeashDropsAggro()
    {
        var h = new MapTestHarness(Store());
        var (s, _) = await h.EnterAsync(1, 1, 1, preSeeded: Hero());
        var mon = Mob(50, hostId: 1, x: 3663f, z: 557f);
        mon.Mode = 1;              // MT_BATTLE
        mon.TargetId = 1; mon.TargetType = 1;
        h.State.AddMonster(mon);

        // ChaseRange is 800; move it far beyond that from its spawn anchor.
        await h.Service.DispatchClientAsync(s, MonMoveReq(50, 6000f, 0f, 3000f));

        Assert.NotEqual(1, mon.Mode);   // left MT_BATTLE
    }
}
