using System.Net;
using TMap.Protocol;

namespace TMap.Server.Net;

/// <summary>Outbound sink to the World server (the MW/SM/DM planes). Implemented by <see cref="WorldLink"/>;
/// faked in tests to capture world-bound packets.</summary>
public interface IWorldSink
{
    void Send(byte[] packet);
    void Send(PacketWriter writer);
}

/// <summary>A single client's outbound channel + control. Implemented by <see cref="ClientConnection"/>;
/// faked in tests to capture client-bound packets without a socket.</summary>
public interface IClientChannel
{
    IPEndPoint? RemoteEndPoint { get; }
    void Send(byte[] packet);
    void Send(PacketWriter writer);
    void Close();
}
