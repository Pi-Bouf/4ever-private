using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Phase 41 — the shield-block roll (C++ <c>CTObjBase::GetShieldDP</c>/<c>GetShieldMDP</c>, run per damage
/// component inside <c>CalcDamage</c>): a defender's equipped shield rolls its block probability
/// (<c>ABILITY_SDR</c> = base <c>bBlockProb</c> + <c>MTYPE_SDR</c> enchants) and, on a hit
/// (<c>rate &gt; rand()%100</c>), adds its defence power (<c>ABILITY_SDP</c>) to normal defence — additive
/// reduction, never a fixed %/full negation (the 5/7 min-damage floor holds) — and flags the reported hit
/// <c>HT_BLOCK</c>. Ported as a pure <see cref="StatEngine"/> function and wired live into the
/// monster→player melee path (a PC defender with an equipped shield). All DB-free.
/// </summary>
public class ShieldBlockTests
{
    private const byte HtNormal = 1, HtBlock = 3, HtLastHit = 4;
    private const byte ItShield = 6, IkShield = 12, IkMultiVajra = 11;
    private const byte MtypeSdr = 34, MtypeSpdpow = 58, MtypeSmdr = 60, MtypeSmdpow = 59;

    /// <summary>A shield item: <c>IT_SHIELD</c> of the given kind, with an attr carrying the base block prob
    /// (<paramref name="blockProb"/>) and block power — <paramref name="dp"/> for a physical shield's
    /// <c>m_wDP</c>, <paramref name="magicDp"/> for a multivajra's <c>m_wMDP</c>.</summary>
    private static Item Shield(byte blockProb, ushort dp, byte kind = IkShield, ushort magicDp = 0)
        => new()
        {
            ItemSlot = 1, TemplateId = 600, Count = 1,
            Template = new ItemTemplate(600, 0, new float[4], Type: ItShield, Kind: kind),
            Attr = new ItemAttr(600, kind, 0, 0, 0, dp, 0, 0, magicDp, blockProb),
        };

    private static Character Shielded(params Item[] equipped)
    {
        var ch = new Character { CharId = 1, Name = "Def", Level = 5, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50 };
        var equip = new Inven { InvenId = 0xFE };
        foreach (var it in equipped) equip.Items.Add(it);
        ch.Invens.Add(equip);
        return ch;
    }

    // ==================== pure function ====================

    [Fact]
    public void ShieldBlockDp_BlockProb100_AlwaysBlocks_ReturnsPower()
    {
        var ch = Shielded(Shield(blockProb: 100, dp: 30));
        var t = new TemplateStore();
        for (int i = 0; i < 200; i++)
            Assert.Equal(30u, StatEngine.ShieldBlockDp(ch, new Random(i), t)); // rate 100 > any rand%100 ⇒ block ⇒ DP
    }

    [Fact]
    public void ShieldBlockDp_BlockProb0_NeverBlocks()
    {
        var ch = Shielded(Shield(blockProb: 0, dp: 30));
        var t = new TemplateStore();
        for (int i = 0; i < 200; i++)
            Assert.Equal(0u, StatEngine.ShieldBlockDp(ch, new Random(i), t)); // rate 0 is never > rand%100 (strictly greater)
    }

    [Fact]
    public void ShieldBlockDp_NoShield_NeverBlocks()
        => Assert.Equal(0u, StatEngine.ShieldBlockDp(Shielded(), new Random(1), new TemplateStore()));

    [Fact]
    public void ShieldBlockDp_EnchantAddsToRateAndPower()
    {
        // No base shield prob/DP; a MTYPE_SDR enchant of 100 (rate) + a MTYPE_SPDPOW enchant of 25 (power).
        var sh = Shield(blockProb: 0, dp: 0);
        sh.Magic.Add(new MagicOption(MtypeSdr, 100));    // DB-free ⇒ GetMagicValue returns the raw value
        sh.Magic.Add(new MagicOption(MtypeSpdpow, 25));
        Assert.Equal(25u, StatEngine.ShieldBlockDp(Shielded(sh), new Random(1), new TemplateStore()));
    }

    [Fact]
    public void ShieldBlockDp_BrokenShield_ContributesNothing()
    {
        var sh = Shield(blockProb: 100, dp: 30);
        sh.DuraMax = 10; sh.DuraCur = 0;   // broken ⇒ HavePower false ⇒ skipped (C++ CalcItemAbility gate)
        Assert.Equal(0u, StatEngine.ShieldBlockDp(Shielded(sh), new Random(1), new TemplateStore()));
    }

    [Fact]
    public void ShieldBlockMdp_UsesMultivajraKindAndMagicDp()
    {
        var ch = Shielded(Shield(blockProb: 100, dp: 0, kind: IkMultiVajra, magicDp: 40));
        Assert.Equal(40u, StatEngine.ShieldBlockMdp(ch, new Random(1), new TemplateStore())); // magic block ⇒ MDP
        Assert.Equal(0u, StatEngine.ShieldBlockDp(ch, new Random(1), new TemplateStore()));   // physical ignores a multivajra
    }

    // ==================== live: monster → shielded player ====================

    // Monster hits for a flat 50 (AtkMin == AtkMax, no crit); melee range (player 20 units from the anchor).
    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c)> Attack(Character player)
    {
        var h = new MapTestHarness();
        var (s, c) = await h.EnterAsync(1, 1, 1, x: 120, z: 100, preSeeded: player);
        h.Service.SpawnMonster(new Monster
        {
            Id = 0x50001, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50,
            DefendPower = 10, PosX = 100, PosZ = 100, StartX = 100, StartY = 0, StartZ = 100,
            Mode = 1, TargetId = 1, AtkMin = 50, AtkMax = 50, AtkSpeed = 2000, AtkNextMs = 0,
            AttackLevel = 10, CritProb = 0, Region = 7, Channel = 1, MapId = 0,
        });
        h.Service.CombatRng = new Random(1);
        c.Clear();
        return (h, s, c);
    }

    // Walk the CS_DEFEND_ACK body to the bAtkHit byte (the hit-result field that carries HT_BLOCK / HT_LASTHIT).
    private static byte AtkHitOf(byte[] pkt)
    {
        var r = new PacketReader(pkt);
        r.ReadUInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadByte(); // attack/target ids + types
        r.ReadUInt32(); r.ReadByte();                               // host id + type
        r.ReadUInt32(); r.ReadUInt32();                             // act / ani
        r.ReadByte(); r.ReadUInt32();                               // bIsMaintain / dwMaintainTick
        r.ReadByte();                                               // bHit (== attacker crit prob)
        return r.ReadByte();                                        // bAtkHit
    }

    [Fact]
    public async Task MonsterHit_ShieldedPlayer_BlocksAndReducesDamage()
    {
        var player = Shielded(Shield(blockProb: 100, dp: 30));   // block power 30 subtracts from the 50 swing
        var (h, s, c) = await Attack(player);

        h.Service.RunMonsterAI(1_000);

        Assert.Equal(80u, player.Hp);                            // 100 − max(50 − 30, 5) = 100 − 20
        Assert.Equal(HtBlock, AtkHitOf(c.Last(Msg.CS_DEFEND_ACK)!));
    }

    [Fact]
    public async Task MonsterHit_UnshieldedPlayer_NoBlock_FullDamage()
    {
        var player = Shielded();                                 // no shield equipped
        var (h, s, c) = await Attack(player);

        h.Service.RunMonsterAI(1_000);

        Assert.Equal(50u, player.Hp);                            // 100 − 50 (no reduction)
        Assert.Equal(HtNormal, AtkHitOf(c.Last(Msg.CS_DEFEND_ACK)!));
    }

    [Fact]
    public async Task MonsterHit_LethalThroughShield_ReportsLastHitNotBlock()
    {
        var player = Shielded(Shield(blockProb: 100, dp: 30));
        player.Hp = 10;                                          // the 20 blocked damage is still lethal
        var (h, s, c) = await Attack(player);

        h.Service.RunMonsterAI(1_000);

        Assert.Equal(0u, player.Hp);
        Assert.Equal(HtLastHit, AtkHitOf(c.Last(Msg.CS_DEFEND_ACK)!)); // a kill wins over the block flag in the report
    }
}
