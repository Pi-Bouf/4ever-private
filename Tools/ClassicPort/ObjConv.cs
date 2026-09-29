// Classic .TOB record -> our .TOB record (layout: CTachyonRes::LoadOBJ).
// Mapping derived statistically from the 5.5k objects present in both clients (see FieldStats):
//   - ours starts with BYTE m_bPivotCount; Classic writes it per action (before the action's ANI list)
//   - pivot pairs:  ours (index, id)          Classic (id, index)
//   - SFXINST:      ours pos,rot / func,tick  Classic rot,pos / tick,func
//   - OBJTEX:       type0/type1 swapped; ours' 2-side byte is Classic's last byte
//                   Classic flattened alpha blend 6/5 (INVSRCALPHA/SRCALPHA) to 1/2 — restored
//   - attribute and action maps are written in a different order (irrelevant: loaded into maps)
namespace ClassicPort;

public static class ObjConv
{
    public static int DroppedEmptySfx;

    public static byte[] FromClassic(byte[] src)
    {
        var r = new BinaryReader(new MemoryStream(src));
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        void Copy(int n) => w.Write(r.ReadBytes(n));
        int CopyInt() { int v = r.ReadInt32(); w.Write(v); return v; }
        // Classic has SFX instances with sfx id 0 (empty ANI slots); its engine skips them, ours would
        // resolve them to a NULL LPSFX and crash in CTachyonSFX::InitSFX. They are dropped here.
        void Sfx()
        {
            int n = r.ReadInt32();
            var kept = new List<byte[]>();
            for (int i = 0; i < n; i++)
            {
                var o = new MemoryStream();
                var ow = new BinaryWriter(o);
                byte[] keyId = r.ReadBytes(8);                      // key, sfx id
                byte[] a = r.ReadBytes(12), b = r.ReadBytes(12);    // Classic: rot, pos
                byte[] pivBias = r.ReadBytes(4 + 1);                // pivot, bias
                byte[] tick = r.ReadBytes(4), func = r.ReadBytes(4);
                byte[] del = r.ReadBytes(4);                        // delete-on-stop
                if (BitConverter.ToUInt32(keyId, 4) == 0) { DroppedEmptySfx++; continue; }
                ow.Write(keyId); ow.Write(b); ow.Write(a); ow.Write(pivBias); ow.Write(func); ow.Write(tick); ow.Write(del);
                kept.Add(o.ToArray());
            }
            w.Write(kept.Count);
            foreach (var k in kept) w.Write(k);
        }
        void Snd(int n) => Copy(20 * n);
        void Tex(int n)
        {
            for (int i = 0; i < n; i++)
            {
                Copy(8);                                            // tex ids
                byte t1 = r.ReadByte(), t0 = r.ReadByte();
                w.Write(t0); w.Write(t1);
                Copy(4 + 4 + 4 + 4);                                // colorid, color, order, op
                int dest = r.ReadInt32(), srcb = r.ReadInt32();
                if (dest == 1 && srcb == 2) (dest, srcb) = (6, 5);
                w.Write(dest); w.Write(srcb);
                Copy(4 + 4 + 3);                                    // blendop, intensity, minalpha, zenable, zwrite
                byte unused = r.ReadByte();
                byte[] dirAmb = r.ReadBytes(8);
                byte twoSide = r.ReadByte();
                w.Write(twoSide); w.Write(dirAmb); w.Write(unused);
            }
        }

        long pivotPos = ms.Position;
        w.Write((byte)0);                                           // pivot count, patched below
        byte pivots = 0;
        int np = CopyInt();
        for (int i = 0; i < np; i++) { uint id = r.ReadUInt32(), index = r.ReadUInt32(); w.Write(index); w.Write(id); }
        Sfx();
        Snd(CopyInt());
        int na = CopyInt();
        for (int i = 0; i < na; i++) { Copy(5); Copy(CopyInt()); }
        int nact = CopyInt();
        for (int i = 0; i < nact; i++)
        {
            Copy(4);
            Sfx();
            Snd(CopyInt());
            pivots = Math.Max(pivots, r.ReadByte());
            int nani = CopyInt();
            for (int j = 0; j < nani; j++) { Copy(8); Sfx(); Snd(CopyInt()); }
        }
        int nclk = CopyInt();
        for (int i = 0; i < nclk; i++)
        {
            Copy(4);
            int ncl = CopyInt();
            for (int j = 0; j < ncl; j++)
            {
                Copy(4);
                int nm = CopyInt();
                for (int k = 0; k < nm; k++) { Copy(8); Tex(CopyInt()); }
            }
        }
        if (r.BaseStream.Position != src.Length) throw new InvalidDataException("OBJ conversion did not consume the record");
        var o = ms.ToArray();
        o[pivotPos] = pivots;
        return o;
    }
}

public static class AniConv
{
    // ours ANIKEY field i is stored in Classic slot Map[i] (time, pos xyz, rot xyz, scale xyz, event)
    static readonly int[] Map = [0, 1, 3, 4, 5, 2, 6, 7, 9, 8, 10];

    /// <summary>Classic ANISET -> ours. Key fields permuted; Classic stores event + keyCount.</summary>
    public static byte[] FromClassic(byte[] src, out int consumed)
    {
        var r = new BinaryReader(new MemoryStream(src));
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int n = r.ReadInt32(); w.Write(n);
        w.Write(r.ReadBytes(5 * n));
        int nk = r.ReadInt32(); w.Write(nk);
        for (int i = 0; i < nk; i++)
        {
            var slots = Enumerable.Range(0, 11).Select(_ => r.ReadBytes(4)).ToArray();
            for (int f = 0; f < 10; f++) w.Write(slots[Map[f]]);
            w.Write(BitConverter.ToUInt32(slots[10]) - (uint)nk);
        }
        w.Write(r.ReadBytes(9));                                     // timescale, loop id, loop
        int size = r.ReadInt32(); w.Write(size);
        w.Write(r.ReadBytes(size));                                  // TAF blob (v300/301, unchanged)
        consumed = (int)r.BaseStream.Position;
        return ms.ToArray();
    }
}
