using Microsoft.Extensions.Logging;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Small client-plane handlers: ping, disconnect/terminate, and the handshake-adjacent requests that are
/// acknowledged-but-deferred in Phase-1 (mode change, region, channel change). See PORT_STATUS.md.
/// </summary>
public sealed partial class MapService
{
    private void OnCS_PINGMEASUREMENT_REQ(ClientSession s, PacketReader r)
    {
        uint tick = r.ReadUInt32();
        var w = new PacketWriter(Msg.CS_PINGMEASUREMENT_ACK);
        w.WriteUInt32(tick); // echo the client tick back (round-trip time measurement)
        s.Send(w);
    }

    private void OnCS_DISCONNECT_REQ(ClientSession s, PacketReader r)
    {
        // C++: CloseSession(pPlayer). The socket teardown drives OnClientDisconnectAsync (world close + view leave).
        s.Conn.Close();
    }

    private void OnCS_TERMINATE_REQ(ClientSession s, PacketReader r)
    {
        // The C++ treats CS_TERMINATE_REQ as a backdoor-attack probe: it logs and does nothing else.
        _log.LogWarning("CS_TERMINATE_REQ from {Endpoint} (char {Char}); ignored.", s.Conn.RemoteEndPoint, s.CharId);
    }

    private void OnCS_CHGMODE_REQ(ClientSession s, PacketReader r)
    {
        // Combat/peace/sit mode change — the full stat/skill implications are deferred (PORT_STATUS.md).
        _log.LogDebug("CS_CHGMODE_REQ from char {Char} (deferred).", s.CharId);
    }

    private void OnCS_REGION_REQ(ClientSession s, PacketReader r)
    {
        // Region-discovery / world-map reveal — deferred (PORT_STATUS.md).
        _log.LogDebug("CS_REGION_REQ from char {Char} (deferred).", s.CharId);
    }

    private void OnCS_CHGCHANNEL_REQ(ClientSession s, PacketReader r)
    {
        // Channel switch requires the world routing plane; deferred (PORT_STATUS.md).
        _log.LogDebug("CS_CHGCHANNEL_REQ from char {Char} (deferred).", s.CharId);
    }
}
