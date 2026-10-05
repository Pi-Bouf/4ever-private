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
