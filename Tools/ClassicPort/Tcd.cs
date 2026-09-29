// .tcd tables touched by the port. Layouts follow CTChart::InitTPET / InitTMONTEMP.
using System.Text;

namespace ClassicPort;

public record Mount(ushort Id, ushort MonId, ushort SaddleMonId, ushort Icon, float Scale);

public static class TMount
{
    /// <summary>Ours: 4 WORDs per row. Classic: 4 WORDs + float scale.</summary>
    public static List<Mount> Load(string path, bool withScale)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        int n = r.ReadUInt16();
        var list = new List<Mount>();
        for (int i = 0; i < n; i++)
            list.Add(new Mount(r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), withScale ? r.ReadSingle() : 1f));
        if (r.BaseStream.Position != r.BaseStream.Length) throw new InvalidDataException(path);
        return list;
    }

    public static void Save(string path, IEnumerable<Mount> rows, bool withScale)
    {
        var list = rows.ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)list.Count);
        foreach (var m in list)
        {
            w.Write(m.Id); w.Write(m.MonId); w.Write(m.SaddleMonId); w.Write(m.Icon);
            if (withScale) w.Write(m.Scale);
        }
    }
}

public class MonRec
{
    public ushort Id;
    public string Name = "";
    public byte[] Raw = [];      // whole record
    public int TailOfs;          // offset of the fixed tail inside Raw
    public uint Obj => BitConverter.ToUInt32(Raw, TailOfs + 12);
}

public static class TMon
{
    public const int OurTail = 125;
    public const int ClassicTail = 128;
    public static readonly Encoding Cp949;

    static TMon()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp949 = Encoding.GetEncoding(949);
    }

    public static List<MonRec> Load(string path, int tail) => Parse(File.ReadAllBytes(path), tail, path);

    /// <summary>One bare record (no count prefix).</summary>
    public static MonRec ParseOne(byte[] raw, int tail) => Parse([1, 0, .. raw], tail, "record")[0];

    static List<MonRec> Parse(byte[] b, int tail, string path)
    {
        int n = BitConverter.ToUInt16(b, 0), p = 2;
        var list = new List<MonRec>();
        for (int i = 0; i < n; i++)
        {
            int start = p;
            ushort id = BitConverter.ToUInt16(b, p); p += 4;
            string name = "";
            for (int k = 0; k < 2; k++)
            {
                int l = b[p++];
                if (l == 0xFF) { l = BitConverter.ToUInt16(b, p); p += 2; }
                if (k == 1) name = Cp949.GetString(b, p, l);
                p += l;
            }
            int tailOfs = p - start;
            p += tail;
            list.Add(new MonRec { Id = id, Name = name, Raw = b[start..p], TailOfs = tailOfs });
        }
        if (p != b.Length) throw new InvalidDataException($"{path}: {b.Length - p} trailing bytes");
        return list;
    }

    // Tail offset of the equip-item WORD array (after fLB, fLOST, fAB, dwOBJ).
    const int ItemsOfs = 16, OurItems = 19;

    /// <summary>
    /// Classic tail = ours + a 20th equip-item WORD (appended to the item array) + 1 trailing byte.
    /// Returns null warning text when the dropped bytes are non-zero.
    /// </summary>
    public static MonRec FromClassic(MonRec c, out string? warn)
    {
        int t = c.TailOfs, cut = t + ItemsOfs + OurItems * 2;
        warn = null;
        if (c.Raw[cut] != 0 || c.Raw[cut + 1] != 0) warn = $"monster {c.Id}: 20th equip slot = {BitConverter.ToUInt16(c.Raw, cut)} dropped";
        if (c.Raw[^1] != 0) warn = (warn == null ? "" : warn + "; ") + $"monster {c.Id}: trailing byte {c.Raw[^1]} dropped";
        byte[] raw = [.. c.Raw[..cut], .. c.Raw[(cut + 2)..^1]];
        return new MonRec { Id = c.Id, Name = c.Name, Raw = raw, TailOfs = t };
    }

    public static void Save(string path, IEnumerable<MonRec> recs)
    {
        var list = recs.ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)list.Count);
        foreach (var r in list) w.Write(r.Raw);
    }
}
