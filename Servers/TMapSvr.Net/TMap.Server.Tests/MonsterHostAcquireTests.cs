using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 45 — monster host acquisition (aggro-on-sight, C++ <c>CTAICmdSetHost</c>): an idle
/// <b>aggressive</b> monster (the <c>Aggressive</c> gate; production value comes from the unloaded
/// <c>TAICHART</c>, so it defaults off) picks the nearest recently-moved host-eligible player in its 3×3 view
/// and enters battle onto them — folding the C++ wake→ChgHost→ChgMode chain into the Phase-44 retarget. A
/// passive monster (the default) never auto-aggros, so nothing regresses.</summary>
public class MonsterHostAcquireTests
{
    private static Monster AggressiveMob(uint id, float x, float z, bool aggressive = true)
        => new() { Id = id, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            DefendPower = 100, PosX = x, PosZ = z, StartX = x, StartY = 0, StartZ = z,
            Mode = 0, Country = 3 /* TCONTRY_N */, Aggressive = aggressive, Area = 0,
            Region = 7, Channel = 1, MapId = 0, RoamNextMs = 0 };

    [Fact]
    public async Task Aggressive_AcquiresRecentlyMovedPlayer_EntersBattle()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        s.Char!.CanHost = true; s.Char!.LastMoveMs = 1000;   // the player moved recently (host-eligible)
        var mob = AggressiveMob(0x50001, 100, 100);
        h.Service.SpawnMonster(mob);
        c.Clear();

        h.Service.RunMonsterAI(1000);

        Assert.Equal(1u, mob.TargetId);          // acquired the player on sight
        Assert.Equal((byte)1, mob.Mode);         // MT_BATTLE
        var r = new PacketReader(c.Last(Msg.CS_MONHOST_ACK)!);
        Assert.Equal(0x50001u, r.ReadUInt32());  // dwMonID
        Assert.Equal((byte)1, r.ReadByte());     // bSet TRUE — the acquired player is the new host
    }

    [Fact]
    public async Task Passive_DoesNotAutoAggro()
    {
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        s.Char!.CanHost = true; s.Char!.LastMoveMs = 1000;
        var mob = AggressiveMob(0x50002, 100, 100, aggressive: false);   // passive (the default)
        h.Service.SpawnMonster(mob);

        h.Service.RunMonsterAI(1000);

        Assert.Equal(0u, mob.TargetId);          // no host acquired
        Assert.Equal((byte)0, mob.Mode);         // stays MT_NORMAL
    }

    [Fact]
    public async Task Aggressive_PicksNearestPlayer()
    {
        var h = new MapTestHarness();
        var (sA, _) = await h.EnterAsync(1, 1, 1, x: 180, z: 180);   // farther (still in the 3×3 view)
        var (sB, _) = await h.EnterAsync(2, 2, 2, x: 120, z: 120);   // nearer
        sA.Char!.CanHost = true; sA.Char!.LastMoveMs = 1000;
        sB.Char!.CanHost = true; sB.Char!.LastMoveMs = 1000;
        var mob = AggressiveMob(0x50003, 100, 100);
        h.Service.SpawnMonster(mob);

        h.Service.RunMonsterAI(1000);

        Assert.Equal(2u, mob.TargetId);          // nearest by Manhattan = B
    }

    [Fact]
    public async Task Aggressive_LazilyActivatesNeverMovedPlayer()
    {
        // C++ SetHost lazy fallback (TAICmdSetHost.cpp:57-64): a first in-view player who was never host-eligible
        // is force-activated (m_bCanHost = TRUE) and acquired.
        var h = new MapTestHarness();
        var (s, _) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);   // never moved: CanHost=false, LastMoveMs=0
        var mob = AggressiveMob(0x50004, 100, 100);
        h.Service.SpawnMonster(mob);

        h.Service.RunMonsterAI(1000);

        Assert.Equal(1u, mob.TargetId);
        Assert.True(s.Char!.CanHost);            // the fallback made the player host-eligible
    }

    [Fact]
    public async Task Move_MakesPlayerEligible_ThenAggressiveMobAcquires()
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 100, z: 100);
        var mob = AggressiveMob(0x50005, 100, 100);
        h.Service.SpawnMonster(mob);
        h.Service.NowMs = 2000;

        // A real move (TA_RUN = 4): a stand does not make the player host-eligible (C++ CSHandler.cpp:555).
        await h.Service.DispatchClientAsync(s, MapTestHarness.MoveReq(0, 105, 0, 105, dir: 0, speed: 1.0f, action: 4));
        Assert.True(s.Char!.CanHost);            // the MOVE stamped host-eligibility + the recency clock
        c.Clear();

        h.Service.RunMonsterAI(2000);

        Assert.Equal(1u, mob.TargetId);          // acquired end-to-end from the move
        Assert.Equal((byte)1, mob.Mode);
    }
}
