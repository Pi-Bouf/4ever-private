using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// In-memory party (CTParty) — up to <see cref="Proto.MaxPartyMember"/> members, a chief, a loot
/// (obtain) type, and an optional parent corps. Purely ephemeral: never persisted, dissolved when the
/// last member leaves.
/// </summary>
public sealed class Party
{
    public ushort Id { get; set; }
    public uint ChiefId { get; set; }
    public byte ObtainType { get; set; }
    public ushort CorpsId { get; set; }      // 0 = not in a corps
    public List<Character> Members { get; } = new();

    public byte Size => (byte)Members.Count;
    public bool IsFull => Members.Count >= Proto.MaxPartyMember;
    public bool IsChief(uint charId) => ChiefId == charId;

    public Character? FindMember(uint charId) => Members.FirstOrDefault(m => m.CharId == charId);
    public bool IsMember(uint charId) => FindMember(charId) is not null;
    public Character? Chief => FindMember(ChiefId);

    public bool AddMember(Character c)
    {
        if (c.Party is not null || IsFull || IsMember(c.CharId)) return false;
        Members.Add(c);
        c.Party = this;
        return true;
    }

    public bool DelMember(uint charId)
    {
        var m = FindMember(charId);
        if (m is null) return false;
        Members.Remove(m);
        m.Party = null;
        return true;
    }

    /// <summary>If <paramref name="leavingId"/> is the chief, pick the next member as chief (mirrors GetNextChief).</summary>
    public Character? NextChiefAfter(uint leavingId)
    {
        if (ChiefId != leavingId) return Chief;
        var next = Members.FirstOrDefault(m => m.CharId != leavingId);
        if (next is not null) ChiefId = next.CharId;
        return next;
    }
}

/// <summary>In-memory corps (CTCorps) — a squad of parties under a commander/general. Ephemeral.</summary>
public sealed class Corps
{
    public ushort Id { get; set; }
    public ushort Commander { get; set; }    // commanding party id
    public uint GeneralId { get; set; }      // commanding party's chief
    public Dictionary<ushort, Party> Parties { get; } = new();
}

/// <summary>Allocates/recycles party (and corps) ids from the 0x100..0xFFFF range (m_qGenPartyID).</summary>
public sealed class PartyIdPool
{
    private readonly Queue<ushort> _free = new();

    public PartyIdPool()
    {
        for (int id = Proto.PartyIdMin; id < Proto.PartyIdMax; id++)
            _free.Enqueue((ushort)id);
    }

    public ushort Alloc() => _free.Count > 0 ? _free.Dequeue() : (ushort)0;
    public void Free(ushort id)
    {
        if (id != 0) _free.Enqueue(id);
    }
}
