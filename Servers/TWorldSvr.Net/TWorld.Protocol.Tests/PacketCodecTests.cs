using TWorld.Protocol;
using Xunit;

namespace TWorld.Protocol.Tests;

public class PacketCodecTests
{
    [Fact]
    public void Writer_Reader_Roundtrip()
    {
        var w = new PacketWriter(Msg.MW_ENTERCHAR_REQ);
        w.WriteUInt32(0xDEADBEEF);
        w.WriteUInt32(0x12345678);
        w.WriteByte(7);
        w.WriteString("Hero");
        w.WriteUInt16(0xABCD);
        w.WriteFloat(3664.405f);
        w.WriteInt64(0x0102030405060708);
        w.WriteRaw(new byte[] { 0x00 }); // bRecallCnt = 0
        byte[] packet = w.ToArray();

        Assert.Equal(packet.Length, PacketHeader.ReadSize(packet));
        Assert.Equal(Msg.MW_ENTERCHAR_REQ, PacketHeader.ReadId(packet));
        Assert.Equal(0u, PacketHeader.ReadNumber(packet));   // plaintext: number unused
        Assert.Equal(0L, PacketHeader.ReadChecksum(packet)); // plaintext: checksum unused

        var r = new PacketReader(packet);
        Assert.Equal(Msg.MW_ENTERCHAR_REQ, r.Id);
        Assert.Equal(0xDEADBEEFu, r.ReadUInt32());
        Assert.Equal(0x12345678u, r.ReadUInt32());
        Assert.Equal((byte)7, r.ReadByte());
        Assert.Equal("Hero", r.ReadString());
        Assert.Equal((ushort)0xABCD, r.ReadUInt16());
        Assert.Equal(3664.405f, r.ReadFloat());
        Assert.Equal(0x0102030405060708, r.ReadInt64());
        Assert.Equal(new byte[] { 0x00 }, r.ReadRemaining().ToArray());
    }

    [Fact]
    public void Framer_SplitsStream()
    {
        var p1 = new PacketWriter(Msg.MW_CONNECT_ACK).ToArray();
        var p2 = new PacketWriter(Msg.MW_ADDCHAR_ACK).ToArray();
        var framer = new PacketFramer();
        framer.Append(p1.AsSpan(0, 3));
        Assert.False(framer.TryReadPacket(out _));
        framer.Append(p1.AsSpan(3));
        framer.Append(p2);
        Assert.True(framer.TryReadPacket(out var a));
        Assert.Equal(Msg.MW_CONNECT_ACK, PacketHeader.ReadId(a));
        Assert.True(framer.TryReadPacket(out var b));
        Assert.Equal(Msg.MW_ADDCHAR_ACK, PacketHeader.ReadId(b));
        Assert.False(framer.TryReadPacket(out _));
    }

    [Fact]
    public void MessageIds_MatchProtocolBases()
    {
        Assert.Equal(0x9001, Msg.MW_BASE);
        Assert.Equal(0x9002, Msg.MW_CONNECT_ACK);
        Assert.Equal(0x9003, Msg.MW_ADDCHAR_ACK);
        Assert.Equal(0x9008, Msg.MW_ENTERSVR_REQ);
        Assert.Equal(0x900A, Msg.MW_INVALIDCHAR_REQ);
        Assert.Equal(0x903F, Msg.MW_CHAT_ACK);
        Assert.Equal(0x9359, Msg.CT_CTRLSVR_REQ);   // CT_CONTROL + 0x0058 (CTProtocol.h; 0x9302 is CT_OPLOGIN)
        Assert.Equal(0x999A, Msg.RW_RELAYSVR_REQ);
    }
}
