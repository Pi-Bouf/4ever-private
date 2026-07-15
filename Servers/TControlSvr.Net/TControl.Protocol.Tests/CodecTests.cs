using TControl.Protocol;
using Xunit;

namespace TControl.Protocol.Tests;

public class CodecTests
{
    [Fact]
    public void Header_size_and_id_round_trip()
    {
        var w = new PacketWriter(Msg.CT_OPLOGIN_ACK);
        w.WriteByte(0).WriteByte(1).WriteUInt32(42);
        byte[] packet = w.ToArray();

        Assert.Equal(Msg.CT_OPLOGIN_ACK, PacketHeader.ReadId(packet));
        Assert.Equal(packet.Length, PacketHeader.ReadSize(packet));

        var r = new PacketReader(packet);
        Assert.Equal(Msg.CT_OPLOGIN_ACK, r.Id);
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(1, r.ReadByte());
        Assert.Equal(42u, r.ReadUInt32());
    }

    [Fact]
    public void Scalars_and_string_round_trip()
    {
        var w = new PacketWriter(Msg.CT_CHATBAN_REQ);
        w.WriteString("héllo").WriteUInt16(60).WriteUInt32(7).WriteUInt32(3).WriteInt64(-123).WriteFloat(1.5f);
        var r = new PacketReader(w.ToArray());
        _ = r.Id;
        Assert.Equal("héllo", r.ReadString());
        Assert.Equal(60, r.ReadUInt16());
        Assert.Equal(7u, r.ReadUInt32());
        Assert.Equal(3u, r.ReadUInt32());
        Assert.Equal(-123, r.ReadInt64());
        Assert.Equal(1.5f, r.ReadFloat());
    }

    [Fact]
    public void Read_past_end_returns_zero()
    {
        var r = new PacketReader(new PacketWriter(Msg.CT_TIMER_REQ).ToArray());
        Assert.Equal(0, r.ReadByte());
        Assert.Equal(0u, r.ReadUInt32());
        Assert.Equal("", r.ReadString());
    }

    [Fact]
    public void Framer_splits_and_coalesces()
    {
        byte[] a = new PacketWriter(Msg.CT_TIMER_REQ).WriteUInt32(1).ToArray();
        byte[] b = new PacketWriter(Msg.CT_OPLOGIN_ACK).WriteUInt32(2).ToArray();
        var framer = new PacketFramer();
        framer.Append(a.AsSpan(0, 3));           // partial
        Assert.False(framer.TryReadPacket(out _));
        framer.Append(a.AsSpan(3));              // complete a
        framer.Append(b);                        // + b
        Assert.True(framer.TryReadPacket(out var p1));
        Assert.True(framer.TryReadPacket(out var p2));
        Assert.False(framer.TryReadPacket(out _));
        Assert.Equal(Msg.CT_TIMER_REQ, PacketHeader.ReadId(p1));
        Assert.Equal(Msg.CT_OPLOGIN_ACK, PacketHeader.ReadId(p2));
    }

    [Theory]
    [InlineData(Msg.CT_CONTROL, 0x9301)]
    [InlineData(Msg.CT_OPLOGIN_REQ, 0x9302)]
    [InlineData(Msg.CT_SERVICEMONITOR_REQ, 0x931E)]
    [InlineData(Msg.CT_SERVICEMONITOR_ACK, 0x931F)]
    [InlineData(Msg.CT_CTRLSVR_REQ, 0x9359)]
    [InlineData(Msg.CT_CHATBAN_REQ, 0x934D)]
    [InlineData(Msg.CT_CHATBAN_ACK, 0x934E)]
    [InlineData(Msg.CT_CMGIFTLIST_ACK, 0x9381)]
    [InlineData(Msg.CT_CMGIFTCHARTUPDATE_REQ, 0x9382)]
    [InlineData(Msg.SM_BATTLESTATUS_REQ, 0x159D)]
    public void Golden_message_ids(ushort actual, int expected) => Assert.Equal((ushort)expected, actual);

    [Fact]
    public void EventInfo_round_trips_all_sub_vectors()
    {
        var e = new EventInfo
        {
            Index = 100, Id = (byte)EventType.CashSale, State = 1, GroupId = 1, SvrType = 3, SvrId = 0,
            StartDate = 1_700_000_000, EndDate = 1_700_003_600, Value = 50, MapId = 0xFF,
            StartAlarm = 10, EndAlarm = 5, StartMsg = "start", EndMsg = "end", Title = "sale", PartTime = 1, LotMsg = "gg",
        };
        e.CashItems.Add(new CashItemSale { Id = 11, SaleValue = 30 });
        e.CashItems.Add(new CashItemSale { Id = 22, SaleValue = 40 });
        e.Mon.StartAction = 1; e.Mon.EndAction = 2; e.Mon.SpawnIds.Add(7); e.Mon.SpawnIds.Add(8);
        e.MonRegens.Add(new MonRegen { MonId = 5, Delay = 1000, MapId = 3, PosX = 1.5f, PosY = 2.5f, PosZ = 3.5f });
        e.Lotteries.Add(new Lottery { ItemId = 1203, Num = 1, Winner = 10 });

        var w = new PacketWriter(Msg.CT_EVENTLIST_ACK);
        e.Write(w);
        var r = new PacketReader(w.ToArray());
        _ = r.Id;
        var back = EventInfo.Read(r);

        Assert.Equal(e.Index, back.Index);
        Assert.Equal(e.Id, back.Id);
        Assert.Equal(e.State, back.State);
        Assert.Equal(e.StartDate, back.StartDate);
        Assert.Equal(e.EndDate, back.EndDate);
        Assert.Equal(e.Value, back.Value);
        Assert.Equal(e.MapId, back.MapId);
        Assert.Equal(e.Title, back.Title);
        Assert.Equal(e.LotMsg, back.LotMsg);
        Assert.Equal(2, back.CashItems.Count);
        Assert.Equal(22, back.CashItems[1].Id);
        Assert.Equal(40, back.CashItems[1].SaleValue);
        Assert.Equal(new ushort[] { 7, 8 }, back.Mon.SpawnIds.ToArray());
        Assert.Equal(2, back.Mon.EndAction);
        Assert.Single(back.MonRegens);
        Assert.Equal(3.5f, back.MonRegens[0].PosZ);
        Assert.Single(back.Lotteries);
        Assert.Equal(1203, back.Lotteries[0].ItemId);
    }

    [Fact]
    public void EventInfo_szValue_round_trips_cashsale_and_lottery()
    {
        var e = new EventInfo { Id = (byte)EventType.CashSale };
        e.ParseStrValue("11-30;22-40;");
        Assert.Equal(2, e.CashItems.Count);
        Assert.Equal(22, e.CashItems[1].Id);
        Assert.Equal(40, e.CashItems[1].SaleValue);
        Assert.Equal("11-30;22-40;", e.MakeStrValue());

        var lot = new EventInfo { Id = (byte)EventType.Lottery };
        lot.ParseStrValue("1203x1-10;55x2-1;|Congrats");
        Assert.Equal(2, lot.Lotteries.Count);
        Assert.Equal(1203, lot.Lotteries[0].ItemId);
        Assert.Equal(1, lot.Lotteries[0].Num);
        Assert.Equal(10, lot.Lotteries[0].Winner);
        Assert.Equal("Congrats", lot.LotMsg);
    }
}
