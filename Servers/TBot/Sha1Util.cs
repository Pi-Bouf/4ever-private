using System.Security.Cryptography;
using System.Text;

namespace TBot;

public static class Sha1Util
{
    /// <summary>
    /// Uppercase SHA-1 hex of the password, matching what the 4Story client sends in CS_LOGIN_REQ and what
    /// the seed migration stores: <c>UPPER(CONVERT(VARCHAR(40), HASHBYTES('SHA1', pw), 2))</c>. ASCII bytes.
    /// </summary>
    public static string UpperHex(string password)
    {
        byte[] hash = SHA1.HashData(Encoding.ASCII.GetBytes(password));
        return Convert.ToHexString(hash); // already uppercase, no separators
    }
}
