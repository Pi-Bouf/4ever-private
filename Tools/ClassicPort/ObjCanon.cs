// Canonical form of an our-layout OBJ record, equal for two records the engine would load identically:
// keyed lists sorted by key (LoadOBJ inserts them into std::maps), OBJTEX pad byte zeroed (read into a
// dummy), pivot count dropped when there are no actions (LockOBJ recomputes it from mesh/ani nodes).
namespace ClassicPort;

public static class ObjCanon
{
    public static byte[] Canon(byte[] rec)
    {
        var r = new BinaryReader(new MemoryStream(rec));
        byte pivot = r.ReadByte();

        byte[] Bytes(int n) => r.ReadBytes(n);
        List<byte[]> Keyed(Func<byte[]> item) { int n = r.ReadInt32(); return Enumerable.Range(0, n).Select(_ => item()).ToList(); }
        byte[] Join(List<byte[]> items)
        {
            var sorted = items.OrderBy(x => BitConverter.ToUInt32(x, 0)).ThenBy(Convert.ToHexString);
            return [.. BitConverter.GetBytes(items.Count), .. sorted.SelectMany(x => x)];
        }
        byte[] Sfx() => Bytes(49);
        byte[] Snd() => Bytes(20);

        var pivots = Keyed(() => { var b = Bytes(8); return [.. b[4..], .. b[..4]]; });    // sort by id
        var osfx = Keyed(Sfx);
        var osnd = Keyed(Snd);
        var attrs = Keyed(() => { var h = Bytes(5); int sz = r.ReadInt32(); return [.. h, .. BitConverter.GetBytes(sz), .. Bytes(sz)]; });
        var acts = Keyed(() =>
        {
            var key = Bytes(4);
            var s = Join(Keyed(Sfx)); var n = Join(Keyed(Snd));
            var anis = Keyed(() => { var h = Bytes(8); return [.. h, .. Join(Keyed(Sfx)), .. Join(Keyed(Snd))]; });
            return [.. key, .. s, .. n, .. Join(anis)];
        });
        var clks = Keyed(() =>
        {
            var key = Bytes(4);
            var cls = Keyed(() =>
            {
                var k2 = Bytes(4);
                var meshes = Keyed(() =>
                {
                    var h = Bytes(8);
                    var texs = Keyed(() => { var t = Bytes(55); t[54] = 0; return t; }).ToList();
                    return [.. h, .. BitConverter.GetBytes(texs.Count), .. texs.SelectMany(x => x)];   // texture order matters
                });
                return [.. k2, .. Join(meshes)];
            });
            return [.. key, .. Join(cls)];
        });
        if (r.BaseStream.Position != rec.Length) throw new InvalidDataException("canon: trailing bytes");
        return [acts.Count > 0 ? pivot : (byte)0, .. Join(pivots), .. Join(osfx), .. Join(osnd), .. Join(attrs), .. Join(acts), .. Join(clks)];
    }
}
