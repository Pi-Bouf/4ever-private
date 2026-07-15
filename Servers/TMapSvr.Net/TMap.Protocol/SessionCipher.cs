using TMap.Protocol.Crypto;

namespace TMap.Protocol;

/// <summary>
/// Per-connection cipher state, mirroring <c>CSession::Encrypt/Decrypt</c> in
/// <c>Servers/TNetLib/Session.cpp</c>. The transform is asymmetric:
/// <list type="bullet">
/// <item>Outbound (server → client): XOR body + checksum, then XOR header. No RC4.</item>
/// <item>Inbound (client → server): RC4 over the whole buffer (outermost layer), then XOR header,
/// then verify sequence number and XOR body/checksum.</item>
/// </list>
/// Sequence numbers start at 0 and pre-increment, so the first packet in each direction is number 1.
///
/// The map server faces two planes: the client plane (CS_MAP) is encrypted (UseCrypt = true, the
/// C++ TMapSvr sets <c>m_bUseCrypt = TRUE</c> at accept), while the world plane (MW) is plaintext
/// server↔server traffic (a cipher with UseCrypt = false, i.e. pass-through).
/// </summary>
public sealed class SessionCipher
{
    private uint _sendNumber;
    private uint _recvNumber;

    public bool UseCrypt { get; set; } = true;

    public void EncryptOutbound(byte[] buf, int wSize)
    {
        if (!UseCrypt) return;

        _sendNumber++;
        PacketHeader.WriteNumber(buf, _sendNumber);
        long key = ProtocolKeys.XorKeys[_sendNumber % ProtocolKeys.KeyCount];
        PacketCrypto.EncryptBody(buf, wSize, key);
        PacketCrypto.EncryptHeader(buf, key);
    }

    public bool DecryptInbound(byte[] buf)
    {
        if (!UseCrypt) return true;

        _recvNumber++;
        long key = ProtocolKeys.XorKeys[_recvNumber % ProtocolKeys.KeyCount];

        int wSize = PacketHeader.ReadSize(buf);
        Rc4.Apply(ProtocolKeys.Rc4Key, buf, wSize);
        PacketHeader.WriteSize(buf, (ushort)wSize); // RC4 scrambled bytes 0-1; restore the known size

        PacketCrypto.DecryptHeader(buf, key);

        if (PacketHeader.ReadNumber(buf) != _recvNumber)
            return false;

        return PacketCrypto.DecryptBody(buf, wSize, key);
    }
}
