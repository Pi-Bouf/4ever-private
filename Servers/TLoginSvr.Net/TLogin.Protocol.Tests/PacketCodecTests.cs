using TLogin.Protocol;
using Xunit;

namespace TLogin.Protocol.Tests;

public class PacketCodecTests
{
    [Fact]
    public void Writer_Reader_Roundtrip_PreservesFields()
    {
        var w = new PacketWriter(Msg.CS_LOGIN_ACK);
        w.WriteByte(0x12);
        w.WriteUInt16(0xABCD);
        w.WriteUInt32(0xDEADBEEF);
        w.WriteInt64(0x0102030405060708);
        w.WriteString("Hello-4Story");
        w.WriteString("");
        byte[] packet = w.ToArray();

        Assert.Equal(packet.Length, PacketHeader.ReadSize(packet));
        Assert.Equal(Msg.CS_LOGIN_ACK, PacketHeader.ReadId(packet));

        var r = new PacketReader(packet);
        Assert.Equal(Msg.CS_LOGIN_ACK, r.Id);
        Assert.Equal((byte)0x12, r.ReadByte());
        Assert.Equal((ushort)0xABCD, r.ReadUInt16());
        Assert.Equal(0xDEADBEEFu, r.ReadUInt32());
        Assert.Equal(0x0102030405060708, r.ReadInt64());
        Assert.Equal("Hello-4Story", r.ReadString());
        Assert.Equal("", r.ReadString());
    }

    [Fact]
    public void Reader_PastEnd_ReturnsZeroAndEmpty()
    {
        var w = new PacketWriter(Msg.CS_TESTVERSION_ACK);
        w.WriteByte(7);
        var r = new PacketReader(w.ToArray());

        Assert.Equal((byte)7, r.ReadByte());
        Assert.Equal((byte)0, r.ReadByte());      // past the body
        Assert.Equal(0u, r.ReadUInt32());
        Assert.Equal("", r.ReadString());
    }

    [Fact]
    public void Framer_SplitsAndCoalescesStream()
    {
        var p1 = BuildPacket(Msg.CS_GROUPLIST_REQ, 3);
        var p2 = BuildPacket(Msg.CS_CHARLIST_REQ, 9);

        var framer = new PacketFramer();
        // Feed p1 split across two appends, then all of p2 at once.
        framer.Append(p1.AsSpan(0, 5));
        Assert.False(framer.TryReadPacket(out _));
        framer.Append(p1.AsSpan(5));
        framer.Append(p2);

        Assert.True(framer.TryReadPacket(out var got1));
        Assert.Equal(Msg.CS_GROUPLIST_REQ, PacketHeader.ReadId(got1));
        Assert.True(framer.TryReadPacket(out var got2));
        Assert.Equal(Msg.CS_CHARLIST_REQ, PacketHeader.ReadId(got2));
        Assert.False(framer.TryReadPacket(out _));
    }

    [Fact]
    public void Framer_RejectsImpossibleSize()
    {
        var framer = new PacketFramer();
        var bad = new byte[16];
        PacketHeader.WriteSize(bad, 4); // < header size
        framer.Append(bad);
        Assert.Throws<InvalidDataException>(() => framer.TryReadPacket(out _));
    }

    private static byte[] BuildPacket(ushort id, byte payload)
    {
        var w = new PacketWriter(id);
        w.WriteByte(payload);
        return w.ToArray();
    }
}
