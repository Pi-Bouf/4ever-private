using TPatch.Protocol;
using Xunit;

namespace TPatch.Protocol.Tests;

/// <summary>Wire-format tests for the 8-byte patch-plane framing.</summary>
public class PacketTests
{
    [Fact]
    public void Header_Is_Eight_Bytes()
    {
        Assert.Equal(8, PacketHeader.Size);
    }

    [Fact]
    public void Writer_Sets_Size_And_Id_In_Header()
    {
        var w = new PacketWriter(Msg.CT_NEWPATCH_ACK);
        w.WriteUInt32(0xDEADBEEF);
        byte[] p = w.ToArray();

        Assert.Equal(PacketHeader.Size + 4, p.Length);
        Assert.Equal(p.Length, PacketHeader.ReadSize(p));
        Assert.Equal(Msg.CT_NEWPATCH_ACK, PacketHeader.ReadId(p));
        Assert.Equal(0u, PacketHeader.ReadChecksum(p)); // plaintext — checksum stays zero
    }

    [Fact]
    public void Scalar_And_String_RoundTrip()
    {
        var w = new PacketWriter(Msg.CT_PATCH_ACK);
        w.WriteString("http://host/patch");
        w.WriteUInt32(0x0100007F); // 127.0.0.1 packed as inet_addr
        w.WriteUInt16(4815);
        w.WriteUInt16(2);

        var r = new PacketReader(w.ToArray());
        Assert.Equal(Msg.CT_PATCH_ACK, r.Id);
        Assert.Equal("http://host/patch", r.ReadString());
        Assert.Equal(0x0100007Fu, r.ReadUInt32());
        Assert.Equal((ushort)4815, r.ReadUInt16());
        Assert.Equal((ushort)2, r.ReadUInt16());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Reads_Past_End_Return_Zero()
    {
        var r = new PacketReader(new PacketWriter(Msg.CT_PATCHSTART_REQ).ToArray());
        Assert.Equal(0u, r.ReadUInt32());   // empty body
        Assert.Equal("", r.ReadString());
        Assert.Equal(0, r.Remaining);
    }

    [Fact]
    public void Skip_Advances_Past_Leading_Int64()
    {
        var w = new PacketWriter(Msg.CT_SERVICEMONITOR_ACK);
        w.WriteInt64(0);          // the leading INT64 the C++ skips
        w.WriteUInt32(0x1234);    // the tick

        var r = new PacketReader(w.ToArray());
        r.Skip(8);
        Assert.Equal(0x1234u, r.ReadUInt32());
    }

    [Fact]
    public void Empty_String_Serializes_As_Zero_Length()
    {
        var w = new PacketWriter(Msg.CT_NEWPATCH_ACK);
        w.WriteString("");
        var r = new PacketReader(w.ToArray());
        Assert.Equal("", r.ReadString());
    }
}
