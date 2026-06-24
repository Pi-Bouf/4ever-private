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

    /// <summary>Round-robin loot pointer for PT_ORDER parties (m_dwOrder): the member due to receive next.</summary>
    public uint Order { get; set; }

    /// <summary>Whether the party has opted into arena play (m_bArena).</summary>
    public byte Arena { get; set; }

    public byte Size => (byte)Members.Count;
    public bool IsFull => Members.Count >= Proto.MaxPartyMember;
    public bool IsChief(uint charId) => ChiefId == charId;

    public Character? FindMember(uint charId) => Members.FirstOrDefault(m => m.CharId == charId);
    public bool IsMember(uint charId) => FindMember(charId) is not null;
    public Character? Chief => FindMember(ChiefId);

    public bool AddMember(Character c)
    {
        if (c.Party is not null || IsFull || IsMember(c.CharId)) return false;
        if (Members.Count == 0) Order = c.CharId;   // first member seeds the loot order (CTParty::AddMember)
        Members.Add(c);
        c.Party = this;
        return true;
    }

    public bool DelMember(uint charId)
    {
        var m = FindMember(charId);
        if (m is null) return false;
        if (charId == Order) SetNextOrder(charId);  // advance the order pointer before removing (CTParty::DelMember)
        Members.Remove(m);
        m.Party = null;
        return true;
    }

    private byte OrderIndex(uint charId)
    {
        for (byte i = 0; i < Members.Count; i++) if (Members[i].CharId == charId) return i;
        return 0;
    }

    /// <summary>Advance <see cref="Order"/> to the member after <paramref name="charId"/>, wrapping. CTParty::SetNextOrder.</summary>
    private void SetNextOrder(uint charId)
    {
        int idx = OrderIndex(charId) + 1;
        Order = Members.Count <= idx ? Members[0].CharId : Members[idx].CharId;
    }

    /// <summary>Pick the next member (among those eligible in <paramref name="eligible"/>) to receive an
    /// ordered-loot drop, advancing the rotation. Ported from CTParty::GetNextOrder.</summary>
    public Character? GetNextOrder(IReadOnlyList<uint> eligible)
    {
        var byIndex = new SortedDictionary<byte, uint>();
        foreach (var id in eligible)
            for (byte j = 0; j < Members.Count; j++)
                if (Members[j].CharId == id) byIndex[j] = id;

        if (byIndex.Count == 0) return null;

        foreach (var kv in byIndex)
            if (kv.Value == Order) { var n = FindMember(kv.Value); SetNextOrder(kv.Value); return n; }

        byte bIndex = OrderIndex(Order);
        foreach (var kv in byIndex)
            if (kv.Key > bIndex) { var n = FindMember(kv.Value); SetNextOrder(kv.Value); return n; }

        var first = byIndex.First();
        var fn = FindMember(first.Value); SetNextOrder(first.Value); return fn;
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
