using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 11 — monster spawn + visibility: a monster occupies a grid cell and becomes visible to
/// players in its 3×3 block via CS_ADDMON_ACK / CS_DELMON_ACK (spawn-announce, player-enter, player-move
/// enter/leave diff, despawn).</summary>
public class MonsterTests
{
    private static Monster Mob(uint id, float x, float z, ushort chart = 500, byte level = 5,
        uint maxHp = 200, uint hp = 200, byte channel = 1, ushort mapId = 0)
        => new() { Id = id, ChartId = chart, Level = level, MaxHp = maxHp, Hp = hp, MaxMp = 50, Mp = 50,
            PosX = x, PosY = 0, PosZ = z, Pitch = 0, Dir = 100, Mode = 0, Country = 0, Region = 7,
            Channel = channel, MapId = mapId };

    [Fact]
    public void MakeId_ComposesSpawnChannelSlot()
    {
        // C++ m_dwID = (spawnId << 16) | (channel << 8) | slot.
        Assert.Equal(0x0007_02_03u, Monster.MakeId(spawnId: 7, channel: 2, slot: 3));
    }

    [Fact]
    public async Task Spawn_AnnouncesToNearbyPlayer_AsNewMember()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        c.Clear();

        h.Service.SpawnMonster(Mob(0x10001, 100, 100));

        var add = c.Last(Msg.CS_ADDMON_ACK);
        Assert.NotNull(add);
        var r = new PacketReader(add!);
        Assert.Equal(0x10001u, r.ReadUInt32());   // id
        Assert.Equal((ushort)500, r.ReadUInt16()); // chart id
        Assert.Equal((byte)5, r.ReadByte());       // level
        Assert.Equal(200u, r.ReadUInt32());        // maxHp
        Assert.Equal(200u, r.ReadUInt32());        // hp
        r.ReadUInt32(); r.ReadUInt32();            // maxMp, mp
        Assert.Equal(100f, r.ReadFloat());         // posX
        r.ReadFloat(); r.ReadFloat();              // posY, posZ
        r.ReadUInt16();                            // pitch
        Assert.Equal((ushort)100, r.ReadUInt16()); // dir
        r.ReadByte(); r.ReadByte(); r.ReadByte();  // mouseDir, keyDir, action
        Assert.Equal((byte)0, r.ReadByte());       // mode (MT_NORMAL)
        Assert.Equal((byte)1, r.ReadByte());       // bNewMember (spawn)
        r.ReadByte();                              // country
        r.ReadByte();                              // color
        Assert.Equal(7u, r.ReadUInt32());          // region
        Assert.Equal((byte)0, r.ReadByte());       // maintain-skill count
    }

    [Fact]
    public async Task Spawn_FarAway_NotSeen()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);   // cell (1,1)
        c.Clear();

        h.Service.SpawnMonster(Mob(0x10002, 1000, 1000));           // cell (15,15) — out of the 3×3

        Assert.False(c.Has(Msg.CS_ADDMON_ACK));
    }

    [Fact]
    public async Task PlayerEntering_SeesPreSpawnedMonster()
    {
        var h = new MapTestHarness();
        h.Service.SpawnMonster(Mob(0x10003, 100, 100));             // spawned before anyone is watching
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);   // learns of it at go-live

        var add = c.Last(Msg.CS_ADDMON_ACK);
        Assert.NotNull(add);
        var r = new PacketReader(add!);
        Assert.Equal(0x10003u, r.ReadUInt32());
        r.ReadUInt16(); r.ReadByte();              // chart, level
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); // hp/mp
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); r.ReadUInt16(); r.ReadUInt16();
        r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();     // mouse/key/action/mode
        Assert.Equal((byte)0, r.ReadByte());       // bNewMember = 0 (a player entering, not a fresh spawn)
    }

    [Fact]
    public async Task PlayerMovesTowardMonster_ThenAway_EntersThenLeavesView()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);   // cell (1,1)
        h.Service.SpawnMonster(Mob(0x10004, 1000, 1000));           // cell (15,15) — out of view
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveReq(0, 1000, 0, 1000, dir: 0, speed: 1f));
        Assert.True(c.Has(Msg.CS_ADDMON_ACK));                      // monster entered the mover's view
        c.Clear();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveReq(0, 100, 0, 100, dir: 0, speed: 1f));
        var del = c.Last(Msg.CS_DELMON_ACK);
        Assert.NotNull(del);
        Assert.Equal(0x10004u, new PacketReader(del!).ReadUInt32());
    }

    [Fact]
    public async Task Despawn_TellsNearbyPlayer()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mob = Mob(0x10005, 100, 100);
        h.Service.SpawnMonster(mob);
        c.Clear();

        h.Service.DespawnMonster(mob);

        var del = c.Last(Msg.CS_DELMON_ACK);
        Assert.NotNull(del);
        var r = new PacketReader(del!);
        Assert.Equal(0x10005u, r.ReadUInt32());  // id
        Assert.Equal((byte)0, r.ReadByte());     // exitMap
        Assert.Null(h.State.FindMonster(0x10005)); // removed from the registry
    }

    [Fact]
    public async Task Despawn_FarAway_NotAnnounced()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mob = Mob(0x10006, 1000, 1000);
        h.Service.SpawnMonster(mob);
        c.Clear();

        h.Service.DespawnMonster(mob);

        Assert.False(c.Has(Msg.CS_DELMON_ACK));  // player never saw it
        Assert.Null(h.State.FindMonster(0x10006));
    }
}
