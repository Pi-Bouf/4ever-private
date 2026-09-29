// TItem.tcd (CTChart::InitTITEMTEMP): WORD id, BYTE type, BYTE kind, WORD attr, CString name, WORD useValue, 101-byte tail.
namespace ClassicPort;

public class ItemRec
{
    public ushort Id; public byte Type, Kind; public ushort UseValue;
    public string Name = "";
    public byte[] Raw = [];
    public int TailOfs;     // offset of the 101-byte tail inside Raw
}

public static class TItem
{
    public const int Tail = 101;

    public static List<ItemRec> Load(string path)
    {
        var b = File.ReadAllBytes(path);
        int n = BitConverter.ToUInt16(b, 0), p = 2;
        var list = new List<ItemRec>();
        for (int i = 0; i < n; i++)
        {
            int s = p;
            var it = new ItemRec { Id = BitConverter.ToUInt16(b, p), Type = b[p + 2], Kind = b[p + 3] };
            p += 6;
            int l = b[p++]; if (l == 0xFF) { l = BitConverter.ToUInt16(b, p); p += 2; }
            it.Name = TMon.Cp949.GetString(b, p, l); p += l;
            it.UseValue = BitConverter.ToUInt16(b, p); p += 2;
            it.TailOfs = p - s;
            p += Tail;
            it.Raw = b[s..p];
            list.Add(it);
        }
        if (p != b.Length) throw new InvalidDataException(path);
        return list;
    }

    public static void Save(string path, IEnumerable<ItemRec> items)
    {
        var l = items.ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)l.Count);
        foreach (var it in l) w.Write(it.Raw);
    }
}
