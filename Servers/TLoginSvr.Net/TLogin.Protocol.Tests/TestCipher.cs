using TLogin.Protocol;
using TLogin.Protocol.Crypto;

namespace TLogin.Protocol.Tests;

/// <summary>
/// A client-side mirror of <see cref="SessionCipher"/> for tests: it encrypts the way the game client
/// does (XOR body + header, then RC4 over the whole buffer, with wSize kept plaintext) and decrypts
/// server→client packets (XOR only, no RC4). This lets tests drive the server's inbound decrypt and
/// verify its outbound encrypt without the real client.
/// </summary>
public sealed class TestClientCipher
{
    private uint _sendNumber;
    private uint _recvNumber;

    /// <summary>Encrypt a packet the way the client sends it to the server (RC4 outermost).</summary>
    public void EncryptToServer(byte[] buf)
    {
        _sendNumber++;
        int wSize = PacketHeader.ReadSize(buf);
        long key = ProtocolKeys.XorKeys[_sendNumber % ProtocolKeys.KeyCount];
        PacketHeader.WriteNumber(buf, _sendNumber);
        PacketCrypto.EncryptBody(buf, wSize, key);
        PacketCrypto.EncryptHeader(buf, key);
        Rc4.Apply(ProtocolKeys.Rc4Key, buf, wSize);
        PacketHeader.WriteSize(buf, (ushort)wSize); // keep size plaintext on the wire
    }

    /// <summary>Decrypt a server→client packet (the server applies no RC4 outbound).</summary>
    public bool DecryptFromServer(byte[] buf)
    {
        _recvNumber++;
        int wSize = PacketHeader.ReadSize(buf);
        long key = ProtocolKeys.XorKeys[_recvNumber % ProtocolKeys.KeyCount];
        PacketCrypto.DecryptHeader(buf, key);
        if (PacketHeader.ReadNumber(buf) != _recvNumber) return false;
        return PacketCrypto.DecryptBody(buf, wSize, key);
    }
}
