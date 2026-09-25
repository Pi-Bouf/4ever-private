using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// The death penalty and priest resurrection.
///
/// <para><b>Aftermath</b> (C++ <c>m_aftermath</c>, <c>CTPlayer::SetAftermath</c>/<c>ResetAftermath</c>,
/// TPlayer.cpp:3445-3545): a step from 0 to 100. Reviving adds to it — 20 in town, 10 on the spot, 12 by a
/// priest, 0 by one's own skill — from level 10 up. Every primary stat loses <c>step · 0.3</c> % of its base
/// (<see cref="StatEngine.Stat"/>), and one step recovers every <c>27 + step · 0.3</c> seconds. The step is
/// what the DB saves; the C++ also computes a cooldown increase (<c>m_fReuseInc</c>) that nothing ever reads.</para>
///
/// <para><b>Resurrection</b>: a revival cure (<c>SCT_REVIVAL</c>) cast on a dead player asks them
/// (<c>CS_REVIVALASK_ACK</c>); a yes (<c>CS_REVIVALASK_REQ</c>) revives in place with the skill's HP/MP, a no
/// tells the caster (<c>CS_REVIVALREPLY_ACK</c>). Cast on oneself it revives at once.</para>
///
/// <para><b>Not ported / changed:</b> the equipment that speeds recovery (<c>ABILITY_AFTERMATH</c>) — recovery is
/// one step at a time; a recovery that would go below 0 stops at 0 (the C++ BYTE subtraction would wrap); and a
/// "yes" from a player who is not dead is ignored (the C++ does not check, which let any player refill to full).</para>
/// </summary>
public sealed partial class MapService
{
    private const byte AftermathNone = 0, AftermathGhost = 10, AftermathHelp = 12, AftermathAtOnce = 20;   // TMapType.h:59
    private const byte SctRevival = 1, SctAftermath = 13;                                                  // SKILL_CURE_TYPE
    private const ushort TrevivalSkill = 800;                                                              // TREVIVAL_SKILL
    private const byte AskYes = 0;

    private static uint AftermathInterval(byte step) => (uint)(27 + step * 0.3) * 1000;   // DWORD(27 + step·0.3)·1000

    /// <summary>C++ <c>CTPlayer::SetAftermath</c> — add <paramref name="step"/> (players under level 10 are exempt).
    /// Returns whether the step changed, in which case HP/MP are clamped to the lowered maxima and everyone around
    /// is told.</summary>
    private bool SetAftermath(ClientSession s, Character ch, byte step)
    {
        if (ch.Level < 10) return false;
        byte prev = ch.Persist.Aftermath;
        ch.Persist.Aftermath = (byte)Math.Min(100, prev + step);
        if (ch.Persist.Aftermath == prev) return false;

        ch.AftermathTick = NowMs + AftermathInterval(ch.Persist.Aftermath);
        ch.Hp = Math.Min(ch.Hp, MaxHpFor(ch));
        ch.Mp = Math.Min(ch.Mp, MaxMpFor(ch));
        BroadcastAftermath(s, ch);
        return true;
    }

    /// <summary>C++ <c>CTPlayer::ResetAftermath</c> (TPlayer.cpp:3497), run from the player timer.</summary>
    public void RunAftermath(uint now)
    {
        foreach (var s in _state.AllInGame())
        {
            if (s.Char is not { } ch || ch.Persist.Aftermath == 0 || ch.AftermathTick >= now) continue;

            ch.Persist.Aftermath = (byte)Math.Max(0, ch.Persist.Aftermath - 1);
            ch.AftermathTick += AftermathInterval(ch.Persist.Aftermath);
            uint maxHp = MaxHpFor(ch), maxMp = MaxMpFor(ch);
            var ack = BuildCS_AFTERMATH_ACK(ch.CharId, ch.Persist.Aftermath);
            foreach (var p in _state.InView(s))
            {
                p.Send(ack);
                SendSelfHpMp(p, ch.CharId, maxHp, ch.Hp, maxMp, ch.Mp);
            }
            SendCS_CHARSTATINFO_ACK(s, ch);
        }
    }

    /// <summary>At login the stored step is re-applied through <c>SetAftermath</c> from zero (SSHandler.cpp:5674),
    /// which also arms the recovery timer — and drops it for a player under level 10.</summary>
    private void RestoreAftermath(Character ch)
    {
        byte stored = ch.Persist.Aftermath;
        ch.Persist.Aftermath = 0;
        if (ch.Level < 10 || stored == 0) return;
        ch.Persist.Aftermath = (byte)Math.Min(100, (int)stored);
        ch.AftermathTick = NowMs + AftermathInterval(ch.Persist.Aftermath);
    }

    /// <summary>C++ <c>CTPlayer::Revival</c> (TPlayer.cpp:3610) — the penalty, HP/MP by kind, back to normal, the
    /// news to everyone around, and the short revival-protection buff.</summary>
    private void Revive(ClientSession s, Character ch, byte aftermath, SkillTemplate? tpl, byte level)
    {
        if (SetAftermath(s, ch, aftermath)) SendCS_CHARSTATINFO_ACK(s, ch);

        uint maxHp = MaxHpFor(ch), maxMp = MaxMpFor(ch);
        ch.Action = 0;                                                  // TA_STAND
        ch.Mode = MtNormal;                                             // ChgMode(MT_NORMAL), told to everyone around
        var mode = new PacketWriter(Msg.CS_CHGMODE_ACK, capacity: 8);
        mode.WriteUInt32(ch.CharId); mode.WriteByte(OtPc); mode.WriteByte(MtNormal);
        var modeAck = mode.ToArray();
        foreach (var p in _state.InView(s)) p.Send(modeAck);
        switch (aftermath)
        {
            case AftermathAtOnce: ch.Hp = (uint)(maxHp * 0.3); ch.Mp = (uint)(maxMp * 0.3); break;
            case AftermathGhost: ch.Hp = (uint)(maxHp * 0.4); ch.Mp = (uint)(maxMp * 0.4); break;
            case AftermathHelp when tpl is not null:
                ch.Hp = unchecked((uint)((int)maxHp + tpl.CalcValue(level, SkillTemplate.SdtAbility, MtypeHp, maxHp)));
                ch.Mp = unchecked((uint)((int)maxMp + tpl.CalcValue(level, SkillTemplate.SdtAbility, MtypeMp, maxMp)));
                break;
            case AftermathNone: ch.Hp = maxHp; ch.Mp = maxMp; break;
        }
        if (ch.Hp == 0) ch.Hp = 1;
        RespawnCompanion(s, ch);                                        // TPlayer.cpp:3744
        ch.RecoverHpTick = NowMs; ch.RecoverMpTick = NowMs;

        var reviveAck = BuildRevivalAck(ch.CharId, ch.PosX, ch.PosY, ch.PosZ);
        foreach (var p in _state.InView(s))
        {
            p.Send(reviveAck);
            SendSelfHpMp(p, ch.CharId, maxHp, ch.Hp, maxMp, ch.Mp);
        }
        ForceMaintain(s, ch, TrevivalSkill, ch.CharId, OtPc, ch.CharId, OtPc, 0);
    }

    /// <summary>C++ <c>OnCS_REVIVALASK_REQ</c> (CSHandler.cpp:10569) — the dead player's answer to a priest.</summary>
    private void OnCS_REVIVALASK_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        byte reply = r.ReadByte();
        uint attackerId = r.ReadUInt32();
        byte attackerType = r.ReadByte();
        ushort skillId = r.ReadUInt16();
        byte level = r.ReadByte();

        if (reply == AskYes)
        {
            if (ch.Hp != 0) return;                                     // see the class remarks
            if (_templates.Skills.TryGetValue(skillId, out var tpl)) Revive(s, ch, AftermathHelp, tpl, level);
        }
        else if (attackerType == OtPc && _state.FindByChar(attackerId) is { } caster)
        {
            var w = new PacketWriter(Msg.CS_REVIVALREPLY_ACK);
            w.WriteByte(reply); w.WriteUInt32(ch.CharId);
            caster.Send(w);
        }
    }

    /// <summary>The two cure effects that touch death: <c>SCT_REVIVAL</c> (ask, or revive oneself) and
    /// <c>SCT_AFTERMATH</c> (take steps off the penalty) — C++ <c>PerformSkill</c>, TObjBase.cpp:3392-3440.</summary>
    private void ApplyDeathCures(ClientSession targetSession, Character target, uint attackId, SkillTemplate tpl, byte level)
    {
        foreach (var d in tpl.Data)
        {
            if (d.Type != SkillTemplate.SdtCure) continue;
            if (d.Exec == SctRevival && target.Hp == 0)
            {
                if (attackId != target.CharId)
                {
                    var w = new PacketWriter(Msg.CS_REVIVALASK_ACK);
                    w.WriteUInt32(attackId); w.WriteByte(OtPc); w.WriteUInt16(tpl.Id); w.WriteByte(level);
                    targetSession.Send(w);
                }
                else Revive(targetSession, target, AftermathNone, tpl, 0);
            }
            else if (d.Exec == SctAftermath)
            {
                int v = tpl.DataValue(d, level);
                target.Persist.Aftermath = target.Persist.Aftermath > v ? (byte)(target.Persist.Aftermath - v) : (byte)0;
                SendCS_CHARSTATINFO_ACK(targetSession, target);
                BroadcastAftermath(targetSession, target);
            }
        }
    }

    private void BroadcastAftermath(ClientSession s, Character ch)
    {
        var ack = BuildCS_AFTERMATH_ACK(ch.CharId, ch.Persist.Aftermath);
        foreach (var p in _state.InView(s)) p.Send(ack);
    }

    /// <summary>C++ <c>SendCS_AFTERMATH_ACK</c> (CSSender.cpp:5810) — <c>dwCharID · bStep</c>.</summary>
    private static byte[] BuildCS_AFTERMATH_ACK(uint charId, byte step)
    {
        var w = new PacketWriter(Msg.CS_AFTERMATH_ACK, capacity: 8);
        w.WriteUInt32(charId); w.WriteByte(step);
        return w.ToArray();
    }
}
