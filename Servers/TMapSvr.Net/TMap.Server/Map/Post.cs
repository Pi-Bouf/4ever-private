namespace TMap.Server.Map;

/// <summary>The mail a player has open (C++ <c>TPOST</c>, built by <c>CTPlayer::MakePost</c>). Take-item, delete
/// and return all act on this one.</summary>
public sealed class Post
{
    public uint PostId { get; init; }
    public uint SendId { get; init; }
    public string Sender { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public byte Type { get; init; }
    public byte Read { get; set; }
    public long TimeRecv { get; init; }
    public uint Gold { get; set; }
    public uint Silver { get; set; }
    public uint Cooper { get; set; }
    public byte Contain { get; init; }
    public List<Item> Items { get; } = new();
}
