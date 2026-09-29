// Compares ANISET records (LoadANI layout) between clients: per-slot key-frame field matches and blob equality.
namespace ClassicPort;

public static class AniStats
{
    public record AniSet(List<byte[]> Refs, List<byte[][]> Keys, byte[] Tail, byte[] Blob, int Length);

    static readonly string[] KeyFields = ["time", "posX", "posY", "posZ", "rotX", "rotY", "rotZ", "sclX", "sclY", "sclZ", "event"];

    public static AniSet Parse(byte[] b)
    {
        var r = new BinaryReader(new MemoryStream(b));
        var refs = new List<byte[]>();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++) refs.Add(r.ReadBytes(5));
        var keys = new List<byte[][]>();
        n = r.ReadInt32();
        for (int i = 0; i < n; i++) keys.Add(Enumerable.Range(0, 11).Select(_ => r.ReadBytes(4)).ToArray());
        var tail = r.ReadBytes(9);                       // timescale, loopid, loop
        int size = r.ReadInt32();
        var blob = r.ReadBytes(size);
        return new AniSet(refs, keys, tail, blob, (int)r.BaseStream.Position);
    }

    public static void Run(Client ours, Client cls, TextWriter o)
    {
        int tot = 0, shape = 0, blobSame = 0, tailSame = 0, refsSame = 0;
        var hits = new int[11, 11]; int keyTot = 0;
        var blobVer = new Dictionary<string, int>();
        string? blobSample = null;
        var samples = new List<string>();
        var evMap = new Dictionary<string, int>();
        var deltas = new Dictionary<string, int>();
        var deltasXor = new Dictionary<string, int>();
        foreach (var e in cls.Idx[Kind.Ani].Entries)
        {
            if (!ours.Has(Kind.Ani, e.Id)) continue;
            tot++;
            var a = Parse(ours.Get(Kind.Ani, e.Id)!.Raw);
            var c = Parse(cls.Get(Kind.Ani, e.Id)!.Raw);
            if (a.Refs.Count != c.Refs.Count || a.Keys.Count != c.Keys.Count) continue;
            shape++;
            if (a.Refs.Zip(c.Refs).All(p => p.First.AsSpan().SequenceEqual(p.Second))) refsSame++;
            if (a.Tail.AsSpan().SequenceEqual(c.Tail)) tailSame++;
            if (a.Blob.AsSpan().SequenceEqual(c.Blob)) blobSame++;
            else if (a.Blob.Length >= 4 && c.Blob.Length >= 4)
            {
                string k = $"ours v{BitConverter.ToInt32(a.Blob)} len{(a.Blob.Length == c.Blob.Length ? "=" : "!=")} cls v{BitConverter.ToInt32(c.Blob)}";
                blobVer[k] = blobVer.GetValueOrDefault(k) + 1;
                if (blobSample == null && a.Blob.Length == c.Blob.Length)
                {
                    int d = 0; while (a.Blob[d] == c.Blob[d]) d++;
                    blobSample = $"{e.Id:X8} blob len {a.Blob.Length} first diff @{d}: ours {Convert.ToHexString(a.Blob, Math.Max(0, d - 16), 64)} cls {Convert.ToHexString(c.Blob, Math.Max(0, d - 16), 64)}";
                }
            }
            int ki = 0;
            foreach (var (ka, kc) in a.Keys.Zip(c.Keys))
            {
                long delta = (long)BitConverter.ToUInt32(kc[10]) - BitConverter.ToUInt32(ka[10]);
                string dk = $"delta-index={delta - ki} (n={a.Keys.Count})";
                deltas[delta == a.Keys.Count ? "event == ours + keyCount" : "OTHER"] = deltas.GetValueOrDefault(delta == a.Keys.Count ? "event == ours + keyCount" : "OTHER") + 1;
                deltasXor[$"xor={(BitConverter.ToUInt32(kc[10]) ^ BitConverter.ToUInt32(ka[10]))} idx={ki} n={a.Keys.Count}"] = 1 + deltasXor.GetValueOrDefault($"xor={(BitConverter.ToUInt32(kc[10]) ^ BitConverter.ToUInt32(ka[10]))} idx={ki} n={a.Keys.Count}");
                ki++;
                if (samples.Count < 6 && BitConverter.ToUInt32(ka[10]) != 0)
                    samples.Add($"ours  {string.Join(" ", ka.Select(Convert.ToHexString))} | cls   {string.Join(" ", kc.Select(Convert.ToHexString))} | tail ours {Convert.ToHexString(a.Tail)} cls {Convert.ToHexString(c.Tail)}");
                keyTot++;
                string ev = $"{BitConverter.ToUInt32(ka[10]):X}->{BitConverter.ToUInt32(kc[10]):X}";
                evMap[ev] = evMap.GetValueOrDefault(ev) + 1;
                for (int x = 0; x < 11; x++) for (int y = 0; y < 11; y++) if (ka[x].AsSpan().SequenceEqual(kc[y])) hits[x, y]++;
            }
        }
        o.WriteLine($"common anisets {tot}, same shape {shape}: refs equal {refsSame}, tail equal {tailSame}, blob equal {blobSame}");
        foreach (var (k, v) in blobVer.OrderByDescending(x => x.Value)) o.WriteLine($"  blob diff: {k}: {v}");
        if (blobSample != null) o.WriteLine("  " + blobSample);
        o.WriteLine($"  keyframes compared: {keyTot}");
        foreach (var (k, v) in deltas.OrderByDescending(x => x.Value).Take(8)) o.WriteLine($"  {k}: {v}");
        foreach (var (k, v) in deltasXor.OrderByDescending(x => x.Value).Take(20)) o.WriteLine($"  {k}: {v}");
        for (int x = 0; x < 11; x++)
        {
            var best = Enumerable.Range(0, 11).OrderByDescending(y => hits[x, y]).Take(3).Select(y => $"{KeyFields[y]}:{100.0 * hits[x, y] / Math.Max(1, keyTot):F0}%");
            o.WriteLine($"  ours {KeyFields[x],-6} same-slot {100.0 * hits[x, x] / Math.Max(1, keyTot),5:F1}%  best: {string.Join("  ", best)}");
        }
    }
}
