using TLogin.Protocol;
using TLogin.Protocol.Crypto;

namespace TBot;

/// <summary>
/// The client side of the asymmetric session cipher (the mirror of the server's
/// <see cref="SessionCipher"/>). Send to server: XOR body + header, then RC4 over the whole buffer
/// (RC4 outermost), wSize kept plaintext. Receive from server: XOR only (the server applies no RC4
/// outbound). Sequence numbers pre-increment, so the first packet in each direction is number 1.
///
/// This is exactly the transform in TLogin.Protocol.Tests.TestClientCipher / TcpTestClient, lifted into
/// the bot so it is shared by the live connection and the wire-format tests.
/// </summary>
public sealed class ClientCipher
{
    private uint _sendNumber;
    private uint _recvNumber;

    /// <summary>Diagnostic switch: when false, packets pass through unencrypted (to probe a crypt-off server).</summary>
    public bool UseCrypt { get; init; } = true;

    /// <summary>Encrypts an outgoing packet in place the way the client sends it to the server.</summary>
    public void EncodeToServer(byte[] buf)
    {
        if (!UseCrypt) return;
        _sendNumber++;
        int wSize = PacketHeader.ReadSize(buf);
        long key = ProtocolKeys.XorKeys[_sendNumber % ProtocolKeys.KeyCount];
        PacketHeader.WriteNumber(buf, _sendNumber);
        PacketCrypto.EncryptBody(buf, wSize, key);
        PacketCrypto.EncryptHeader(buf, key);
        Rc4.Apply(ProtocolKeys.Rc4Key, buf, wSize);
        PacketHeader.WriteSize(buf, (ushort)wSize); // keep size plaintext on the wire
    }

    /// <summary>Decrypts a server→client packet in place (no RC4). Returns false on checksum mismatch.</summary>
    public bool DecodeFromServer(byte[] buf)
    {
        if (!UseCrypt) return true;
        _recvNumber++;
        int wSize = PacketHeader.ReadSize(buf);
        long key = ProtocolKeys.XorKeys[_recvNumber % ProtocolKeys.KeyCount];
        PacketCrypto.DecryptHeader(buf, key);
        // The server's outbound sequence number is trusted; we don't reject on it here.
        return PacketCrypto.DecryptBody(buf, wSize, key);
    }
}
