// Texture records (LoadTEX layout).
//   ours:    chunk{ header, orgSize, size, data }            one file per detail level (0_/1_/2_)
//   Classic: raw  { header, 4 x (u32 filePos, u32 0), then blobs { orgSize, size, data } at those positions }
// header = nTex, nTex x id, nKey, nKey x 24B UVKEY, totalTick, mipFilter, mipBias, opt(1), fmt(1)
// data   = zlib-compressed image file (DDS/...) handed to D3DX.
using System.IO.Compression;

namespace ClassicPort;

public static class TexConv
{
    public static int HeaderLength(byte[] b, int p0)
    {
        int p = p0;
        int n = BitConverter.ToInt32(b, p); p += 4 + 4 * n;
        n = BitConverter.ToInt32(b, p); p += 4 + 24 * n;
        return p + 12 + 2 - p0;
    }

    /// <summary>Parse a Classic raw record at <paramref name="pos"/> of its file.</summary>
    public static (byte[] header, uint[] slots, int length) ReadClassic(byte[] file, int pos)
    {
        int h = HeaderLength(file, pos);
        var slots = new uint[4];
        for (int i = 0; i < 4; i++) slots[i] = BitConverter.ToUInt32(file, pos + h + 8 * i);
        // record ends after the blob that ends last
        int end = pos + h + 32;
        foreach (var s in slots.Take(3).Where(s => s != 0).Distinct())   // slot 3 is not a file offset
            end = Math.Max(end, (int)s + 8 + BitConverter.ToInt32(file, (int)s + 4));
        return (file[pos..(pos + h)], slots, end - pos);
    }

    public static byte[] Blob(byte[] file, uint slot) => file[(int)slot..((int)slot + 8 + BitConverter.ToInt32(file, (int)slot + 4))];

    /// <summary>Width x height of a DDS image inside a blob (orgSize, size, zlib data).</summary>
    public static string Dims(byte[] blob, int ofs = 0)
    {
        int size = BitConverter.ToInt32(blob, ofs + 4);
        if (size == 0) return "empty";
        try
        {
            using var z = new ZLibStream(new MemoryStream(blob, ofs + 8, size), CompressionMode.Decompress);
            var head = new byte[32];
            z.ReadAtLeast(head, 32, false);
            string magic = System.Text.Encoding.ASCII.GetString(head, 0, 4);
            if (magic == "DDS ") return $"DDS {BitConverter.ToInt32(head, 16)}x{BitConverter.ToInt32(head, 12)}";
            return $"{Convert.ToHexString(head, 0, 8)}";
        }
        catch (Exception e) { return "ERR " + e.Message; }
    }
}

public static class TexConvert
{
    /// <summary>Classic raw record -> our (uncompressed) record using the full-resolution slot.</summary>
    public static byte[] FromClassic(byte[] file, int pos, out int length)
    {
        var (hdr, slots, len) = TexConv.ReadClassic(file, pos);
        length = len;
        byte[] blob = slots[0] != 0 ? TexConv.Blob(file, slots[0]) : new byte[8];
        return [.. hdr, .. blob];
    }
}
