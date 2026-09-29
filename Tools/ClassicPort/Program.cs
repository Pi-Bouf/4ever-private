// ClassicPort — pulls the mounts that exist in the 4Classic client (CLASSIC/) but not in ours (Game/).
//   dotnet run -- analyze      report what's missing + ID collisions, change nothing
using ClassicPort;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
string game = Path.Combine(repo, "Game"), classic = Path.Combine(repo, "CLASSIC");
string cmd = args.FirstOrDefault() ?? "analyze";

var ourMounts = TMount.Load(Path.Combine(game, "Tcd", "TMount.tcd"), false);
var clsMounts = TMount.Load(Path.Combine(classic, "Tcd", "TMount.tcd"), true);
var ourMon = TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).ToDictionary(m => m.Id);
var clsMon = TMon.Load(Path.Combine(classic, "Tcd", "TMon.tcd"), TMon.ClassicTail).ToDictionary(m => m.Id);

// IDs 50-54 are injected by InitTPET, count them as present.
var ourIds = ourMounts.Select(m => m.Id).Concat(new ushort[] { 50, 51, 52, 53, 54 }).ToHashSet();
var newMounts = clsMounts.Where(m => !ourIds.Contains(m.Id)).ToList();

if (cmd == "tail")
{
    // Align the fixed tails of monsters present in both files to locate the 3 extra Classic bytes.
    foreach (ushort id in new ushort[] { 1, 2, 31025, 31100 })
    {
        if (!ourMon.TryGetValue(id, out var a) || !clsMon.TryGetValue(id, out var c)) continue;
        Console.WriteLine($"{id} {a.Name}");
        Console.WriteLine("  ours " + Convert.ToHexString(a.Raw, a.TailOfs, TMon.OurTail));
        Console.WriteLine("  cls  " + Convert.ToHexString(c.Raw, c.TailOfs, TMon.ClassicTail));
    }
    return;
}

if (cmd == "objcheck")
{
    // Walk every .TOB record back to back; a correct parser ends exactly at EOF.
    foreach (var root in new[] { game, classic })
        foreach (var f in Directory.GetFiles(Path.Combine(root, "Data", "OBJ"), "*.TOB"))
        {
            var b = File.ReadAllBytes(f);
            var s = new BinaryReader(new MemoryStream(b));
            int n = 0;
            string err = "";
            try
            {
                while (s.BaseStream.Position < b.Length) { long p = s.BaseStream.Position; Client.ParseObj(s, new(), out _, root == classic); n++; }
            }
            catch (Exception e) { err = $" FAIL after {n} recs at {s.BaseStream.Position}: {e.Message}"; }
            Console.WriteLine($"{f.Substring(repo.Length)}: {n} recs, pos={s.BaseStream.Position}/{b.Length}{err}");
        }
    return;
}

if (cmd == "port")
{
    bool apply = args.Contains("--apply");
    var (mounts, monsters) = new Port(game, classic, Console.Out).Run(apply);
    new PortItems(game, classic, Path.Combine(repo, "Database", "migrations", "010_classic_mounts.sql"), Console.Out).Run(apply, mounts, monsters);
    return;
}

if (cmd == "verify")
{
    // Everything below uses our-layout readers only, i.e. what TClient will do.
    int bad = 0;
    var g = new Client(game, "2_TClientS.IDX", isClassic: false);
    for (int i = 0; i < 2; i++) ClassicPort.Idx.Load(Path.Combine(game, "Index", $"{i}_TClientS.IDX"));
    {
        var b = File.ReadAllBytes(Path.Combine(game, "Data", "OBJ", "Classic.TOB"));
        var s = new BinaryReader(new MemoryStream(b)); int n = 0;
        while (s.BaseStream.Position < b.Length) { Client.ParseObj(s, new(), out _); n++; }
        Console.WriteLine($"Classic.TOB: {n} objects parse to EOF");
    }
    {
        var b = File.ReadAllBytes(Path.Combine(game, "Data", "Skin", "2_Classic.TTX")); int p = 0, n = 0;
        while (p < b.Length)
        {
            var raw = Client.Unchunk(b, p, out int len);
            int h = TexConv.HeaderLength(raw, 0);
            if (h + 8 + BitConverter.ToInt32(raw, h + 4) != raw.Length || !TexConv.Dims(raw, h).StartsWith("DDS")) { bad++; Console.WriteLine($"  bad texture chunk @{p}: {TexConv.Dims(raw, h)}"); }
            p += len; n++;
        }
        Console.WriteLine($"2_Classic.TTX: {n} chunks, all DDS: {bad == 0}");
    }
    Console.WriteLine($"TMon.tcd: {TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).Count} records parse to EOF");
    var mounts = TMount.Load(Path.Combine(game, "Tcd", "TMount.tcd"), false);
    var mons = TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).ToDictionary(m => m.Id);
    int unresolved = 0, reached = 0;
    foreach (var m in mounts)
        foreach (var mon in new[] { m.MonId, m.SaddleMonId })
        {
            if (!mons.TryGetValue(mon, out var mr)) { Console.WriteLine($"  mount {m.Id}: monster {mon} missing"); unresolved++; continue; }
            var st = new Stack<(Kind, uint)>(); st.Push((Kind.Obj, mr.Obj)); var seenV = new HashSet<(Kind, uint)>();
            while (st.Count > 0)
            {
                var key = st.Pop();
                if (key.Item2 == 0 || !seenV.Add(key)) continue;
                if (!g.Has(key.Item1, key.Item2)) { if (unresolved++ < 10) Console.WriteLine($"  mount {m.Id}: unresolved {key.Item1} {key.Item2:X8}"); continue; }
                reached++;
                if (key.Item1 == Kind.Media) continue;
                var rec = g.Get(key.Item1, key.Item2)!;
                if (key.Item1 == Kind.Mesh) MeshCheck(rec.Raw);
                foreach (var r in rec.Refs) st.Push(r);
            }
        }
    Console.WriteLine($"TMount: {mounts.Count} mounts, {reached} resource refs resolved, {unresolved} unresolved");
    // Zero ids the engine dereferences without a null check (SFX instance -> InitSFX, mesh/ani -> LockOBJ).
    var zero = new Dictionary<string, List<string>>();
    foreach (var m in mounts)
        foreach (var mon in new[] { m.MonId, m.SaddleMonId }.Distinct())
        {
            if (!mons.TryGetValue(mon, out var mr) || !g.Has(Kind.Obj, mr.Obj)) continue;
            var t = FieldStats.Tokenize(g.Get(Kind.Obj, mr.Obj)!.Raw, false);
            foreach (var (st, f) in new[] { ("sfxinst", "sfx"), ("mesh", "mesh"), ("ani", "ani") })
                if (t.Any(x => x.Struct == st && x.Field == f && BitConverter.ToUInt32(x.Val) == 0))
                    (zero.TryGetValue(st, out var l) ? l : zero[st] = new()).Add($"{m.Id}");
        }
    foreach (var (st, l) in zero) Console.WriteLine($"  ZERO {st} ids in mounts: {string.Join(",", l.Distinct())}");
    Console.WriteLine($"zero-id refs in mount objects: {zero.Values.Sum(l => l.Count)}");

    // Looks: every equip item of a mount monster selects (clk, cl, mesh) through its visual; the object must have it.
    var itemsV = TItem.Load(Path.Combine(game, "Tcd", "TItem.tcd")).ToDictionary(i => i.Id);
    var vbV = File.ReadAllBytes(Path.Combine(game, "Tcd", "TItemVisual.tcd"));
    var visV = Enumerable.Range(0, BitConverter.ToUInt16(vbV, 0)).ToDictionary(i => BitConverter.ToUInt16(vbV, 2 + 63 * i), i => vbV[(2 + 63 * i)..(2 + 63 * i + 63)]);
    int looks = 0, badLooks = 0;
    foreach (var m in mounts)
        foreach (var mon in new[] { m.MonId, m.SaddleMonId }.Distinct())
        {
            if (!mons.TryGetValue(mon, out var mr) || !g.Has(Kind.Obj, mr.Obj)) continue;
            var paths = new HashSet<string>(); string clk = "", cl = "";
            foreach (var t in FieldStats.Tokenize(g.Get(Kind.Obj, mr.Obj)!.Raw, false))
            {
                uint v = t.Val.Length >= 4 ? BitConverter.ToUInt32(t.Val) : 0;
                if (t.Struct == "clk" && t.Field == "key") clk = $"{v:X}";
                if (t.Struct == "cl" && t.Field == "key") cl = $"{clk}/{v:X}";
                if (t.Struct == "mesh" && t.Field == "key") paths.Add($"{cl}/{v:X}");
            }
            for (int sl = 0; sl < 19; sl++)
            {
                ushort iid = BitConverter.ToUInt16(mr.Raw, mr.TailOfs + 16 + 2 * sl);
                if (iid == 0 || !itemsV.TryGetValue(iid, out var it)) continue;
                if (!visV.TryGetValue(BitConverter.ToUInt16(it.Raw, it.TailOfs + 63), out var vr)) { badLooks++; Console.WriteLine($"  LOOK mount {m.Id} mon {mon}: item {iid} has no visual"); continue; }
                looks++;
                string want = $"{BitConverter.ToUInt32(vr, 10):X}/{BitConverter.ToUInt32(vr, 14):X}/{BitConverter.ToUInt32(vr, 18):X}";
                if (!paths.Contains(want)) { badLooks++; Console.WriteLine($"  LOOK mount {m.Id} mon {mon} '{mr.Name}': item {iid} wants clk/cl/mesh {want}, object {mr.Obj:X8} lacks it"); }
            }
        }
    Console.WriteLine($"mount looks checked {looks}, missing {badLooks}");
    var noModel = mounts.Where(m => new[] { m.MonId, m.SaddleMonId }.Any(mon => !mons.TryGetValue(mon, out var mr) || mr.Obj == 0 || !g.Has(Kind.Obj, mr.Obj))).ToList();
    foreach (var m in noModel) Console.WriteLine($"  NO MODEL mount {m.Id} (monsters {m.MonId}/{m.SaddleMonId})");
    Console.WriteLine($"mounts without a model: {noModel.Count}");
    return;

    // Walk our mesh layout (LoadMESH) to make sure the record is self-consistent.
    static void MeshCheck(byte[] a)
    {
        var r = new BinaryReader(new MemoryStream(a));
        uint mc = r.ReadUInt32(), nc = r.ReadUInt32(); r.ReadByte(); uint lv = r.ReadUInt32();
        r.ReadBytes(16 + (int)nc * 64);
        uint vc = r.ReadUInt32(); r.ReadBytes((int)vc * (nc > 0 ? 48 : 40));
        for (uint i = 0; i < mc * lv; i++) { uint nib = r.ReadUInt32(); for (uint k = 0; k < nib; k++) { uint c = r.ReadUInt32(); r.ReadUInt16(); for (uint q = 0; q < c; q++) if (r.ReadUInt16() >= vc) throw new InvalidDataException("index out of vertex range"); } }
        r.ReadBytes(4 * (int)Math.Max(0, lv - 1));
    }
}

if (cmd == "items")
{
    var oi = TItem.Load(Path.Combine(game, "Tcd", "TItem.tcd")).ToDictionary(i => i.Id);
    var ci = TItem.Load(Path.Combine(classic, "Tcd", "TItem.tcd"));
    // scramble check on common items with same name
    int common = 0, rawSame = 0; var slotHits = new int[TItem.Tail];
    foreach (var c in ci)
        if (oi.TryGetValue(c.Id, out var o) && o.Name == c.Name)
        {
            common++;
            if (o.Raw.AsSpan().SequenceEqual(c.Raw)) rawSame++;
            for (int k = 0; k < TItem.Tail; k++) if (o.Raw[o.TailOfs + k] == c.Raw[c.TailOfs + k]) slotHits[k]++;
        }
    Console.WriteLine($"common same-name items {common}, byte-identical {rawSame}");
    Console.WriteLine("tail bytes with <70% same-slot match: " + string.Join(" ", Enumerable.Range(0, TItem.Tail).Where(k => slotHits[k] < 0.7 * common).Select(k => $"{k}:{100 * slotHits[k] / common}%")));
    foreach (var nm in new[] { "Rathapanda", "Hell Steed", "Kitsune", "Fire Dragon", "Appa", "Capybara", "Alpaca", "Triceratops", "Magic Carpet" })
        foreach (var c in ci.Where(c => c.Name.Contains(nm)).Take(4))
        {
            var o = oi.GetValueOrDefault(c.Id);
            Console.WriteLine($"  {c.Id,5} t{c.Type,-3} k{c.Kind,-3} use {c.UseValue,5} '{c.Name}' ours:{(o == null ? "-" : $"'{o.Name}' t{o.Type} use {o.UseValue}")} | head {Convert.ToHexString(c.Raw, 0, 6)} tail[40..50] {Convert.ToHexString(c.Raw, c.TailOfs + 40, 10)} tail[92..] {Convert.ToHexString(c.Raw, c.TailOfs + 92, 9)}");
        }
    var newIds = newMounts.Select(m => m.Id).ToHashSet();
    foreach (var c in ci.Where(c => c.Type == 12 && newIds.Contains(c.UseValue)))
    {
        string mine = oi.TryGetValue(c.Id, out var o) ? $"OURS HAS id: '{o.Name}' type {o.Type} use {o.UseValue}" : "free";
        Console.WriteLine($"  item {c.Id,5} kind {c.Kind,3} mount {c.UseValue,3} '{c.Name}'  -> {mine}");
    }
    return;
}

if (cmd == "icons")
{
    var mounts = TMount.Load(Path.Combine(game, "Tcd", "TMount.tcd"), false);          // after port
    var ported = mounts.Select(m => m.Id).ToHashSet();
    var oi = TItem.Load(Path.Combine(game, "Tcd", "TItem.tcd")).ToDictionary(i => i.Id);
    var ci = TItem.Load(Path.Combine(classic, "Tcd", "TItem.tcd"));
    var ov = File.ReadAllBytes(Path.Combine(game, "Tcd", "TItemVisual.tcd"));
    var cv = File.ReadAllBytes(Path.Combine(classic, "Tcd", "TItemVisual.tcd"));
    var ovis = Enumerable.Range(0, BitConverter.ToUInt16(ov, 0)).ToDictionary(i => BitConverter.ToUInt16(ov, 2 + 63 * i), i => ov[(2 + 63 * i)..(2 + 63 * i + 63)]);
    var cvis = Enumerable.Range(0, BitConverter.ToUInt16(cv, 0)).GroupBy(i => BitConverter.ToUInt16(cv, 2 + 64 * i)).ToDictionary(g => g.Key, g => cv[(2 + 64 * g.First())..(2 + 64 * g.First() + 63)]);
    var pak = new HashSet<int>();
    { var b = File.ReadAllBytes(Path.Combine(classic, "Data", "Img", "Custom", "Classic.pak")); int n = BitConverter.ToUInt16(b, 0), p = 2; for (int i = 0; i < n; i++) { pak.Add(BitConverter.ToUInt16(b, p)); p += 6 + BitConverter.ToInt32(b, p + 2); } }
    var custom = Directory.GetFiles(Path.Combine(game, "Data", "Img", "Custom"), "*.png").Select(Path.GetFileNameWithoutExtension).Where(s => int.TryParse(s, out _)).Select(int.Parse).ToHashSet();

    var items = ci.Where(c => c.Type == 12 && ported.Contains(c.UseValue) && !oi.ContainsKey(c.Id)).ToList();
    Console.WriteLine($"mount items to add: {items.Count}  (kinds: {string.Join(",", items.Select(i => i.Kind).Distinct())}); ours already has for these mounts: {oi.Values.Count(o => o.Type == 12 && ported.Contains(o.UseValue))}");
    var visIds = items.SelectMany(it => Enumerable.Range(0, 5).Select(k => BitConverter.ToUInt16(it.Raw, it.TailOfs + 63 + 2 * k))).Where(v => v != 0).Distinct().ToList();
    int vMissing = visIds.Count(v => !ovis.ContainsKey(v)), vSame = visIds.Count(v => ovis.TryGetValue(v, out var o) && cvis.TryGetValue(v, out var c) && o.AsSpan().SequenceEqual(c));
    Console.WriteLine($"visuals referenced: {visIds.Count}: missing in ours {vMissing}, identical {vSame}, differ {visIds.Count - vMissing - vSame}");
    var icons = visIds.Where(cvis.ContainsKey).Select(v => (int)BitConverter.ToUInt16(cvis[v], 34))
        .Concat(TMount.Load(Path.Combine(classic, "Tcd", "TMount.tcd"), true).Where(m => ported.Contains(m.Id)).Select(m => (int)m.Icon)).Distinct().OrderBy(x => x).ToList();
    var hi = icons.Where(i => i >= 60000).ToList();
    Console.WriteLine($"icons: {icons.Count} total; >=60000: {hi.Count} (in Classic.pak {hi.Count(pak.Contains)}, already a Custom png here {hi.Count(custom.Contains)}); <60000 (TIF list): {icons.Count - hi.Count}");
    Console.WriteLine("  <60000: " + string.Join(",", icons.Where(i => i < 60000)));
    Console.WriteLine("  <60000 also in pak: " + string.Join(",", icons.Where(i => i < 60000 && pak.Contains(i))));
    Console.WriteLine("  >=60000 clashing with our Custom pngs: " + string.Join(",", hi.Where(custom.Contains)));
    var cI = ClassicPort.Idx.Load(Path.Combine(classic, "Index", "TClientI.IDX"));
    var oI = ClassicPort.Idx.Load(Path.Combine(game, "Index", "TClientI.IDX"));
    var notPak = hi.Where(i => !pak.Contains(i)).ToList();
    Console.WriteLine($"  not in pak: {notPak.Count}; of those, sprite ids in Classic TClientI.IDX: {notPak.Count(i => cI.ById.ContainsKey((uint)i))}; in ours: {notPak.Count(i => oI.ById.ContainsKey((uint)i))}");
    Console.WriteLine($"  classic I.IDX id range {cI.Entries.Min(e => e.Id)}..{cI.Entries.Max(e => e.Id)} ({cI.Entries.Count}), ours {oI.Entries.Min(e => e.Id)}..{oI.Entries.Max(e => e.Id)} ({oI.Entries.Count}); files: {string.Join(",", cI.Files)}");
    Console.WriteLine("  sample not-in-pak: " + string.Join(",", notPak.Take(15)));
    return;
}

if (cmd == "vismap")
{
    // Our 63-byte TITEMVISUAL fields (InitTITEMVISUAL order) vs every offset of Classic's 64-byte record.
    (string, int)[] f = [("id", 2), ("inven", 4), ("obj", 4), ("clk", 4), ("cli", 4), ("mesh0", 4), ("mesh1", 4), ("piv0", 4), ("piv1", 4),
        ("icon", 2), ("hideSlot", 1), ("hidePart", 1), ("hideRace", 1), ("slashColor", 4), ("slashTex", 4), ("slashLen", 4), ("fx0", 4), ("fx1", 4), ("costumeHide", 4)];
    var ob = File.ReadAllBytes(Path.Combine(game, "Tcd", "TItemVisual.tcd"));
    var cb = File.ReadAllBytes(Path.Combine(classic, "Tcd", "TItemVisual.tcd"));
    int on = BitConverter.ToUInt16(ob, 0), cn = BitConverter.ToUInt16(cb, 0);
    var orec = Enumerable.Range(0, on).Select(i => ob[(2 + 63 * i)..(2 + 63 * i + 63)]).ToList();
    var crec = Enumerable.Range(0, cn).Select(i => cb[(2 + 64 * i)..(2 + 64 * i + 64)]).ToList();
    // find the Classic id offset: the WORD offset whose values overlap our ids the most
    var oids = orec.Select(r => BitConverter.ToUInt16(r, 0)).ToHashSet();
    int idOfs = Enumerable.Range(0, 63).OrderByDescending(o => crec.Count(r => oids.Contains(BitConverter.ToUInt16(r, o)))).First();
    var cmap = crec.GroupBy(r => BitConverter.ToUInt16(r, idOfs)).ToDictionary(g => g.Key, g => g.First());
    var pairs = orec.Where(r => cmap.ContainsKey(BitConverter.ToUInt16(r, 0))).Select(r => (o: r, c: cmap[BitConverter.ToUInt16(r, 0)])).ToList();
    Console.WriteLine($"classic id at offset {idOfs}; matched {pairs.Count} records");
    int ofs = 0;
    foreach (var (name, sz) in f)
    {
        var best = Enumerable.Range(0, 65 - sz).Select(co => (co, hits: pairs.Count(p => p.o.AsSpan(ofs, sz).SequenceEqual(p.c.AsSpan(co, sz)))))
            .OrderByDescending(x => x.hits).Take(3).Select(x => $"@{x.co}:{100 * x.hits / pairs.Count}%");
        Console.WriteLine($"  {name,-11} ours@{ofs,-2} -> {string.Join("  ", best)}");
        ofs += sz;
    }
    return;
}

if (cmd == "visprobe")
{
    foreach (var (path, label) in new[] { (Path.Combine(game, "Tcd", "TItemVisual.tcd"), "ours"), (Path.Combine(classic, "Tcd", "TItemVisual.tcd"), "classic") })
    {
        var b = File.ReadAllBytes(path);
        int n = BitConverter.ToUInt16(b, 0);
        Console.WriteLine($"{label}: count {n}, bytes/record {(b.Length - 2) / (double)n:F3}");
        Console.WriteLine("   first 2 recs: " + Convert.ToHexString(b, 2, Math.Min(140, b.Length - 2)));
    }
    return;
}

if (cmd == "itemprobe")
{
    // Our TItem record: WORD id, BYTE type, BYTE kind, WORD attr, CString name, WORD useValue, 101-byte tail.
    foreach (var (path, label) in new[] { (Path.Combine(game, "Tcd", "TItem.tcd"), "ours"), (Path.Combine(classic, "Tcd", "TItem.tcd"), "classic") })
    {
        var b = File.ReadAllBytes(path);
        int n = BitConverter.ToUInt16(b, 0);
        for (int tail = 90; tail <= 140; tail++)
        {
            int p = 2, i = 0;
            for (; i < n && p + 7 < b.Length; i++)
            {
                p += 6; int l = b[p++]; if (l == 0xFF) { l = BitConverter.ToUInt16(b, p); p += 2; }
                p += l + 2 + tail;
            }
            if (i == n && p == b.Length) Console.WriteLine($"{label}: {n} items, tail after useValue = {tail}");
        }
    }
    return;
}

if (cmd == "objtex")
{
    // All OBJTEX fields of object <hexid> (ours layout, after conversion).
    var g = new Client(game, "2_TClientS.IDX", isClassic: false);
    var t = FieldStats.Tokenize(g.Get(Kind.Obj, Convert.ToUInt32(args[1], 16))!.Raw, false);
    var cur = new List<string>();
    foreach (var x in t)
    {
        if (x.Struct == "mesh" && x.Field == "mesh") Console.WriteLine($" mesh {BitConverter.ToUInt32(x.Val):X8}");
        if (x.Struct != "objtex") continue;
        cur.Add($"{x.Field}={(x.Val.Length == 4 ? BitConverter.ToUInt32(x.Val).ToString("X") : x.Val[0].ToString())}");
        if (x.Field == "pad") { Console.WriteLine("   " + string.Join(" ", cur)); cur.Clear(); }
    }
    return;
}

if (cmd == "health")
{
    // Per mount: rider pivot (ID_PIVOT_MOUNT 0x5E7C), animated actions, textures on every mesh.
    var g = new Client(game, "2_TClientS.IDX", isClassic: false);
    var mons = TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).ToDictionary(m => m.Id);
    int bad = 0;
    foreach (var m in TMount.Load(Path.Combine(game, "Tcd", "TMount.tcd"), false))
        foreach (var mon in new[] { m.MonId, m.SaddleMonId }.Distinct())
        {
            var mr = mons[mon];
            var t = FieldStats.Tokenize(g.Get(Kind.Obj, mr.Obj)!.Raw, false);
            var issues = new List<string>();
            bool pivot = t.Where(x => x.Struct == "pivot" && x.Field == "b").Any(x => BitConverter.ToUInt32(x.Val) == 0x5E7C);
            if (!pivot) issues.Add("no rider pivot");
            int anis = t.Count(x => x.Struct == "ani" && x.Field == "ani" && g.Has(Kind.Ani, BitConverter.ToUInt32(x.Val)));
            if (anis == 0) issues.Add("no animations");
            int texTotal = t.Count(x => x.Struct == "objtex" && x.Field == "tex0");
            int texOk = t.Count(x => x.Struct == "objtex" && x.Field == "tex0" && g.Has(Kind.Tex, BitConverter.ToUInt32(x.Val)));
            int meshes = t.Count(x => x.Struct == "mesh" && x.Field == "mesh");
            if (texTotal == 0 || texOk < texTotal) issues.Add($"textures {texOk}/{texTotal} on {meshes} meshes");
            if (issues.Count > 0) { bad++; Console.WriteLine($"mount {m.Id,3} mon {mon} '{mr.Name}' obj {mr.Obj:X8}: {string.Join(", ", issues)}"); }
        }
    Console.WriteLine($"unhealthy mount monsters: {bad}");
    if (args.Contains("--tex"))
    {
        // Where are the missing textures in Classic? (tex0/tex1 of every OBJTEX of the unhealthy objects)
        var c = new Client(classic, "TClientS.IDX", isClassic: true);
        foreach (uint obj in args.Skip(1).Where(a => a != "--tex").Select(a => Convert.ToUInt32(a, 16)))
        {
            var t = FieldStats.Tokenize(g.Get(Kind.Obj, obj)!.Raw, false);
            foreach (var x in t.Where(x => x.Struct == "objtex" && x.Field is "tex0" or "tex1"))
            {
                uint id = BitConverter.ToUInt32(x.Val);
                if (id == 0 || g.Has(Kind.Tex, id)) continue;
                string where = c.Idx[Kind.Tex].ById.TryGetValue(id, out var ce) ? $"Classic {c.Idx[Kind.Tex].Files[ce.File]}@{ce.Pos}" : "NOT IN CLASSIC EITHER";
                Console.WriteLine($"  obj {obj:X8} {x.Field} {id:X8}: {where}");
            }
        }
    }
    return;
}

if (cmd == "anibones")
{
    // For mount <id>: mesh bone counts vs node counts of every animation its object's actions use (blob header).
    ushort mid = ushort.Parse(args[1]);
    var g = new Client(game, "2_TClientS.IDX", isClassic: false);
    var mount = TMount.Load(Path.Combine(game, "Tcd", "TMount.tcd"), false).First(m => m.Id == mid);
    var mr = TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).First(m => m.Id == mount.MonId);
    var t = FieldStats.Tokenize(g.Get(Kind.Obj, mr.Obj)!.Raw, false);
    foreach (var mk in t.Where(x => x.Struct == "mesh" && x.Field == "mesh").Select(x => BitConverter.ToUInt32(x.Val)).Distinct())
        Console.WriteLine($"mesh {mk:X8}: bones {BitConverter.ToUInt32(g.Get(Kind.Mesh, mk)!.Raw, 4)}");
    var nodeCounts = new Dictionary<string, int>();
    foreach (var set in t.Where(x => x.Struct == "ani" && x.Field == "ani").Select(x => BitConverter.ToUInt32(x.Val)).Distinct())
    {
        if (!g.Has(Kind.Ani, set)) { Console.WriteLine($"  aniset {set:X8} MISSING"); continue; }
        var a = AniStats.Parse(g.Get(Kind.Ani, set)!.Raw);
        var leaves = a.Refs.Select(r => BitConverter.ToUInt32(r, 0)).ToList();
        if (a.Blob.Length >= 8) leaves.Insert(0, set);
        foreach (var leaf in leaves.Distinct())
        {
            var la = leaf == set ? a : AniStats.Parse(g.Get(Kind.Ani, leaf)!.Raw);
            string k = la.Blob.Length >= 8 ? $"v{BitConverter.ToInt32(la.Blob, 0)} nodes {BitConverter.ToInt32(la.Blob, 4)}" : "no blob";
            nodeCounts[k] = nodeCounts.GetValueOrDefault(k) + 1;
        }
    }
    foreach (var (k, v) in nodeCounts) Console.WriteLine($"  animations: {k} x{v}");
    return;
}

if (cmd == "montail")
{
    // Classic TMon fields that matter for riding: fSize/scale (tail 74..89), petSize (tail 124 in Classic), dropped bytes.
    var cm = TMon.Load(Path.Combine(classic, "Tcd", "TMon.tcd"), TMon.ClassicTail).ToDictionary(m => m.Id);
    var cmounts = TMount.Load(Path.Combine(classic, "Tcd", "TMount.tcd"), true).ToDictionary(m => m.Id);
    foreach (var a in args.Skip(1))
    {
        var mt = cmounts[ushort.Parse(a)];
        foreach (var mon in new[] { mt.MonId, mt.SaddleMonId }.Distinct())
        {
            var r = cm[mon]; int t = r.TailOfs;
            float F(int o) => BitConverter.ToSingle(r.Raw, t + o);
            Console.WriteLine($"mount {mt.Id} scale {mt.Scale} mon {mon} '{r.Name}': fSize {F(76):F2} scale {F(80):F2}/{F(84):F2}/{F(88):F2} petSize {F(123):F2} slot20 {BitConverter.ToUInt16(r.Raw, t + 54)} last {r.Raw[^1]}");
        }
    }
    return;
}

if (cmd == "pivots")
{
    // Pivot maps (id -> bone index) of a mount's object in Game/ (converted) and raw Classic, plus the mesh bone count.
    ushort mid = ushort.Parse(args[1]);
    var g = new Client(game, "2_TClientS.IDX", isClassic: false);
    var c = new Client(classic, "TClientS.IDX", isClassic: true);
    var mount = TMount.Load(Path.Combine(game, "Tcd", "TMount.tcd"), false).First(m => m.Id == mid);
    var mr = TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).First(m => m.Id == mount.MonId);
    var raw = g.Get(Kind.Obj, mr.Obj)!.Raw;
    var t = FieldStats.Tokenize(raw, false);
    Console.WriteLine($"mount {mid} '{mr.Name}' obj {mr.Obj:X8} pivotCount byte {raw[0]}");
    Console.WriteLine("  ours (index,id): " + string.Join(" ", t.Where(x => x.Struct == "pivot").Chunk(2).Select(p => $"({BitConverter.ToUInt32(p[0].Val)},{BitConverter.ToUInt32(p[1].Val):X})")));
    var ct = FieldStats.Tokenize(c.Get(Kind.Obj, mr.Obj)!.Raw, true);
    Console.WriteLine("  classic raw (a,b): " + string.Join(" ", ct.Where(x => x.Struct == "pivot").Chunk(2).Select(p => $"({BitConverter.ToUInt32(p[0].Val):X},{BitConverter.ToUInt32(p[1].Val)})")));
    foreach (var mk in t.Where(x => x.Struct == "mesh" && x.Field == "mesh").Select(x => BitConverter.ToUInt32(x.Val)).Distinct())
        if (g.Has(Kind.Mesh, mk)) Console.WriteLine($"  mesh {mk:X8}: bones {BitConverter.ToUInt32(g.Get(Kind.Mesh, mk)!.Raw, 4)}");
    return;
}

if (cmd == "monlook")
{
    // Monster <id>: equip items (TMon tail +16, 19 WORDs) -> visuals -> CLK/CL/mesh; and whether the object has them.
    ushort mon = ushort.Parse(args[1]);
    var mons = TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).ToDictionary(m => m.Id);
    var items = TItem.Load(Path.Combine(game, "Tcd", "TItem.tcd")).ToDictionary(i => i.Id);
    var citems = TItem.Load(Path.Combine(classic, "Tcd", "TItem.tcd")).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
    var vb = File.ReadAllBytes(Path.Combine(game, "Tcd", "TItemVisual.tcd"));
    var vis = Enumerable.Range(0, BitConverter.ToUInt16(vb, 0)).ToDictionary(i => BitConverter.ToUInt16(vb, 2 + 63 * i), i => vb[(2 + 63 * i)..(2 + 63 * i + 63)]);
    var cvb = File.ReadAllBytes(Path.Combine(classic, "Tcd", "TItemVisual.tcd"));
    var cvis = Enumerable.Range(0, BitConverter.ToUInt16(cvb, 0)).GroupBy(i => BitConverter.ToUInt16(cvb, 2 + 64 * i)).ToDictionary(g => g.Key, g => cvb[(2 + 64 * g.First())..(2 + 64 * g.First() + 63)]);
    var m = mons[mon];
    var g = new Client(game, "2_TClientS.IDX", isClassic: false);
    var c = new Client(classic, "TClientS.IDX", isClassic: true);
    List<string> Clk(byte[] raw) => FieldStats.Tokenize(raw, false).Where(t => t.Struct is "clk" or "cl" or "mesh" && t.Field is "key" or "mesh")
        .Select(t => $"{t.Struct}:{BitConverter.ToUInt32(t.Val):X}").ToList();
    Console.WriteLine($"monster {mon} '{m.Name}' obj {m.Obj:X8}");
    var ourLook = Clk(g.Get(Kind.Obj, m.Obj)!.Raw);
    var theirs = Clk(ObjConv.FromClassic(c.Get(Kind.Obj, m.Obj)!.Raw));
    Console.WriteLine($"  our object:     {string.Join(" ", ourLook)}");
    Console.WriteLine($"  classic object: {string.Join(" ", theirs)}");
    for (int s = 0; s < 19; s++)
    {
        ushort id = BitConverter.ToUInt16(m.Raw, m.TailOfs + 16 + 2 * s);
        if (id == 0) continue;
        string Desc(ItemRec? it, Dictionary<ushort, byte[]> vv)
        {
            if (it == null) return "MISSING";
            ushort v = BitConverter.ToUInt16(it.Raw, it.TailOfs + 63);
            if (!vv.TryGetValue(v, out var r)) return $"'{it.Name}' visual {v} MISSING";
            return $"'{it.Name}' visual {v}: clk {BitConverter.ToUInt32(r, 10):X} cli {BitConverter.ToUInt32(r, 14):X} mesh {BitConverter.ToUInt32(r, 18):X}/{BitConverter.ToUInt32(r, 22):X}";
        }
        Console.WriteLine($"  slot {s,2} item {id,5}: ours {Desc(items.GetValueOrDefault(id), vis)} | classic {Desc(citems.GetValueOrDefault(id), cvis)}");
    }
    return;
}

if (cmd == "sfxinst")
{
    // Every SFX instance of object <hexid> in Game/, as our LoadOBJ reads it (tokenized, zero ids kept).
    uint obj = Convert.ToUInt32(args[1], 16);
    bool fromClassic = args.Contains("--classic");
    var g = new Client(game, "2_TClientS.IDX", isClassic: false);
    var toks = fromClassic ? FieldStats.Tokenize(new Client(classic, "TClientS.IDX", isClassic: true).Get(Kind.Obj, obj)!.Raw, true)
                           : FieldStats.Tokenize(g.Get(Kind.Obj, obj)!.Raw, false);
    string ctx = "";
    for (int i = 0; i < toks.Count; i++)
    {
        if (toks[i].Field == "count" && toks[i].Struct is "osfx" or "asfx" or "nsfx") ctx = toks[i].Struct;
        if (toks[i].Struct == "act" && toks[i].Field == "key") ctx = $"act {BitConverter.ToUInt32(toks[i].Val)}";
        if (toks[i].Struct == "sfxinst" && toks[i].Field == "key")
        {
            uint sfx = BitConverter.ToUInt32(toks[i + 1].Val);
            Console.WriteLine($"  [{ctx}] key {BitConverter.ToUInt32(toks[i].Val),-6} sfx {sfx:X8} {(g.Has(Kind.Sfx, sfx) ? "ok" : "UNRESOLVED")}  pivot {BitConverter.ToInt32(toks[i + 8].Val)} bias {toks[i + 9].Val[0]} func {BitConverter.ToUInt32(toks[i + 10].Val)} tick {BitConverter.ToUInt32(toks[i + 11].Val)}");
        }
    }
    return;
}

if (cmd == "mountrefs")
{
    // For mount <id>: its monsters' objects and every SFX they reference, resolved against Game/ as TClient will.
    ushort mid = ushort.Parse(args[1]);
    var g = new Client(game, "2_TClientS.IDX", isClassic: false);
    var mount = TMount.Load(Path.Combine(game, "Tcd", "TMount.tcd"), false).First(m => m.Id == mid);
    var mons = TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).ToDictionary(m => m.Id);
    foreach (var mon in new[] { mount.MonId, mount.SaddleMonId }.Distinct())
    {
        uint obj = mons[mon].Obj;
        var oe = g.Idx[Kind.Obj].ById[obj];
        Console.WriteLine($"monster {mon} '{mons[mon].Name}' obj {obj:X8} in {g.Idx[Kind.Obj].Files[oe.File]}");
        foreach (var (k, id) in g.Get(Kind.Obj, obj)!.Refs.Where(r => r.Item1 == Kind.Sfx).Distinct())
        {
            string where = g.Idx[Kind.Sfx].ById.TryGetValue(id, out var se) ? g.Idx[Kind.Sfx].Files[se.File] + "@" + se.Pos : "NOT IN INDEX";
            Console.WriteLine($"   sfx {id:X8} -> {where}");
        }
    }
    return;
}

if (cmd == "grantsql")
{
    // INSERTs giving account <userId> every ported mount (permanent), named after its monster.
    uint user = uint.Parse(args[1]);
    var st = new BinaryReader(File.OpenRead(Path.Combine(game, "Tcd", "ClassicPort.state")));
    int n = st.ReadInt32(); var ids = Enumerable.Range(0, n).Select(_ => st.ReadUInt16()).ToList(); st.Dispose();
    var mounts = TMount.Load(Path.Combine(game, "Tcd", "TMount.tcd"), false).ToDictionary(m => m.Id);
    var mons = TMon.Load(Path.Combine(game, "Tcd", "TMon.tcd"), TMon.OurTail).ToDictionary(m => m.Id);
    Console.WriteLine("USE [TGame_gsp];"); Console.WriteLine("SET NOCOUNT ON;");
    foreach (var id in ids.OrderBy(x => x))
    {
        string name = mons[mounts[id].MonId].Name.Replace("'", "''");
        if (name.Length > 50) name = name[..50];
        Console.WriteLine($"IF NOT EXISTS (SELECT 1 FROM dbo.TPETTABLE WHERE dwUserID = {user} AND wPetID = {id}) INSERT INTO dbo.TPETTABLE (dwUserID, wPetID, szName, timeUse, bEffect) VALUES ({user}, {id}, '{name}', '1900-01-01', 0);");
    }
    return;
}

if (cmd == "monids")
{
    foreach (var id in newMounts.SelectMany(m => new[] { m.MonId, m.SaddleMonId }).Distinct().Where(ourMon.ContainsKey))
        Console.WriteLine($"{id,6}  ours '{ourMon[id].Name}' obj {ourMon[id].Obj:X8}   classic '{clsMon[id].Name}' obj {clsMon[id].Obj:X8}");
    return;
}

var ours = new Client(game, "2_TClientS.IDX", isClassic: false);
var cls = new Client(classic, "TClientS.IDX", isClassic: true);

if (cmd == "validate")
{
    // Convert every record present in both clients and count byte-identical results.
    var o2 = ClassicPort.Idx.Load(Path.Combine(game, "Index", "2_TClientS.IDX"));
    var fc = new Dictionary<string, byte[]>();
    byte[] F(string p) => fc.TryGetValue(p, out var b) ? b : fc[p] = File.ReadAllBytes(p);
    var objDiff = new Dictionary<int, int>(); string? objSample = null;
    int texHdr = 0, texDims = 0; var texBad = new List<string>();
    foreach (var k in new[] { Kind.Tex })
    {
        int tot = 0, same = 0;
        foreach (var e in cls.Idx[k].Entries)
        {
            if (!ours.Has(k, e.Id)) continue;
            tot++;
            byte[] conv, mine;
            if (k == Kind.Tex)
            {
                conv = TexConvert.FromClassic(F(cls.DataPath(k, e)), (int)e.Pos, out _);
                mine = ours.Get(k, e.Id)!.Raw;
            }
            else
            {
                var raw = cls.Get(k, e.Id)!.Raw;
                conv = k switch { Kind.Obj => ObjConv.FromClassic(raw), Kind.Ani => AniConv.FromClassic(raw, out _), _ => MeshConv.FromClassic(raw, out _) };
                mine = ours.Get(k, e.Id)!.Raw;
                if (k != Kind.Obj && mine.Length > conv.Length) mine = mine[..conv.Length];   // raw slices may run into unindexed records
            }
            if (k == Kind.Obj) { conv = ObjCanon.Canon(conv); mine = ObjCanon.Canon(mine); }
            if (k == Kind.Tex)
            {
                int h = TexConv.HeaderLength(mine, 0);
                if (conv.AsSpan(0, Math.Min(h, conv.Length)).SequenceEqual(mine.AsSpan(0, h))) texHdr++;
                string dc = TexConv.Dims(conv, TexConv.HeaderLength(conv, 0)), dm = TexConv.Dims(mine, h);
                if (dc == dm) texDims++; else if (texBad.Count < 5) texBad.Add($"{e.Id:X8} ours {dm} conv {dc}");
            }
            if (conv.AsSpan().SequenceEqual(mine)) same++;
            else if (k == Kind.Obj && conv.Length == mine.Length)
            {
                int d = 0; while (conv[d] == mine[d]) d++;
                objDiff[d] = objDiff.GetValueOrDefault(d) + 1;
                if (objSample == null) objSample = $"{e.Id:X8} @{d} ours {Convert.ToHexString(mine, Math.Max(0, d - 24), Math.Min(64, mine.Length - Math.Max(0, d - 24)))} conv {Convert.ToHexString(conv, Math.Max(0, d - 24), Math.Min(64, conv.Length - Math.Max(0, d - 24)))}";
            }
        }
        if (k == Kind.Tex) { Console.WriteLine($"   tex header equal {texHdr}, same decoded dims {texDims}"); texBad.ForEach(x => Console.WriteLine("   " + x)); }
        if (k == Kind.Obj) { foreach (var (d, c) in objDiff.OrderByDescending(x => x.Value).Take(6)) Console.WriteLine($"   obj first diff @{d}: {c}"); Console.WriteLine("   " + objSample); }
        Console.WriteLine($"{k,-5} common {tot,6}: identical after conversion {same,6} ({100.0 * same / Math.Max(1, tot):F1}%)");
    }
    return;
}

if (cmd == "texdims")
{
    var det = Enumerable.Range(0, 3).Select(i => ClassicPort.Idx.Load(Path.Combine(game, "Index", $"{i}_TClientS.IDX"))).ToArray();
    var files = new Dictionary<string, byte[]>();
    byte[] F(string p) => files.TryGetValue(p, out var b) ? b : files[p] = File.ReadAllBytes(p);
    foreach (var e in cls.Idx[Kind.Tex].Entries.Where(x => ours.Has(Kind.Tex, x.Id)).Where((_, i) => i % 97 == 0).Take(12))
    {
        var cf = F(cls.DataPath(Kind.Tex, e));
        var (hdr, slots, len) = TexConv.ReadClassic(cf, (int)e.Pos);
        string line = $"{e.Id:X8} cls slots [{string.Join(",", slots.Select(s => s == 0 ? "-" : TexConv.Dims(TexConv.Blob(cf, s))))}]";
        for (int d = 0; d < 3; d++)
        {
            if (!det[d].ById.TryGetValue(e.Id, out var oe)) { line += $"  {d}_:none"; continue; }
            var raw = Client.Unchunk(F(Path.Combine(game, "Data", det[d].Files[oe.File])), (int)oe.Pos, out _);
            int h = TexConv.HeaderLength(raw, 0);
            line += $"  {d}_:{TexConv.Dims(raw, h)} hdr{(raw.AsSpan(0, h).SequenceEqual(hdr) ? "=" : "!=")}";
        }
        Console.WriteLine(line);
    }
    return;
}

if (cmd == "texdump")
{
    foreach (var e in cls.Idx[Kind.Tex].Entries.Where(x => ours.Has(Kind.Tex, x.Id)).Take(3))
    {
        var oe = ours.Idx[Kind.Tex].ById[e.Id];
        var a = Client.Unchunk(File.ReadAllBytes(ours.DataPath(Kind.Tex, oe)), (int)oe.Pos, out _);
        var cb = File.ReadAllBytes(cls.DataPath(Kind.Tex, e));
        Console.WriteLine($"{e.Id:X8} ours {ours.Idx[Kind.Tex].Files[oe.File]}@{oe.Pos} len={a.Length}  cls {cls.Idx[Kind.Tex].Files[e.File]}@{e.Pos}");
        Console.WriteLine($"   ours {Convert.ToHexString(a, 0, Math.Min(96, a.Length))}");
        Console.WriteLine($"   cls  {Convert.ToHexString(cb, (int)e.Pos, 96)}");
    }
    return;
}

if (cmd == "meshconv")
{
    // Convert every common mesh; ours slices may include trailing unindexed records, so compare the prefix.
    int same = 0, tot = 0, fail = 0;
    var cat = new Dictionary<string, int>();
    foreach (var e in cls.Idx[Kind.Mesh].Entries)
    {
        if (!ours.Has(Kind.Mesh, e.Id)) continue;
        tot++;
        var a = ours.Get(Kind.Mesh, e.Id)!.Raw;
        try
        {
            var c = MeshConv.FromClassic(cls.Get(Kind.Mesh, e.Id)!.Raw, out _);
            if (c.Length <= a.Length && a.AsSpan(0, c.Length).SequenceEqual(c)) { same++; cat[$"ok nodes>0={BitConverter.ToUInt32(a, 4) > 0} vb={a[8]}"] = cat.GetValueOrDefault($"ok nodes>0={BitConverter.ToUInt32(a, 4) > 0} vb={a[8]}") + 1; }
            else cat["mismatch"] = cat.GetValueOrDefault("mismatch") + 1;
        }
        catch (Exception ex)
        {
            string key = $"fail nodes>0={BitConverter.ToUInt32(a, 4) > 0} vb={a[8]}";
            cat[key] = cat.GetValueOrDefault(key) + 1;
            if (fail++ < 1)
            {
                var b = cls.Get(Kind.Mesh, e.Id)!.Raw;
                int d = 0; while (a[d] == b[d]) d++;
                int s0 = Math.Max(0, d - 20);
                Console.WriteLine($"{e.Id:X8}: {ex.Message} verts={BitConverter.ToUInt32(a, 33)} lens {a.Length}/{b.Length} first diff @{d} | ours {Convert.ToHexString(a, s0, 96)} | cls  {Convert.ToHexString(b, s0, 96)}");
            }
        }
    }
    Console.WriteLine($"common meshes {tot}: identical after conversion {same}, failed {fail}");
    foreach (var (k, v) in cat) Console.WriteLine($"  {k}: {v}");
    return;
}

if (cmd == "meshlen")
{
    // Walk our mesh layout up to the first IB block and show both clients' bytes there.
    foreach (var e in cls.Idx[Kind.Mesh].Entries.Where(x => ours.Has(Kind.Mesh, x.Id)).Take(4))
    {
        var a = ours.Get(Kind.Mesh, e.Id)!.Raw; var b = cls.Get(Kind.Mesh, e.Id)!.Raw;
        var r = new BinaryReader(new MemoryStream(a));
        uint mc = r.ReadUInt32(), nc = r.ReadUInt32(); r.ReadByte(); uint lv = r.ReadUInt32();
        r.ReadBytes(16 + (int)nc * 64);
        uint vc = r.ReadUInt32();
        int vsz = nc > 0 ? 48 : 40;
        r.ReadBytes((int)vc * vsz);
        int ib = (int)r.BaseStream.Position;
        int same = 0; for (int i = 13; i < ib; i++) if (a[i] == b[i]) same++;
        Console.WriteLine($"{e.Id:X8} ours len={a.Length} cls len={b.Length} meshes={mc} nodes={nc} lv={lv} verts={vc} ib@{ib}  pre-ib bytes equal {same}/{ib - 13}");
        Console.WriteLine($"   ours ib: {Convert.ToHexString(a, ib, 48)}");
        Console.WriteLine($"   cls  ib: {Convert.ToHexString(b, ib, 48)}");
    }
    return;
}

if (cmd == "meshcmp")
{
    // Swap Classic's meshCount/level header fields, then report remaining diffs.
    int same = 0, tot = 0;
    var firstDiff = new Dictionary<int, int>();
    string? sample = null;
    foreach (var e in cls.Idx[Kind.Mesh].Entries)
    {
        if (!ours.Has(Kind.Mesh, e.Id) || tot >= 2000) continue;
        tot++;
        var a = ours.Get(Kind.Mesh, e.Id)!.Raw; var b = (byte[])cls.Get(Kind.Mesh, e.Id)!.Raw.Clone();
        (b[0], b[1], b[2], b[3], b[9], b[10], b[11], b[12]) = (b[9], b[10], b[11], b[12], b[0], b[1], b[2], b[3]);
        int n = Math.Min(a.Length, b.Length);
        int d = 0; while (d < n && a[d] == b[d]) d++;
        if (d == n) { same++; continue; }
        firstDiff[d] = firstDiff.GetValueOrDefault(d) + 1;
        if (sample == null && a.Length > 200) sample = $"{e.Id:X8} nodes={BitConverter.ToUInt32(a, 4)} diff@{d} | ours {Convert.ToHexString(a, Math.Max(0, d - 16), 64)} | cls  {Convert.ToHexString(b, Math.Max(0, d - 16), 64)}";
    }
    Console.WriteLine($"meshes {tot}: identical after header swap {same}");
    foreach (var (k, v) in firstDiff.OrderByDescending(x => x.Value).Take(8)) Console.WriteLine($"  first diff at byte {k}: {v}");
    Console.WriteLine(sample);
    return;
}

if (cmd == "rescmp")
{
    // For each non-OBJ kind: of the IDs present in both clients, how many records are byte-identical?
    foreach (var k in new[] { Kind.Mesh, Kind.Ani, Kind.Tex, Kind.Sfx })
    {
        int same = 0, sameLen = 0, tot = 0;
        string? sample = null;
        foreach (var e in cls.Idx[k].Entries)
        {
            if (!ours.Has(k, e.Id) || tot >= 400) continue;
            tot++;
            var a = ours.Get(k, e.Id)!.Raw; var b = cls.Get(k, e.Id)!.Raw;
            if (k == Kind.Mesh) { int n = Math.Min(a.Length, b.Length); a = a[..n]; b = b[..n]; }
            if (a.AsSpan().SequenceEqual(b)) same++;
            else if (a.Length == b.Length) { sameLen++; if (sample == null) { int d = 0; while (a[d] == b[d]) d++; sample = $"{e.Id:X8} len={a.Length} first diff @{d}: ours {Convert.ToHexString(a, Math.Max(0, d - 8), Math.Min(48, a.Length - Math.Max(0, d - 8)))} cls {Convert.ToHexString(b, Math.Max(0, d - 8), Math.Min(48, b.Length - Math.Max(0, d - 8)))}"; } }
        }
        Console.WriteLine($"{k,-5} sampled {tot}: identical {same}, same-length-but-different {sameLen}, other {tot - same - sameLen}");
        if (sample != null) Console.WriteLine("   " + sample);
    }
    return;
}

if (cmd == "anistats") { AniStats.Run(ours, cls, Console.Out); return; }

if (cmd == "fieldstats") { FieldStats.Run(ours, cls, Console.Out); return; }

// Monster templates the new mounts need.
var monIds = newMounts.SelectMany(m => new[] { m.MonId, m.SaddleMonId }).Distinct().ToList();
var missingMon = monIds.Where(id => !ourMon.ContainsKey(id)).ToList();
Console.WriteLine($"new mounts: {newMounts.Count}, monster templates needed: {monIds.Count}, missing in ours: {missingMon.Count}");
foreach (var id in monIds.Where(id => !clsMon.ContainsKey(id)))
    Console.WriteLine($"  !! monster {id} not in Classic TMon either");

// Resource closure starting from the monsters' objects.
var need = new HashSet<(Kind, uint)>();
var todo = new Stack<(Kind, uint)>();
foreach (var id in monIds)
    if (clsMon.TryGetValue(id, out var m) && m.Obj != 0) todo.Push((Kind.Obj, m.Obj));

var pull = new List<Rec>();         // missing in ours -> copy from Classic
var collide = new List<string>();   // same ID, different bytes
var dangling = new List<string>();
while (todo.Count > 0)
{
    var key = todo.Pop();
    if (!need.Add(key)) continue;
    var (k, id) = key;
    var rec = cls.Get(k, id);
    if (rec == null) { dangling.Add($"{k} {id:X8}"); continue; }
    if (ours.Has(k, id))
    {
        if (k != Kind.Media && k != Kind.Mesh)
        {
            var mine = ours.Get(k, id)!;
            if (!mine.Raw.AsSpan().SequenceEqual(rec.Raw)) collide.Add($"{k} {id:X8} ours={mine.Raw.Length}B classic={rec.Raw.Length}B");
        }
        // Still walk: a shared record may point at something new.
    }
    else pull.Add(rec);
    foreach (var r in rec.Refs) todo.Push(r);
}

Console.WriteLine($"resources reachable: {need.Count}, to pull from Classic: {pull.Count}");
foreach (var g in pull.GroupBy(r => r.Kind))
    Console.WriteLine($"  {g.Key,-6} {g.Count(),5} records, {g.Sum(r => (long)r.Raw.Length) / 1024 / 1024.0:F1} MB " +
                      $"from {string.Join(", ", g.Select(r => cls.Idx[g.Key].Files[cls.Idx[g.Key].ById[r.Id].File]).Distinct())}");
Console.WriteLine($"collisions (same id, different bytes): {collide.Count}");
foreach (var c in collide.Take(30)) Console.WriteLine("  " + c);
Console.WriteLine($"dangling refs (not in Classic either): {dangling.Count}");
foreach (var d in dangling.Take(10)) Console.WriteLine("  " + d);
