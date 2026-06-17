using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

public class GuildTests
{
    private const byte ServerId = 1, ServerType = 4;
    private static ushort WId => (ushort)((ServerType << 8) | ServerId);

    /// <summary>Drives a character through connect/addchar/chardata to the point ENTERCHAR_REQ is emitted,
    /// returns that reader, then confirms entry. Assumes MW_CONNECT_ACK was already sent on this client.</summary>
    private static PacketReader EnterChar(TcpTestClient client, uint charId, uint key, byte level, byte country)
    {
        var add = new PacketWriter(Msg.MW_ADDCHAR_ACK);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteUInt32(0x0100007F); add.WriteUInt16(5816); add.WriteUInt32(charId + 1000);
        client.Send(add);

        var enterSvr = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_ENTERSVR_REQ, enterSvr.Id);

        var cd = new PacketWriter(Msg.MW_CHARDATA_ACK);
        cd.WriteUInt32(charId); cd.WriteUInt32(key); cd.WriteByte(0); cd.WriteByte(level);
        cd.WriteUInt32(500); cd.WriteUInt32(500); cd.WriteUInt32(200); cd.WriteUInt32(200);
        cd.WriteByte(country); cd.WriteByte(0); cd.WriteByte(0); // recall count 0
        client.Send(cd);

        var enterChar = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_ENTERCHAR_REQ, enterChar.Id);
        return enterChar;
    }

    private static void ConfirmEnter(TcpTestClient client, uint charId, uint key)
    {
        var ack = new PacketWriter(Msg.MW_ENTERCHAR_ACK);
        ack.WriteUInt32(charId); ack.WriteUInt32(key);
        client.Send(ack);

        // Complete the main-server check handshake added to the enter flow (CHECKMAIN_REQ -> _ACK -> CONRESULT_REQ).
        Assert.Equal(Msg.MW_CHECKMAIN_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
        var cm = new PacketWriter(Msg.MW_CHECKMAIN_ACK);
        cm.WriteUInt32(charId); cm.WriteUInt32(key);
        client.Send(cm);
        Assert.Equal(Msg.MW_CONRESULT_REQ, client.Receive(TimeSpan.FromSeconds(5)).Id);
    }

    private static void Connect(TcpTestClient client)
    {
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(WId); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    [Fact]
    public async Task GuildMember_Enter_PopulatesGuildFields_And_MemberList()
    {
        const uint charId = 1001, key = 0xAAAA0001;
        await using var host = new WorldTestHost(state =>
        {
            var gl = new GuildLevel { Level = 1, MaxCnt = 20 };
            state.GuildLevels[1] = gl;
            var g = new Guild { Id = 7, Name = "Knights", Chief = charId, ChiefName = "Hero", Level = 1, Fame = 1234, LevelChart = gl };
            g.Members[charId] = new GuildMember { CharId = charId, Name = "Hero", Level = 1, Duty = (byte)GuildDuty.Chief };
            state.Guilds[7] = g;
            state.CharGuild[charId] = 7;
        });
        using var client = await host.ConnectAsync();
        Connect(client);

        // ENTERCHAR_REQ must now carry the real guild id/name.
        var ec = EnterChar(client, charId, key, level: 10, country: 4);
        Assert.Equal(charId, ec.ReadUInt32());
        Assert.Equal(key, ec.ReadUInt32());
        ec.ReadByte();                                  // startAct
        Assert.Equal("Hero", ec.ReadString());          // name learned from the guild roster
        ec.ReadUInt16();                                // mapId
        ec.ReadFloat(); ec.ReadFloat(); ec.ReadFloat(); // pos
        Assert.Equal(7u, ec.ReadUInt32());              // dwGuildID
        ec.ReadUInt32();                                // fame
        ec.ReadUInt32();                                // fameColor
        Assert.Equal("Knights", ec.ReadString());       // guild name

        ConfirmEnter(client, charId, key);

        var list = new PacketWriter(Msg.MW_GUILDMEMBERLIST_ACK);
        list.WriteUInt32(charId); list.WriteUInt32(key);
        client.Send(list);

        var resp = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_GUILDMEMBERLIST_REQ, resp.Id);
        Assert.Equal(charId, resp.ReadUInt32());
        Assert.Equal(key, resp.ReadUInt32());
        Assert.Equal((byte)GuildResult.Success, resp.ReadByte());
        Assert.Equal(7u, resp.ReadUInt32());
        Assert.Equal("Knights", resp.ReadString());
        Assert.Equal((ushort)1, resp.ReadUInt16());     // member count
        Assert.Equal(charId, resp.ReadUInt32());        // first member
        Assert.Equal("Hero", resp.ReadString());
    }

    [Fact]
    public async Task GuildArticle_Add_Then_List()
    {
        const uint charId = 3001, key = 0xCCCC0003;
        await using var host = new WorldTestHost(state =>
        {
            var gl = new GuildLevel { Level = 1, MaxCnt = 20 };
            state.GuildLevels[1] = gl;
            var g = new Guild { Id = 8, Name = "Scribes", Chief = charId, ChiefName = "Chief", Level = 1, LevelChart = gl };
            g.Members[charId] = new GuildMember { CharId = charId, Name = "Chief", Level = 1, Duty = (byte)GuildDuty.Chief };
            state.Guilds[8] = g;
            state.CharGuild[charId] = 8;
        });
        using var client = await host.ConnectAsync();
        Connect(client);
        EnterChar(client, charId, key, level: 20, country: 4);
        ConfirmEnter(client, charId, key);

        var add = new PacketWriter(Msg.MW_GUILDARTICLEADD_ACK);
        add.WriteUInt32(charId); add.WriteUInt32(key); add.WriteString("Notice"); add.WriteString("Meeting tonight");
        client.Send(add);
        var addResp = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_GUILDARTICLEADD_REQ, addResp.Id);
        addResp.ReadUInt32(); addResp.ReadUInt32();
        Assert.Equal((byte)GuildResult.Success, addResp.ReadByte());

        var list = new PacketWriter(Msg.MW_GUILDARTICLELIST_ACK);
        list.WriteUInt32(charId); list.WriteUInt32(key);
        client.Send(list);
        var listResp = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_GUILDARTICLELIST_REQ, listResp.Id);
        listResp.ReadUInt32(); listResp.ReadUInt32();
        Assert.Equal((byte)1, listResp.ReadByte());       // article count (BYTE, per GetArticleSize)
        listResp.ReadUInt32();                            // article id
        listResp.ReadByte();                              // duty
        Assert.Equal("Chief", listResp.ReadString());     // writer
        Assert.Equal("Notice", listResp.ReadString());    // title
        Assert.Equal("Meeting tonight", listResp.ReadString()); // body
    }

    [Fact]
    public async Task GuildEstablish_CreatesGuild_AndReplies()
    {
        const uint charId = 2002, key = 0xBBBB0002;
        await using var host = new WorldTestHost();
        using var client = await host.ConnectAsync();
        Connect(client);

        var ec = EnterChar(client, charId, key, level: 5, country: 4);
        Assert.Equal(charId, ec.ReadUInt32()); ec.ReadUInt32(); ec.ReadByte(); ec.ReadString();
        ec.ReadUInt16(); ec.ReadFloat(); ec.ReadFloat(); ec.ReadFloat();
        Assert.Equal(0u, ec.ReadUInt32());              // no guild yet
        ConfirmEnter(client, charId, key);

        var est = new PacketWriter(Msg.MW_GUILDESTABLISH_ACK);
        est.WriteUInt32(charId); est.WriteUInt32(key); est.WriteString("NewGuild");
        client.Send(est);

        var resp = client.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_GUILDESTABLISH_REQ, resp.Id);
        Assert.Equal(charId, resp.ReadUInt32());
        Assert.Equal(key, resp.ReadUInt32());
        Assert.Equal((byte)GuildResult.Success, resp.ReadByte());
        Assert.NotEqual(0u, resp.ReadUInt32());          // assigned guild id
        Assert.Equal("NewGuild", resp.ReadString());

        // Give the batch task a beat, then confirm the guild is tracked.
        await Task.Delay(100);
        Assert.Single(host.State.Guilds);
        Assert.True(host.State.CharGuild.ContainsKey(charId));
    }
}
