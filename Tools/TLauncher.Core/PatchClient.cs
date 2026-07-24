using System.Net;
using System.Net.Sockets;
using TPatch.Protocol;

namespace TLauncher;

/// <summary>One patch-file entry from CT_NEWPATCH_ACK (mirrors the C++ <c>tagPATCHFILE</c>).</summary>
public sealed record PatchFileEntry(uint Version, string Path, string Name, uint Size, uint BetaVer);

/// <summary>The decoded CT_NEWPATCH_ACK: download base, login endpoint, min beta, and the file list.</summary>
public sealed record PatchManifest(string FtpUrl, string LoginIp, ushort LoginPort, uint MinBetaVer, IReadOnlyList<PatchFileEntry> Files);

/// <summary>
/// The launcher side of the patch handshake — the C# equivalent of what <c>4StoryDlg.cpp</c> does in
/// <c>OnConnect</c> → <c>SendCT_NEWPATCH_REQ</c> → <c>OnCT_NEWPATCH_ACK</c> → <c>SendCT_PATCHSTART_REQ</c>.
/// Speaks the 8-byte plaintext framing via the shared <see cref="TPatch.Protocol"/> primitives.
/// </summary>
public static class PatchClient
{
    public static async Task<PatchManifest> RequestAsync(string host, int port, uint version, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);
        await using var ns = tcp.GetStream();

        // CT_NEWPATCH_REQ { DWORD version }
        var req = new PacketWriter(Msg.CT_NEWPATCH_REQ);
        req.WriteUInt32(version);
        await ns.WriteAsync(req.ToArray(), ct);

        byte[] packet = await ReadPacketAsync(ns, ct);
        var r = new PacketReader(packet);
        if (r.Id != Msg.CT_NEWPATCH_ACK)
            throw new InvalidDataException($"Unexpected reply id 0x{r.Id:X4} (expected CT_NEWPATCH_ACK 0x{Msg.CT_NEWPATCH_ACK:X4}).");

        // Body order matches TPatchSvr Sender.cpp / the launcher's OnCT_NEWPATCH_ACK.
        string ftp = r.ReadString();
        uint ipPacked = r.ReadUInt32();               // sin_addr.s_addr (network-order octets)
        ushort loginPort = r.ReadUInt16();
        uint minBeta = r.ReadUInt32();
        ushort count = r.ReadUInt16();

        var files = new List<PatchFileEntry>(count);
        for (int i = 0; i < count; i++)
            files.Add(new PatchFileEntry(r.ReadUInt32(), r.ReadString(), r.ReadString(), r.ReadUInt32(), r.ReadUInt32()));

        string ip = new IPAddress(BitConverter.GetBytes(ipPacked)).ToString();

        // CT_PATCHSTART_REQ (no body) — mirrors the launcher; the server closes the session after this.
        await ns.WriteAsync(new PacketWriter(Msg.CT_PATCHSTART_REQ).ToArray(), ct);

        return new PatchManifest(ftp, ip, loginPort, minBeta, files);
    }

    private static async Task<byte[]> ReadPacketAsync(NetworkStream ns, CancellationToken ct)
    {
        var framer = new PacketFramer();
        var buf = new byte[8192];
        while (true)
        {
            if (framer.TryReadPacket(out var pkt)) return pkt;
            int n = await ns.ReadAsync(buf, ct);
            if (n == 0) throw new IOException("Patch server closed the connection before replying.");
            framer.Append(buf.AsSpan(0, n));
        }
    }
}
