// OfficialPort — pulls content that exists in the official Gameforge FR client (OFFICIAL/) but not in ours (Game/).
//   dotnet run -- analyze          where the costume mesh keys live in both clients
//   dotnet run -- port [--apply]   port the missing costumes (see CostumePort.cs)
using ClassicPort;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
string game = Path.Combine(repo, "Game"), off = Path.Combine(repo, "OFFICIAL");
string cmd = args.FirstOrDefault() ?? "analyze";

var G = new Client(game, "2_TClientS.IDX", false);
var O = new Client(off, "2_TClientS.IDX", false);

if (cmd == "port")
{
    new CostumePort(game, G, O, Console.Out).Run(args.Contains("--apply"));
    return;
}

if (cmd == "analyze")
{
    uint[] keys = [0x6EFE, 0x6F12, 0x6F1C, 0x6F1A, 0x6F1D, 0x6F1B];
    foreach (var (name, c) in new[] { ("OFF", O), ("OUR", G) })
    {
        int objs = 0;
        foreach (var id in c.Idx[Kind.Obj].ById.Keys)
        {
            var rec = c.Get(Kind.Obj, id)!;
            var cl = ObjCloth.Read(rec.Raw);
            foreach (var k in cl.Kinds) foreach (var cli in k.Cloths) foreach (var m in cli.Meshes)
                if (keys.Contains(m.Key))
                {
                    objs++;
                    Console.WriteLine($"{name} obj {id:X8} clk {k.Id:X} cli {cli.Id:X} key {m.Key:X} mesh {m.MeshId:X8} tex [{string.Join(",", m.TexIds.Select(t => t.ToString("X8")))}] ourHasMesh={G.Has(Kind.Mesh, m.MeshId)} ourHasObj={G.Has(Kind.Obj, id)}");
                }
        }
        Console.WriteLine($"{name}: {objs} hits");
    }
}

/// <summary>The trailing CLKIND section of a .TOB record (LoadOBJ order), with mesh entries kept raw.</summary>
public class ObjCloth
{
    public int Start;                 // offset of the CLK count inside the record
    public List<ClKind> Kinds = new();
    public class ClKind { public uint Id; public List<Cloth> Cloths = new(); }
    public class Cloth { public uint Id; public List<MeshEnt> Meshes = new(); }
    public class MeshEnt
    {
        public uint Key, MeshId; public byte[] Raw = [];   // Raw = whole entry incl. key/mesh/textures
        public List<uint> TexIds = new();
    }
    const int TexEntry = 8 + 2 + 7 * 4 + 4 + 4 + 4 + 4 + 1;

    public static ObjCloth Read(byte[] rec)
    {
        // Walk the prefix with the shared parser, then re-read the cloth section ourselves.
        var s = new BinaryReader(new MemoryStream(rec));
        SkipToCloth(s);
        var o = new ObjCloth { Start = (int)s.BaseStream.Position };
        int n = s.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var k = new ClKind { Id = s.ReadUInt32() };
            int ncl = s.ReadInt32();
            for (int j = 0; j < ncl; j++)
            {
                var c = new Cloth { Id = s.ReadUInt32() };
                int nm = s.ReadInt32();
                for (int q = 0; q < nm; q++)
                {
                    long p = s.BaseStream.Position;
                    var m = new MeshEnt { Key = s.ReadUInt32(), MeshId = s.ReadUInt32() };
                    int nt = s.ReadInt32();
                    for (int t = 0; t < nt; t++)
                    {
                        m.TexIds.Add(s.ReadUInt32()); m.TexIds.Add(s.ReadUInt32());
                        s.BaseStream.Seek(TexEntry - 8, SeekOrigin.Current);
                    }
                    m.Raw = rec[(int)p..(int)s.BaseStream.Position];
                    c.Meshes.Add(m);
                }
                k.Cloths.Add(c);
            }
            o.Kinds.Add(k);
        }
        if (s.BaseStream.Position != rec.Length) throw new InvalidDataException("cloth section did not end the record");
        return o;
    }

    public byte[] Write(byte[] rec)
    {
        var ms = new MemoryStream();
        ms.Write(rec, 0, Start);
        var w = new BinaryWriter(ms);
        w.Write(Kinds.Count);
        foreach (var k in Kinds)
        {
            w.Write(k.Id); w.Write(k.Cloths.Count);
            foreach (var c in k.Cloths) { w.Write(c.Id); w.Write(c.Meshes.Count); foreach (var m in c.Meshes) w.Write(m.Raw); }
        }
        return ms.ToArray();
    }

    static void SkipToCloth(BinaryReader s)
    {
        s.ReadByte();
        int n = s.ReadInt32(); s.BaseStream.Seek(8 * n, SeekOrigin.Current);
        n = s.ReadInt32(); s.BaseStream.Seek(n * (4 + 4 + 24 + 4 + 1 + 4 + 4 + 4), SeekOrigin.Current);
        n = s.ReadInt32(); s.BaseStream.Seek(n * (4 + 4 + 12), SeekOrigin.Current);
        n = s.ReadInt32();
        for (int i = 0; i < n; i++) { s.ReadUInt32(); s.ReadByte(); int sz = s.ReadInt32(); s.BaseStream.Seek(sz, SeekOrigin.Current); }
        n = s.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            s.ReadUInt32();
            int m = s.ReadInt32(); s.BaseStream.Seek(m * (4 + 4 + 24 + 4 + 1 + 4 + 4 + 4), SeekOrigin.Current);
            m = s.ReadInt32(); s.BaseStream.Seek(m * (4 + 4 + 12), SeekOrigin.Current);
            m = s.ReadInt32();
            for (int j = 0; j < m; j++)
            {
                s.ReadUInt32(); s.ReadUInt32();
                int k = s.ReadInt32(); s.BaseStream.Seek(k * (4 + 4 + 24 + 4 + 1 + 4 + 4 + 4), SeekOrigin.Current);
                k = s.ReadInt32(); s.BaseStream.Seek(k * (4 + 4 + 12), SeekOrigin.Current);
            }
        }
    }
}
