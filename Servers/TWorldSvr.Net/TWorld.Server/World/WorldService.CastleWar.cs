using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5d — the castle-war scoreboard + defender/attacker selection, ported from <c>SSHandler.cpp</c>
/// (OnMW_CASTLEWARINFO_ACK), <c>TWorldSvr.cpp</c> (NotifyCastleWarInfo / SelectCastleWarGuild /
/// CompareOccupation / CompareGuildRank) and <c>SSSender.cpp</c>. A map sends each castle's per-local
/// occupation snapshot; the world accumulates per-guild bonus + a 6-day history, then on a full recompute
/// ranks the top-3 guilds per country and picks one defender and one attacker per castle, with a guild
/// never holding two roles (cross-castle deconfliction). The result is broadcast as the scoreboard the
/// client renders. The OnTimer-driven war start/end (using the selected def/atk) is a later slice.
/// </summary>
public sealed partial class WorldService
{
    // CDef / CCra (Defugel / Craxion country indexes) are defined in WorldService.Bow.cs.

    /// <summary>A map reports a castle's occupation snapshot, or (castle==0) asks for the current scoreboard.
    /// C++ OnMW_CASTLEWARINFO_ACK.</summary>
    private void OnMW_CASTLEWARINFO_ACK(ServerSession session, PacketReader r)
    {
        ushort castle = r.ReadUInt16();
        if (castle == 0) { NotifyCastleWarInfo(session); return; }

        _ = r.ReadUInt32();              // dwGuild — read and ignored, exactly as the C++ does
        byte localCnt = r.ReadByte();

        var cwi = new CastleWarInfo { Id = castle };
        for (byte i = 0; i < localCnt; i++)
        {
            _ = r.ReadUInt16();          // localId
            for (byte j = 0; j < 6; j++)
            {
                uint occGuild = r.ReadUInt32();
                byte occType = r.ReadByte();
                ushort bonus = occType == (byte)OccupyType.Defend ? (ushort)11 : (ushort)10;

                if (_state.FindGuild(occGuild) is null) continue;

                cwi.MapGuild[occGuild] = (cwi.MapGuild.TryGetValue(occGuild, out var b) ? b : 0u) + bonus;
                if (occType == (byte)OccupyType.Accept)
                {
                    if (!cwi.MapOccupy.TryGetValue(occGuild, out var arr)) { arr = new ushort[6]; cwi.MapOccupy[occGuild] = arr; }
                    arr[j] += bonus;
                }
            }
        }

        _state.CastleWarInfo[castle] = cwi;
        NotifyCastleWarInfo(null);
    }

    /// <summary>Send the scoreboard. With a server, just push the stored state to it; otherwise recompute
    /// country points, the per-country top-3, and the defender/attacker for every castle, then broadcast to
    /// all maps. C++ NotifyCastleWarInfo.</summary>
    private void NotifyCastleWarInfo(ServerSession? server)
    {
        if (server is not null)
        {
            foreach (var cwi in _state.CastleWarInfo.Values)
                server.Send(BuildCastleWarInfoReq(cwi, _state.FindGuild(cwi.DefGuild), _state.FindGuild(cwi.AtkGuild)));
            return;
        }

        // Reset the working state for every castle.
        foreach (var cwi in _state.CastleWarInfo.Values)
        {
            cwi.EnableGuild.Clear();
            foreach (var kv in cwi.MapGuild) cwi.EnableGuild[kv.Key] = kv.Value;
            cwi.Top3[CDef].Clear(); cwi.Top3[CCra].Clear();
            cwi.AtkGuild = 0; cwi.DefGuild = 0;
            Array.Clear(cwi.CountryPoint);
        }

        // Country points + per-country top-3, then select each castle's defender.
        foreach (var cwi in _state.CastleWarInfo.Values)
        {
            foreach (var (guildId, bonus) in cwi.MapGuild.OrderBy(kv => kv.Key))
            {
                var guild = _state.FindGuild(guildId);
                if (guild is null) continue;
                if (guild.Country >= Proto.CountryCount) continue;

                cwi.CountryPoint[guild.Country] += (ushort)bonus;
                InsertTop3(cwi, guild, bonus);
            }
            SelectCastleWarGuild(true, cwi);
        }

        // A defender can't also attack: drop every selected defender from every castle's working set.
        foreach (var cwi in _state.CastleWarInfo.Values)
            foreach (var other in _state.CastleWarInfo.Values)
                other.EnableGuild.Remove(cwi.DefGuild);

        // Select each castle's attacker, then broadcast.
        foreach (var cwi in _state.CastleWarInfo.Values) SelectCastleWarGuild(false, cwi);

        foreach (var cwi in _state.CastleWarInfo.Values)
        {
            var def = _state.FindGuild(cwi.DefGuild);
            var atk = _state.FindGuild(cwi.AtkGuild);
            BroadcastServers(BuildCastleWarInfoReq(cwi, def, atk));
        }
    }

    /// <summary>Rank a guild into its country's top-3 by occupation bonus (ties broken by occupation history
    /// then guild rank). Faithful port of the inline top-3 block in C++ NotifyCastleWarInfo.</summary>
    private void InsertTop3(CastleWarInfo cwi, Guild guild, uint bonus)
    {
        var top = cwi.Top3[guild.Country];
        byte bId = (byte)(top.Count + 1);
        foreach (var kv in top)
        {
            var pTop = _state.FindGuild(kv.Value.Id);
            if (pTop is null || pTop.Id == guild.Id) { bId = 4; break; }

            if (kv.Value.Point < bonus) bId--;
            else if (kv.Value.Point == bonus)
            {
                uint sel = CompareOccupation(cwi, guild.Id, cwi, pTop.Id);
                if (sel == guild.Id) bId--;
                else if (sel == 0 && CompareGuildRank(guild, pTop)) bId--;
            }
        }

        if (bId >= 4) return;

        var newtop = new CastleTop3 { Id = guild.Id, Name = guild.Name, Point = (ushort)bonus };
        top.Remove(3);
        if (bId < 3 && top.TryGetValue(2, out var t2)) top[3] = t2;
        if (bId == 1 && top.TryGetValue(1, out var t1)) { top.Remove(2); top[2] = t1; }
        top.Remove(bId);
        top[bId] = newtop;
    }

    /// <summary>Pick the highest-bonus eligible guild as this castle's defender (bIsDef) or attacker, resolving
    /// ties and ensuring no guild is selected by two castles. Recursive, mirroring C++ SelectCastleWarGuild.</summary>
    private Guild? SelectCastleWarGuild(bool isDef, CastleWarInfo castle)
    {
        if (isDef) { castle.DefGuild = 0; castle.DefCountry = (byte)Contry.None; }
        else castle.AtkGuild = 0;

        Guild? pTop = null;
        uint topBonus = 0;
        foreach (var (gid, bonus) in castle.EnableGuild.OrderBy(kv => kv.Key))
        {
            var guild = _state.FindGuild(gid);
            if (guild is null) continue;
            if (!isDef && guild.Country == castle.DefCountry) continue;   // attacker must differ from defender's country

            if (pTop is null || topBonus < bonus) { pTop = guild; topBonus = bonus; }
            else if (topBonus == bonus)
            {
                if (pTop.Country != guild.Country && castle.CountryPoint[CDef] > castle.CountryPoint[CCra] && guild.Country == CDef)
                { pTop = guild; topBonus = bonus; }
                else if (pTop.Country != guild.Country && castle.CountryPoint[CCra] > castle.CountryPoint[CDef] && guild.Country == CCra)
                { pTop = guild; topBonus = bonus; }
                else
                {
                    uint sel = CompareOccupation(castle, guild.Id, castle, pTop.Id);
                    if (sel == guild.Id) { pTop = guild; topBonus = bonus; }
                    else if (sel == 0 && CompareGuildRank(guild, pTop)) { pTop = guild; topBonus = bonus; }
                }
            }
        }

        // Cross-castle deconfliction: if another castle already claimed pTop, the castle where pTop has the
        // higher occupation keeps it and the other re-selects.
        foreach (var other in _state.CastleWarInfo.Values)
        {
            if (pTop is null) break;
            if (!castle.EnableGuild.TryGetValue(pTop.Id, out uint hereBonus)) { pTop = null; break; }

            uint claimed = isDef ? other.DefGuild : other.AtkGuild;
            if (claimed != pTop.Id) continue;
            if (!other.MapGuild.TryGetValue(claimed, out uint thereBonus)) continue;

            if (thereBonus < hereBonus) { other.EnableGuild.Remove(claimed); SelectCastleWarGuild(isDef, other); }
            else if (thereBonus > hereBonus) { castle.EnableGuild.Remove(claimed); pTop = SelectCastleWarGuild(isDef, castle); }
            else
            {
                // CompareOccupation(this, claimed, other, claimed): both ids are equal, so the original can
                // only ever take the first branch (the else-if is unreachable) — preserved faithfully.
                uint sel = CompareOccupation(castle, claimed, other, claimed);
                if (sel == claimed) { other.EnableGuild.Remove(claimed); SelectCastleWarGuild(isDef, other); }
            }
        }

        if (pTop is not null)
        {
            if (isDef) { castle.DefGuild = pTop.Id; castle.DefCountry = pTop.Country; }
            else castle.AtkGuild = pTop.Id;
            castle.EnableGuild.Remove(pTop.Id);
        }
        return pTop;
    }

    /// <summary>Compare two guilds' per-day occupation history (most recent war-day first, wrapping the
    /// 6-day window at the castle battle-day). Returns the winning guild id, or 0 for a tie. C++ CompareOccupation.</summary>
    private uint CompareOccupation(CastleWarInfo c1, uint g1, CastleWarInfo c2, uint g2)
    {
        bool h1 = c1.MapOccupy.TryGetValue(g1, out var o1);
        bool h2 = c2.MapOccupy.TryGetValue(g2, out var o2);
        if (!h1 && !h2) return 0;
        if (h1 && !h2) return g1;
        if (!h1 && h2) return g2;

        // Castle battle-day is 1..7 in practice (the C++ loop assumes ≥1); clamp if the schedule is absent.
        byte day = _state.Battles?[BattleType.Castle].Day ?? 1;
        if (day == 0) day = 1;

        for (int d = day; d > 1; d--)
        {
            if (o1![d - 2] > o2![d - 2]) return g1;
            if (o1[d - 2] < o2[d - 2]) return g2;
            if (o1[d - 2] != 0) return c1.Id < c2.Id ? g2 : c1.Id > c2.Id ? g1 : 0u;
        }
        for (int d = 6; d >= day; d--)
        {
            if (o1![d - 1] > o2![d - 1]) return g1;
            if (o1[d - 1] < o2[d - 1]) return g2;
            if (o1[d - 1] != 0) return c1.Id < c2.Id ? g2 : c1.Id > c2.Id ? g1 : 0u;
        }
        return 0;
    }

    /// <summary>Tie-break by total PvP points, then member count, then earliest establish time. C++ CompareGuildRank.</summary>
    private static bool CompareGuildRank(Guild g1, Guild g2)
    {
        if (g1.PvPTotalPoint > g2.PvPTotalPoint) return true;
        if (g1.PvPTotalPoint != g2.PvPTotalPoint) return false;
        if (g1.Members.Count > g2.Members.Count) return true;
        if (g1.Members.Count != g2.Members.Count) return false;
        return g1.TimeEstablish < g2.TimeEstablish;
    }

    private static byte[] BuildCastleWarInfoReq(CastleWarInfo cwi, Guild? def, Guild? atk)
    {
        var w = new PacketWriter(Msg.MW_CASTLEWARINFO_REQ);
        w.WriteUInt16(cwi.Id);
        w.WriteUInt32(def?.Id ?? 0); w.WriteString(def?.Name ?? "");
        w.WriteByte(cwi.DefCountry);
        w.WriteUInt16(cwi.CountryPoint[CDef]);
        w.WriteUInt32(atk?.Id ?? 0); w.WriteString(atk?.Name ?? "");
        w.WriteUInt16(cwi.CountryPoint[CCra]);

        w.WriteUInt32((uint)cwi.MapGuild.Count);
        foreach (var (gid, bonus) in cwi.MapGuild.OrderBy(kv => kv.Key)) { w.WriteUInt32(gid); w.WriteUInt32(bonus); }

        w.WriteByte((byte)(cwi.Top3[CDef].Count + cwi.Top3[CCra].Count));
        foreach (var kv in cwi.Top3[CDef]) { w.WriteByte(CDef); w.WriteString(kv.Value.Name); w.WriteUInt16(kv.Value.Point); }
        foreach (var kv in cwi.Top3[CCra]) { w.WriteByte(CCra); w.WriteString(kv.Value.Name); w.WriteUInt16(kv.Value.Point); }
        return w.ToArray();
    }
}
