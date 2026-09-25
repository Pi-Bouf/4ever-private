using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 20 — monster-attacks-player: a battle monster whose target is in melee range hits it on
/// the attack cadence (CS_MONATTACK_ACK + CS_DEFEND_ACK + CS_HPMP_ACK), the player enters battle, and at
/// 0 HP dies (CS_DIE_ACK) + the monster drops aggro. Out of melee range it chases instead.</summary>
public class MonsterAttackTests
{
    // Naked player (no PDP formula ⇒ DefendPower 0). Monster AtkMin=AtkMax=20 ⇒ each hit is exactly 20.
    private static Character Victim(uint hp = 100)
        => new() { CharId = 1, Name = "Victim", MaxHp = 100, Hp = hp, MaxMp = 50, Mp = 50 };

    // AttackLevel ≥ 2 ⇒ the monster connects (a PC is never dodged; wAL 0/1 would always miss); CritProb 0 ⇒
    // no crit, so each landed hit is the flat AtkMin..AtkMax roll (= 20).
    private static Monster Attacker(uint id = 0x50001)
        => new() { Id = id, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            DefendPower = 10, PosX = 100, PosZ = 100, StartX = 100, StartY = 0, StartZ = 100,
            Mode = 1, TargetId = 1, AtkMin = 20, AtkMax = 20, AtkSpeed = 2000, AtkNextMs = 0,
            AttackLevel = 10, CritProb = 0,
            Region = 7, Channel = 1, MapId = 0 };

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Monster mob)> Setup(
        Character ch, Monster mob, float px = 120, float pz = 100)
    {
        var h = new MapTestHarness(MapTestHarness.WithMonsterMelee());
        var (s, c) = await h.EnterAsync(1, 1, 1, x: px, z: pz, preSeeded: ch);
        h.Service.SpawnMonster(mob);
        h.Service.CombatRng = new Random(1);
        c.Clear();
        return (h, s, c, mob);
    }

    [Fact]
    public async Task Attack_InMeleeRange_DamagesPlayer_AndBroadcasts()
    {
        var ch = Victim(hp: 100);
        var (h, s, c, mob) = await Setup(ch, Attacker()); // player 20 units from the anchor (< AttackRange 50)

        h.Service.RunMonsterAI(1_000);

        Assert.Equal(80u, ch.Hp);                    // 100 − 20
        Assert.True(c.Has(Msg.CS_MONATTACK_ACK));    // the swing announce
        Assert.True(c.Has(Msg.CS_DEFEND_ACK));       // the hit
        Assert.True(c.Has(Msg.CS_HPMP_ACK));         // the player's new HP bar
    }

    [Fact]
    public async Task Attack_RespectsCadence()
    {
        var ch = Victim(hp: 100);
        var (h, s, c, mob) = await Setup(ch, Attacker());

        h.Service.RunMonsterAI(1_000);               // hit (arms cadence to 3000)
        Assert.Equal(80u, ch.Hp);

        h.Service.RunMonsterAI(1_500);               // within AtkSpeed ⇒ no hit
        Assert.Equal(80u, ch.Hp);

        h.Service.RunMonsterAI(3_000);               // due again
        Assert.Equal(60u, ch.Hp);
    }

    [Fact]
    public async Task Attack_KillsPlayer_SendsDie_AndDropsAggro()
    {
        var ch = Victim(hp: 20);                     // one 20-damage hit is lethal
        var (h, s, c, mob) = await Setup(ch, Attacker());

        h.Service.RunMonsterAI(1_000);

        Assert.Equal(0u, ch.Hp);
        Assert.True(c.Has(Msg.CS_DIE_ACK));
        Assert.Equal(0u, mob.TargetId);              // disengaged
        Assert.Equal((byte)0, mob.Mode);             // MT_NORMAL
    }

    [Fact]
    public async Task Attack_HitAck_IsMonsterToPlayer()
    {
        var ch = Victim(hp: 100);
        var (h, s, c, mob) = await Setup(ch, Attacker());

        h.Service.RunMonsterAI(1_000);

        var r = new PacketReader(c.Last(Msg.CS_DEFEND_ACK)!);
        Assert.Equal(mob.Id, r.ReadUInt32());        // dwAttackID = the monster
        Assert.Equal(1u, r.ReadUInt32());            // dwTargetID = the player
        Assert.Equal((byte)2, r.ReadByte());         // bAttackType OT_MON
        Assert.Equal((byte)1, r.ReadByte());         // bTargetType OT_PC
    }

    // Walk the CS_DEFEND_ACK body to the byte-audit fields (Phase 28): bHit (== attacker crit prob), bPerform
    // (0 on miss), and the damage-map entry count.
    private static (byte bHit, byte bPerform, byte mapCount) ParseDefend(byte[] pkt)
    {
        var r = new PacketReader(pkt);
        r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadByte();     // attack/target ids + types
        r.ReadUInt32(); r.ReadByte();                                    // host id + type
        r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadUInt32();    // act/ani/isMaintain/maintainTick
        byte bHit = r.ReadByte(); r.ReadByte();                          // bHit, bAtkHit
        r.ReadUInt16(); r.ReadByte();                                    // wAttackLevel, bAttackerLevel
        r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32();  // pys/mg power band
        r.ReadByte(); r.ReadByte(); r.ReadByte(); r.ReadByte();          // canSelect/cancel/country/aid
        r.ReadUInt16(); r.ReadByte(); r.ReadUInt16();                    // wSkillID/bSkillLevel/wBackSkillID
        byte bPerform = r.ReadByte();
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat();                     // atk pos
        r.ReadFloat(); r.ReadFloat(); r.ReadFloat();                     // def pos
        byte mapCount = r.ReadByte();
        return (bHit, bPerform, mapCount);
    }

    [Fact]
    public async Task HitAck_CarriesCritProb_AndPerformOnLandedHit()   // D1 (bHit=bCP) + D2 (bPerform=1 landed)
    {
        var mob = Attacker(); mob.CritProb = 77;
        var (h, s, c, _) = await Setup(Victim(hp: 100), mob);

        h.Service.RunMonsterAI(1_000);

        var (bHit, bPerform, mapCount) = ParseDefend(c.Last(Msg.CS_DEFEND_ACK)!);
        Assert.Equal((byte)77, bHit);        // bHit carries the monster's crit prob, not 0
        Assert.Equal((byte)1, bPerform);     // landed ⇒ PERFORM_SUCCESS
        Assert.Equal((byte)1, mapCount);     // one damage entry
    }

    [Fact]
    public async Task Miss_LowAttackLevel_NoDamage_PerformZero_EmptyMap()  // D2 (bPerform=0) + D5 (empty map)
    {
        var mob = Attacker(); mob.AttackLevel = 1;   // wAL == 1 ⇒ a PC is always missed
        var ch = Victim(hp: 100);
        var (h, s, c, _) = await Setup(ch, mob);

        h.Service.RunMonsterAI(1_000);

        Assert.Equal(100u, ch.Hp);                   // no damage on a miss
        var (_, bPerform, mapCount) = ParseDefend(c.Last(Msg.CS_DEFEND_ACK)!);
        Assert.Equal((byte)0, bPerform);             // miss ⇒ PERFORM_MISS ⇒ wire 0
        Assert.Equal((byte)0, mapCount);             // empty damage map
    }

    [Fact]
    public async Task OutOfMeleeRange_Chases_DoesNotAttack()
    {
        var ch = Victim(hp: 100);
        // 60 units from the anchor: past AttackRange (50), within leash (800), still in the monster's 3×3.
        var (h, s, c, mob) = await Setup(ch, Attacker(), px: 100, pz: 160);

        h.Service.RunMonsterAI(1_000);

        Assert.Equal(100u, ch.Hp);                   // not hit
        Assert.False(c.Has(Msg.CS_MONATTACK_ACK));
        Assert.True(c.Has(Msg.CS_MONACTION_ACK));    // chased (TA_FOLLOW) instead
    }
}
