// Derives Classic's per-struct field order by comparing objects present in both clients.
// Each object is tokenized as ours-layout fields; for Classic the same byte ranges are used
// (sizes are equal), so a permuted field shows up as a low same-slot match rate and a high
// cross-slot rate.
namespace ClassicPort;

public record Tok(string Struct, string Field, byte[] Val);

public static class FieldStats
{
    static readonly (string, int)[] SfxInst = [("key", 4), ("sfx", 4), ("posX", 4), ("posY", 4), ("posZ", 4), ("rotX", 4), ("rotY", 4), ("rotZ", 4), ("pivot", 4), ("bias", 1), ("func", 4), ("tick", 4), ("del", 4)];
    static readonly (string, int)[] SndInst = [("key", 4), ("media", 4), ("func", 4), ("tick", 4), ("del", 4)];
    static readonly (string, int)[] ObjTex = [("tex0", 4), ("tex1", 4), ("type0", 1), ("type1", 1), ("colorid", 4), ("color", 4), ("order", 4), ("op", 4), ("dest", 4), ("src", 4), ("blendop", 4), ("intensity", 4), ("minalpha", 1), ("zenable", 1), ("zwrite", 1), ("twoside", 1), ("dirlight", 4), ("ambient", 4), ("pad", 1)];

    public static List<Tok> Tokenize(byte[] rec, bool classic)
    {
        var t = new List<Tok>();
        var r = new BinaryReader(new MemoryStream(rec));
        void S(string name, (string, int)[] layout) { foreach (var (f, n) in layout) t.Add(new Tok(name, f, r.ReadBytes(n))); }
        int Cnt(string name) { int v = r.ReadInt32(); t.Add(new Tok(name, "count", BitConverter.GetBytes(v))); return v; }

        if (!classic) t.Add(new Tok("obj", "pivotcount", r.ReadBytes(1)));
        int n = Cnt("pivots");
        for (int i = 0; i < n; i++) S("pivot", [("a", 4), ("b", 4)]);
        n = Cnt("osfx"); for (int i = 0; i < n; i++) S("sfxinst", SfxInst);
        n = Cnt("osnd"); for (int i = 0; i < n; i++) S("sndinst", SndInst);
        n = Cnt("attrs");
        for (int i = 0; i < n; i++) { S("attr", [("key", 4), ("type", 1)]); int sz = Cnt("attrsize"); t.Add(new Tok("attr", "value", r.ReadBytes(sz))); }
        n = Cnt("acts");
        for (int i = 0; i < n; i++)
        {
            S("act", [("key", 4)]);
            int m = Cnt("asfx"); for (int j = 0; j < m; j++) S("sfxinst", SfxInst);
            m = Cnt("asnd"); for (int j = 0; j < m; j++) S("sndinst", SndInst);
            if (classic) t.Add(new Tok("act", "extra", r.ReadBytes(1)));
            m = Cnt("anis");
            for (int j = 0; j < m; j++)
            {
                S("ani", [("key", 4), ("ani", 4)]);
                int k = Cnt("nsfx"); for (int q = 0; q < k; q++) S("sfxinst", SfxInst);
                k = Cnt("nsnd"); for (int q = 0; q < k; q++) S("sndinst", SndInst);
            }
        }
        n = Cnt("clks");
        for (int i = 0; i < n; i++)
        {
            S("clk", [("key", 4)]);
            int ncl = Cnt("cls");
            for (int j = 0; j < ncl; j++)
            {
                S("cl", [("key", 4)]);
                int nm = Cnt("meshes");
                for (int k = 0; k < nm; k++)
                {
                    S("mesh", [("key", 4), ("mesh", 4)]);
                    int nt = Cnt("texs");
                    for (int q = 0; q < nt; q++) S("objtex", ObjTex);
                }
            }
        }
        return t;
    }

    /// <summary>
    /// For every struct field of ours, count how often its value equals each same-sized Classic
    /// slot of the same struct instance. Only objects whose token shapes line up are used.
    /// </summary>
    public static void Run(Client ours, Client cls, TextWriter o)
    {
        // (struct, ourField, classicField) -> hits ; (struct, ourField) -> total
        var hits = new Dictionary<(string, string, string), int>();
        var total = new Dictionary<(string, string), int>();
        int used = 0, attrSame = 0, attrTot = 0, actSame = 0, actTot = 0;
        var joint = new Dictionary<string, int>();
        foreach (var e in cls.Idx[Kind.Obj].Entries)
        {
            if (!ours.Has(Kind.Obj, e.Id)) continue;
            var a = Tokenize(ours.Get(Kind.Obj, e.Id)!.Raw, false).Where(x => x.Struct != "obj").ToList();
            var b = Tokenize(cls.Get(Kind.Obj, e.Id)!.Raw, true).Where(x => x.Field != "extra").ToList();
            if (a.Count != b.Count || !a.Zip(b).All(p => p.First.Struct == p.Second.Struct && p.First.Field == p.Second.Field && p.First.Val.Length == p.Second.Val.Length)) continue;
            used++;
            for (int q = 0; q < a.Count; q++)
                if (a[q].Struct == "objtex" && a[q].Field == "dest")
                    joint[$"dest/src ours={BitConverter.ToInt32(a[q].Val)}/{BitConverter.ToInt32(a[q + 1].Val)} cls={BitConverter.ToInt32(b[q].Val)}/{BitConverter.ToInt32(b[q + 1].Val)}"] =
                        joint.GetValueOrDefault($"dest/src ours={BitConverter.ToInt32(a[q].Val)}/{BitConverter.ToInt32(a[q + 1].Val)} cls={BitConverter.ToInt32(b[q].Val)}/{BitConverter.ToInt32(b[q + 1].Val)}") + 1;
            string Bag(List<Tok> l, string st) => string.Join("|", l.Where(x => x.Struct == st).Select(x => x.Field + Convert.ToHexString(x.Val)).Order());
            attrTot++; if (Bag(a, "attr") == Bag(b, "attr")) attrSame++;
            actTot++; if (Bag(a, "act") == Bag(b, "act")) actSame++;
            // group into struct instances: consecutive tokens of the same struct starting at its first field
            int i = 0;
            while (i < a.Count)
            {
                string st = a[i].Struct;
                int j = i + 1;
                while (j < a.Count && a[j].Struct == st && a[j].Field != a[i].Field) j++;
                for (int x = i; x < j; x++)
                {
                    var tk = (st, a[x].Field);
                    total[tk] = total.GetValueOrDefault(tk) + 1;
                    for (int y = i; y < j; y++)
                        if (a[x].Val.Length == b[y].Val.Length && a[x].Val.AsSpan().SequenceEqual(b[y].Val))
                            hits[(st, a[x].Field, b[y].Field)] = hits.GetValueOrDefault((st, a[x].Field, b[y].Field)) + 1;
                }
                i = j;
            }
        }
        o.WriteLine($"objects with aligned shape: {used}");
        foreach (var (k, v) in joint.OrderByDescending(x => x.Value).Take(25)) o.WriteLine($"  joint {k}: {v}");
        o.WriteLine($"  attr multiset equal: {attrSame}/{attrTot}, act-key set equal: {actSame}/{actTot}");
        foreach (var ((st, f), tot) in total.OrderBy(k => k.Key))
        {
            if (tot < 5) continue;
            var best = hits.Where(h => h.Key.Item1 == st && h.Key.Item2 == f).OrderByDescending(h => h.Value).Take(3)
                           .Select(h => $"{h.Key.Item3}:{100.0 * h.Value / tot:F0}%");
            o.WriteLine($"  {st,-8} {f,-10} n={tot,-6} same-slot={100.0 * hits.GetValueOrDefault((st, f, f)) / tot,5:F1}%   best: {string.Join("  ", best)}");
        }
    }
}
