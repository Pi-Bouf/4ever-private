using System.Security.Cryptography;

namespace TLogin.Protocol.Crypto;

/// <summary>
/// Wire-crypto constants taken verbatim from THIS repo's <c>Servers/TNetLib/Session.cpp</c>
/// (the Araz-4ever build — its keys differ from the 4retro fork).
/// </summary>
public static class ProtocolKeys
{
    public const int KeyCount = 7;

    /// <summary>The <c>g_4skey[KEY_COUNT]</c> XOR key table, indexed by <c>dwNumber % 7</c>.</summary>
    public static readonly long[] XorKeys =
    {
        unchecked((long)0x5193817ae183aceeUL),
        unchecked((long)0x3891aeacbed18eadUL),
        unchecked((long)0x549aeced13de13a1UL),
        unchecked((long)0x09aeb1498c1eade9UL),
        unchecked((long)0x19861acea1720ae7UL),
        unchecked((long)0x0139aecea89541a2UL),
        unchecked((long)0x6b97253c5fbb8b06UL),
    };

    /// <summary>
    /// Raw bytes of <c>g_strSecretKey</c>. The literal is
    /// <c>"A5$$8AFS13A1::-11#!..'\xA7" + "19716AC&amp;\xA7" + "/D1;;1#"</c> (two 0xA7 section-sign
    /// bytes). The project is built MultiByte (<c>sizeof(TCHAR)==1</c>), and the RC4 key is hashed
    /// from <c>(GetLength()+1)*sizeof(TCHAR)</c> bytes — i.e. these 39 bytes plus a NUL terminator.
    /// </summary>
    public static readonly byte[] SecretKeyBytes = BuildSecretKey();

    /// <summary>RC4 key = MD5 of the secret key bytes including the NUL terminator (CryptDeriveKey(CALG_RC4) over a CALG_MD5 hash).</summary>
    public static byte[] Rc4Key { get; } = MD5.HashData(SecretKeyBytes);

    private static byte[] BuildSecretKey()
    {
        ReadOnlySpan<byte> part1 = "A5$$8AFS13A1::-11#!..'"u8;
        ReadOnlySpan<byte> part2 = "19716AC&"u8;
        ReadOnlySpan<byte> part3 = "/D1;;1#"u8;

        var key = new byte[part1.Length + 1 + part2.Length + 1 + part3.Length + 1];
        int i = 0;
        part1.CopyTo(key.AsSpan(i)); i += part1.Length;
        key[i++] = 0xA7;
        part2.CopyTo(key.AsSpan(i)); i += part2.Length;
        key[i++] = 0xA7;
        part3.CopyTo(key.AsSpan(i)); i += part3.Length;
        key[i++] = 0x00; // NUL terminator hashed by the original (length includes the terminator)
        return key;
    }
}
