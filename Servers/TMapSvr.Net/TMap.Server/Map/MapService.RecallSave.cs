using TMap.Data;

namespace TMap.Server.Map;

/// <summary>
/// Summons across logout — C++ <c>SendDM_SAVECHAR_REQ</c>'s summon part (SSSender.cpp:1300) and <c>OnDM_LOADCHAR_ACK</c>'s
/// (SSHandler.cpp:5224). Every summon still alive when the character is saved (its life left, or for good) is kept in
/// <c>TRECALLMONTABLE</c> with its buffs in <c>TRECALLMAINTAINTABLE</c>; at the next login each one comes back next to
/// its owner with the life it had left, and its buffs on.
/// <list type="bullet">
/// <item>A mount comes back only while its licence lasts, for at most its licence's time (as when it is called).</item>
/// <item>The C++ makes them on the spot with their old ids. The port's summons get their id from the world, so a saved
/// summon is asked of the world with no id, and its buffs wait until it arrives — matched on its chart and mount. One that
/// has not arrived when the character is saved again is saved as it was.</item>
/// <item>The enslaved monster is not saved: it comes back at every login by itself (MapService.Tame.cs). It still comes
/// after the saved ones, so — both being main summons — it is the one that stays, as in the C++.</item>
/// <item>A new summon starts at full HP/MP (see MapService.Recall.cs), so the saved HP/MP are kept but not used.</item>
/// <item>The player's own buffs ride along (<see cref="PlayerMaintains"/>): this baseline's <c>TSaveChar</c> clears them, and the
/// C++ writes them back with <c>TSaveSkillMaintain</c> — without it every save wiped them.</item>
/// </list>
/// </summary>
public sealed partial class MapService
{
    /// <summary>The save snapshot's summons and their buffs (C++ skips one whose life ran out).</summary>
    public static CharSaveData WithRecalls(CharSaveData d, Character ch, uint now)
    {
        var rows = new List<RecallSaveRow>();
        var buffs = new List<RecallMaintainRow>();
        foreach (var m in ch.Recalls.Values)
        {
            if (m.Expired(now) || IsTamedSummon(ch, m) || m.Attr is not { } attr) continue;
            rows.Add(new RecallSaveRow(m.Id, m.ChartId, m.PetId, (uint)(attr.Id | (attr.Level << 16)), m.Level, m.Hp, m.Mp,
                m.AtkSkillLevel, (short)m.PosX, (short)m.PosY, (short)m.PosZ, m.LifeLeft(now), m.Effect));
            foreach (var b in m.MaintainSkills)
            {
                uint remain = b.StartTick == 0 ? 0 : b.GetRemainTick(now);
                if (b.StartTick != 0 && remain == 0) continue;
                buffs.Add(new RecallMaintainRow(m.Id, b.SkillId, b.Level, remain, b.AttackType, b.AttackId, b.HostType, b.HostId,
                    b.AttackCountry));
            }
        }
        foreach (var (row, waiting) in ch.PendingRecalls)                                 // not back yet: as they were
        {
            rows.Add(row);
            buffs.AddRange(waiting);
        }
        return d with { Recalls = rows, RecallMaintains = buffs, Maintains = PlayerMaintains(ch, now) };
    }

    /// <summary>C++ <c>SendDM_SAVECHAR_REQ</c>'s player buffs (SSSender.cpp:1110): all but the shop's "storing" buff, those
    /// for good or with time left. (The territory zone buffs it also skips are not ported.)</summary>
    private static List<MaintainLoadRow> PlayerMaintains(Character ch, uint now)
    {
        var list = new List<MaintainLoadRow>();
        foreach (var b in ch.MaintainSkills)
        {
            if (b.SkillId == TstoreSkill || IsZoneBuff(ch, b.SkillId)) continue;   // nor a territory's zone buffs
            uint remain = b.StartTick == 0 ? 0 : b.GetRemainTick(now);
            if (b.StartTick != 0 && remain == 0) continue;
            list.Add(new MaintainLoadRow(b.SkillId, b.Level, remain, b.AttackType, b.AttackId, b.HostType, b.HostId, b.AttackCountry));
        }
        return list;
    }

    private const ushort TstoreSkill = 804;                                            // TSTORE_SKILL

    private static bool IsTamedSummon(Character ch, RecallMon m)
        => ch.Class == TclassSorcerer && ch.Persist.TemptedMon != 0 && m.ChartId == ch.Persist.TemptedMon && m.RecallType == TrecallMain;

    /// <summary>The rows read at char load, kept until login (the C++ skips the tutorial; a row with no chart is the
    /// baseline proc's empty row).</summary>
    public static void LoadSavedRecalls(Character ch, (List<RecallSaveRow> Recalls, List<RecallMaintainRow> Buffs) saved)
    {
        ch.PendingRecalls.Clear();
        foreach (var row in saved.Recalls)
            if (row.MonId != 0) ch.PendingRecalls.Add((row, saved.Buffs.Where(b => b.RecallId == row.Id).ToList()));
    }

    /// <summary>C++ <c>OnDM_LOADCHAR_ACK</c> → <c>CreateRecallMon</c> per saved summon: asked of the world, 2 units behind
    /// the owner.</summary>
    private void RestoreSavedRecalls(ClientSession s, Character ch)
    {
        if (IsTutorial(ch)) { ch.PendingRecalls.Clear(); return; }
        float rad = ch.Dir * MathF.PI / 900f;
        float x = ch.PosX - 2f * MathF.Sin(rad), z = ch.PosZ - 2f * MathF.Cos(rad);
        long now = UnixNow();
        foreach (var (row, _) in ch.PendingRecalls.ToList())
        {
            if (!_templates.MonsterTemplates.TryGetValue(row.MonId, out var tpl)) { DropPending(ch, row); continue; }
            uint life = row.Time;
            string name = "";
            if (tpl.RecallType == RecallMon.TypePet)
            {
                // The licence must still be good; the mount lives at most what is left of it (PET_LIVE_DURATION capped).
                if (!ch.Pets.TryGetValue(row.PetId, out var pet) || (pet.EndTime != 0 && pet.EndTime <= now)) { DropPending(ch, row); continue; }
                life = (uint)(Math.Min(PetLiveDuration, pet.EndTime != 0 ? pet.EndTime - now : 0) * 1000);
                name = pet.Name;
            }
            SendMW_CREATERECALLMON_ACK(new RecallRecord(ch.CharId, s.Key, 0, row.MonId, row.Attr, row.PetId, row.Effect, name,
                row.Level, tpl.Class, tpl.Race, TaStand, 1 /* OS_WAKEUP */, MtNormal, 0, 0, row.Hp, row.Mp, 100, row.SkillLevel,
                x, ch.PosY, z, 0, life, RecallAuto: 0, TargetId: 0, TargetType: 0, tpl.Skills.ToList()));
        }
    }

    private static void DropPending(Character ch, RecallSaveRow row) => ch.PendingRecalls.RemoveAll(p => ReferenceEquals(p.Row, row));

    /// <summary>A summon the world made: if it is a saved one coming back, its saved buffs go back on (C++ SSHandler.cpp:5340 —
    /// a remaining time counts from now; 0 is for good).</summary>
    private void RestoredRecallArrived(Character ch, RecallMon mon)
    {
        int i = ch.PendingRecalls.FindIndex(p => p.Row.MonId == mon.ChartId && p.Row.PetId == mon.PetId);
        if (i < 0) return;
        var buffs = ch.PendingRecalls[i].Buffs;
        ch.PendingRecalls.RemoveAt(i);
        foreach (var b in buffs)
        {
            if (mon.MaintainSkills.Any(m => m.SkillId == b.SkillId) || _templates.Skill(b.SkillId) is not { } tpl) continue;
            var buff = new MaintainSkill
            {
                SkillId = b.SkillId, Level = b.Level, AttackType = b.AttackType, AttackId = b.AttackId, HostType = b.HostType,
                HostId = b.HostId, AttackCountry = b.AttackCountry, Template = tpl,
            };
            if (b.Remain != 0) buff.SetLoopEndTick(NowMs, b.Remain);
            mon.MaintainSkills.Add(buff);
        }
    }
}
