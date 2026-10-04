// Ports the costumes that exist in the official Gameforge FR client (OFFICIAL/) but not in ours (Game/):
//   meshes + textures (all 3 detail levels) appended to our packs, the costume CLK/CLI/mesh entries spliced into
//   the 6 player objects (Character.TOB rebuilt in place), TItem/TItemVisual rows, and the icons as own
//   images (Data\Img\Custom\<id>.png, icon index >= 60000, auto-registered by TClientWnd).
// OFFICIAL uses our exact formats (no Classic-style scrambling), so records are copied byte for byte.
using System.Text;
using ClassicPort;

public class CostumePort
{
    // OFFICIAL item -> our English name. Visuals and icons follow from the item.
    public static readonly (ushort id, string name)[] Items =
    [
        (12063, "Sublime Outfit"),
        (12064, "Snorkel"),
        (12065, "Seasoned Beer Drinker"),
        (12066, "Vampire"),
        (12067, "Christmas Panda"),
        (12080, "Seasoned Beer Drinker Hat"),
        (12081, "Vampire Mask"),
    ];
    const ushort FirstIcon = 60100;                    // own images 60000-60016 / 61000+ are taken
    static readonly uint[] PlayerObjs = [0xF56A4, 0xF56A5, 0xF56A6, 0xF56A7, 0xF56A8, 0xF56A9];

    readonly string game; readonly Client G, O; readonly TextWriter log;
    public CostumePort(string game, Client g, Client o, TextWriter log) { this.game = game; G = g; O = o; this.log = log; }

    string D(string rel) => Path.Combine(game, "Data", rel);
    string I(string name) => Path.Combine(game, "Index", name);
    string OD(string rel) => Path.Combine(O.Root, "Data", rel);
    string OI(string name) => Path.Combine(O.Root, "Index", name);

    public void Run(bool apply)
    {
        // ---- items / visuals -------------------------------------------------------------------------
        var oItems = TItemTcd.Load(Path.Combine(O.Root, "Tcd", "TItem.tcd"));
        var gItems = TItemTcd.Load(Path.Combine(game, "Tcd", "TItem.tcd"));
        var oVis = TVisualTcd.Load(Path.Combine(O.Root, "Tcd", "TItemVisual.tcd"));
        var gVis = TVisualTcd.Load(Path.Combine(game, "Tcd", "TItemVisual.tcd"));
        var gItemIds = gItems.Select(i => i.Id).ToHashSet();
        var gVisIds = gVis.Select(v => v.Id).ToHashSet();

        var newItems = new List<TItemRec>();
        var newVis = new List<byte[]>();
        var icons = new List<(ushort srcIcon, ushort newIcon)>();
        foreach (var (id, name) in Items)
        {
            if (gItemIds.Contains(id)) { log.WriteLine($"item {id} already in Game/ - skipped"); continue; }
            var it = oItems.First(i => i.Id == id).WithName(name);
            newItems.Add(it);
            ushort visId = it.Visual0;
            if (gVisIds.Contains(visId) || newVis.Any(v => BitConverter.ToUInt16(v, 0) == visId)) continue;
            var v = oVis.First(x => x.Id == visId).Raw.ToArray();
            ushort src = BitConverter.ToUInt16(v, TVisualTcd.IconOfs);
            var known = icons.FirstOrDefault(x => x.srcIcon == src);
            ushort ni = known.newIcon != 0 ? known.newIcon : (ushort)(FirstIcon + icons.Count);
            if (known.newIcon == 0) icons.Add((src, ni));
            BitConverter.GetBytes(ni).CopyTo(v, TVisualTcd.IconOfs);
            newVis.Add(v);
            log.WriteLine($"item {id} {name}: visual {visId}, icon {src} -> {ni}");
        }

        // ---- object cloth entries --------------------------------------------------------------------
        var tobIdx = G.Idx[Kind.Obj];
        var newObj = new Dictionary<uint, byte[]>();
        var needMesh = new SortedSet<uint>(); var needTex = new SortedSet<uint>();
        foreach (var oid in PlayerObjs)
        {
            byte[] ours = G.Get(Kind.Obj, oid)!.Raw, theirs = O.Get(Kind.Obj, oid)!.Raw;
            var gc = ObjCloth.Read(ours); var oc = ObjCloth.Read(theirs);
            int added = 0;
            foreach (var ok in oc.Kinds)
                foreach (var ocl in ok.Cloths)
                    foreach (var m in ocl.Meshes)
                    {
                        // Only the ported costumes' meshes: anything else differing between the clients is left alone.
                        if (!newVis.Any(v => TVisualTcd.Uses(v, ok.Id, ocl.Id, m.Key))) continue;
                        var gk = gc.Kinds.FirstOrDefault(k => k.Id == ok.Id);
                        if (gk == null) gc.Kinds.Add(gk = new ObjCloth.ClKind { Id = ok.Id });
                        var gcl = gk.Cloths.FirstOrDefault(c => c.Id == ocl.Id);
                        if (gcl == null) gk.Cloths.Add(gcl = new ObjCloth.Cloth { Id = ocl.Id });
                        if (gcl.Meshes.Any(x => x.Key == m.Key)) continue;
                        gcl.Meshes.Add(m); added++;
                        if (!G.Has(Kind.Mesh, m.MeshId)) needMesh.Add(m.MeshId);
                        foreach (var t in m.TexIds) if (t != 0) AddTex(t, needTex);
                    }
            if (added > 0) { newObj[oid] = gc.Write(ours); log.WriteLine($"obj {oid:X8}: +{added} mesh entries ({ours.Length} -> {newObj[oid].Length} bytes)"); }
        }
        log.WriteLine($"meshes to copy: {needMesh.Count}, textures to copy: {needTex.Count}");
        foreach (var m in needMesh) if (!O.Has(Kind.Mesh, m)) throw new InvalidDataException($"mesh {m:X8} missing in OFFICIAL");

        if (!apply) { log.WriteLine("dry run - nothing written (use: port --apply)"); return; }
        var before = Snapshot(G);

        // ---- write packs -----------------------------------------------------------------------------
        // Character.TOB: replace the player objects in place, shift everything after them.
        if (newObj.Count > 0)
        {
            int f = FileIndex(tobIdx, @"OBJ\Character.TOB");
            byte[] tob = File.ReadAllBytes(D(@"OBJ\Character.TOB"));
            var idAt = tobIdx.Entries.Where(e => e.File == f).GroupBy(e => e.Pos).ToDictionary(g => g.Key, g => g.First().Id);
            var outp = new MemoryStream(); var move = new Dictionary<uint, uint>();
            var rd = new BinaryReader(new MemoryStream(tob));
            while (rd.BaseStream.Position < tob.Length)
            {
                uint p = (uint)rd.BaseStream.Position;
                Client.ParseObj(rd, new(), out _);
                int len = (int)(rd.BaseStream.Position - p);
                move[p] = (uint)outp.Position;
                if (idAt.TryGetValue(p, out var id) && newObj.TryGetValue(id, out var rec)) outp.Write(rec);
                else outp.Write(tob, (int)p, len);
            }
            tobIdx.Entries = tobIdx.Entries.Select(e => e.File == f ? e with { Pos = move[e.Pos] } : e).ToList();
            File.WriteAllBytes(D(@"OBJ\Character.TOB"), outp.ToArray());
            tobIdx.Save(I("TClientO.IDX"));
        }

        // Meshes: raw records, appended to the same-named pack.
        var mIdx = G.Idx[Kind.Mesh];
        foreach (var grp in needMesh.GroupBy(m => O.Idx[Kind.Mesh].Files[O.Idx[Kind.Mesh].ById[m].File]))
        {
            int f = FileIndex(mIdx, grp.Key);
            using var fs = new FileStream(D(grp.Key), FileMode.Append);
            foreach (var m in grp)
            {
                var raw = O.Get(Kind.Mesh, m)!.Raw;
                mIdx.Entries.Add(new IdxEntry(m, f, (uint)fs.Position)); fs.Write(raw);
            }
        }
        mIdx.Save(I("TClientM.IDX"));

        // Textures: zlib chunks, per detail level.
        for (int lvl = 0; lvl < 3; lvl++)
        {
            var oi = Idx.Load(OI($"{lvl}_TClientS.IDX")); var gi = Idx.Load(I($"{lvl}_TClientS.IDX"));
            foreach (var grp in needTex.GroupBy(t => oi.Files[oi.ById[t].File]))
            {
                int f = FileIndex(gi, grp.Key);
                byte[] src = File.ReadAllBytes(OD(grp.Key));
                using var fs = new FileStream(D(grp.Key), FileMode.Append);
                foreach (var t in grp)
                {
                    uint pos = oi.ById[t].Pos; int len = 8 + BitConverter.ToInt32(src, (int)pos + 4);
                    gi.Entries.Add(new IdxEntry(t, f, (uint)fs.Position)); fs.Write(src, (int)pos, len);
                }
            }
            gi.Save(I($"{lvl}_TClientS.IDX"));
        }

        // ---- tcds + icons ----------------------------------------------------------------------------
        TItemTcd.Save(Path.Combine(game, "Tcd", "TItem.tcd"), gItems.Concat(newItems).OrderBy(i => i.Id));
        TVisualTcd.Save(Path.Combine(game, "Tcd", "TItemVisual.tcd"),
            gVis.Select(v => v.Raw).Concat(newVis).OrderBy(v => BitConverter.ToUInt16(v, 0)));
        string iconSrc = Path.Combine(AppContext.BaseDirectory, "icons");
        foreach (var (src, ni) in icons)
            File.Copy(Path.Combine(iconSrc, $"{src}.png"), D($@"Img\Custom\{ni}.png"), overwrite: true);
        log.WriteLine($"written: Character.TOB, {needMesh.Count} meshes, {needTex.Count}x3 textures, {newItems.Count} items, {newVis.Count} visuals, {icons.Count} icons");

        // ---- read back through the new indexes ---------------------------------------------------------
        var after = Snapshot(new Client(game, "2_TClientS.IDX", false));
        var expect = new Dictionary<string, byte[]>(before);
        foreach (var (id, rec) in newObj) expect[$"Obj:{id:X8}"] = rec;
        foreach (var m in needMesh) expect[$"Mesh:{m:X8}"] = O.Get(Kind.Mesh, m)!.Raw;
        foreach (var t in needTex) expect[$"Tex:{t:X8}"] = O.Get(Kind.Tex, t)!.Raw;
        int bad = expect.Count(kv => !after.TryGetValue(kv.Key, out var a) || !a.AsSpan().SequenceEqual(kv.Value)) + Math.Abs(after.Count - expect.Count);
        log.WriteLine($"read-back: {after.Count} records, {bad} unexpected");
        if (bad != 0) throw new InvalidDataException("pack rewrite changed records - restore Game/ from git");
    }

    static Dictionary<string, byte[]> Snapshot(Client c)
    {
        var snap = new Dictionary<string, byte[]>();
        foreach (var k in new[] { Kind.Obj, Kind.Mesh, Kind.Tex })
            foreach (var id in c.Idx[k].ById.Keys)
                snap[$"{k}:{id:X8}"] = c.Get(k, id)!.Raw;
        return snap;
    }

    void AddTex(uint t, SortedSet<uint> need)
    {
        if (G.Has(Kind.Tex, t) || !need.Add(t)) return;
        var rec = O.Get(Kind.Tex, t) ?? throw new InvalidDataException($"texture {t:X8} missing in OFFICIAL");
        foreach (var (_, r) in rec.Refs) AddTex(r, need);
    }

    static int FileIndex(Idx idx, string rel)
    {
        int f = idx.Files.FindIndex(x => x.Equals(rel, StringComparison.OrdinalIgnoreCase));
        if (f < 0) throw new InvalidDataException($"{rel} is not a pack of ours");
        return f;
    }
}

public class TItemRec
{
    public ushort Id; public byte[] Raw = []; public int NameOfs, NameLen;   // NameLen includes the length prefix
    public ushort Visual0 => BitConverter.ToUInt16(Raw, NameOfs + NameLen + 2 + 63);
    public TItemRec WithName(string name)
    {
        var b = TItemTcd.Cp949.GetBytes(name);
        if (b.Length >= 0xFF) throw new ArgumentException(name);
        byte[] raw = [.. Raw[..NameOfs], (byte)b.Length, .. b, .. Raw[(NameOfs + NameLen)..]];
        return new TItemRec { Id = Id, Raw = raw, NameOfs = NameOfs, NameLen = 1 + b.Length };
    }
}

/// <summary>TItem.tcd (CTChart::InitTITEMTEMP): WORD id, BYTE type, BYTE kind, WORD attr, CString, WORD useValue, 101-byte tail.</summary>
public static class TItemTcd
{
    public static readonly Encoding Cp949;
    static TItemTcd() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Cp949 = Encoding.GetEncoding(949); }

    public static List<TItemRec> Load(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int n = BitConverter.ToUInt16(b, 0), p = 2;
        var list = new List<TItemRec>();
        for (int i = 0; i < n; i++)
        {
            int start = p; p += 6;
            int l = b[p], nl = 1;
            if (l == 0xFF) { l = BitConverter.ToUInt16(b, p + 1); nl = 3; }
            p += nl + l + 2 + 101;
            list.Add(new TItemRec { Id = BitConverter.ToUInt16(b, start), Raw = b[start..p], NameOfs = 6, NameLen = nl + l });
        }
        if (p != b.Length) throw new InvalidDataException($"{path}: {b.Length - p} trailing bytes");
        return list;
    }

    public static void Save(string path, IEnumerable<TItemRec> recs)
    {
        var list = recs.ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)list.Count);
        foreach (var r in list) w.Write(r.Raw);
    }
}

/// <summary>TItemVisual.tcd (CTChart::InitTITEMVISUAL): 63-byte rows.</summary>
public static class TVisualTcd
{
    public const int Row = 63, IconOfs = 34;
    public record Vis(ushort Id, byte[] Raw);

    public static List<Vis> Load(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int n = BitConverter.ToUInt16(b, 0);
        if (2 + n * Row != b.Length) throw new InvalidDataException(path);
        return Enumerable.Range(0, n).Select(i => new Vis(BitConverter.ToUInt16(b, 2 + i * Row), b[(2 + i * Row)..(2 + (i + 1) * Row)])).ToList();
    }

    /// <summary>True when the visual dresses (clk, cli) with this mesh key (normal or battle mesh).</summary>
    public static bool Uses(byte[] v, uint clk, uint cli, uint key) =>
        BitConverter.ToUInt32(v, 10) == clk && BitConverter.ToUInt32(v, 14) == cli &&
        (BitConverter.ToUInt32(v, 18) == key || BitConverter.ToUInt32(v, 22) == key);

    public static void Save(string path, IEnumerable<byte[]> rows)
    {
        var list = rows.ToList();
        using var w = new BinaryWriter(File.Create(path));
        w.Write((ushort)list.Count);
        foreach (var r in list) w.Write(r);
    }
}
