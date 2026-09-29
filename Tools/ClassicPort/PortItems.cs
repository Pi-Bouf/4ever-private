// Phase 2 of the port (runs after Port): the items that make the ported mounts, their visuals, their
// icons, and the server-side DB migration.
//   - TItem.tcd:       Classic mount items (IT_PET/IK_PET, useValue = ported mount) — same layout as ours
//   - TItemVisual.tcd: their visuals — Classic record = ours + 1 trailing byte
//   - icons:           >= 60000 -> Data\Img\Custom\<id>.png from Classic's Img\Custom\Classic.pak
//                      (u16 count, then u16 id, u32 len, PNG); anything we can't show -> fallback icon
//   - SQL migration:   TMOUNTCHART rows, TMONSTERCHART + TITEMCHART rows cloned from siblings
// Re-runnable: rows added by a previous run are dropped first (ClassicPort.items.state).
using System.Globalization;
using System.Text;

namespace ClassicPort;

public class PortItems
{
    const int OurIconListCount = 2141;       // children of TClientCmd.tif's item-icon IMAGELIST (indices beyond crash RenderImgList)
    const ushort FallbackIcon = 1775;        // an existing mount icon of ours
    const ushort ItemSibling = 30261;        // Polar Battle Bear (Permanent): IT_PET / IK_PET mount item in TITEMCHART
    const ushort DefMonSibling = 31025, UpgMonSibling = 31100;   // Brown Horse, plain / saddled, in TMONSTERCHART
    const int VisOurs = 63, VisClassic = 64;

    readonly string game, classic, migration;
    readonly TextWriter log;
    public PortItems(string game, string classic, string migration, TextWriter log) { this.game = game; this.classic = classic; this.migration = migration; this.log = log; }

    static Dictionary<ushort, byte[]> LoadVisuals(string path, int size)
    {
        var b = File.ReadAllBytes(path);
        var d = new Dictionary<ushort, byte[]>();
        int n = BitConverter.ToUInt16(b, 0);
        if (2 + n * size != b.Length) throw new InvalidDataException(path);
        for (int i = 0; i < n; i++) d.TryAdd(BitConverter.ToUInt16(b, 2 + size * i), b[(2 + size * i)..(2 + size * i + VisOurs)]);
        return d;
    }

    static Dictionary<ushort, byte[]> LoadPak(string path)
    {
        var b = File.ReadAllBytes(path);
        int n = BitConverter.ToUInt16(b, 0), p = 2;
        var d = new Dictionary<ushort, byte[]>();
        for (int i = 0; i < n; i++)
        {
            ushort id = BitConverter.ToUInt16(b, p); int len = BitConverter.ToInt32(b, p + 2); p += 6;
            d[id] = b[p..(p + len)]; p += len;
        }
        if (p != b.Length) throw new InvalidDataException(path);
        return d;
    }

    public void Run(bool apply, IReadOnlyCollection<Mount> newMounts, IReadOnlyCollection<ushort> newMonsters)
    {
        string itemPath = Path.Combine(game, "Tcd", "TItem.tcd"), visPath = Path.Combine(game, "Tcd", "TItemVisual.tcd");
        string mountPath = Path.Combine(game, "Tcd", "TMount.tcd"), state = Path.Combine(game, "Tcd", "ClassicPort.items.state");
        string customDir = Path.Combine(game, "Data", "Img", "Custom");

        // ---- undo a previous run ----------------------------------------------------------------------
        var prevItems = new HashSet<ushort>(); var prevVis = new HashSet<ushort>(); var prevPng = new List<string>();
        if (File.Exists(state))
            foreach (var line in File.ReadAllLines(state))
            {
                var kv = line.Split('=', 2);
                if (kv[0] == "item") prevItems.Add(ushort.Parse(kv[1]));
                else if (kv[0] == "visual") prevVis.Add(ushort.Parse(kv[1]));
                else if (kv[0] == "png") prevPng.Add(kv[1]);
            }
        var ourItems = TItem.Load(itemPath).Where(i => !prevItems.Contains(i.Id)).ToList();
        var ourItemIds = ourItems.Select(i => i.Id).ToHashSet();
        var ourVis = LoadVisuals(visPath, VisOurs);
        foreach (var v in prevVis) ourVis.Remove(v);
        var ourCustom = Directory.GetFiles(customDir, "*.png").Select(f => Path.GetFileName(f)).Except(prevPng)
            .Select(Path.GetFileNameWithoutExtension).Where(s => int.TryParse(s, out _)).Select(s => int.Parse(s!)).ToHashSet();

        var clsItems = TItem.Load(Path.Combine(classic, "Tcd", "TItem.tcd"));
        var clsVis = LoadVisuals(Path.Combine(classic, "Tcd", "TItemVisual.tcd"), VisClassic);
        var pak = LoadPak(Path.Combine(classic, "Data", "Img", "Custom", "Classic.pak"));

        // ---- which icons can we show? -----------------------------------------------------------------
        var pngs = new Dictionary<ushort, byte[]>();
        bool Showable(ushort icon)
        {
            if (icon < 60000) return icon < OurIconListCount;
            if (ourCustom.Contains(icon)) return true;
            if (pak.TryGetValue(icon, out var png)) { pngs[icon] = png; return true; }
            return false;
        }

        // ---- items + visuals --------------------------------------------------------------------------
        var mountIds = newMounts.Select(m => m.Id).ToHashSet();
        var addItems = clsItems.Where(c => c.Type == 12 && c.Kind == 23 && mountIds.Contains(c.UseValue) && !ourItemIds.Contains(c.Id)).ToList();
        var addVis = new Dictionary<ushort, byte[]>();
        var mountIcon = newMounts.ToDictionary(m => m.Id, m => m.Icon);
        int iconFallbacks = 0;
        foreach (var it in addItems)
            for (int k = 0; k < 5; k++)
            {
                ushort v = BitConverter.ToUInt16(it.Raw, it.TailOfs + 63 + 2 * k);
                if (v == 0 || ourVis.ContainsKey(v) || addVis.ContainsKey(v)) continue;
                byte[] rec;
                if (clsVis.TryGetValue(v, out var cv)) rec = (byte[])cv.Clone();
                else if (v >= 60000)
                {
                    // Classic lets m_wVisual carry an own-image icon id directly; ours needs a visual row.
                    var tmplItem = ourItems.First(o => o.Id == ItemSibling);
                    rec = (byte[])ourVis[BitConverter.ToUInt16(tmplItem.Raw, tmplItem.TailOfs + 63)].Clone();
                    BitConverter.GetBytes(v).CopyTo(rec, 0);
                    BitConverter.GetBytes(v).CopyTo(rec, 34);
                    log.WriteLine($"  visual {v}: icon id used as visual by Classic -> synthesized visual row (icon {v})");
                }
                else { log.WriteLine($"  item {it.Id}: visual {v} missing in Classic too"); continue; }
                ushort icon = BitConverter.ToUInt16(rec, 34);
                if (!Showable(icon))
                {
                    ushort fb = mountIcon.TryGetValue(it.UseValue, out var mi) && Showable(mi) ? mi : FallbackIcon;   // prefer the mount's own icon
                    BitConverter.GetBytes(fb).CopyTo(rec, 34); iconFallbacks++;
                    log.WriteLine($"  visual {v} ('{it.Name}'): icon {icon} unavailable -> {fb}");
                }
                addVis[v] = rec;
            }

        // ---- mount icons ------------------------------------------------------------------------------
        var mounts = TMount.Load(mountPath, false);
        var fixedMounts = mounts.Select(m =>
        {
            if (!mountIds.Contains(m.Id) || Showable(m.Icon)) return m;
            iconFallbacks++;
            log.WriteLine($"  mount {m.Id}: icon {m.Icon} unavailable -> fallback {FallbackIcon}");
            return m with { Icon = FallbackIcon };
        }).ToList();

        log.WriteLine($"items to add: {addItems.Count}, visuals: {addVis.Count}, icon PNGs: {pngs.Count}, icon fallbacks: {iconFallbacks}");

        // ---- SQL migration ----------------------------------------------------------------------------
        string sql = BuildSql(newMounts, newMonsters, addItems);
        if (!apply) { log.WriteLine("dry run — nothing written"); return; }

        TItem.Save(itemPath, ourItems.Concat(addItems).OrderBy(i => i.Id));
        using (var w = new BinaryWriter(File.Create(visPath)))
        {
            var all = ourVis.Concat(addVis).OrderBy(kv => kv.Key).ToList();
            w.Write((ushort)all.Count);
            foreach (var (_, rec) in all) w.Write(rec);
        }
        TMount.Save(mountPath, fixedMounts, withScale: false);
        foreach (var f in prevPng) File.Delete(Path.Combine(customDir, f));
        foreach (var (id, png) in pngs) File.WriteAllBytes(Path.Combine(customDir, $"{id}.png"), png);
        File.WriteAllText(migration, sql, new UTF8Encoding(false));
        File.WriteAllLines(state, addItems.Select(i => $"item={i.Id}").Concat(addVis.Keys.Select(v => $"visual={v}")).Concat(pngs.Keys.Select(p => $"png={p}.png")));
        log.WriteLine($"written: TItem +{addItems.Count}, TItemVisual +{addVis.Count}, {pngs.Count} PNGs, {Path.GetFileName(migration)}");
    }

    // ---------------------------------------------------------------------------------------------------
    static string N(string s) => "N'" + s.Replace("'", "''") + "'";

    /// <summary>TITEMCHART columns taken from the client record (same list as WEB gen:missing).</summary>
    static string ItemAssignments(ItemRec it)
    {
        byte[] r = it.Raw; int t = it.TailOfs;
        byte B(int o) => r[t + o];
        ushort W(int o) => BitConverter.ToUInt16(r, t + o);
        uint D(int o) => BitConverter.ToUInt32(r, t + o);
        return string.Join(", ", new[]
        {
            $"wItemID={it.Id}", $"szNAME={N(it.Name)}", $"bType={it.Type}", $"bKind={it.Kind}", $"wAttrID={BitConverter.ToUInt16(r, 4)}",
            $"wUseValue={it.UseValue}", $"dwSlotID={D(0)}", $"dwClassID={D(4)}", $"bPrmSlotID={B(8)}", $"bSubSlotID={B(9)}",
            $"bLevel={B(10)}", $"bCanRepair={B(11)}", $"dwDuraMax={D(12)}", $"bRefineMax={B(16)}", $"bMinRange={B(25)}",
            $"bMaxRange={B(26)}", $"bStack={B(27)}", $"bSlotCount={B(28)}", $"bCanGamble={B(29)}", $"bGambleProb={B(30)}",
            $"bDestroyProb={B(31)}", $"bCanGrade={B(34)}", $"bCanMagic={B(35)}", $"bCanRare={B(36)}", $"wDelayGroupID={W(37)}",
            $"dwDelay={D(39)}", $"bIsSpecial={B(45)}", $"wUseTime={W(46)}", $"bUseType={B(48)}", $"bCanWrap={B(81)}",
            $"dwCode={D(82)}", $"bCanColor={B(86)}",
        });
    }

    static string BuildSql(IReadOnlyCollection<Mount> mounts, IReadOnlyCollection<ushort> newMonsters, List<ItemRec> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Mounts ported from the 4Classic client (Tools/ClassicPort — generated, do not edit by hand).");
        sb.AppendLine("--");
        sb.AppendLine("-- TMOUNTCHART: mount -> plain / saddled recall monster.");
        sb.AppendLine($"-- TMONSTERCHART: the recall monsters, cloned from Brown Horse ({DefMonSibling} plain / {UpgMonSibling} saddled).");
        sb.AppendLine($"-- TITEMCHART: the items that make the mounts, cloned from {ItemSibling} with the client-file fields overwritten");
        sb.AppendLine("--             (same technique and column list as 006_titemchart_missing_mountpet_items.sql).");
        sb.AppendLine("-- Monsters that already exist (e.g. 31002-31009) are left untouched: they are already mount monsters.");
        sb.AppendLine("-- NOTE: chart tables load once at startup - RESTART TMapSvr/TWorldSvr after applying.");
        sb.AppendLine("-- Idempotent: every insert is guarded by IF NOT EXISTS.");
        sb.AppendLine("USE [TGame_gsp];");
        sb.AppendLine("GO");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("GO");
        sb.AppendLine();

        var saddled = mounts.Select(m => m.SaddleMonId).ToHashSet();
        foreach (var id in newMonsters.OrderBy(x => x))
        {
            ushort sib = saddled.Contains(id) && !mounts.Any(m => m.MonId == id) ? UpgMonSibling : DefMonSibling;
            sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM dbo.TMONSTERCHART WHERE wID = {id}) AND EXISTS (SELECT 1 FROM dbo.TMONSTERCHART WHERE wID = {sib})");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    SELECT * INTO #m FROM dbo.TMONSTERCHART WHERE wID = {sib};");
            sb.AppendLine($"    UPDATE #m SET wID = {id};");
            sb.AppendLine("    INSERT INTO dbo.TMONSTERCHART SELECT * FROM #m;");
            sb.AppendLine("    DROP TABLE #m;");
            sb.AppendLine("END");
            sb.AppendLine("GO");
        }
        sb.AppendLine();
        foreach (var m in mounts.OrderBy(m => m.Id))
            sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM dbo.TMOUNTCHART WHERE wMountID = {m.Id}) INSERT INTO dbo.TMOUNTCHART (wMountID, wDefMonID, wUpgMonID) VALUES ({m.Id}, {m.MonId}, {m.SaddleMonId});");
        sb.AppendLine("GO");
        sb.AppendLine();
        foreach (var it in items.OrderBy(i => i.Id))
        {
            sb.AppendLine($"-- {it.Id}  {it.Name}  (mount {it.UseValue})");
            sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = {it.Id}) AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = {ItemSibling})");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    SELECT * INTO #t FROM dbo.TITEMCHART WHERE wItemID = {ItemSibling};");
            sb.AppendLine($"    UPDATE #t SET {ItemAssignments(it)};");
            sb.AppendLine("    INSERT INTO dbo.TITEMCHART SELECT * FROM #t;");
            sb.AppendLine("    DROP TABLE #t;");
            sb.AppendLine("END");
            sb.AppendLine("GO");
        }
        return sb.ToString();
    }
}
