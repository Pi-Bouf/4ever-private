using TMap.Protocol;
using TMap.Protocol.Crypto;
using Xunit;

namespace TMap.Protocol.Tests;

public class PacketCodecTests
{
    [Fact]
    public void Writer_Reader_Roundtrip_AllTypes()
    {
        var w = new PacketWriter(Msg.CS_CHARINFO_ACK);
        w.WriteUInt32(0xDEADBEEF);
        w.WriteByte(0x7F);
        w.WriteUInt16(0x1234);
        w.WriteInt64(unchecked((long)0x0102030405060708));
        w.WriteFloat(3663.5f);
        w.WriteString("Eldrin");
        w.WriteBool(true);
        byte[] packet = w.ToArray();

        Assert.Equal(packet.Length, PacketHeader.ReadSize(packet));
        Assert.Equal(Msg.CS_CHARINFO_ACK, PacketHeader.ReadId(packet));

        var r = new PacketReader(packet);
        Assert.Equal(Msg.CS_CHARINFO_ACK, r.Id);
        Assert.Equal(0xDEADBEEFu, r.ReadUInt32());
        Assert.Equal((byte)0x7F, r.ReadByte());
        Assert.Equal((ushort)0x1234, r.ReadUInt16());
        Assert.Equal(unchecked((long)0x0102030405060708), r.ReadInt64());
        Assert.Equal(3663.5f, r.ReadFloat());
        Assert.Equal("Eldrin", r.ReadString());
        Assert.True(r.ReadBool());
    }

    [Fact]
    public void Reader_PastEnd_ZeroFills()
    {
        var r = new PacketReader(new PacketWriter(Msg.CS_MOVE_REQ).ToArray());
        Assert.Equal(0u, r.ReadUInt32()); // no body → zero
        Assert.Equal("", r.ReadString());
    }

    [Fact]
    public void Framer_SplitsConcatenatedStream()
    {
        byte[] a = new PacketWriter(Msg.CS_MOVE_REQ).WriteUInt32(1).ToArray();
        byte[] b = new PacketWriter(Msg.CS_CHAT_REQ).WriteString("hi").ToArray();

        var framer = new PacketFramer();
        // Feed in awkward chunks to exercise reassembly.
        framer.Append(a.AsSpan(0, 3));
        framer.Append(a.AsSpan(3));
        framer.Append(b);

        Assert.True(framer.TryReadPacket(out var p1));
        Assert.Equal(Msg.CS_MOVE_REQ, PacketHeader.ReadId(p1));
        Assert.True(framer.TryReadPacket(out var p2));
        Assert.Equal(Msg.CS_CHAT_REQ, PacketHeader.ReadId(p2));
        Assert.False(framer.TryReadPacket(out _));
    }

    [Fact]
    public void MessageId_Bases_MatchProtocolHeaders()
    {
        Assert.Equal(0x5281, Msg.CS_CONNECT_REQ);
        Assert.Equal(0x5285, Msg.CS_CHARINFO_ACK);
        Assert.Equal(0x5289, Msg.CS_MOVE_REQ);
        Assert.Equal(0x9002, Msg.MW_CONNECT_ACK);
        Assert.Equal(0x9003, Msg.MW_ADDCHAR_ACK);
        Assert.Equal(0x9008, Msg.MW_ENTERSVR_REQ);
        Assert.Equal(0x900B, Msg.MW_CONRESULT_REQ);
        Assert.Equal(0x90C5, Msg.MW_CHECKMAIN_REQ);
        Assert.Equal(0x2918, Proto.ClientVersion);
    }

    [Fact]
    public void MakeServerId_PacksTypeInHighByte()
    {
        ushort wid = Proto.MakeServerId(7, SvrType.Map);
        Assert.Equal((byte)7, Proto.ServerIdOf(wid));
        Assert.Equal(SvrType.Map, Proto.ServerTypeOf(wid));
        Assert.Equal((ushort)((4 << 8) | 7), wid);
    }

    [Fact]
    public void Rc4_IsInvolutive()
    {
        byte[] data = { 1, 2, 3, 4, 5, 250, 251, 252 };
        byte[] copy = (byte[])data.Clone();
        Rc4.Apply(ProtocolKeys.Rc4Key, copy, copy.Length);
        Assert.NotEqual(data, copy);
        Rc4.Apply(ProtocolKeys.Rc4Key, copy, copy.Length);
        Assert.Equal(data, copy);
    }

    [Fact]
    public void ServerToClient_Cipher_Roundtrips()
    {
        var server = new SessionCipher();
        var client = new TestClientCipher();

        byte[] packet = new PacketWriter(Msg.CS_MOVE_ACK).WriteUInt32(42).WriteFloat(1234.5f).ToArray();
        byte[] plain = (byte[])packet.Clone();

        server.EncryptOutbound(packet, packet.Length);      // server → client (XOR only)
        Assert.True(client.DecryptFromServer(packet));       // client recovers (XOR only, no RC4)

        // header dwNumber/checksum differ; body must match.
        Assert.Equal(plain.AsSpan(PacketHeader.Size).ToArray(), packet.AsSpan(PacketHeader.Size).ToArray());
        var r = new PacketReader(packet);
        Assert.Equal(42u, r.ReadUInt32());
        Assert.Equal(1234.5f, r.ReadFloat());
    }

    [Fact]
    public void ClientToServer_Cipher_Roundtrips()
    {
        var server = new SessionCipher();
        var client = new TestClientCipher();

        byte[] packet = new PacketWriter(Msg.CS_CONNECT_REQ).WriteUInt16(Proto.ClientVersion).WriteUInt32(99).ToArray();
        byte[] plainBody = packet.AsSpan(PacketHeader.Size).ToArray();

        client.EncryptToServer(packet);                      // client → server (XOR + RC4 outermost)
        Assert.True(server.DecryptInbound(packet));          // server recovers + checksum/sequence OK

        Assert.Equal(plainBody, packet.AsSpan(PacketHeader.Size).ToArray());
        var r = new PacketReader(packet);
        Assert.Equal(Proto.ClientVersion, r.ReadUInt16());
        Assert.Equal(99u, r.ReadUInt32());
    }

    [Fact]
    public void ConnectChecksum_IsDeterministic_AndSensitive()
    {
        long c1 = Proto.ComputeConnectChecksum(Proto.ClientVersion, userId: 5, key: 0x1111, charId: 7);
        long c2 = Proto.ComputeConnectChecksum(Proto.ClientVersion, userId: 5, key: 0x1111, charId: 7);
        long c3 = Proto.ComputeConnectChecksum(Proto.ClientVersion, userId: 5, key: 0x1111, charId: 8);
        Assert.Equal(c1, c2);
        Assert.NotEqual(c1, c3);
    }
}

/// <summary>
/// The client-side mirror of <see cref="SessionCipher"/> (from the client's point of view), used only by
/// tests to prove the asymmetric transform round-trips. Send = XOR body+header then RC4 (outermost),
/// keeping wSize plaintext; Recv = XOR only (no RC4).
/// </summary>
internal sealed class TestClientCipher
{
    private uint _sendNumber;
    private uint _recvNumber;

    public void EncryptToServer(byte[] buf)
    {
        _sendNumber++;
        long key = ProtocolKeys.XorKeys[_sendNumber % ProtocolKeys.KeyCount];
        PacketHeader.WriteNumber(buf, _sendNumber);
        PacketCrypto.EncryptBody(buf, buf.Length, key);
        PacketCrypto.EncryptHeader(buf, key);
        ushort size = PacketHeader.ReadSize(buf);
        Rc4.Apply(ProtocolKeys.Rc4Key, buf, size);
        PacketHeader.WriteSize(buf, size); // keep wSize plaintext on the wire
    }

    public bool DecryptFromServer(byte[] buf)
    {
        _recvNumber++;
        long key = ProtocolKeys.XorKeys[_recvNumber % ProtocolKeys.KeyCount];
        PacketCrypto.DecryptHeader(buf, key);
        if (PacketHeader.ReadNumber(buf) != _recvNumber) return false;
        return PacketCrypto.DecryptBody(buf, PacketHeader.ReadSize(buf), key);
    }
}
