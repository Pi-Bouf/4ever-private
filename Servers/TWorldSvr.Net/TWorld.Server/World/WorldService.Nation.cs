using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5f — nation balance, ported from <c>SSHandler.cpp</c> (OnMW_WARCOUNTRYBALANCE_ACK) and
/// <c>TWorldSvr.cpp</c> (SetCharLevel / GetWarCountry / GetWarCountryGap). Online level-130..179 characters
/// are bucketed by war-country (their aid-country if any, else their country) and 10-level gap; a map can
/// ask how many Defugel vs Craxion players share a character's gap so it can apply the under-dog war bonus.
/// (The C++ also rebuilds these buckets from a periodic active-char DB refresh, <c>DM_ACTIVECHARUPDATE</c>;
/// that cross-server aggregate refresh is a separate deferred DM flow — here the buckets track this world's
/// online characters, which is exact for a single-server deployment.)
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchNation(ServerSession session, PacketReader r)
    {
        if (r.Id == Msg.MW_WARCOUNTRYBALANCE_ACK) { OnMW_WARCOUNTRYBALANCE_ACK(session, r); return true; }
        return false;
    }

    /// <summary>A char's effective war country: its aid-country if it has one, else its own. C++ GetWarCountry.</summary>
    private static byte GetWarCountry(Character ch)
        => ch.AidCountry != (byte)Contry.None ? ch.AidCountry : ch.Country;

    /// <summary>Level → gap bucket (0..MaxGap). MaxGap means "not tracked" (below the base level). C++ GetWarCountryGap.</summary>
    private static byte GetWarCountryGap(byte level)
        => level < Proto.BroaBaseLevel ? Proto.WarCountryMaxGap : (byte)((level - Proto.BroaBaseLevel) / 10);

    /// <summary>C++ SetCharLevel — update the war-country gap bucket on a level change, then set the level and
    /// keep the guild roster's copy in sync.</summary>
    private void SetCharLevel(Character ch, byte level)
    {
        if (ch.Level == level) return;

        byte gapNew = GetWarCountryGap(level);
        byte country = GetWarCountry(ch);
        if (gapNew < Proto.WarCountryMaxGap && country < (byte)Contry.Broa)
        {
            byte gapOld = GetWarCountryGap(ch.Level);
            if (gapOld < Proto.WarCountryMaxGap && gapOld != gapNew)
                _state.WarCountry[country][gapOld].Remove(ch.CharId);
            _state.WarCountry[country][gapNew].Add(ch.CharId);
        }

        ch.Level = level;
        if (ch.Guild?.FindMember(ch.CharId) is { } mem) mem.Level = level;
    }

    /// <summary>Add a now-online char to its war-country gap bucket (the effect SetCharLevel has during the
    /// C++ enter flow, applied once the char is fully in the world).</summary>
    private void WarCountryEnter(Character ch)
    {
        byte gap = GetWarCountryGap(ch.Level);
        byte country = GetWarCountry(ch);
        if (gap < Proto.WarCountryMaxGap && country < (byte)Contry.Broa)
            _state.WarCountry[country][gap].Add(ch.CharId);
    }

    /// <summary>Remove a leaving char from every war-country bucket.</summary>
    private void WarCountryLeave(Character ch)
    {
        foreach (var byGap in _state.WarCountry)
            foreach (var bucket in byGap)
                bucket.Remove(ch.CharId);
    }

    /// <summary>A char asks for the nation balance in its level-gap: reply with the online Defugel/Craxion
    /// counts sharing that gap. C++ OnMW_WARCOUNTRYBALANCE_ACK.</summary>
    private void OnMW_WARCOUNTRYBALANCE_ACK(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;

        byte gap = GetWarCountryGap(ch.Level);
        if (gap >= Proto.WarCountryMaxGap) return;

        var w = new PacketWriter(Msg.MW_WARCOUNTRYBALANCE_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)_state.WarCountry[(byte)Contry.Defugel][gap].Count);
        w.WriteUInt32((uint)_state.WarCountry[(byte)Contry.Craxion][gap].Count);
        w.WriteByte(gap);
        session.Send(w.ToArray());
    }
}
