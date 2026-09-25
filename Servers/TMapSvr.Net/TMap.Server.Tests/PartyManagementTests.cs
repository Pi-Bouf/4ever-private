using TMap.Protocol;
using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>
/// Party management, the map's half: client requests are relayed to the world, the world's answers become
/// client packets, and only <c>MW_PARTYJOIN_REQ</c>/<c>MW_PARTYATTR_REQ</c> change a member's party fields
/// (C++ CSHandler.cpp:3377-3506, SSHandler.cpp:7808-8069 / 10392-10511).
/// </summary>
public class PartyManagementTests
{
    private const uint Key = 11;

    private static async Task<(MapTestHarness h, ClientSession s, FakeClientChannel c, Character ch)> Setup()
    {
        var h = new MapTestHarness();
        var ch = new Character { CharId = 1, Name = "Leader", MaxHp = 100, Hp = 90, MaxMp = 50, Mp = 40, Level = 7 };
        var (s, c) = await h.EnterAsync(1, 1, Key, name: "Leader", preSeeded: ch);
        c.Clear(); h.World.Clear();
        return (h, s, c, ch);
    }

    private static void Party(Character ch, ushort id, uint chief)
    {
        ch.PartyId = id; ch.PartyChiefId = chief; ch.PartyType = 0;
    }

    // ======================= client → world =======================

    [Fact]
    public async Task AnInvite_IsRelayedToTheWorld_WithTheInvitersVitals()
    {
        var (h, s, _, ch) = await Setup();
        var w = new PacketWriter(Msg.CS_PARTYADD_REQ);
        w.WriteString("Friend"); w.WriteByte(2);

        await h.Service.DispatchClientAsync(s, w.ToArray());

        var r = new PacketReader(h.World.Last(Msg.MW_PARTYADD_ACK)!);
        Assert.Equal("Leader", r.ReadString());
        Assert.Equal("Friend", r.ReadString());
        Assert.Equal(2, r.ReadByte());
        r.ReadUInt32();
        Assert.Equal(90u, r.ReadUInt32());       // HP
        r.ReadUInt32();
        Assert.Equal(40u, r.ReadUInt32());       // MP
    }

    [Fact]
    public async Task OnlyTheChief_CanKickSomeoneElse()
    {
        var (h, s, _, ch) = await Setup();
        Party(ch, 5, chief: 99);                 // a plain member
        var kick = new PacketWriter(Msg.CS_PARTYDEL_REQ);
        kick.WriteUInt32(42);

        await h.Service.DispatchClientAsync(s, kick.ToArray());
        Assert.False(h.World.Has(Msg.MW_PARTYDEL_ACK));

        var leave = new PacketWriter(Msg.CS_PARTYDEL_REQ);
        leave.WriteUInt32(1);                    // itself: always allowed
        await h.Service.DispatchClientAsync(s, leave.ToArray());

        var r = new PacketReader(h.World.Last(Msg.MW_PARTYDEL_ACK)!);
        Assert.Equal(5, r.ReadUInt16());
        Assert.Equal(1u, r.ReadUInt32());
        Assert.Equal(0, r.ReadByte());           // bKick = FALSE when leaving
    }

    [Fact]
    public async Task TheChiefsKick_IsFlaggedAsAKick()
    {
        var (h, s, _, ch) = await Setup();
        Party(ch, 5, chief: 1);
        var kick = new PacketWriter(Msg.CS_PARTYDEL_REQ);
        kick.WriteUInt32(42);

        await h.Service.DispatchClientAsync(s, kick.ToArray());

        var r = new PacketReader(h.World.Last(Msg.MW_PARTYDEL_ACK)!);
        r.ReadUInt16(); r.ReadUInt32();
        Assert.Equal(1, r.ReadByte());
    }

    [Fact]
    public async Task LeavingWithoutAParty_SendsNothing()
    {
        var (h, s, _, _) = await Setup();
        var leave = new PacketWriter(Msg.CS_PARTYDEL_REQ);
        leave.WriteUInt32(1);

        await h.Service.DispatchClientAsync(s, leave.ToArray());

        Assert.False(h.World.Has(Msg.MW_PARTYDEL_ACK));
    }

    // ======================= world → client =======================

    [Fact]
    public async Task AnInviteThatArrives_AsksTheTarget()
    {
        var (h, _, c, _) = await Setup();
        var w = new PacketWriter(Msg.MW_PARTYADD_REQ);
        w.WriteUInt32(1); w.WriteUInt32(Key); w.WriteString("Inviter"); w.WriteString("Leader");
        w.WriteByte(2); w.WriteByte(0 /* PARTY_AGREE */); w.WriteUInt32(77);

        await h.Service.DispatchWorldAsync(w.ToArray());

        var r = new PacketReader(c.Last(Msg.CS_PARTYJOINASK_ACK)!);
        Assert.Equal("Inviter", r.ReadString());
        Assert.Equal(2, r.ReadByte());
    }

    [Fact]
    public async Task ADeadTarget_AnswersBusy_WithoutBeingAsked()
    {
        var (h, _, c, ch) = await Setup();
        ch.Hp = 0;                               // IsActionBlock
        var w = new PacketWriter(Msg.MW_PARTYADD_REQ);
        w.WriteUInt32(1); w.WriteUInt32(Key); w.WriteString("Inviter"); w.WriteString("Leader");
        w.WriteByte(2); w.WriteByte(0); w.WriteUInt32(77);

        await h.Service.DispatchWorldAsync(w.ToArray());

        Assert.False(c.Has(Msg.CS_PARTYJOINASK_ACK));
        var r = new PacketReader(h.World.Last(Msg.MW_PARTYJOIN_ACK)!);
        r.ReadString(); r.ReadString(); r.ReadByte();
        Assert.Equal(2, r.ReadByte());           // ASK_BUSY
    }

    [Fact]
    public async Task AWrongSessionKey_IsIgnored_AndAnsweredBusy()
    {
        var (h, _, c, _) = await Setup();
        var w = new PacketWriter(Msg.MW_PARTYADD_REQ);
        w.WriteUInt32(1); w.WriteUInt32(Key + 1); w.WriteString("Inviter"); w.WriteString("Leader");
        w.WriteByte(2); w.WriteByte(0); w.WriteUInt32(77);

        await h.Service.DispatchWorldAsync(w.ToArray());

        Assert.False(c.Has(Msg.CS_PARTYJOINASK_ACK));
        Assert.True(h.World.Has(Msg.MW_PARTYJOIN_ACK));
    }

    [Fact]
    public async Task PartyAttr_UpdatesTheFields_AndTellsTheNeighbours()
    {
        var (h, s, c, ch) = await Setup();
        var (_, viewer) = await h.EnterAsync(2, 2, 22, name: "Viewer");
        c.Clear(); viewer.Clear();
        var w = new PacketWriter(Msg.MW_PARTYATTR_REQ);
        w.WriteUInt32(1); w.WriteUInt32(Key); w.WriteUInt16(9); w.WriteByte(0); w.WriteUInt32(1); w.WriteUInt16(0);

        await h.Service.DispatchWorldAsync(w.ToArray());

        Assert.Equal(9, ch.PartyId);
        Assert.Equal(1u, ch.PartyChiefId);
        foreach (var client in new[] { c, viewer })
        {
            var r = new PacketReader(client.Last(Msg.CS_PARTYATTR_ACK)!);
            Assert.Equal(1u, r.ReadUInt32());
            Assert.Equal(9, r.ReadUInt16());
        }
    }

    [Fact]
    public async Task PartyDel_EchoesTheCurrentFields_NotThePackets()
    {
        // The C++ reads the new chief/commander/party and then sends the member's current ones.
        var (h, _, c, ch) = await Setup();
        Party(ch, 5, chief: 1);
        var w = new PacketWriter(Msg.MW_PARTYDEL_REQ);
        w.WriteUInt32(1); w.WriteUInt32(Key); w.WriteUInt32(42); w.WriteUInt32(99); w.WriteUInt16(0); w.WriteUInt16(0);
        w.WriteByte(1);

        await h.Service.DispatchWorldAsync(w.ToArray());

        var r = new PacketReader(c.Last(Msg.CS_PARTYDEL_ACK)!);
        Assert.Equal(42u, r.ReadUInt32());
        Assert.Equal(1u, r.ReadUInt32());        // current chief, not the packet's 99
        r.ReadUInt16();
        Assert.Equal(5, r.ReadUInt16());         // current party, not the packet's 0
        Assert.Equal(5, ch.PartyId);             // and nothing was changed
    }

    [Fact]
    public async Task AFailedTypeChange_KeepsTheOldType()
    {
        var (h, _, c, ch) = await Setup();
        ch.PartyType = 0;
        var w = new PacketWriter(Msg.MW_CHGPARTYTYPE_REQ);
        w.WriteUInt32(1); w.WriteUInt32(Key); w.WriteByte(1 /* failed */); w.WriteByte(3);

        await h.Service.DispatchWorldAsync(w.ToArray());

        Assert.Equal(0, ch.PartyType);
        Assert.True(c.Has(Msg.CS_CHGPARTYTYPE_ACK));
    }

    // ======================= HP bars =======================

    private static async Task<(MapTestHarness h, ClientSession s, Character ch, Monster mon)> Fight()
    {
        var h = new MapTestHarness(MapTestHarness.WithMonsterMelee());
        var ch = new Character { CharId = 1, Name = "Leader", MaxHp = 100, Hp = 90, MaxMp = 50, Mp = 40, Level = 7 };
        var (s, _) = await h.EnterAsync(1, 1, Key, name: "Leader", x: 120, z: 100, preSeeded: ch);
        var mon = new Monster { Id = 0x50001, ChartId = 500, Level = 5, MaxHp = 100, Hp = 100, PosX = 100, PosZ = 100,
            StartX = 100, StartZ = 100, AtkMin = 20, AtkMax = 20, AttackLevel = 10, Channel = 1, MapId = 0, Region = 7 };
        h.Service.SpawnMonster(mon);
        h.Service.CombatRng = new Random(1);
        h.World.Clear();
        return (h, s, ch, mon);
    }

    [Fact]
    public async Task APartiedPlayersOwnHpChange_IsSentToTheParty()
    {
        var (h, s, ch, mon) = await Fight();
        Party(ch, 5, chief: 1);

        await h.Service.DispatchClientAsync(s, MapTestHarness.MonsterHitReq(mon.Id, 1));   // shows the player its HP

        var r = new PacketReader(h.World.Last(Msg.MW_PARTYMANSTAT_ACK)!);
        Assert.Equal(5, r.ReadUInt16());         // wPartyID
        Assert.Equal(1u, r.ReadUInt32());        // dwID
        Assert.Equal(1, r.ReadByte());           // OT_PC
        Assert.Equal(ch.Level, r.ReadByte());    // bLevel
        r.ReadUInt32();
        Assert.Equal(70u, r.ReadUInt32());       // the new HP
    }

    [Fact]
    public async Task ASoloPlayersHpChange_StaysLocal()
    {
        var (h, s, _, mon) = await Fight();

        await h.Service.DispatchClientAsync(s, MapTestHarness.MonsterHitReq(mon.Id, 1));

        Assert.False(h.World.Has(Msg.MW_PARTYMANSTAT_ACK));
    }

    private static byte[] PartyAttr(uint charId, uint key, ushort party, uint chief)
    {
        var w = new PacketWriter(Msg.MW_PARTYATTR_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt16(party); w.WriteByte(0); w.WriteUInt32(chief); w.WriteUInt16(0);
        return w.ToArray();
    }
}
