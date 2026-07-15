namespace TMap.Protocol.Crypto;

/// <summary>
/// Plain RC4 stream cipher (KSA + PRGA), symmetric so the same call encrypts and decrypts.
/// A fresh key schedule is built on every call — there is no state carried between packets,
/// matching the per-packet <c>EncryptBuffer(CALG_RC4, ...)</c> usage in the C++ TNetLib.
/// </summary>
public static class Rc4
{
    public static void Apply(byte[] key, byte[] buffer, int length)
    {
        var s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        int a = 0, b = 0;
        for (int n = 0; n < length; n++)
        {
            a = (a + 1) & 0xFF;
            b = (b + s[a]) & 0xFF;
            (s[a], s[b]) = (s[b], s[a]);
            byte k = s[(s[a] + s[b]) & 0xFF];
            buffer[n] ^= k;
        }
    }
}
