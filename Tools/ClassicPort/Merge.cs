// One-way follow-up to Port/PortItems (works on Game/ only — CLASSIC/ is not needed):
//   1. moves the records of the separate Classic.* packs into the base packs (PET packs, Character.TOB/TFX),
//      replacing our object in place where Classic's version overrode it;
//   2. renumbers the ported mounts / mount monsters / mount items so they follow our own sequences,
//      and writes the matching DB migration.
// Icons are moved into the atlas by IconMerge (separate step).
using System.Text;

namespace ClassicPort;

public class Merge
{
    public const string Marker = "ClassicPort.merged";

    // Classic pack -> base pack, per index (texture: the same chunks go into each detail level's PET pack).
    static readonly (string idx, string from, string to)[] Packs =
    [
        ("TClientO.IDX", @"OBJ\Classic.TOB", @"OBJ\Character.TOB"),
        ("TClientM.IDX", @"Mesh\Classic.TMH", @"Mesh\PET.TMH"),
        ("TClientA.IDX", @"Action\Classic.TAC", @"Action\PET.TAC"),
        ("TClientX.IDX", @"SFX\Classic.TFX", @"SFX\Character.TFX"),
        ("0_TClientS.IDX", @"Skin\2_Classic.TTX", @"Skin\0_PET.TTX"),
        ("1_TClientS.IDX", @"Skin\2_Classic.TTX", @"Skin\1_PET.TTX"),
        ("2_TClientS.IDX", @"Skin\2_Classic.TTX", @"Skin\2_PET.TTX"),
    ];
    const string MediaFrom = @"Media\Classic_1_285964683.TMD", MediaTo = @"Media\1_285964683.TMD";

    // Sequences the ported ids continue (see plan): mounts 40-49 then 55+ (50-54 are injected by InitTPET).
    const ushort FirstMonster = 31880, FirstItem = 31324;
    static readonly ushort[] HardcodedMounts = [50, 51, 52, 53, 54];

    readonly string game, migration;
    readonly TextWriter log;
    public Merge(string game, string migration, TextWriter log) { this.game = game; this.migration = migration; this.log = log; }

    string D(string rel) => Path.Combine(game, "Data", rel);
    string I(string name) => Path.Combine(game, "Index", name);

    static bool Chunked(string rel) => rel.EndsWith(".TTX", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".TFX", StringComparison.OrdinalIgnoreCase);

    /// <summary>Byte length of the record at pos: zlib chunk header for TTX/TFX, else up to the next record start.</summary>
    static int RecLen(byte[] file, uint pos, string rel, SortedSet<uint> starts)
    {
        if (Chunked(rel)) return 8 + BitConverter.ToInt32(file, (int)pos + 4);
        var next = starts.GetViewBetween(pos + 1, uint.MaxValue);
        return (int)((next.Count > 0 ? next.Min : (uint)file.Length) - pos);
    }

    /// <summary>Every (kind, id) -> record bytes, read through the current indexes (for the before/after check).</summary>
    Dictionary<string, byte[]> Snapshot()
    {
        var snap = new Dictionary<string, byte[]>();
        var c = new Client(game, "2_TClientS.IDX", isClassic: false);
        foreach (var k in new[] { Kind.Obj, Kind.Mesh, Kind.Ani, Kind.Sfx, Kind.Tex })
            foreach (var id in c.Idx[k].ById.Keys)
                snap[$"{k}:{id:X8}"] = c.Get(k, id)!.Raw;
        for (int d = 0; d < 2; d++)                                   // low texture detail levels
        {
            var idx = ClassicPort.Idx.Load(I($"{d}_TClientS.IDX"));
            foreach (var (id, e) in idx.ById)
                snap[$"Tex{d}:{id:X8}"] = Client.Unchunk(File.ReadAllBytes(D(idx.Files[e.File])), (int)e.Pos, out _);
        }
        return snap;
    }

    public void Run(bool apply)
    {
        if (File.Exists(Path.Combine(game, "Tcd", Marker))) { log.WriteLine("already merged (Game/Tcd/" + Marker + ")"); return; }
        foreach (var (_, from, _) in Packs) if (!File.Exists(D(from))) throw new FileNotFoundException(D(from));

        log.WriteLine("snapshot before...");
        var before = apply ? Snapshot() : null;

        // ---- 1. packs ---------------------------------------------------------------------------------
        var appended = new Dictionary<string, MemoryStream>();          // target rel -> bytes to append (shared by S idx)
        var targetLen = new Dictionary<string, long>();
        var idxs = Packs.Select(p => p.idx).Distinct().ToDictionary(n => n, n => ClassicPort.Idx.Load(I(n)));
        var rebuiltTob = (byte[]?)null;

        foreach (var (name, from, to) in Packs)
        {
            var idx = idxs[name];
            int fFrom = idx.Files.IndexOf(from), fTo = idx.Files.IndexOf(to);
            if (fFrom < 0 || fTo < 0) throw new InvalidDataException($"{name}: {from} or {to} not in file list");
            byte[] src = File.ReadAllBytes(D(from));
            var starts = new SortedSet<uint>(idx.Entries.Where(e => e.File == fFrom).Select(e => e.Pos));

            if (!targetLen.ContainsKey(to)) targetLen[to] = new FileInfo(D(to)).Length;
            bool fresh = !appended.ContainsKey(to);
            if (fresh) appended[to] = new MemoryStream();
            var buf = appended[to];

            // Objects that Classic overrode: the Classic entry comes first, ours (in Character.TOB) later.
            var overridden = name == "TClientO.IDX"
                ? idx.Entries.Where(e => e.File == fFrom).Select(e => e.Id)
                    .Where(id => idx.Entries.Any(o => o.Id == id && o.File == fTo)).ToHashSet()
                : new HashSet<uint>();

            var newPos = new Dictionary<uint, uint>();                   // pos in Classic file -> pos in target
            Dictionary<uint, uint>? tobMove = null;                      // old pos in Character.TOB -> new pos
            if (overridden.Count > 0)
            {
                // Rebuild Character.TOB: replace our record by Classic's, shift everything after it.
                byte[] ours = File.ReadAllBytes(D(to));
                var ourPosOf = overridden.ToDictionary(id => idx.Entries.First(o => o.Id == id && o.File == fTo).Pos, id => id);
                var clsPosOf = overridden.ToDictionary(id => id, id => idx.Entries.First(o => o.Id == id && o.File == fFrom).Pos);
                var outp = new MemoryStream(); tobMove = new();
                var rd = new BinaryReader(new MemoryStream(ours));
                while (rd.BaseStream.Position < ours.Length)
                {
                    uint p = (uint)rd.BaseStream.Position;
                    Client.ParseObj(rd, new(), out _);
                    int len = (int)(rd.BaseStream.Position - p);
                    tobMove[p] = (uint)outp.Position;
                    if (ourPosOf.TryGetValue(p, out var id))
                    {
                        uint cp = clsPosOf[id];
                        outp.Write(src, (int)cp, RecLen(src, cp, from, starts));
                        newPos[cp] = tobMove[p];
                    }
                    else outp.Write(ours, (int)p, len);
                }
                rebuiltTob = outp.ToArray();
                targetLen[to] = rebuiltTob.Length;
                log.WriteLine($"  {to}: {overridden.Count} objects replaced in place ({ours.Length} -> {rebuiltTob.Length} bytes before append)");
            }

            // Append every remaining Classic record (once per target file).
            foreach (var p in starts)
            {
                if (newPos.ContainsKey(p)) continue;
                if (fresh) { newPos[p] = (uint)(targetLen[to] + buf.Position); buf.Write(src, (int)p, RecLen(src, p, from, starts)); }
            }
            if (!fresh)
            {
                // Same Classic file already appended to this target (not the case today, guard anyway).
                throw new InvalidOperationException($"{to} targeted twice");
            }

            // Re-point entries, drop the overridden duplicates, remove the Classic file from the list.
            var list = new List<IdxEntry>();
            foreach (var e in idx.Entries)
            {
                if (e.File == fFrom) list.Add(new IdxEntry(e.Id, fTo, newPos[e.Pos]));
                else if (e.File == fTo && overridden.Contains(e.Id)) continue;
                else if (e.File == fTo && tobMove != null) list.Add(e with { Pos = tobMove[e.Pos] });
                else list.Add(e);
            }
            idx.Files.RemoveAt(fFrom);
            idx.Entries = list.Select(e => e.File > fFrom ? e with { File = e.File - 1 } : e).ToList();
            log.WriteLine($"  {name}: {starts.Count} records {from} -> {to}");
        }

        // Media: one file per sound, just rename.
        var w = ClassicPort.Idx.Load(I("TClientW.IDX"));
        int mi = w.Files.IndexOf(MediaFrom);
        if (mi >= 0) { w.Files[mi] = MediaTo; log.WriteLine($"  TClientW.IDX: {MediaFrom} -> {MediaTo}"); }

        // ---- 2. renumbering ---------------------------------------------------------------------------
        var (mountMap, monMap, itemMap) = BuildMaps();
        log.WriteLine($"renumber: {mountMap.Count(kv => kv.Key != kv.Value)} mounts, {monMap.Count} monsters, {itemMap.Count} items");
        string sql = BuildSql(mountMap, monMap, itemMap);

        if (!apply) { log.WriteLine("dry run — nothing written (use: merge --apply)"); return; }

        // write packs
        foreach (var (to, buf) in appended)
        {
            if (to == @"OBJ\Character.TOB" && rebuiltTob != null) File.WriteAllBytes(D(to), [.. rebuiltTob, .. buf.ToArray()]);
            else using (var fs = new FileStream(D(to), FileMode.Append)) buf.WriteTo(fs);
        }
        foreach (var (name, idx) in idxs) idx.Save(I(name));
        if (mi >= 0) { File.Move(D(MediaFrom), D(MediaTo)); w.Save(I("TClientW.IDX")); }
        foreach (var from in Packs.Select(p => p.from).Distinct()) File.Delete(D(from));

        log.WriteLine("checking every record through the new indexes...");
        var after = Snapshot();
        int diff = before!.Count(kv => !after.TryGetValue(kv.Key, out var a) || !a.AsSpan().SequenceEqual(kv.Value));
        log.WriteLine($"  records before {before.Count}, after {after.Count}, changed/missing {diff}");
        if (diff != 0) throw new InvalidDataException("merge changed record bytes — restore Game/ from git");

        ApplyRenumber(mountMap, monMap, itemMap);
        File.WriteAllText(migration, sql, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(game, "Tcd", Marker), $"merged {DateTime.Now:s}; Classic packs folded into base packs, ids renumbered\n");
        log.WriteLine($"written: packs, indexes, renumbered tcds, {Path.GetFileName(migration)}");
    }

    // ---------------------------------------------------------------------------------------------------
    (Dictionary<ushort, ushort> mount, Dictionary<ushort, ushort> mon, Dictionary<ushort, ushort> item) BuildMaps()
    {
        var st = new BinaryReader(File.OpenRead(Path.Combine(game, "Tcd", "ClassicPort.state")));
        var mounts = Enumerable.Range(0, st.ReadInt32()).Select(_ => st.ReadUInt16()).OrderBy(x => x).ToList();
        var mons = Enumerable.Range(0, st.ReadInt32()).Select(_ => st.ReadUInt16()).ToList();
        var replaced = Enumerable.Range(0, st.ReadInt32()).Select(_ => { ushort id = st.ReadUInt16(); st.ReadBytes(st.ReadInt32()); return id; }).ToHashSet();
        st.Dispose();
        var items = File.ReadAllLines(Path.Combine(game, "Tcd", "ClassicPort.items.state"))
            .Where(l => l.StartsWith("item=")).Select(l => ushort.Parse(l[5..])).OrderBy(x => x).ToList();

        var mountMap = new Dictionary<ushort, ushort>();
        ushort next = 40;
        foreach (var m in mounts) { while (HardcodedMounts.Contains(next)) next++; mountMap[m] = next++; }
        var monMap = mons.Where(m => !replaced.Contains(m)).OrderBy(x => x).Select((m, i) => (m, (ushort)(FirstMonster + i))).ToDictionary(x => x.m, x => x.Item2);
        var itemMap = items.Select((m, i) => (m, (ushort)(FirstItem + i))).ToDictionary(x => x.m, x => x.Item2);
        return (mountMap, monMap, itemMap);
    }

    void ApplyRenumber(Dictionary<ushort, ushort> mountMap, Dictionary<ushort, ushort> monMap, Dictionary<ushort, ushort> itemMap)
    {
        ushort Mn(ushort x) => monMap.TryGetValue(x, out var y) ? y : x;
        string tcd = Path.Combine(game, "Tcd");

        var mounts = TMount.Load(Path.Combine(tcd, "TMount.tcd"), false)
            .Select(m => m with { Id = mountMap.TryGetValue(m.Id, out var n) ? n : m.Id, MonId = Mn(m.MonId), SaddleMonId = Mn(m.SaddleMonId) })
            .OrderBy(m => m.Id).ToList();
        TMount.Save(Path.Combine(tcd, "TMount.tcd"), mounts, withScale: false);

        var mons = TMon.Load(Path.Combine(tcd, "TMon.tcd"), TMon.OurTail);
        foreach (var r in mons)
        {
            if (monMap.TryGetValue(r.Id, out var n)) { r.Id = n; BitConverter.GetBytes(n).CopyTo(r.Raw, 0); }
            for (int s = 0; s < 19; s++)                                 // equip items of a mount monster
            {
                int o = r.TailOfs + 16 + 2 * s;
                if (itemMap.TryGetValue(BitConverter.ToUInt16(r.Raw, o), out var ni)) BitConverter.GetBytes(ni).CopyTo(r.Raw, o);
            }
        }
        TMon.Save(Path.Combine(tcd, "TMon.tcd"), mons.OrderBy(m => m.Id));

        var items = TItem.Load(Path.Combine(tcd, "TItem.tcd"));
        foreach (var it in items)
        {
            if (!itemMap.TryGetValue(it.Id, out var n)) continue;
            it.Id = n; BitConverter.GetBytes(n).CopyTo(it.Raw, 0);
            if (mountMap.TryGetValue(it.UseValue, out var nm)) { it.UseValue = nm; BitConverter.GetBytes(nm).CopyTo(it.Raw, it.TailOfs - 2); }
        }
        TItem.Save(Path.Combine(tcd, "TItem.tcd"), items.OrderBy(i => i.Id));

        // Keep the port's state files in line with the new ids.
        string stPath = Path.Combine(tcd, "ClassicPort.state");
        var st = new BinaryReader(File.OpenRead(stPath));
        var sm = Enumerable.Range(0, st.ReadInt32()).Select(_ => st.ReadUInt16()).ToList();
        var so = Enumerable.Range(0, st.ReadInt32()).Select(_ => st.ReadUInt16()).ToList();
        var sb = Enumerable.Range(0, st.ReadInt32()).Select(_ => (st.ReadUInt16(), st.ReadBytes(st.ReadInt32()))).ToList();
        st.Dispose();
        using (var wr = new BinaryWriter(File.Create(stPath)))
        {
            wr.Write(sm.Count); foreach (var m in sm) wr.Write(mountMap[m]);
            wr.Write(so.Count); foreach (var m in so) wr.Write(Mn(m));
            wr.Write(sb.Count); foreach (var (id, raw) in sb) { wr.Write(id); wr.Write(raw.Length); wr.Write(raw); }
        }
        string isPath = Path.Combine(tcd, "ClassicPort.items.state");
        File.WriteAllLines(isPath, File.ReadAllLines(isPath).Select(l => l.StartsWith("item=") ? "item=" + itemMap[ushort.Parse(l[5..])] : l));
    }

    static string BuildSql(Dictionary<ushort, ushort> mountMap, Dictionary<ushort, ushort> monMap, Dictionary<ushort, ushort> itemMap)
    {
        var moved = mountMap.Where(kv => kv.Key != kv.Value).OrderBy(kv => kv.Key).ToList();
        var firstItem = itemMap.OrderBy(kv => kv.Key).First();
        var sb = new StringBuilder();
        sb.AppendLine("-- Renumber the mounts ported from 4Classic (010) so they follow our own sequences");
        sb.AppendLine("-- (Tools/ClassicPort `merge` — generated, do not edit by hand):");
        sb.AppendLine($"--   mounts   {string.Join(",", mountMap.Keys.OrderBy(x => x).Take(1))}… -> 40-49, 55-{mountMap.Values.Max()}   (50-54 are hardcoded in the client)");
        sb.AppendLine($"--   monsters -> {monMap.Values.Min()}-{monMap.Values.Max()}   (after 31879, Polar Battle Bear)");
        sb.AppendLine($"--   items    -> {itemMap.Values.Min()}-{itemMap.Values.Max()}   (after 31323, Hopping Cow)");
        sb.AppendLine("-- Old and new mount ranges overlap, so mount ids go through old+10000 first.");
        sb.AppendLine("-- Runs only while the old ids are still present (idempotent; no-op on an already renumbered DB).");
        sb.AppendLine("-- NOTE: chart tables load once at startup - RESTART TMapSvr/TWorldSvr after applying.");
        sb.AppendLine("USE [TGame_gsp];");
        sb.AppendLine("GO");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine($"IF EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = {firstItem.Key}) AND NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = {firstItem.Value})");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    BEGIN TRAN;");
        sb.AppendLine();
        sb.AppendLine("    -- items: mount reference first (keyed by the old item id), then the item id itself");
        foreach (var (o, n) in itemMap.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"    UPDATE dbo.TITEMCHART SET wItemID = {n} WHERE wItemID = {o};");
            sb.AppendLine($"    UPDATE dbo.TITEMTABLE SET wItemID = {n} WHERE wItemID = {o};");
        }
        sb.AppendLine();
        sb.AppendLine("    -- monsters");
        foreach (var (o, n) in monMap.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"    UPDATE dbo.TMONSTERCHART SET wID = {n} WHERE wID = {o};");
            sb.AppendLine($"    UPDATE dbo.TMOUNTCHART SET wDefMonID = {n} WHERE wDefMonID = {o};");
            sb.AppendLine($"    UPDATE dbo.TMOUNTCHART SET wUpgMonID = {n} WHERE wUpgMonID = {o};");
        }
        sb.AppendLine();
        sb.AppendLine("    -- mounts, pass 1: park on old+10000");
        string list = string.Join(", ", moved.Select(kv => kv.Key));
        sb.AppendLine($"    UPDATE dbo.TMOUNTCHART SET wMountID = wMountID + 10000 WHERE wMountID IN ({list});");
        sb.AppendLine($"    UPDATE dbo.TPETTABLE SET wPetID = wPetID + 10000 WHERE wPetID IN ({list});");
        sb.AppendLine($"    UPDATE dbo.TITEMCHART SET wUseValue = wUseValue + 10000 WHERE bType = 12 AND wItemID BETWEEN {itemMap.Values.Min()} AND {itemMap.Values.Max()} AND wUseValue IN ({list});");
        sb.AppendLine("    -- mounts, pass 2: final ids");
        foreach (var (o, n) in moved)
        {
            sb.AppendLine($"    UPDATE dbo.TMOUNTCHART SET wMountID = {n} WHERE wMountID = {o + 10000};");
            sb.AppendLine($"    UPDATE dbo.TPETTABLE SET wPetID = {n} WHERE wPetID = {o + 10000};");
            sb.AppendLine($"    UPDATE dbo.TITEMCHART SET wUseValue = {n} WHERE bType = 12 AND wUseValue = {o + 10000};");
        }
        sb.AppendLine();
        sb.AppendLine("    COMMIT;");
        sb.AppendLine("END");
        sb.AppendLine("GO");
        return sb.ToString();
    }
}
