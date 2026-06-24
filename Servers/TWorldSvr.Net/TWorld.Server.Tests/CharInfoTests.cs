using TWorld.Protocol;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase 5h — character-info / mail / misc. Stat-inspect &amp; mail route to the owning char's main;
/// hero selection broadcasts; a base change updates state and fans out (appearance to connections, name/title
/// to all maps). No DB.</summary>
public class CharInfoTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    private static void Connect(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void EnterChar(TcpTestClient client, uint charId, uint key)
    {
        var add = new PacketWriter(Msg.MW_ADDCHAR_ACK);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteUInt32(0x0100007F); add.WriteUInt16(5816); add.WriteUInt32(charId + 1000);
        client.Send(add);
        Assert.Equal(Msg.MW_ENTERSVR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cd = new PacketWriter(Msg.MW_CHARDATA_ACK);
        cd.WriteUInt32(charId); cd.WriteUInt32(key); cd.WriteByte(0); cd.WriteByte(20);
        cd.WriteUInt32(500); cd.WriteUInt32(500); cd.WriteUInt32(200); cd.WriteUInt32(200);
        cd.WriteByte(0); cd.WriteByte(0); cd.WriteByte(0);
        client.Send(cd);
        Assert.Equal(Msg.MW_ENTERCHAR_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var ack = new PacketWriter(Msg.MW_ENTERCHAR_ACK);
        ack.WriteUInt32(charId); ack.WriteUInt32(key);
        client.Send(ack);
        Assert.Equal(Msg.MW_CHECKMAIN_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        client.Send(cm);
        Assert.Equal(Msg.MW_CONRESULT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
    }

    [Fact]
    public async Task CharStatInfo_DeliversInspectRequestToTargetMain()
    {
        const uint charId = 50, key = 0xF1;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var s = new PacketWriter(Msg.MW_CHARSTATINFO_ACK);
        s.WriteUInt32(999);        // requester
        s.WriteUInt32(charId);     // target
        client.Send(s);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CHARSTATINFOANS_REQ, req.Id);
        Assert.Equal(999u, req.ReadUInt32());
        Assert.Equal(charId, req.ReadUInt32());
    }

    [Fact]
    public async Task HeroSelect_Broadcasts()
    {
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        var h = new PacketWriter(Msg.MW_HEROSELECT_ACK);
        h.WriteUInt16(12); h.WriteString("Champion"); h.WriteInt64(1700000000);
        client.Send(h);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_HEROSELECT_REQ, req.Id);
        Assert.Equal((ushort)12, req.ReadUInt16());
        Assert.Equal("Champion", req.ReadString());
        Assert.Equal(1700000000L, req.ReadInt64());
    }

    [Fact]
    public async Task ChangeCharBase_Face_UpdatesStateAndFansToConnection()
    {
        const uint charId = 51, key = 0xF2;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var c = new PacketWriter(Msg.MW_CHANGECHARBASE_ACK);
        c.WriteUInt32(charId); c.WriteUInt32(key); c.WriteByte(45 /*IK_FACE*/); c.WriteByte(7); c.WriteUInt16(0); c.WriteString("");
        client.Send(c);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CHANGECHARBASE_REQ, req.Id);
        Assert.Equal(charId, req.ReadUInt32()); Assert.Equal(key, req.ReadUInt32());
        Assert.Equal((byte)45, req.ReadByte()); Assert.Equal((byte)7, req.ReadByte());
        Assert.Equal((byte)7, host.State.Characters[charId].Face);
    }

    [Fact]
    public async Task ChangeCharBase_Name_RenamesAndBroadcasts()
    {
        const uint charId = 52, key = 0xF3;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var ch = host.State.Characters[charId];
        ch.Name = "Old";
        host.State.CharactersByName["Old"] = ch;

        var c = new PacketWriter(Msg.MW_CHANGECHARBASE_ACK);
        c.WriteUInt32(charId); c.WriteUInt32(key); c.WriteByte(48 /*IK_NAME*/); c.WriteByte(0); c.WriteUInt16(0); c.WriteString("New");
        client.Send(c);

        Assert.Equal(Msg.MW_CHANGECHARBASE_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        await Task.Delay(40);
        Assert.Equal("New", ch.Name);
        Assert.True(host.State.CharactersByName.ContainsKey("New"));
        Assert.False(host.State.CharactersByName.ContainsKey("Old"));
    }

    [Fact]
    public async Task ChangeCharBase_Title_UpdatesAndBroadcasts()
    {
        const uint charId = 53, key = 0xF4;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var c = new PacketWriter(Msg.MW_CHANGECHARBASE_ACK);
        c.WriteUInt32(charId); c.WriteUInt32(key); c.WriteByte(103 /*IK_TITLE*/); c.WriteByte(0); c.WriteUInt16(42); c.WriteString("");
        client.Send(c);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CHANGECHARBASE_REQ, req.Id);
        await Task.Delay(40);
        Assert.Equal((ushort)42, host.State.Characters[charId].TitleId);
    }

    [Fact]
    public async Task ChangeCharBase_AidCountry_RebucketsAndFans()
    {
        const uint charId = 54, key = 0xF5;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var ch = host.State.Characters[charId];
        ch.Level = 140;                 // in the tracked nation-balance band (>=130)
        ch.Country = (byte)TWorld.Protocol.Contry.Defugel;
        ch.AidCountry = (byte)TWorld.Protocol.Contry.None;

        var c = new PacketWriter(Msg.MW_CHANGECHARBASE_ACK);
        c.WriteUInt32(charId); c.WriteUInt32(key); c.WriteByte(97 /*IK_AIDCOUNTRY*/); c.WriteByte((byte)TWorld.Protocol.Contry.Craxion); c.WriteUInt16(0); c.WriteString("");
        client.Send(c);

        var req = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_CHANGECHARBASE_REQ, req.Id);
        await Task.Delay(40);
        Assert.Equal((byte)TWorld.Protocol.Contry.Craxion, ch.AidCountry);
        // Now bucketed under Craxion in its level gap; gap = (140-130)/10 = 1.
        Assert.Contains(charId, host.State.WarCountry[(byte)TWorld.Protocol.Contry.Craxion][1]);
        Assert.DoesNotContain(charId, host.State.WarCountry[(byte)TWorld.Protocol.Contry.Defugel][1]);
    }

    [Fact]
    public async Task ChangeCharBase_Country_LeavesGuild()
    {
        const uint charId = 55, key = 0xF6;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key);

        var ch = host.State.Characters[charId];
        ch.Name = "Member";
        var guild = new TWorld.Server.World.Guild { Id = 30, Name = "Knights" };
        guild.Members[charId] = new TWorld.Server.World.GuildMember { CharId = charId, Name = "Member" };
        host.State.Guilds[30] = guild;
        host.State.CharGuild[charId] = 30;
        ch.Guild = guild;

        var c = new PacketWriter(Msg.MW_CHANGECHARBASE_ACK);
        c.WriteUInt32(charId); c.WriteUInt32(key); c.WriteByte(96 /*IK_COUNTRY*/); c.WriteByte((byte)TWorld.Protocol.Contry.Craxion); c.WriteUInt16(0); c.WriteString("");
        client.Send(c);

        // The char's map gets the guild-leave, then the base-change fan.
        Assert.Equal(Msg.MW_GUILDLEAVE_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        Assert.Equal(Msg.MW_CHANGECHARBASE_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        await Task.Delay(40);
        Assert.Null(ch.Guild);
        Assert.False(guild.Members.ContainsKey(charId));
        Assert.False(host.State.CharGuild.ContainsKey(charId));
        Assert.Equal((byte)TWorld.Protocol.Contry.Craxion, ch.Country);
    }
}
