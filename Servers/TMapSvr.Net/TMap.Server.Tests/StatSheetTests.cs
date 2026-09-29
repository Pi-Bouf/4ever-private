using TMap.Data;
using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// When the player gets its stat sheet (<c>CS_CHARSTATINFO_ACK</c>) without asking: at login (C++ OnMW_CHARINFO_REQ), and
/// when a buff lands on it or leaves it (C++ Defend / EraseMaintainSkill). The client's skill cooldowns are
/// <c>(delay + attack delay) · attack delay rate / 100</c>, with the rates from this packet only — before it, every
/// cooldown came out 0.
/// </summary>
public class StatSheetTests
{
    private const uint A = 1;
    private const ushort Might = 960;

    private static TemplateStore Store()
    {
        var t = new TemplateStore { Rate1st = 1.0f };
        t.Classes[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        t.Races[0] = new StatSeed(1, 1, 1, 1, 1, 1);
        var might = new SkillTemplate(Might, Kind: 0, UseMp: 0, UseMpType: 0, UseHp: 0, UseHpType: 0,
            StartLevel: 1, MaxLevel: 1, NextLevel: 1, ReuseDelay: 0, ReuseDelayInc: 0, LoopDelay: 0, KindDelay: 0,
            SpeedApply: 0, Positive: 1, MapId: 0, Duration: 1_000);
        might.Data.Add(new SkillDataRow(3 /* SA_BUFF */, 1 /* SDT_ABILITY */, 0, 1, 1, 5, 0, 0));
        t.Skills[Might] = might;
        return t;
    }

    private static async Task<(MapTestHarness H, ClientSession S, FakeClientChannel C, Character Ch)> Enter()
    {
        var h = new MapTestHarness(Store());
        var ch = new Character { CharId = A, Name = "Ann", Level = 10, MaxHp = 100, Hp = 100, MaxMp = 50, Mp = 50 };
        var (s, c) = await h.EnterAsync(A, 1, 1, name: "Ann", preSeeded: ch);
        return (h, s, c, ch);
    }

    [Fact]
    public async Task AtLogin_ThePlayerGetsItsStatSheet_WithTheAttackDelayRates()
    {
        var (_, _, c, _) = await Enter();

        var sheet = c.Last(Msg.CS_CHARSTATINFO_ACK);
        Assert.NotNull(sheet);
        var r = new PacketReader(sheet!);
        Assert.Equal(A, r.ReadUInt32());
        for (int i = 0; i < 6; i++) r.ReadUInt16();                    // STR..MEN
        for (int i = 0; i < 5; i++) r.ReadUInt32();                    // AP / DP / long AP
        for (int i = 0; i < 3; i++) r.ReadUInt32();                    // attack delays
        Assert.Equal(new uint[] { 100, 100, 100 }, new[] { r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32() });
    }

    [Fact]
    public async Task ABuffLandingOrEnding_SendsTheNewSheet()
    {
        var (h, s, c, ch) = await Enter();
        c.Clear();

        Assert.True(h.Service.ForceMaintain(s, ch, Might, A, 1, A, 1, remainTick: 0));
        Assert.Single(c.WithId(Msg.CS_CHARSTATINFO_ACK));

        c.Clear();
        h.Service.RunMaintainSkills(h.Service.NowMs + 60_000);
        Assert.Empty(ch.MaintainSkills);
        Assert.Single(c.WithId(Msg.CS_CHARSTATINFO_ACK));
    }
}
