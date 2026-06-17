using TWorld.Protocol;
using TWorld.Server.World;
using Xunit;

namespace TWorld.Server.Tests;

/// <summary>Phase-3 ranking tests: a connecting map receives the monthly ladder, and a MONTHRANKUPDATE
/// re-orders the in-memory ladder and is broadcast back. No DB.</summary>
public class RankTests
{
    private static void Connect(TcpTestClient client, byte serverId)
    {
        ushort wid = (ushort)((4 << 8) | serverId);
        var c = new PacketWriter(Msg.MW_CONNECT_ACK);
        c.WriteUInt16(wid); c.WriteByte(1); c.WriteByte(0);
        client.Send(c);
    }

    private static void WriteRanker(PacketWriter w, uint charId, string name, uint totalPoint, uint monthPoint, byte country)
    {
        w.WriteUInt32(0);          // totalRank
        w.WriteUInt32(0);          // monthRank
        w.WriteUInt32(charId);
        w.WriteString(name);
        w.WriteUInt32(totalPoint);
        w.WriteUInt32(monthPoint);
        w.WriteUInt16(0);          // monthWin
        w.WriteUInt16(0);          // monthLose
        w.WriteUInt32(0);          // totalWin
        w.WriteUInt32(0);          // totalLose
        w.WriteByte(country);
        w.WriteByte(0);            // level
        w.WriteByte(0);            // class
        w.WriteByte(0);            // race
        w.WriteByte(0);            // sex
        w.WriteByte(0);            // hair
        w.WriteByte(0);            // face
        w.WriteString("");         // say
        w.WriteString("");         // guild
    }

    [Fact]
    public async Task ConnectingMap_ReceivesMonthRankList()
    {
        await using var host = new WorldTestHost(s =>
        {
            s.RankMonth = 6;
            s.MonthRank[0][1].CharId = 4242;
            s.MonthRank[0][1].Name = "Champ";
            s.MonthRank[0][1].MonthPoint = 100;
        });
        using var c = await host.ConnectAsync();
        Connect(c, 1);

        var list = c.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_MONTHRANKLIST_REQ, list.Id);
        Assert.Equal((byte)6, list.ReadByte());                  // rank month
        Assert.Equal((byte)Proto.MonthRankCount, list.ReadByte()); // ladder depth

        // country 0, rank 0 (warlord, empty) then rank 1 (the seeded champ).
        SkipRanker(list);
        list.ReadUInt32(); list.ReadUInt32();                    // totalRank, monthRank
        Assert.Equal(4242u, list.ReadUInt32());
        Assert.Equal("Champ", list.ReadString());

        static void SkipRanker(PacketReader r)
        {
            r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt32(); r.ReadString();
            r.ReadUInt32(); r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();
            r.ReadUInt32(); r.ReadUInt32();
            for (int i = 0; i < 7; i++) r.ReadByte();
            r.ReadString(); r.ReadString();
        }
    }

    [Fact]
    public async Task MonthRankUpdate_InsertsAndBroadcasts()
    {
        await using var host = new WorldTestHost(s =>
        {
            s.RankMonth = 6;
            s.MonthRank[0][1].CharId = 100;
            s.MonthRank[0][1].MonthPoint = 10;
        });
        using var c = await host.ConnectAsync();
        Connect(c, 1);
        Assert.Equal(Msg.MW_MONTHRANKLIST_REQ, c.Receive(TimeSpan.FromSeconds(5)).Id); // drain the initial ladder

        var upd = new PacketWriter(Msg.MW_MONTHRANKUPDATE_ACK);
        upd.WriteByte(6);   // month
        upd.WriteByte(0);   // country
        WriteRanker(upd, charId: 200, name: "Riser", totalPoint: 50, monthPoint: 20, country: 0);
        c.Send(upd);

        var resp = c.Receive(TimeSpan.FromSeconds(5));
        Assert.Equal(Msg.MW_MONTHRANKUPDATE_REQ, resp.Id);
        Assert.Equal((byte)6, resp.ReadByte());   // month
        Assert.Equal((byte)0, resp.ReadByte());   // country

        await Task.Delay(100);
        Assert.Equal(200u, host.State.MonthRank[0][1].CharId);   // new top of the monthly ladder
        Assert.Equal(20u, host.State.MonthRank[0][1].MonthPoint);
    }
}
