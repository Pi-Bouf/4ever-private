using TControl.Protocol;
using TControl.Server.Net;

namespace TControl.Server.Control;

/// <summary>
/// Per-connection state for an outbound game-server peer — the ported C++ <c>CTServer</c>. Links back to
/// the <see cref="ServiceInstance"/> it belongs to.
/// </summary>
public sealed class ServerSession
{
    public ServerSession(PacketConnection conn) => Conn = conn;

    public PacketConnection Conn { get; }
    public ServiceInstance? Service { get; set; }

    public void Send(byte[] packet) => Conn.Send(packet);
    public void Send(PacketWriter w) => Conn.Send(w);
}
