using System.Security.Cryptography;
using TLogin.Protocol;
using TLogin.Protocol.Crypto;
using Xunit;

namespace TLogin.Protocol.Tests;

public class CryptoTests
{
    [Fact]
    public void SecretKey_Is39BytesPlusNul()
    {
        // 22 + 1 (0xA7) + 8 + 1 (0xA7) + 7 ascii = 39, plus the NUL terminator = 40.
        Assert.Equal(40, ProtocolKeys.SecretKeyBytes.Length);
        Assert.Equal(0xA7, ProtocolKeys.SecretKeyBytes[22]);
        Assert.Equal(0xA7, ProtocolKeys.SecretKeyBytes[31]);
        Assert.Equal(0x00, ProtocolKeys.SecretKeyBytes[^1]);
    }

    [Fact]
    public void Rc4Key_IsMd5OfSecret_GoldenVector()
    {
        // Locks the RC4 key derivation (MD5 over the 40 secret bytes).
        byte[] expected = MD5.HashData(ProtocolKeys.SecretKeyBytes);
        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(ProtocolKeys.Rc4Key));
        Assert.Equal(16, ProtocolKeys.Rc4Key.Length);
    }

    [Fact]
    public void Rc4_IsSymmetric()
    {
        byte[] data = System.Text.Encoding.ASCII.GetBytes("the quick brown fox jumps over");
        byte[] work = (byte[])data.Clone();
        Rc4.Apply(ProtocolKeys.Rc4Key, work, work.Length);
        Assert.NotEqual(data, work);
        Rc4.Apply(ProtocolKeys.Rc4Key, work, work.Length);
        Assert.Equal(data, work);
    }

    [Fact]
    public void Inbound_ClientEncrypt_ServerDecrypt_Roundtrips()
    {
        var server = new SessionCipher();
        var client = new TestClientCipher();

        for (uint i = 1; i <= 10; i++)
        {
            var w = new PacketWriter(Msg.CS_LOGIN_REQ);
            w.WriteUInt16(Proto.ClientVersion);
            w.WriteString("zombie3");
            w.WriteString("secretpw");
            w.WriteString("z1");
            w.WriteString("z2");
            w.WriteString($"user{i}");
            w.WriteInt64(0);
            w.WriteInt64(Proto.ComputeLoginChecksum(Proto.ClientVersion));
            byte[] packet = w.ToArray();

            client.EncryptToServer(packet);
            Assert.True(server.DecryptInbound(packet), $"decrypt failed at packet {i}");

            var r = new PacketReader(packet);
            Assert.Equal(Msg.CS_LOGIN_REQ, r.Id);
            Assert.Equal(Proto.ClientVersion, r.ReadUInt16());
            Assert.Equal("zombie3", r.ReadString());
            Assert.Equal("secretpw", r.ReadString());
            Assert.Equal("z1", r.ReadString());
            Assert.Equal("z2", r.ReadString());
            Assert.Equal($"user{i}", r.ReadString());
        }
    }

    [Fact]
    public void Outbound_ServerEncrypt_ClientDecrypt_Roundtrips()
    {
        var server = new SessionCipher();
        var client = new TestClientCipher();

        for (uint i = 1; i <= 10; i++)
        {
            var w = new PacketWriter(Msg.CS_LOGIN_ACK);
            w.WriteByte((byte)LoginResult.Success);
            w.WriteUInt32(1000 + i);
            w.WriteString("payload");
            byte[] packet = w.ToArray();

            server.EncryptOutbound(packet, packet.Length);
            Assert.True(client.DecryptFromServer(packet), $"client decrypt failed at packet {i}");

            var r = new PacketReader(packet);
            Assert.Equal(Msg.CS_LOGIN_ACK, r.Id);
            Assert.Equal((byte)LoginResult.Success, r.ReadByte());
            Assert.Equal(1000 + i, r.ReadUInt32());
            Assert.Equal("payload", r.ReadString());
        }
    }

    [Fact]
    public void Inbound_OutOfSequence_IsRejected()
    {
        var server = new SessionCipher();

        // Forge a packet whose embedded sequence number is wrong (the server expects 1 first).
        var w = new PacketWriter(Msg.CS_AGREEMENT_REQ);
        byte[] packet = w.ToArray();
        long key = ProtocolKeys.XorKeys[5 % ProtocolKeys.KeyCount];
        PacketHeader.WriteNumber(packet, 5); // server will expect 1
        PacketCrypto.EncryptBody(packet, packet.Length, key);
        PacketCrypto.EncryptHeader(packet, key);
        Rc4.Apply(ProtocolKeys.Rc4Key, packet, packet.Length);
        PacketHeader.WriteSize(packet, (ushort)packet.Length);

        Assert.False(server.DecryptInbound(packet));
    }

    [Fact]
    public void NoCrypt_PassesThroughUnchanged()
    {
        var server = new SessionCipher { UseCrypt = false };
        var w = new PacketWriter(Msg.CS_TERMINATE_REQ);
        w.WriteUInt32(720809425);
        byte[] packet = w.ToArray();
        byte[] copy = (byte[])packet.Clone();

        Assert.True(server.DecryptInbound(packet));
        Assert.Equal(copy, packet);
    }

    [Fact]
    public void LoginChecksum_MatchesKnownVersion()
    {
        // version 0x2918 (10520): 10520*2-500 = 20540; 20540 % 8 = 4 iterations.
        long expected = 20540;
        long body = 20540 / 8;
        for (int i = 0; i < 4; i++) { expected ^= body; expected += Proto.LoginChecksumKey; }
        Assert.Equal(expected, Proto.ComputeLoginChecksum(0x2918));
    }
}
