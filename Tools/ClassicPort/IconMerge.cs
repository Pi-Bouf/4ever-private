// Moves the ported mount icons (Data\Img\Custom\<id>.png, "own images" with icon index >= 60000) into the
// real icon atlas, so they get normal icon indices after ours (2141+):
//   PNGs -> one new A8R8G8B8 DDS page appended to Img\IconSrc.TIS   (TClientList.LST page total +1)
//        -> one IMAGESET sprite per icon appended to Img\IconImg.TIM (+ Index\TClientI.IDX entries)
//        -> one child per sprite appended to the shared item-icon list of TClientCmd.tif (v2)
//   TItemVisual.m_wIcon / TMount.m_wIcon rewritten to the new indices; the PNGs are deleted.
// Formats: CTachyonRes::LoadIMGBUF / LoadIMG, CD3DImage::Load / GetMask, TCMLParser v2 (Tools/TifDedupe).
using System.IO.Compression;

namespace ClassicPort;

public class IconMerge
{
    const int Slot = 32, PageSize = 512;                 // 16x16 slots of 32 px, like our icon pages
    const byte FmtNonComp = 5;                            // NON_COMP -> D3DFMT_A8R8G8B8
    const int NodeFixed = 48 + 8 + 20 + 8 + 16 + 2 + 68;  // TCMLParser::LoadFRAME fixed part after dwID+bType
    const int ItemList = 0;                               // shared list #0 = item icons (2141 children)
    const int ItemListPrefix = 21;                        // shared list #21 = first 2099 item icons (5 users)

    readonly string game; readonly TextWriter log;
    public IconMerge(string game, TextWriter log) { this.game = game; this.log = log; }

    string G(params string[] p) => Path.Combine([game, .. p]);

    // ------------------------------------------------------------------ PNG (8-bit RGB / RGBA, no interlace)
    public static (int w, int h, byte[] rgba) DecodePng(byte[] b)
    {
        int w = BE(b, 16), h = BE(b, 20); byte depth = b[24], ct = b[25], il = b[28];
        if (depth != 8 || (ct != 2 && ct != 6) || il != 0) throw new NotSupportedException($"png depth {depth} type {ct} interlace {il}");
        int bpp = ct == 6 ? 4 : 3;
        var idat = new MemoryStream();
        for (int p = 8; p < b.Length;)
        {
            int len = BE(b, p); string type = System.Text.Encoding.ASCII.GetString(b, p + 4, 4);
            if (type == "IDAT") idat.Write(b, p + 8, len);
            p += 12 + len;
        }
        idat.Position = 0;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        byte[] d = raw.ToArray();
        int stride = w * bpp; var px = new byte[h * stride]; var prev = new byte[stride];
        for (int y = 0; y < h; y++)
        {
            byte f = d[y * (stride + 1)]; var cur = new byte[stride];
            Buffer.BlockCopy(d, y * (stride + 1) + 1, cur, 0, stride);
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? cur[x - bpp] : 0, up = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                cur[x] += f switch { 0 => 0, 1 => (byte)a, 2 => (byte)up, 3 => (byte)((a + up) / 2), 4 => Paeth(a, up, c), _ => throw new InvalidDataException("png filter") };
            }
            Buffer.BlockCopy(cur, 0, px, y * stride, stride); prev = cur;
        }
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            rgba[4 * i] = px[bpp * i]; rgba[4 * i + 1] = px[bpp * i + 1]; rgba[4 * i + 2] = px[bpp * i + 2];
            rgba[4 * i + 3] = bpp == 4 ? px[bpp * i + 3] : (byte)255;
        }
        return (w, h, rgba);
    }
    static int BE(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
    static byte Paeth(int a, int b, int c) { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); return (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c); }

    // ------------------------------------------------------------------ TIF v2
    class Tif
    {
        public byte[] B = [];
        public List<(int count, int start, int end)> Lists = new();   // children byte span per shared list
        public int FramesStart, TailStart;
        public List<int> RefPositions = new();                         // positions of negative child counts in frames
        public List<int> ListChildStarts(int list)
        {
            var (count, start, _) = Lists[list]; var o = new List<int>(); int p = start;
            for (int i = 0; i < count; i++) { o.Add(p); p = SkipNode(B, p, null); }
            return o;
        }
    }

    static int I32(byte[] b, int p) => BitConverter.ToInt32(b, p);

    /// <summary>Skips one node at p (v1 children inline, or v2 shared ref); records ref positions.</summary>
    static int SkipNode(byte[] b, int p, List<int>? refs)
    {
        p += 5 + NodeFixed;
        int tl = I32(b, p); p += 4 + Math.Max(tl, 0);
        int xl = I32(b, p); p += 4 + Math.Max(xl, 0);
        int n = I32(b, p);
        if (n < 0) { refs?.Add(p); return p + 4; }
        p += 4;
        for (int i = 0; i < n; i++) p = SkipNode(b, p, refs);
        return p;
    }

    static Tif ParseTif(byte[] b)
    {
        var t = new Tif { B = b };
        if (I32(b, 0) != -2) throw new InvalidDataException("TClientCmd.tif is not v2");
        int nsh = I32(b, 4), p = 8;
        for (int s = 0; s < nsh; s++)
        {
            int n = I32(b, p); p += 4; int st = p;
            for (int i = 0; i < n; i++) p = SkipNode(b, p, null);
            t.Lists.Add((n, st, p));
        }
        t.FramesStart = p;
        int fc = I32(b, p); p += 4;
        for (int f = 0; f < fc; f++) p = SkipNode(b, p, t.RefPositions);
        t.TailStart = p;
        return t;
    }

    static uint NodeImg0(byte[] b, int nodeStart) => BitConverter.ToUInt32(b, nodeStart + 5 + 48);

    // ------------------------------------------------------------------ run
    public void Run(bool apply)
    {
        string tcd = G("Tcd"), custom = G("Data", "Img", "Custom");
        string statePath = Path.Combine(tcd, "ClassicPort.items.state");
        var pngIds = File.ReadAllLines(statePath).Where(l => l.StartsWith("png=")).Select(l => ushort.Parse(Path.GetFileNameWithoutExtension(l[4..]))).OrderBy(x => x).ToList();
        if (pngIds.Count == 0) { log.WriteLine("no ported icons left to merge"); return; }

        var tif = ParseTif(File.ReadAllBytes(G("TClientCmd.tif")));
        var listChildren = tif.ListChildStarts(ItemList);
        int firstIndex = tif.Lists[ItemList].count;
        log.WriteLine($"item icon list: {firstIndex} icons; prefix list #{ItemListPrefix}: {tif.Lists[ItemListPrefix].count} icons, {tif.RefPositions.Count(r => -I32(tif.B, r) - 1 == ItemListPrefix)} users");

        // sanity: list #21 must be a prefix of list #0 (it is redirected to #0)
        var prefix = tif.ListChildStarts(ItemListPrefix);
        if (prefix.Where((p, i) => NodeImg0(tif.B, p) != NodeImg0(tif.B, listChildren[i])).Any()) throw new InvalidDataException("list #21 is not a prefix of list #0");

        // sprites / pages
        var iidx = ClassicPort.Idx.Load(G("Index", "TClientI.IDX"));
        int timFile = iidx.Files.FindIndex(f => f.EndsWith(@"\IconImg.TIM", StringComparison.OrdinalIgnoreCase));
        uint nextSprite = iidx.Entries.Max(e => e.Id) + 1;
        byte[] lst = File.ReadAllBytes(G("Index", "TClientList.LST"));
        var tisFiles = new List<string>(); { int n = I32(lst, 0), p = 8; for (int i = 0; i < n; i++) { int l = I32(lst, p); tisFiles.Add(System.Text.Encoding.Latin1.GetString(lst, p + 4, l)); p += 4 + l; } }
        uint maxPage = 0; byte[]? fmt5Header = null;
        foreach (var f in tisFiles)
        {
            var b = File.ReadAllBytes(G("Data", f)); int p = 0;
            while (p + 8 <= b.Length)
            {
                var raw = Client.Unchunk(b, p, out int len);
                maxPage = Math.Max(maxPage, BitConverter.ToUInt32(raw, 0));
                if (fmt5Header == null && raw[4] == FmtNonComp)
                {
                    using var z = new ZLibStream(new MemoryStream(raw, 13, BitConverter.ToInt32(raw, 9)), CompressionMode.Decompress);
                    fmt5Header = new byte[128]; z.ReadExactly(fmt5Header);
                }
                p += len;
            }
        }
        uint pageId = maxPage + 1;
        log.WriteLine($"new page {pageId}, sprites {nextSprite}..{nextSprite + pngIds.Count - 1}, icon indices {firstIndex}..{firstIndex + pngIds.Count - 1}");

        // page pixels (BGRA) + one sprite per icon
        var page = new byte[PageSize * PageSize * 4];
        var spriteChunks = new List<(uint id, byte[] chunk)>();
        var newIndex = new Dictionary<ushort, ushort>();
        var pngs = new Dictionary<ushort, (int w, int h, byte[] rgba)>();
        for (int k = 0; k < pngIds.Count; k++)
        {
            var (w, h, rgba) = DecodePng(File.ReadAllBytes(Path.Combine(custom, $"{pngIds[k]}.png")));
            if (w > Slot || h > Slot) throw new InvalidDataException($"{pngIds[k]}.png is {w}x{h}");
            pngs[pngIds[k]] = (w, h, rgba);
            int sx = k % (PageSize / Slot) * Slot, sy = k / (PageSize / Slot) * Slot;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int s = 4 * (y * w + x), d = 4 * ((sy + y) * PageSize + sx + x);
                    page[d] = rgba[s + 2]; page[d + 1] = rgba[s + 1]; page[d + 2] = rgba[s]; page[d + 3] = rgba[s + 3];
                }
            uint sid = nextSprite + (uint)k;
            spriteChunks.Add((sid, Client.Chunk(SpriteRecord(sid, pageId, sx, sy, w, h, rgba))));
            newIndex[pngIds[k]] = (ushort)(firstIndex + k);
        }

        // DDS page: header copied from an existing A8R8G8B8 page, dimensions/pitch patched
        if (fmt5Header == null) throw new InvalidDataException("no A8R8G8B8 page to copy the DDS header from");
        var dds = (byte[])fmt5Header.Clone();
        BitConverter.GetBytes(PageSize).CopyTo(dds, 12); BitConverter.GetBytes(PageSize).CopyTo(dds, 16);
        BitConverter.GetBytes(PageSize * 4).CopyTo(dds, 20);
        dds = [.. dds, .. page];
        var zms = new MemoryStream(); using (var z = new ZLibStream(zms, CompressionLevel.Optimal, true)) z.Write(dds);
        byte[] zdds = zms.ToArray();
        byte[] pageRaw = [.. BitConverter.GetBytes(pageId), FmtNonComp, .. BitConverter.GetBytes(dds.Length), .. BitConverter.GetBytes(zdds.Length), .. zdds];

        // TIF: append children to list #0 (clone of its last child), redirect list #21 users, drop list #21
        byte[] lastChild = tif.B[listChildren[^1]..tif.Lists[ItemList].end];
        uint lastId = BitConverter.ToUInt32(lastChild, 0);
        var added = new MemoryStream();
        for (int k = 0; k < pngIds.Count; k++)
        {
            var c = (byte[])lastChild.Clone();
            BitConverter.GetBytes(lastId + 1 + (uint)k).CopyTo(c, 0);
            BitConverter.GetBytes(nextSprite + (uint)k).CopyTo(c, 5 + 48);
            added.Write(c);
        }
        byte[] newTif = RewriteTif(tif, added.ToArray(), pngIds.Count);

        // icon fields
        var vis = File.ReadAllBytes(Path.Combine(tcd, "TItemVisual.tcd"));
        int visChanged = 0;
        for (int i = 0, n = BitConverter.ToUInt16(vis, 0); i < n; i++)
        {
            int o = 2 + 63 * i + 34;
            if (newIndex.TryGetValue(BitConverter.ToUInt16(vis, o), out var ni)) { BitConverter.GetBytes(ni).CopyTo(vis, o); visChanged++; }
        }
        var mounts = TMount.Load(Path.Combine(tcd, "TMount.tcd"), false);
        int mountChanged = mounts.Count(m => newIndex.ContainsKey(m.Icon));
        mounts = mounts.Select(m => newIndex.TryGetValue(m.Icon, out var ni) ? m with { Icon = ni } : m).ToList();
        log.WriteLine($"icon fields rewritten: {visChanged} visuals, {mountChanged} mounts");

        if (!apply) { log.WriteLine("dry run — nothing written (use: icons --apply)"); return; }

        using (var fs = new FileStream(G("Data", "Img", "IconSrc.TIS"), FileMode.Append)) fs.Write(Client.Chunk(pageRaw));
        BitConverter.GetBytes(I32(lst, 4) + 1).CopyTo(lst, 4);                      // total page count (progress)
        File.WriteAllBytes(G("Index", "TClientList.LST"), lst);
        using (var fs = new FileStream(G("Data", iidx.Files[timFile]), FileMode.Append))   // Position starts at the old length
            foreach (var (id, chunk) in spriteChunks) { iidx.Entries.Add(new IdxEntry(id, timFile, (uint)fs.Position)); fs.Write(chunk); }
        iidx.Save(G("Index", "TClientI.IDX"));
        File.WriteAllBytes(G("TClientCmd.tif"), newTif);
        File.WriteAllBytes(Path.Combine(tcd, "TItemVisual.tcd"), vis);
        TMount.Save(Path.Combine(tcd, "TMount.tcd"), mounts, withScale: false);

        // read every new icon back through TIF -> IDX -> TIM -> TIS and compare with the PNG
        int ok = VerifyIcons(newIndex, pngs);
        log.WriteLine($"icons read back through the atlas: {ok}/{pngIds.Count} pixel-identical");
        if (ok != pngIds.Count) throw new InvalidDataException("atlas read-back mismatch — restore from git");

        foreach (var id in pngIds) File.Delete(Path.Combine(custom, $"{id}.png"));
        File.WriteAllLines(statePath, File.ReadAllLines(statePath).Where(l => !l.StartsWith("png=")));
        log.WriteLine($"written: IconSrc.TIS (+1 page), IconImg.TIM (+{pngIds.Count}), TClientI.IDX, TClientList.LST, TClientCmd.tif, TItemVisual.tcd, TMount.tcd; {pngIds.Count} PNGs deleted");
    }

    /// <summary>IMAGESET record (LoadIMG) holding one CD3DImage blob (CD3DImage::Load) with one part.</summary>
    static byte[] SpriteRecord(uint sid, uint pageId, int sx, int sy, int w, int h, byte[] rgba)
    {
        var blob = new MemoryStream(); var bw = new BinaryWriter(blob);
        bw.Write(1); bw.Write(w); bw.Write(h);
        bw.Write(pageId);
        foreach (var (vx, vy) in new[] { (0, 0), (w, 0), (0, h), (w, h) })
        {
            bw.Write(vx + 0.5f); bw.Write(vy + 0.5f); bw.Write(0.5f); bw.Write(1f);
            bw.Write((sx + vx) / (float)PageSize); bw.Write((sy + vy) / (float)PageSize);
        }
        int pitch = (w + 7) / 8; var mask = new byte[pitch * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (rgba[4 * (y * w + x) + 3] != 0) mask[pitch * y + x / 8] |= (byte)(0x80 >> (x % 8));
        bw.Write(mask.Length); bw.Write(mask);
        byte[] b = blob.ToArray();

        var rec = new MemoryStream(); var rw = new BinaryWriter(rec);
        rw.Write(1); rw.Write(sid);            // frames: the sprite's own image (ComplateIMG maps it by id)
        rw.Write(0);                           // no colour keys
        rw.Write(1000u);                       // total tick, as our icons
        rw.Write((byte)0);                     // format byte, as our icons
        rw.Write(b.Length); rw.Write(b);
        return rec.ToArray();
    }

    static byte[] RewriteTif(Tif t, byte[] addedChildren, int addedCount)
    {
        var o = new MemoryStream(); var w = new BinaryWriter(o);
        w.Write(-2);
        w.Write(t.Lists.Count - 1);
        for (int s = 0; s < t.Lists.Count; s++)
        {
            if (s == ItemListPrefix) continue;
            var (n, st, en) = t.Lists[s];
            w.Write(s == ItemList ? n + addedCount : n);
            w.Write(t.B, st, en - st);
            if (s == ItemList) w.Write(addedChildren);
        }
        // frames + fonts, with shared-list references remapped (#21 -> #0, above #21 shifted down)
        var fr = t.B[t.FramesStart..];
        foreach (int rp in t.RefPositions)
        {
            int list = -I32(t.B, rp) - 1;
            int nl = list == ItemListPrefix ? ItemList : list > ItemListPrefix ? list - 1 : list;
            BitConverter.GetBytes(-(nl + 1)).CopyTo(fr, rp - t.FramesStart);
        }
        w.Write(fr);
        return o.ToArray();
    }

    int VerifyIcons(Dictionary<ushort, ushort> newIndex, Dictionary<ushort, (int w, int h, byte[] rgba)> pngs)
    {
        var tif = ParseTif(File.ReadAllBytes(G("TClientCmd.tif")));
        var kids = tif.ListChildStarts(ItemList);
        if (tif.RefPositions.Any(r => -I32(tif.B, r) - 1 >= tif.Lists.Count)) throw new InvalidDataException("dangling shared list ref");
        var iidx = ClassicPort.Idx.Load(G("Index", "TClientI.IDX"));
        var tis = File.ReadAllBytes(G("Data", "Img", "IconSrc.TIS"));
        var pages = new Dictionary<uint, byte[]>();
        for (int p = 0; p < tis.Length;)
        {
            var raw = Client.Unchunk(tis, p, out int len); p += len;
            if (raw[4] != FmtNonComp) continue;
            using var z = new ZLibStream(new MemoryStream(raw, 13, BitConverter.ToInt32(raw, 9)), CompressionMode.Decompress);
            var ms = new MemoryStream(); z.CopyTo(ms); pages[BitConverter.ToUInt32(raw, 0)] = ms.ToArray();
        }
        int ok = 0;
        foreach (var (pngId, idx) in newIndex)
        {
            uint sid = NodeImg0(tif.B, kids[idx]);
            var e = iidx.ById[sid];
            var rec = Client.Unchunk(File.ReadAllBytes(G("Data", iidx.Files[e.File])), (int)e.Pos, out _);
            var r = new BinaryReader(new MemoryStream(rec));
            r.ReadInt32(); r.ReadUInt32(); r.ReadInt32(); r.ReadUInt32(); r.ReadByte(); r.ReadInt32();
            r.ReadInt32(); int w = r.ReadInt32(), h = r.ReadInt32(); uint pid = r.ReadUInt32();
            r.ReadBytes(16); float u0 = r.ReadSingle(), v0 = r.ReadSingle();
            var dds = pages[pid]; int sx = (int)Math.Round(u0 * PageSize), sy = (int)Math.Round(v0 * PageSize);
            var (pw, ph, rgba) = pngs[pngId];
            bool same = w == pw && h == ph;
            for (int y = 0; same && y < h; y++)
                for (int x = 0; same && x < w; x++)
                {
                    int d = 128 + 4 * ((sy + y) * PageSize + sx + x), s = 4 * (y * w + x);
                    same = dds[d] == rgba[s + 2] && dds[d + 1] == rgba[s + 1] && dds[d + 2] == rgba[s] && dds[d + 3] == rgba[s + 3];
                }
            if (same) ok++;
        }
        return ok;
    }
}
