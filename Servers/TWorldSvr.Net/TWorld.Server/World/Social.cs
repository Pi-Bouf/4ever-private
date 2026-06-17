using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>One friend-list entry — the ported C++ <c>TFRIEND</c>. A "target" (<see cref="FriendType.Target"/>)
/// is someone who added me but whom I have not added back; it is excluded from the visible friend count.</summary>
public sealed class Friend
{
    public uint Id { get; set; }
    public string Name { get; set; } = "";
    public byte Level { get; set; }
    public byte Group { get; set; }
    public byte Class { get; set; }
    public FriendType Type { get; set; }
    public bool Connected { get; set; }
    public uint Region { get; set; }
}

/// <summary>One soulmate link — the ported C++ <c>TSOULMATE</c>. The entry keyed by the owner's own
/// charId is the active pair; <see cref="Time"/> non-zero marks an in-progress break (silence window).</summary>
public sealed class Soulmate
{
    public uint CharId { get; set; }   // owner of this link row
    public uint Target { get; set; }   // the partner
    public string Name { get; set; } = "";
    public byte Level { get; set; }
    public byte Class { get; set; }
    public bool Connected { get; set; }
    public uint Region { get; set; }
    public uint Time { get; set; }     // unix time of pending break, else 0
}
