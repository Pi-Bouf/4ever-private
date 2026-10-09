using System.Net.Sockets;
using TLogin.Protocol;

namespace TBot;

/// <summary>
/// A server-plane tool for the scenarios: it talks to the world server directly (plaintext, as the control server does) to force a
/// war phase — C++ <c>SM_BATTLESTATUS_REQ</c> (<c>bType, bStatus, dwStart, dwSecond</c>), which the world sends on to every map as
/// its <c>MW_*ENABLE_REQ</c>. The world's own schedule is left alone.
/// </summary>
internal static class WorldTool
{
    public const ushort SM_BATTLESTATUS_REQ = 0x1581 + 0x001C;
    public const byte BtLocal = 0, BtCastle = 1, BtMission = 3, BtSkyGarden = 4;
    public const byte BsNormal = 0, BsBattle = 1, BsPeace = 4;

    public static string Host { get; set; } = "127.0.0.1";
    public static int Port { get; set; } = 3816;

    public const ushort CT_CASTLEGUILDCHG_REQ = 0x9301 + 0x005B;

    /// <summary>C++ <c>CT_CASTLEGUILDCHG_REQ</c> (the operator's tool): a castle's defending and attacking guilds and its next war
    /// (unix seconds) — the world tells every map.</summary>
    public static void CastleGuildChg(ushort castle, uint defGuild, uint atkGuild, long nextWar)
    {
        using var tcp = new TcpClient(Host, Port);
        var w = new PacketWriter(CT_CASTLEGUILDCHG_REQ);
        w.WriteUInt16(castle); w.WriteUInt32(defGuild); w.WriteUInt32(atkGuild); w.WriteUInt32(0); w.WriteInt64(nextWar);
        tcp.GetStream().Write(w.ToArray());
        tcp.GetStream().Flush();
        Thread.Sleep(300);
    }

    public static void BattleStatus(byte type, byte status, uint start, uint second)
    {
        using var tcp = new TcpClient(Host, Port);
        var w = new PacketWriter(SM_BATTLESTATUS_REQ);
        w.WriteByte(type); w.WriteByte(status); w.WriteUInt32(start); w.WriteUInt32(second);
        tcp.GetStream().Write(w.ToArray());
        tcp.GetStream().Flush();
        Thread.Sleep(300);                                              // let the world read it before the socket closes
    }
}
