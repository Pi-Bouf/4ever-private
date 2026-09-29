// The actual port: Classic mounts -> Game/.
//   - TMount rows for mount IDs we lack
//   - TMon templates they need (missing, or ours is a "_" placeholder)
//   - every resource reachable from those templates' objects that we lack, converted to our layouts,
//     written to Data/*/Classic.* and appended to our Index/*.IDX
// Re-runnable: previously ported rows/files/index entries are dropped first.
namespace ClassicPort;

public class Port
{
    const string Tag = "Classic";
    static readonly Dictionary<Kind, string> OutFile = new()
    {
        [Kind.Obj] = @"OBJ\Classic.TOB",
        [Kind.Mesh] = @"Mesh\Classic.TMH",
        [Kind.Ani] = @"Action\Classic.TAC",
        [Kind.Tex] = @"Skin\2_Classic.TTX",
        [Kind.Sfx] = @"SFX\Classic.TFX",
    };
    static readonly Dictionary<Kind, string> IdxName = new()
    {
        [Kind.Obj] = "TClientO.IDX", [Kind.Mesh] = "TClientM.IDX", [Kind.Ani] = "TClientA.IDX",
        [Kind.Sfx] = "TClientX.IDX", [Kind.Media] = "TClientW.IDX",
    };

    readonly string game, classic;
    readonly TextWriter log;
    public Port(string game, string classic, TextWriter log) { this.game = game; this.classic = classic; this.log = log; }

    /// <summary>Structural keys of an our-layout object: pivots, actions, action/ani ids, clk/cl/mesh paths.</summary>
    static HashSet<string> ObjKeys(byte[] rec)
    {
        var set = new HashSet<string>();
        string clk = "", cl = "", act = "";
        foreach (var t in FieldStats.Tokenize(rec, false))
        {
            uint v = t.Val.Length >= 4 ? BitConverter.ToUInt32(t.Val) : t.Val[0];
            switch (t.Struct, t.Field)
            {
                case ("act", "key"): act = $"act:{v:X}"; set.Add(act); break;
                case ("ani", "key"): set.Add($"{act}/ani:{v:X}"); break;
                case ("clk", "key"): clk = $"clk:{v:X}"; set.Add(clk); break;
                case ("cl", "key"): cl = $"{clk}/cl:{v:X}"; set.Add(cl); break;
                case ("mesh", "key"): set.Add($"{cl}/mesh:{v:X}"); break;
            }
        }
        return set;
    }

    /// <summary>Removes a previous port's files/entries from an index (in memory).</summary>
    static void Strip(Idx idx)
    {
        var drop = idx.Files.Select((f, i) => (f, i)).Where(x => Path.GetFileName(x.f).Contains(Tag, StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToHashSet();
        if (drop.Count == 0) return;
        var remap = new Dictionary<int, int>();
        var files = new List<string>();
        for (int i = 0; i < idx.Files.Count; i++) if (!drop.Contains(i)) { remap[i] = files.Count; files.Add(idx.Files[i]); }
        idx.Files = files;
        idx.Entries = idx.Entries.Where(e => !drop.Contains(e.File)).Select(e => e with { File = remap[e.File] }).ToList();
        idx.ById = idx.Entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
    }

    public (List<Mount> mounts, List<ushort> monsters) Run(bool apply)
    {
        // ---- load -------------------------------------------------------------------------------------
        string ourMountPath = Path.Combine(game, "Tcd", "TMount.tcd"), ourMonPath = Path.Combine(game, "Tcd", "TMon.tcd");
        string state = Path.Combine(game, "Tcd", "ClassicPort.state");          // ids we added last run
        var prevMounts = new HashSet<ushort>(); var prevMons = new HashSet<ushort>();
        var prevMonBackup = new Dictionary<ushort, byte[]>();
        if (File.Exists(state))
        {
            using var r = new BinaryReader(File.OpenRead(state));
            int n = r.ReadInt32(); for (int i = 0; i < n; i++) prevMounts.Add(r.ReadUInt16());
            n = r.ReadInt32(); for (int i = 0; i < n; i++) prevMons.Add(r.ReadUInt16());
            n = r.ReadInt32(); for (int i = 0; i < n; i++) { ushort id = r.ReadUInt16(); prevMonBackup[id] = r.ReadBytes(r.ReadInt32()); }
        }

        var ourMounts = TMount.Load(ourMountPath, false).Where(m => !prevMounts.Contains(m.Id)).ToList();
        var ourMonList = TMon.Load(ourMonPath, TMon.OurTail)
            .Where(m => !prevMons.Contains(m.Id) || prevMonBackup.ContainsKey(m.Id))
            .Select(m => prevMonBackup.TryGetValue(m.Id, out var raw) ? TMon.ParseOne(raw, TMon.OurTail) : m).ToList();
        var ourMon = ourMonList.ToDictionary(m => m.Id);
        var clsMounts = TMount.Load(Path.Combine(classic, "Tcd", "TMount.tcd"), true);
        var clsMon = TMon.Load(Path.Combine(classic, "Tcd", "TMon.tcd"), TMon.ClassicTail).ToDictionary(m => m.Id);

        var ours = new Client(game, "2_TClientS.IDX", isClassic: false);
        var cls = new Client(classic, "TClientS.IDX", isClassic: true);
        foreach (var idx in ours.Idx.Values) Strip(idx);
        var texIdx = Enumerable.Range(0, 3).Select(i => ClassicPort.Idx.Load(Path.Combine(game, "Index", $"{i}_TClientS.IDX"))).ToArray();
        foreach (var idx in texIdx) Strip(idx);

        // ---- mounts + monster templates ---------------------------------------------------------------
        var have = ourMounts.Select(m => m.Id).Concat(new ushort[] { 50, 51, 52, 53, 54 }).ToHashSet();   // 50-54: InitTPET
        var newMounts = clsMounts.Where(m => !have.Contains(m.Id)).ToList();

        // Our meshes use 16-bit indices; Classic added a few >65535-vertex meshes. Skip mounts that need one.
        bool NeedsBigMesh(uint obj)
        {
            var seenB = new HashSet<(Kind, uint)>();
            var st = new Stack<(Kind, uint)>(); st.Push((Kind.Obj, obj));
            while (st.Count > 0)
            {
                var (k, id) = st.Pop();
                if (!seenB.Add((k, id)) || k == Kind.Media || ours.Has(k, id) || !cls.Has(k, id)) continue;
                var rec = cls.Get(k, id)!;
                if (k == Kind.Mesh && BitConverter.ToUInt32(rec.Raw, 29 + 64 * (int)BitConverter.ToUInt32(rec.Raw, 4)) > ushort.MaxValue) return true;
                foreach (var r in rec.Refs) st.Push(r);
            }
            return false;
        }
        uint ObjOf(ushort mon) => ourMon.TryGetValue(mon, out var m) && m.Name == clsMon[mon].Name ? m.Obj : clsMon[mon].Obj;
        // Classic ships unfinished mounts whose monster has no object at all (Red Fox, mount 100) -> would crash SetPIVOT.
        foreach (var m in newMounts.Where(m => ObjOf(m.MonId) == 0 || ObjOf(m.SaddleMonId) == 0).ToList())
        {
            log.WriteLine($"  skip mount {m.Id} '{clsMon[m.MonId].Name}': its monster has no model (object 0) in Classic");
            newMounts.Remove(m);
        }
        // ...and unfinished ones whose object has no rider pivot (ID_PIVOT_MOUNT): no textures/animations either
        // (Turtle, Saber, Rat, Fox, Dragon Element 138-145) -> white, frozen, not ridable.
        bool HasRiderPivot(uint obj)
        {
            byte[]? rec = ours.Has(Kind.Obj, obj) ? ours.Get(Kind.Obj, obj)!.Raw : cls.Has(Kind.Obj, obj) ? ObjConv.FromClassic(cls.Get(Kind.Obj, obj)!.Raw) : null;
            return rec != null && FieldStats.Tokenize(rec, false).Any(x => x.Struct == "pivot" && x.Field == "b" && BitConverter.ToUInt32(x.Val) == 0x5E7C);
        }
        foreach (var m in newMounts.Where(m => !HasRiderPivot(ObjOf(m.MonId)) || !HasRiderPivot(ObjOf(m.SaddleMonId))).ToList())
        {
            log.WriteLine($"  skip mount {m.Id} '{clsMon[m.MonId].Name}': unfinished in Classic (no rider pivot)");
            newMounts.Remove(m);
        }
        foreach (var m in newMounts.Where(m => NeedsBigMesh(ObjOf(m.MonId)) || NeedsBigMesh(ObjOf(m.SaddleMonId))).ToList())
        {
            log.WriteLine($"  skip mount {m.Id} '{clsMon[m.MonId].Name}': needs a mesh with >65535 vertices (engine uses 16-bit indices)");
            newMounts.Remove(m);
        }
        var monIds = newMounts.SelectMany(m => new[] { m.MonId, m.SaddleMonId }).Distinct().OrderBy(x => x).ToList();

        var addMon = new List<MonRec>();
        var replaced = new Dictionary<ushort, byte[]>();
        foreach (var id in monIds)
        {
            var c = clsMon[id];
            if (ourMon.TryGetValue(id, out var mine) && mine.Name == c.Name) continue;
            var conv = TMon.FromClassic(c, out var warn);
            if (warn != null) log.WriteLine("  warn: " + warn);
            if (mine != null) { replaced[id] = mine.Raw; log.WriteLine($"  monster {id}: replacing our '{mine.Name}' with '{c.Name}'"); }
            addMon.Add(conv);
        }
        log.WriteLine($"mounts to add: {newMounts.Count}; monster templates to add/replace: {addMon.Count} ({replaced.Count} replaced)");

        // ---- resource closure -------------------------------------------------------------------------
        var roots = monIds.Select(id => addMon.FirstOrDefault(m => m.Id == id)?.Obj ?? ourMon[id].Obj).Where(o => o != 0);
        ObjConv.DroppedEmptySfx = 0;
        var seen = new HashSet<(Kind, uint)>();
        var todo = new Stack<(Kind, uint)>(roots.Select(o => (Kind.Obj, o)));
        var pull = new Dictionary<Kind, List<(uint id, byte[] rec)>>();
        foreach (Kind k in Enum.GetValues<Kind>()) pull[k] = new();
        var media = new List<(uint id, string file, uint pos)>();
        int kept = 0, overrides = 0;
        var big = new HashSet<uint>();
        var clsTexFiles = new Dictionary<string, byte[]>();
        while (todo.Count > 0)
        {
            var (k, id) = todo.Pop();
            if (!seen.Add((k, id))) continue;
            if (k == Kind.Obj && ours.Has(k, id) && cls.Has(k, id))
            {
                // Same object id in both. Classic often extended a shared object with new cloth/mesh entries
                // (e.g. Rathapanda skins on the Black Panda object). If Classic's version keeps everything ours
                // has, override ours with it; otherwise keep ours.
                var mine = ours.Get(k, id)!.Raw;
                var theirs = ObjConv.FromClassic(cls.Get(k, id)!.Raw);
                if (!ObjCanon.Canon(mine).AsSpan().SequenceEqual(ObjCanon.Canon(theirs)))
                {
                    var a = ObjKeys(mine); var b = ObjKeys(theirs);
                    if (a.IsProperSubsetOf(b))
                    {
                        log.WriteLine($"  object {id:X8}: Classic version extends ours (+{b.Count - a.Count} entries) -> override");
                        pull[k].Add((id, theirs));
                        foreach (var r in cls.Get(k, id)!.Refs) todo.Push(r);
                        overrides++;
                        continue;
                    }
                    log.WriteLine($"  object {id:X8}: differs from Classic but is not a subset -> keeping ours");
                }
            }
            if (ours.Has(k, id) || (k == Kind.Tex && texIdx[2].ById.ContainsKey(id))) { kept++; continue; }   // engine will follow our own record's refs
            if (!cls.Idx[k].ById.TryGetValue(id, out var ce)) { log.WriteLine($"  dangling {k} {id:X8}"); continue; }
            if (k == Kind.Media) { media.Add((id, cls.Idx[k].Files[ce.File], ce.Pos)); continue; }

            var src = cls.Get(k, id)!;
            if (k == Kind.Mesh && BitConverter.ToUInt32(src.Raw, 29 + 64 * (int)BitConverter.ToUInt32(src.Raw, 4)) > 65535)
            {
                uint nodes = BitConverter.ToUInt32(src.Raw, 4);
                log.WriteLine($"  BIG mesh {id:X8}: {BitConverter.ToUInt32(src.Raw, 29 + 64 * (int)nodes)} verts, {nodes} bones, file {cls.Idx[k].Files[ce.File]}");
                big.Add(id);
                continue;
            }
            byte[] rec = k switch
            {
                Kind.Obj => ObjConv.FromClassic(src.Raw),
                Kind.Mesh => MeshConv.FromClassic(src.Raw, out _),
                Kind.Ani => AniConv.FromClassic(src.Raw, out _),
                Kind.Tex => TexConvert.FromClassic(ClsFile(cls.DataPath(k, ce)), (int)ce.Pos, out _),
                _ => src.Raw,                                         // SFX: same layout
            };
            pull[k].Add((id, rec));
            foreach (var r in src.Refs) todo.Push(r);
        }
        byte[] ClsFile(string p) => clsTexFiles.TryGetValue(p, out var b) ? b : clsTexFiles[p] = File.ReadAllBytes(p);

        log.WriteLine($"objects overridden by Classic's extended version: {overrides}");
        log.WriteLine($"resources reachable {seen.Count}: already ours {kept}, to add:  (dropped {ObjConv.DroppedEmptySfx} empty SFX instances)");
        foreach (var (k, l) in pull.Where(p => p.Value.Count > 0))
            log.WriteLine($"  {k,-5} {l.Count,4} records  {l.Sum(x => (long)x.rec.Length) / 1048576.0,6:F1} MB (uncompressed)");
        if (media.Count > 0) log.WriteLine($"  Media {media.Count,4} files");

        var result = (newMounts, addMon.Select(m => m.Id).ToList());
        if (!apply) { log.WriteLine("dry run — nothing written (use: port --apply)"); return result; }

        // ---- write data files + index entries ---------------------------------------------------------
        foreach (var (k, l) in pull.Where(p => p.Value.Count > 0 && p.Key != Kind.Media))
        {
            string rel = OutFile[k];
            var entries = new List<(uint id, uint pos)>();
            using (var fs = File.Create(Path.Combine(game, "Data", rel)))
                foreach (var (id, rec) in l.OrderBy(x => x.id))
                {
                    entries.Add((id, (uint)fs.Position));
                    fs.Write(k is Kind.Tex or Kind.Sfx ? Client.Chunk(rec) : rec);
                }
            var targets = k == Kind.Tex ? texIdx : [ours.Idx[k]];
            foreach (var idx in targets)
            {
                int fi = idx.Files.Count;
                idx.Files.Add(rel);
                // Inserted FIRST: the engine keeps the first entry of an id, so overridden objects win over ours.
                idx.Entries.InsertRange(0, entries.Select(e => new IdxEntry(e.id, fi, e.pos)));
            }
        }
        if (media.Count > 0)
        {
            var idx = ours.Idx[Kind.Media];
            foreach (var (id, file, pos) in media)
            {
                string rel = Path.Combine(Path.GetDirectoryName(file)!, Tag + "_" + Path.GetFileName(file));
                File.Copy(Path.Combine(classic, "Data", file), Path.Combine(game, "Data", rel), true);
                int fi = idx.Files.Count;
                idx.Files.Add(rel);
                idx.Entries.Add(new IdxEntry(id, fi, pos));
            }
        }
        foreach (var (k, name) in IdxName) ours.Idx[k].Save(Path.Combine(game, "Index", name));
        for (int i = 0; i < 3; i++) texIdx[i].Save(Path.Combine(game, "Index", $"{i}_TClientS.IDX"));

        // ---- tcd tables -------------------------------------------------------------------------------
        var monOut = ourMonList.Where(m => !replaced.ContainsKey(m.Id)).Concat(addMon).OrderBy(m => m.Id).ToList();
        TMon.Save(ourMonPath, monOut);
        var mountOut = ourMounts.Concat(newMounts).OrderBy(m => m.Id).ToList();
        TMount.Save(ourMountPath, mountOut, withScale: false);

        using (var w = new BinaryWriter(File.Create(state)))
        {
            w.Write(newMounts.Count); foreach (var m in newMounts) w.Write(m.Id);
            w.Write(addMon.Count); foreach (var m in addMon) w.Write(m.Id);
            w.Write(replaced.Count); foreach (var (id, raw) in replaced) { w.Write(id); w.Write(raw.Length); w.Write(raw); }
        }
        log.WriteLine($"written: TMount {mountOut.Count} rows, TMon {monOut.Count} templates, data files + indexes updated");
        return result;
    }
}
