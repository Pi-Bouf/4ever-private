using System.Net;
using TMap.Protocol;
using TMap.Server.Net;

namespace TMap.Server.Tests;

/// <summary>Captures every packet the map would send to the world.</summary>
internal sealed class FakeWorldSink : IWorldSink
{
    public List<byte[]> Sent { get; } = new();
    public void Send(byte[] packet) => Sent.Add(packet);
    public void Send(PacketWriter writer) => Sent.Add(writer.ToArray());

    public IEnumerable<byte[]> WithId(ushort id) => Sent.Where(p => PacketHeader.ReadId(p) == id);
    public byte[]? Last(ushort id) => WithId(id).LastOrDefault();
    public bool Has(ushort id) => WithId(id).Any();
    public void Clear() => Sent.Clear();
}

/// <summary>Captures every packet the map would send to a single client, and whether it was closed.</summary>
internal sealed class FakeClientChannel : IClientChannel
{
    public List<byte[]> Sent { get; } = new();
    public bool Closed { get; private set; }
    public IPEndPoint? RemoteEndPoint => null;

    public void Send(byte[] packet) => Sent.Add(packet);
    public void Send(PacketWriter writer) => Sent.Add(writer.ToArray());
    public void Close() => Closed = true;

    public IEnumerable<byte[]> WithId(ushort id) => Sent.Where(p => PacketHeader.ReadId(p) == id);
    public byte[]? Last(ushort id) => WithId(id).LastOrDefault();
    public bool Has(ushort id) => WithId(id).Any();
    public void Clear() => Sent.Clear();
}
