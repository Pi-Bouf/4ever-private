// Classic .TMH record -> our .TMH record (layout: CTachyonRes::LoadMESH).
// Classic differences: header DWORDs meshCount/level swapped; index buffers are 32-bit.
namespace ClassicPort;

public static class MeshConv
{
    public static byte[] FromClassic(byte[] src, out int consumed)
    {
        var r = new BinaryReader(new MemoryStream(src));
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        uint level = r.ReadUInt32(), nodes = r.ReadUInt32();
        byte useVB = r.ReadByte();
        uint meshes = r.ReadUInt32();
        w.Write(meshes); w.Write(nodes); w.Write(useVB); w.Write(level);
        w.Write(r.ReadBytes(16 + (int)nodes * 64));                 // center, radius, node matrices
        uint verts = r.ReadUInt32();
        w.Write(verts);
        w.Write(r.ReadBytes((int)verts * (nodes > 0 ? 48 : 40)));
        for (uint i = 0; i < meshes * level; i++)
        {
            uint nib = r.ReadUInt32();
            w.Write(nib);
            for (uint k = 0; k < nib; k++)
            {
                uint cnt = r.ReadUInt32();
                w.Write(cnt);
                w.Write(r.ReadUInt16());
                for (uint q = 0; q < cnt; q++)
                {
                    uint v = r.ReadUInt32();
                    if (v > ushort.MaxValue) throw new InvalidDataException($"index {v} does not fit 16 bits");
                    w.Write((ushort)v);
                }
            }
        }
        for (int i = 0; i < (int)level - 1; i++) w.Write(r.ReadSingle());
        consumed = (int)r.BaseStream.Position;
        return ms.ToArray();
    }
}
