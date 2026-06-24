using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Per-connection peer state — the ported subset of the C++ <c>CTServer</c>. A peer is a map, control,
/// or relay server. <see cref="WId"/> is <c>MAKEWORD(serverId, serverType)</c>, set when the peer
/// registers (MW_CONNECT_ACK / CT_CTRLSVR_REQ / RW_RELAYSVR_REQ).
/// </summary>
public sealed class ServerSession
{
    public ServerSession(PacketConnection conn) => Conn = conn;

    public PacketConnection Conn { get; }
    public ushort WId { get; set; }
    public HashSet<byte> Channels { get; } = new();

    public byte ServerId => Proto.ServerIdOf(WId);
    public byte ServerType => Proto.ServerTypeOf(WId);

    /// <summary>This map has acknowledged the current cash-item sale (m_bCashSale). A sale only persists once
    /// every connected map has confirmed.</summary>
    public bool CashSale { get; set; }

    public void Send(PacketWriter w) => Conn.Send(w);
    public void Send(byte[] packet) => Conn.Send(packet);
}
