using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5j — miscellaneous handlers that don't fit a larger subsystem. <c>ARENAJOIN</c> flags a party as
/// arena-bound and splits off the members who aren't joining (ported from OnMW_ARENAJOIN_ACK).
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchMisc(ServerSession session, PacketReader r)
    {
        if (r.Id == Msg.MW_ARENAJOIN_ACK) { OnMW_ARENAJOIN_ACK(r); return true; }
        return false;
    }

    /// <summary>A party opts into (or out of) arena play. On opt-in it leaves its corps and any party member
    /// not in the joining list is split off into their own party. C++ OnMW_ARENAJOIN_ACK.</summary>
    private void OnMW_ARENAJOIN_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte join = r.ReadByte();
        uint count = r.ReadUInt32();

        var ch = _state.FindChar(charId, key);
        if (ch?.Party is null) return;
        var party = ch.Party;
        party.Arena = join;
        if (join == 0) return;   // the member list (if any) is left unread, matching the C++

        if (party.CorpsId != 0 && _state.FindCorps(party.CorpsId) is { } corps)
            NotifyCorpsLeave(corps, party);

        var keep = new HashSet<uint>();
        for (uint i = 0; i < count; i++) keep.Add(r.ReadUInt32());

        // Everyone not in the joining list leaves the party.
        foreach (var id in party.Members.Where(m => !keep.Contains(m.CharId)).Select(m => m.CharId).ToList())
            LeaveParty(party, id, 1);
    }
}
