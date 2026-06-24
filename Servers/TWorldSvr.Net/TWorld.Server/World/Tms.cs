namespace TWorld.Server.World;

/// <summary>A TMS conversation (C++ <c>TMS</c>) — a multi-person private chat. <see cref="LastMember"/> holds
/// the name of the most-recent leaver/peer, used to resolve a still-empty 1:1 conversation.</summary>
public sealed class Tms
{
    public uint Id { get; init; }
    public string LastMember { get; set; } = "";
    public Dictionary<uint, Character> Members { get; } = new();
}
