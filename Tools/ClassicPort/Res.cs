// Tachyon resource catalogs (Index/*.IDX) + record parsers for the data files they point at.
// Formats mirror Lib/Own/Engine Lib/Engine Lib/TachyonRes.cpp (Load*).
using System.IO.Compression;
using System.Text;

namespace ClassicPort;

public enum Kind { Tex, Ani, Mesh, Obj, Sfx, Media }

public record IdxEntry(uint Id, int File, uint Pos);

/// <summary>An IDX catalog: int nFiles, int nEntries, nFiles x (int len + ansi), nEntries x (id, fileIdx, pos).</summary>
public class Idx
{
    public List<string> Files = new();
    public List<IdxEntry> Entries = new();
    public Dictionary<uint, IdxEntry> ById = new();

    public static Idx Load(string path)
    {
        var r = new BinaryReader(File.OpenRead(path));
        var idx = new Idx();
        int nFiles = r.ReadInt32(), nTotal = r.ReadInt32();
        for (int i = 0; i < nFiles; i++)
        {
            int l = r.ReadInt32();
            idx.Files.Add(l > 0 ? Encoding.Latin1.GetString(r.ReadBytes(l)) : "");
        }
        for (int i = 0; i < nTotal; i++)
        {
            var e = new IdxEntry(r.ReadUInt32(), r.ReadInt32(), r.ReadUInt32());
            idx.Entries.Add(e);
            idx.ById.TryAdd(e.Id, e);
        }
        if (r.BaseStream.Position != r.BaseStream.Length)
            throw new InvalidDataException($"{path}: {r.BaseStream.Length - r.BaseStream.Position} trailing bytes");
        r.Dispose();
        return idx;
    }

    public void Save(string path)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write(Files.Count);
        w.Write(Entries.Count);
        foreach (var f in Files)
        {
            var b = Encoding.Latin1.GetBytes(f);
            w.Write(b.Length);
            w.Write(b);
        }
        foreach (var e in Entries)
        {
            w.Write(e.Id);
            w.Write(e.File);
            w.Write(e.Pos);
        }
    }
}

/// <summary>One resource record: its bytes in the SOURCE layout plus the IDs it references.</summary>
public class Rec
{
    public Kind Kind;
    public uint Id;
    public byte[] Raw = [];            // uncompressed record bytes
    public List<(Kind, uint)> Refs = new();
}

/// <summary>A client install (Game/ or CLASSIC/) with lazy access to its records.</summary>
public class Client
{
    public readonly string Root;
    public readonly bool IsClassic;    // Classic: raw .TTX, variant .TOB layout
    bool ChunkedTex => !IsClassic;
    public readonly Dictionary<Kind, Idx> Idx = new();
    readonly Dictionary<string, byte[]> fileCache = new();
    readonly Dictionary<(string, uint), (byte[] raw, int len)> chunkCache = new();

    public Client(string root, string texIdx, bool isClassic)
    {
        Root = root;
        IsClassic = isClassic;
        Idx[Kind.Tex] = ClassicPort.Idx.Load(Path.Combine(root, "Index", texIdx));
        Idx[Kind.Ani] = ClassicPort.Idx.Load(Path.Combine(root, "Index", "TClientA.IDX"));
        Idx[Kind.Mesh] = ClassicPort.Idx.Load(Path.Combine(root, "Index", "TClientM.IDX"));
        Idx[Kind.Obj] = ClassicPort.Idx.Load(Path.Combine(root, "Index", "TClientO.IDX"));
        Idx[Kind.Sfx] = ClassicPort.Idx.Load(Path.Combine(root, "Index", "TClientX.IDX"));
        Idx[Kind.Media] = ClassicPort.Idx.Load(Path.Combine(root, "Index", "TClientW.IDX"));
    }

    public bool Has(Kind k, uint id) => Idx[k].ById.ContainsKey(id);

    public string DataPath(Kind k, IdxEntry e) => Path.Combine(Root, "Data", Idx[k].Files[e.File]);

    byte[] FileBytes(string path)
    {
        if (!fileCache.TryGetValue(path, out var b))
            fileCache[path] = b = File.ReadAllBytes(path);
        return b;
    }

    /// <summary>Record bytes. Raw formats are sliced up to the next indexed record of that file.</summary>
    public Rec? Get(Kind k, uint id)
    {
        if (!Idx[k].ById.TryGetValue(id, out var e)) return null;
        if (k == Kind.Media) return new Rec { Kind = k, Id = id };

        string path = DataPath(k, e);
        byte[] file = FileBytes(path);
        byte[] raw;
        bool chunked = k == Kind.Sfx || (k == Kind.Tex && ChunkedTex);
        if (chunked)
            raw = Unchunk(file, (int)e.Pos, out _);
        else if (k == Kind.Tex)   // Classic raw record: only the header is needed here (refs); blobs via TexConvert
            raw = file.AsSpan((int)e.Pos, TexConv.HeaderLength(file, (int)e.Pos)).ToArray();
        else
        {
            uint end = (uint)file.Length;
            foreach (var o in Idx[k].Entries)
                if (o.File == e.File && o.Pos > e.Pos && o.Pos < end) end = o.Pos;
            raw = file.AsSpan((int)e.Pos, (int)(end - e.Pos)).ToArray();
        }
        var rec = new Rec { Kind = k, Id = id, Raw = raw };
        Parse(rec, IsClassic);
        return rec;
    }

    public static byte[] Unchunk(byte[] file, int pos, out int chunkLen)
    {
        int orig = BitConverter.ToInt32(file, pos), len = BitConverter.ToInt32(file, pos + 4);
        chunkLen = 8 + len;
        using var z = new ZLibStream(new MemoryStream(file, pos + 8, len), CompressionMode.Decompress);
        var outp = new byte[orig];
        z.ReadExactly(outp);
        return outp;
    }

    public static byte[] Chunk(byte[] raw)
    {
        var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw);
        var comp = ms.ToArray();
        var o = new byte[8 + comp.Length];
        BitConverter.GetBytes(raw.Length).CopyTo(o, 0);
        BitConverter.GetBytes(comp.Length).CopyTo(o, 4);
        comp.CopyTo(o, 8);
        return o;
    }

    /// <summary>Length of one raw TEXTURESET record (LoadTEX field order).</summary>
    public static int RawTexLength(byte[] b, int p0)
    {
        int p = p0;
        int n = BitConverter.ToInt32(b, p); p += 4 + 4 * n;
        n = BitConverter.ToInt32(b, p); p += 4 + 24 * n;
        p += 12 + 2 + 4;                                  // tick, mipfilter, mipbias, opt, fmt, orgsize
        int size = BitConverter.ToInt32(b, p); p += 4 + size;
        return p - p0;
    }

    static void Parse(Rec r, bool classic)
    {
        var s = new BinaryReader(new MemoryStream(r.Raw));
        switch (r.Kind)
        {
            case Kind.Tex:
            {
                int n = s.ReadInt32();
                for (int i = 0; i < n; i++) r.Refs.Add((Kind.Tex, s.ReadUInt32()));
                break;
            }
            case Kind.Ani:
            {
                int n = s.ReadInt32();
                for (int i = 0; i < n; i++) { r.Refs.Add((Kind.Ani, s.ReadUInt32())); s.ReadByte(); }
                break;
            }
            case Kind.Sfx:
            {
                int n = s.ReadInt32(); s.BaseStream.Seek(8 * n, SeekOrigin.Current);
                n = s.ReadInt32();
                for (int i = 0; i < n; i++) r.Refs.Add((Kind.Sfx, s.ReadUInt32()));
                s.ReadByte();
                byte src = s.ReadByte();
                uint srcId = s.ReadUInt32();
                r.Refs.Add((src switch { 0 => Kind.Tex, 1 => Kind.Obj, _ => Kind.Sfx }, srcId));
                r.Refs.Add((Kind.Ani, s.ReadUInt32()));      // m_pANISRC
                s.BaseStream.Seek(16, SeekOrigin.Current);   // life, follow, sizeX, sizeY
                n = s.ReadInt32(); s.BaseStream.Seek(12 * n, SeekOrigin.Current);
                s.BaseStream.Seek(20, SeekOrigin.Current);   // act, ani, 3 func ids
                r.Refs.Add((Kind.Ani, s.ReadUInt32()));      // m_pSprayANI
                break;
            }
            case Kind.Obj: ParseObj(s, r.Refs, out int len, classic); r.Raw = r.Raw[..len]; break;
        }
        r.Refs.RemoveAll(x => x.Item2 == 0);
    }

    static void SfxInst(BinaryReader s, List<(Kind, uint)> refs, bool withKey)
    {
        if (withKey) s.ReadUInt32();
        refs.Add((Kind.Sfx, s.ReadUInt32()));
        s.BaseStream.Seek(24 + 4 + 1 + 4 + 4 + 4, SeekOrigin.Current);
    }

    static void SndInst(BinaryReader s, List<(Kind, uint)> refs)
    {
        s.ReadUInt32();
        refs.Add((Kind.Media, s.ReadUInt32()));
        s.BaseStream.Seek(12, SeekOrigin.Current);
    }

    /// <summary>LoadOBJ field order. Also yields the exact record length.</summary>
    public static void ParseObj(BinaryReader s, List<(Kind, uint)> refs, out int len, bool classic = false)
    {
        if (!classic) s.ReadByte();                      // m_bPivotCount (Classic: none here)
        int n = s.ReadInt32(); s.BaseStream.Seek(8 * n, SeekOrigin.Current);
        n = s.ReadInt32(); for (int i = 0; i < n; i++) SfxInst(s, refs, true);
        n = s.ReadInt32(); for (int i = 0; i < n; i++) SndInst(s, refs);
        n = s.ReadInt32();
        for (int i = 0; i < n; i++) { s.ReadUInt32(); s.ReadByte(); int sz = s.ReadInt32(); s.BaseStream.Seek(sz, SeekOrigin.Current); }
        n = s.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            s.ReadUInt32();
            int m = s.ReadInt32(); for (int j = 0; j < m; j++) SfxInst(s, refs, true);
            m = s.ReadInt32(); for (int j = 0; j < m; j++) SndInst(s, refs);
            if (classic) s.ReadByte();                   // Classic: extra per-action byte
            m = s.ReadInt32();
            for (int j = 0; j < m; j++)
            {
                s.ReadUInt32();
                refs.Add((Kind.Ani, s.ReadUInt32()));
                int k = s.ReadInt32(); for (int q = 0; q < k; q++) SfxInst(s, refs, true);
                k = s.ReadInt32(); for (int q = 0; q < k; q++) SndInst(s, refs);
            }
        }
        n = s.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            s.ReadUInt32();
            int ncl = s.ReadInt32();
            for (int j = 0; j < ncl; j++)
            {
                s.ReadUInt32();
                int nm = s.ReadInt32();
                for (int k = 0; k < nm; k++)
                {
                    s.ReadUInt32();
                    refs.Add((Kind.Mesh, s.ReadUInt32()));
                    int nt = s.ReadInt32();
                    for (int t = 0; t < nt; t++)
                    {
                        refs.Add((Kind.Tex, s.ReadUInt32()));
                        refs.Add((Kind.Tex, s.ReadUInt32()));
                        s.BaseStream.Seek(2 + 7 * 4 + 4 + 4 + 4 + 4 + 1, SeekOrigin.Current);   // types, 7 dwords, intensity, 4 flags, dirlight, ambient, pad
                    }
                }
            }
        }
        len = (int)s.BaseStream.Position;
    }
}
