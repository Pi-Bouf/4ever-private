using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Monster visibility — <c>CS_ADDMON_ACK</c> / <c>CS_DELMON_ACK</c> (CSSender.cpp:913/1011) and the
/// grid-driven exchange (C++ <c>CTMap::EnterMAP</c>/<c>OnMove</c> → <c>CTCell::EnterMonster</c>/
/// <c>EnterPlayer</c>). A monster occupies one 64-unit cell exactly like a player; it becomes visible to the
/// players in its 3×3 view block, and a player entering / moving into a cell block learns of the monsters
/// there. Monsters use their own packets (not the player <c>CS_ENTER_ACK</c>).
///
/// <para>This phase is spawn + visibility only. Deferred (documented — PORT_STATUS.md): the DB spawn-chart
/// pipeline (<c>TMONSPAWNCHART</c>/<c>TMAPMONCHART</c>/<c>TMONSTERCHART</c>/<c>TMONATTRCHART</c>) and the
/// regen/respawn timer with its RNG weighted type-pick + radius scatter; monster movement/roam and all AI;
/// combat/damage/death/loot; the <c>GetColor</c> faction logic (a constant 0 is sent, as for the player
/// <c>bColor</c>); and monster maintain/buff skills (a fresh monster has none ⇒ a 0 count). Recall-mons,
/// companions, self-objs and NPCs (their own <c>ADD*</c> packets) are also deferred.</para>
/// </summary>
public sealed partial class MapService
{
    /// <summary>Spawns a monster: places it on the grid and announces it to the players in its 3×3 view
    /// (C++ <c>CTMap::EnterMAP(monster)</c> → <c>CTCell::EnterMonster</c>, <c>bNewMember = TRUE</c>).</summary>
    public void SpawnMonster(Monster m)
    {
        _state.AddMonster(m);
        foreach (var p in _state.PlayersAround(m)) SendCS_ADDMON_ACK(p, m, newMember: true);
    }

    /// <summary>Despawns a monster: tells the players in its view it is gone, then removes it (C++
    /// <c>LeaveMonster</c> → <c>SendCS_DELMON_ACK</c>).</summary>
    public void DespawnMonster(Monster m, bool exitMap = false)
    {
        foreach (var p in _state.PlayersAround(m)) SendCS_DELMON_ACK(p, m.Id, exitMap);
        _state.RemoveMonster(m);
    }

    /// <summary>Tells a just-entered player about every monster already in its view (C++
    /// <c>CTCell::EnterPlayer</c> <c>m_mapMONSTER</c> loop, <c>bNewMember = FALSE</c>).</summary>
    private void SendMonstersInView(ClientSession s)
    {
        foreach (var m in _state.MonstersInView(s)) SendCS_ADDMON_ACK(s, m, newMember: false);
    }

    /// <summary>C++ <c>CTPlayer::SendCS_ADDMON_ACK</c> — the monster appearance block, ending with the
    /// monster's maintained-skill (debuff) list. Field order/types are byte-exact with CSSender.cpp:928-972.</summary>
    private void SendCS_ADDMON_ACK(ClientSession s, Monster m, bool newMember)
    {
        var w = new PacketWriter(Msg.CS_ADDMON_ACK, capacity: 64);
        w.WriteUInt32(m.Id);
        w.WriteUInt16(m.ChartId);
        w.WriteByte(m.Level);
        w.WriteUInt32(m.MaxHp);
        w.WriteUInt32(m.Hp);
        w.WriteUInt32(m.MaxMp);
        w.WriteUInt32(m.Mp);
        w.WriteFloat(m.PosX);
        w.WriteFloat(m.PosY);
        w.WriteFloat(m.PosZ);
        w.WriteUInt16(m.Pitch);
        w.WriteUInt16(m.Dir);
        w.WriteByte(m.MouseDir);
        w.WriteByte(m.KeyDir);
        w.WriteByte(m.Action);
        w.WriteByte(m.Mode);
        w.WriteByte(newMember ? (byte)1 : (byte)0);
        w.WriteByte(m.Country);
        w.WriteByte(0);              // GetColor(...) — faction/name color vs viewer (deferred, as the player bColor)
        w.WriteUInt32(m.Region);
        w.WriteByte((byte)m.MaintainSkills.Count);   // the monster's active debuffs (Phase 31)
        foreach (var buff in m.MaintainSkills) WriteMaintainSkill(w, buff, NowMs);
        s.Send(w);
    }

    /// <summary>C++ <c>CTPlayer::SendCS_DELMON_ACK</c> — <c>dwMonID</c> + <c>bExitMap</c>.</summary>
    private static void SendCS_DELMON_ACK(ClientSession s, uint monId, bool exitMap)
    {
        var w = new PacketWriter(Msg.CS_DELMON_ACK, capacity: 8);
        w.WriteUInt32(monId);
        w.WriteByte(exitMap ? (byte)1 : (byte)0);
        s.Send(w);
    }
}
