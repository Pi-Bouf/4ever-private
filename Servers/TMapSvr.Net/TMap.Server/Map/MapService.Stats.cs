using Microsoft.Extensions.Logging;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// The character stat sheet — <c>OnCS_CHARSTATINFO_REQ</c> / <c>SendCS_CHARSTATINFO_ACK</c>
/// (CSHandler.cpp:7119 / CSSender.cpp:3241). The client asks for a character's computed combat stats
/// (opening its own sheet, or inspecting another); the map replies with the 31-field block computed by
/// <see cref="StatEngine"/>. Cross-server targets (not resident on this map) are a documented deferral —
/// the C++ forwards to the world via <c>MW_CHARSTATINFO_ACK</c>.
/// </summary>
public sealed partial class MapService
{
    private void OnCS_CHARSTATINFO_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame) return;
        uint charId = r.ReadUInt32();

        var target = _state.FindByChar(charId);
        if (target is { State: EnterState.InGame, Char: { } ch })
            SendCS_CHARSTATINFO_ACK(s, ch);
        else
            _log.LogDebug("CS_CHARSTATINFO_REQ for char {Char} not resident here (world forward deferred).", charId);
    }

    /// <summary>Builds the 87-byte stat block for <paramref name="ch"/> and sends it to
    /// <paramref name="to"/> (the requester). Every field is computed via <see cref="StatEngine"/>.</summary>
    private void SendCS_CHARSTATINFO_ACK(ClientSession to, Character ch)
    {
        var t = _templates;
        var w = new PacketWriter(Msg.CS_CHARSTATINFO_ACK, capacity: 96);
        w.WriteUInt32(ch.CharId);
        w.WriteUInt16(StatEngine.Ability(ch, StatEngine.MtypeStr, t));
        w.WriteUInt16(StatEngine.Ability(ch, StatEngine.MtypeDex, t));
        w.WriteUInt16(StatEngine.Ability(ch, StatEngine.MtypeCon, t));
        w.WriteUInt16(StatEngine.Ability(ch, StatEngine.MtypeInt, t));
        w.WriteUInt16(StatEngine.Ability(ch, StatEngine.MtypeWis, t));
        w.WriteUInt16(StatEngine.Ability(ch, StatEngine.MtypeMen, t));
        w.WriteUInt32(StatEngine.MinAp(ch, arrow: false, t)); // min melee AP
        w.WriteUInt32(StatEngine.MaxAp(ch, arrow: false, t)); // max melee AP
        w.WriteUInt32(StatEngine.DefendPower(ch, t));         // physical DP
        w.WriteUInt32(StatEngine.MinAp(ch, arrow: true, t));  // min ranged AP
        w.WriteUInt32(StatEngine.MaxAp(ch, arrow: true, t));  // max ranged AP
        w.WriteUInt32(StatEngine.AtkSpeed(ch, StatEngine.TadPhysical, t));
        w.WriteUInt32(StatEngine.AtkSpeed(ch, StatEngine.TadLong, t));
        w.WriteUInt32(StatEngine.AtkSpeed(ch, StatEngine.TadMagic, t));
        w.WriteUInt32(StatEngine.AtkSpeedRate(ch, StatEngine.TadPhysical, t));
        w.WriteUInt32(StatEngine.AtkSpeedRate(ch, StatEngine.TadLong, t));
        w.WriteUInt32(StatEngine.AtkSpeedRate(ch, StatEngine.TadMagic, t));
        w.WriteUInt16(StatEngine.AttackLevel(ch, t));
        w.WriteUInt16(StatEngine.DefendLevel(ch, t));
        w.WriteByte(StatEngine.CriticalPysProb(ch, t));
        w.WriteUInt32(StatEngine.MinMagicAp(ch, t));
        w.WriteUInt32(StatEngine.MaxMagicAp(ch, t));
        w.WriteUInt32(StatEngine.MagicDefPower(ch, t));
        w.WriteUInt16(StatEngine.MagicAtkLevel(ch, t));
        w.WriteUInt16(StatEngine.MagicDefLevel(ch, t));
        w.WriteByte(StatEngine.ChargeSpeed(ch, t));
        w.WriteByte(StatEngine.ChargeProb(ch, t));
        w.WriteByte(StatEngine.CriticalMagicProb(ch, t));
        w.WriteUInt16(0); // m_wSkillPoint (not modelled yet; CS_CHARINFO_ACK also emits 0)
        w.WriteByte(0);   // m_aftermath.m_bStep (death-penalty step; aftermath deferred)
        to.Send(w);
    }
}
