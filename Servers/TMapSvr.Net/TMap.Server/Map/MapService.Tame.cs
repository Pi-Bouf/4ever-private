using TMap.Data;

namespace TMap.Server.Map;

/// <summary>
/// Taming — the sorcerer's two innate skills (max level 0, always usable, see <see cref="LearnedSkill"/>):
/// <list type="bullet">
/// <item><b>Enslave Monster</b> (617, <c>SDT_AI</c>/<c>SDT_TEMPT</c>) is cast on a monster's corpse whose level is at
/// most the caster's (C++ <c>PerformSkill</c> SDT_TEMPT, TObjBase.cpp:3844). The corpse's monster kind becomes the
/// character's tamed monster (<c>m_wTemptedMon</c>, saved in <c>TCHARTABLE.wTemptedMon</c>). The C++ routes this
/// through the world (<c>MW_MONTEMPT_ACK</c> → <c>MW_MONTEMPT_REQ</c>) because the caster may sit on another map server;
/// here the caster is always the sender, so it is set directly. Nothing is sent to the client.</item>
/// <item><b>Evocate Monster</b> (618, <c>SDT_RECALL</c>/<c>SER_MONSTER</c>) is cast on <b>oneself</b> (the client always
/// targets the caster — TClientGame.cpp, <c>TEVOCATE_MONSTER_SKILL</c>): it calls the tamed monster as an ordinary main
/// summon in front of the caster, for the skill's duration — the old sources (MapService.Summon.cs
/// <c>PerformSummon</c>). Landing on a monster it fails, as in the old sources.</item>
/// <item>At login a sorcerer with a tamed monster gets it as a lasting main summon (C++ <c>InitCharInfo</c>,
/// TPlayer.cpp:6006 — the same in the old sources).</item>
/// </list>
/// <para>Sources differ: the old sources require the monster to be tameable (<c>TMONSTERCHART.bTame</c>); 5.0 comments
/// that check out. The old rule is kept. 5.0's Evocate is cast on an enemy and puts three copies of the tamed monster
/// around it (<c>MW_MONTEMPTEVO</c>, <c>EVOCATE_MAX</c>); by choice, only the old self-cast is kept.</para>
/// </summary>
public sealed partial class MapService
{
    private const byte TclassSorcerer = 5;          // TCLASS_SORCERER

    /// <summary>The taming rows of a player's skill that landed on <paramref name="mon"/>; false when one fails
    /// (C++ <c>PERFORM_FAIL</c>). A skill with neither row does nothing here.</summary>
    private bool PerformTameSkill(Character owner, SkillTemplate tpl, Monster mon, byte attackerLevel)
    {
        bool ok = true;
        if (tpl.IsTempt()) ok &= TemptMonster(owner, mon, attackerLevel);
        if (tpl.IsMonRecall()) ok = false;   // old sources: a summon row on anything but a player fails
        return ok;
    }

    /// <summary>C++ <c>PerformSkill</c> <c>SDT_TEMPT</c>: the corpse's kind becomes the caster's tamed monster when it can
    /// be tamed and is not above the caster's level. Returns whether it took.</summary>
    private bool TemptMonster(Character owner, Monster mon, byte attackerLevel)
    {
        if (!_templates.MonsterTemplates.TryGetValue(mon.ChartId, out var mt) || mt.Tame == 0) return false;
        if (mon.Level > attackerLevel) return false;
        owner.Persist.TemptedMon = mon.ChartId;
        _log.LogDebug("[tame] char {Char} enslaved monster kind {Mon}.", owner.CharId, mon.ChartId);
        return true;
    }

    /// <summary>C++ <c>CTPlayer::InitCharInfo</c> (TPlayer.cpp:6006): a sorcerer logs in with its tamed monster as a
    /// main summon that never expires, 2 units behind it, at hit 100 and skill level 1.</summary>
    private void SummonTamedAtLogin(ClientSession s, Character ch)
    {
        if (ch.Class != TclassSorcerer || ch.Persist.TemptedMon == 0) return;
        if (!_templates.MonsterTemplates.TryGetValue(ch.Persist.TemptedMon, out var mt)) return;
        uint attr = (uint)(mt.SummonAttr | (ch.Level << 16));
        float rad = ch.Dir * MathF.PI / 900f;
        SendMW_CREATERECALLMON_ACK(new RecallRecord(ch.CharId, s.Key, 0, mt.Id, attr, 0, 0, "", ch.Level,
            mt.Class, mt.Race, TaStand, 1, MtNormal, 0, 0, 0, 0, Hit: 100, SkillLevel: 1,
            ch.PosX - 2f * MathF.Sin(rad), ch.PosY, ch.PosZ - 2f * MathF.Cos(rad), ch.Dir, Time: 0,
            RecallAuto: 0, TargetId: 0, TargetType: 0, mt.Skills.ToList()));
    }
}
