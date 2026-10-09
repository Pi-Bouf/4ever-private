using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Guilds, batch G4 — the guild skills (C++ <c>SendCS_GUILDSKILLUPDATE_ACK</c> / <c>GetGuildSkill</c>, <c>OnCS_GUILDSKILLACTION_REQ</c>,
/// <c>OnMW_GUILDSKILLACTION_ACK</c>, <c>OnMW_UPDATEGUILDCOOLDOWN_REQ</c>, <c>OnMW_ADDCOOLDOWN_REQ</c>, <c>SaveGuildSkill</c>):
/// <list type="bullet">
/// <item><b>Which</b>: <c>TGUILDSKILLCHART</c> (migration 016) says the duty each needs — member 0, vice-chief 1, chief 2. A
/// member's skills are its own (<c>TGUILDMEMBERSKILLTABLE</c>); the officers' are the guild's (<c>TGUILDMASTERSKILLTABLE</c>),
/// shown to those of that duty or above. They are re-read on entering, on founding / joining / leaving and a duty changed,
/// and when another officer buys, renews or casts one; then sent (<c>CS_GUILDSKILLUPDATE_ACK</c>: id, level, end) and put
/// among the character's skills while their time lasts.</item>
/// <item><b>The founder</b> gets every guild skill at level 1 with no time — the window offers to renew them.</item>
/// <item><b>Renewing</b> one run out costs 300 of the character's PvP points and gives 31 days; <b>buying</b> a level (while it
/// lasts) costs a stat point — the character's for a member skill, the guild's (taken by the world) for an officer's.</item>
/// <item><b>Casting</b>: a guild skill needs the guild and the duty, never touches a monster and always hits; an officer's,
/// cast, starts its cooldown for the whole guild on every map.</item>
/// </list>
/// <para><b>Changed, flagged:</b> guild skills come only with a guild (the C++ kept a member's after it left); they are never
/// saved with the character's own skills (the C++ wrote them to <c>TSKILLTABLE</c> too and deleted them again on the next
/// update); a stat point is checked before the level is raised (the C++ raised it first); and a guild cooldown started
/// elsewhere reaches the members online at once (the C++ stored it after they had re-read their skills).</para>
/// </summary>
public sealed partial class MapService
{
    private const byte GsBuy = 0, GsRenew = 1;                                 // GUILDSKILL_ACTION
    private const uint SkillRenewPrice = 300;                                  // SKILL_RENEW_PRICE
    private const long GuildSkillPeriod = 31 * 86400L;                         // DAY_ONE * 31

    /// <summary>The guild skills store — the game database in production, a fake in tests.</summary>
    public IGuildSkillStore? GuildSkillStore { get; set; }

    /// <summary>C++ <c>m_mapGuildCooldown</c>: per guild, when each officer skill may be cast again (map clock).</summary>
    private readonly Dictionary<uint, Dictionary<ushort, long>> _guildCooldown = new();

    private bool IsGuildSkill(ushort skillId) => _templates.GuildSkillTypes.ContainsKey(skillId);

    /// <summary>C++ <c>SendCS_GUILDSKILLUPDATE_ACK</c>: the guild skills read again (C++ <c>GetGuildSkill</c>) — the officer ones
    /// of the duty or below, then the member ones —, sent, and the live ones put among the character's skills.
    /// <paramref name="notify"/> false on entering, where the character info carries the skills.</summary>
    private async Task RefreshGuildSkills(ClientSession s, bool notify = true)
    {
        if (s.Char is not { } ch) return;
        var rows = new List<(GuildSkillRow Row, byte Type)>();
        if (ch.GuildId != 0 && GuildSkillStore is { } db)
        {
            var (member, guild) = await Safe(() => db.LoadGuildSkillsAsync(ch.CharId, ch.GuildDuty >= GuildDutyViceChief ? ch.GuildId : 0),
                (new List<GuildSkillRow>(), new List<GuildSkillRow>()));
            if (s.Char != ch) return;
            foreach (var g in guild)
                if (_templates.GuildSkillTypes.TryGetValue(g.SkillId, out byte t) && t != 0 && t <= ch.GuildDuty) rows.Add((g, t));
            foreach (var m in member)
                if (_templates.GuildSkillTypes.TryGetValue(m.SkillId, out byte t) && t == 0) rows.Add((m, 0));
        }

        var waits = ch.Skills.Where(k => IsGuildSkill(k.SkillId)).ToDictionary(k => k.SkillId, k => k.GetReuseRemainTick(NowMs));   // kept: reading again is no reset
        ch.Skills.RemoveAll(k => IsGuildSkill(k.SkillId));
        ch.GuildSkills.Clear();
        long now = UnixNow();
        var w = new PacketWriter(Msg.CS_GUILDSKILLUPDATE_ACK, capacity: 1 + rows.Count * 11);
        w.WriteByte((byte)rows.Count);
        foreach (var (row, type) in rows)
        {
            w.WriteUInt16(row.SkillId); w.WriteByte(row.Level); w.WriteInt64(row.EndTime);
            ch.GuildSkills[row.SkillId] = new GuildSkillHeld(row.SkillId, type, row.Level, row.EndTime);
            if (row.EndTime <= now || row.Level == 0) continue;                  // run out: not castable (C++ CheckGuildSkillTick)
            var skill = new Skill { SkillId = row.SkillId, Level = row.Level, Template = _templates.Skill(row.SkillId) };
            uint remain = Math.Max(GuildCooldownRemain(ch.GuildId, row.SkillId), waits.GetValueOrDefault(row.SkillId));
            if (remain != 0) skill.SetCooldown(NowMs, remain);
            ch.Skills.Add(skill);
            if (notify) SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.Success, row.SkillId, row.Level, remain);
        }
        s.Send(w);
    }

    private uint GuildCooldownRemain(uint guildId, ushort skillId)
        => _guildCooldown.TryGetValue(guildId, out var cds) && cds.TryGetValue(skillId, out long end) && end > NowMs ? (uint)(end - NowMs) : 0u;

    /// <summary>C++ <c>SaveGuildSkill</c>: each guild skill held, under the character or the guild.</summary>
    private async Task SaveGuildSkills(Character ch)
    {
        if (GuildSkillStore is not { } db || ch.GuildId == 0) return;
        foreach (var gs in ch.GuildSkills.Values.ToList())
            await Safe(async () => { await db.SaveGuildSkillAsync(ch.CharId, ch.GuildId, gs.Type, new GuildSkillRow(gs.SkillId, gs.Level, gs.EndTime)); return 0; }, 0);
    }

    /// <summary>C++ <c>OnMW_GUILDESTABLISH_REQ</c>: the founder holds every guild skill at level 1, with no time yet.</summary>
    private async Task GiveFounderGuildSkills(ClientSession s, Character ch)
    {
        foreach (var (id, type) in _templates.GuildSkillTypes)
            if (!ch.GuildSkills.ContainsKey(id)) ch.GuildSkills[id] = new GuildSkillHeld(id, type, 1, 0);
        await SaveGuildSkills(ch);
        await RefreshGuildSkills(s);
    }

    /// <summary>C++ <c>OnCS_GUILDSKILLACTION_REQ</c>: at most two words; buying a level needs the skill's duty and room to grow —
    /// the world is told the skill's type (an officer skill takes the guild's stat point there).</summary>
    private void OnCS_GUILDSKILLACTION_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { } ch) return;
        byte action = r.ReadByte(), size = r.ReadByte();
        if (size > 2) return;
        var param = new ushort[size];
        for (int i = 0; i < size; i++) param[i] = r.ReadUInt16();
        if (action == GsBuy)
        {
            if (size < 1 || !_templates.GuildSkillTypes.TryGetValue(param[0], out byte type) || !CheckGuildDuty(ch, type)) return;
            if (ch.GuildSkills.TryGetValue(param[0], out var held) && _templates.Skill(param[0]) is { } t && held.Level >= t.MaxLevel) return;
            param = new[] { param[0], (ushort)type };
        }
        SendToWorld(Msg.MW_GUILDSKILLACTION_REQ, s, w =>
        {
            w.WriteByte(action); w.WriteByte((byte)param.Length);
            foreach (var p in param) w.WriteUInt16(p);
        });
    }

    /// <summary>C++ <c>OnMW_GUILDSKILLACTION_ACK</c>: a level bought (a member skill takes the character's stat point) or a run-out
    /// skill renewed for 300 PvP points — an officer skill then re-read by the guild; saved, the skills sent again.</summary>
    private async Task OnMW_GUILDSKILLACTION_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte action = r.ReadByte(), size = r.ReadByte();
        var param = new ushort[size];
        for (int i = 0; i < size; i++) param[i] = r.ReadUInt16();
        if (FindPlayer(charId, key) is not { IsMain: true, Char: { GuildId: not 0 } ch } s || size == 0) return;
        ushort id = param[0];
        if (!_templates.GuildSkillTypes.TryGetValue(id, out byte type) || _templates.Skill(id) is not { } tpl) return;
        long now = UnixNow();
        ch.GuildSkills.TryGetValue(id, out var held);

        switch (action)
        {
            case GsBuy:
                if (held is not null && held.Level >= tpl.MaxLevel) return;
                if (type == 0)
                {
                    if (ch.Persist.StatPoint == 0) return;
                    ch.Persist.StatPoint--;
                }
                ch.GuildSkills[id] = held = held is null ? new GuildSkillHeld(id, type, 1, now + GuildSkillPeriod) : held with { Level = (byte)(held.Level + 1) };
                if (type != 0) SendUpdateGuildCooldown(ch.GuildId, id, held.Level, renew: true, use: false);
                break;
            case GsRenew:
                if (held is null || ch.PvpUseablePoint < SkillRenewPrice || !CheckGuildDuty(ch, type) || held.EndTime > now) return;
                UsePvPoint(s, ch, SkillRenewPrice, 0, PvpUseable);
                SendCS_MONEY_ACK(s, ch);
                ch.GuildSkills[id] = held = held with { EndTime = now + GuildSkillPeriod };
                if (type != 0) SendUpdateGuildCooldown(ch.GuildId, id, held.Level, renew: true, use: true);
                break;
            default:
                return;
        }

        await SaveGuildSkills(ch);
        if (s.Char != ch) return;
        await RefreshGuildSkills(s);
        var ack = new PacketWriter(Msg.CS_GUILDSKILLACTION_ACK, capacity: 2 + size * 2);
        ack.WriteByte(action); ack.WriteByte(size);
        foreach (var p in param) ack.WriteUInt16(p);
        s.Send(ack);
    }

    private void SendUpdateGuildCooldown(uint guildId, ushort skillId, byte level, bool renew, bool use)
    {
        var w = new PacketWriter(Msg.MW_UPDATEGUILDCOOLDOWN_ACK, capacity: 15);
        w.WriteUInt32(guildId); w.WriteUInt16(skillId); w.WriteByte(level); w.WriteUInt32(renew ? 1u : 0u); w.WriteUInt32(use ? 1u : 0u);
        _world.Send(w);
    }

    /// <summary>C++ <c>OnMW_UPDATEGUILDCOOLDOWN_REQ</c>: another officer changed or cast a guild skill — read them again.</summary>
    private async Task OnMW_UPDATEGUILDCOOLDOWN_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        if (FindPlayer(charId, key) is { IsMain: true } s) await RefreshGuildSkills(s);
    }

    /// <summary>C++ <c>OnMW_ADDCOOLDOWN_REQ</c>: a guild's officer skill was cast — it waits its reuse delay for the whole guild;
    /// the members here who hold it see the wait at once.</summary>
    private void OnMW_ADDCOOLDOWN_REQ(PacketReader r)
    {
        uint guildId = r.ReadUInt32();
        ushort skillId = r.ReadUInt16();
        if (_templates.Skill(skillId) is not { } tpl) return;
        if (!_guildCooldown.TryGetValue(guildId, out var cds)) _guildCooldown[guildId] = cds = new();
        cds[skillId] = NowMs + tpl.ReuseDelay;
        foreach (var s in _state.AllInGame())
            if (s.IsMain && s.Char is { } ch && ch.GuildId == guildId && ch.Skills.FirstOrDefault(k => k.SkillId == skillId) is { } skill)
            {
                skill.SetCooldown(NowMs, tpl.ReuseDelay);
                SendCS_SKILLBUY_ACK(s, ch, SkillUseResult.Success, skillId, skill.Level, tpl.ReuseDelay);
            }
    }

    /// <summary>A guild skill may be cast only with the guild, the duty and its time (C++ OnCS_DEFEND_REQ / OnCS_FINISHSKILL_ACK
    /// checks, CheckGuildSkillTick).</summary>
    private bool CanCastGuildSkill(Character ch, ushort skillId)
        => !_templates.GuildSkillTypes.TryGetValue(skillId, out byte type)
           || (ch.GuildId != 0 && ch.GuildDuty >= type && ch.GuildSkills.TryGetValue(skillId, out var held) && held.EndTime > UnixNow());

    /// <summary>C++ <c>OnCS_SKILLUSE_REQ</c> (CSHandler.cpp:2974): an officer skill cast starts the guild's cooldown.</summary>
    private void GuildSkillCast(Character ch, ushort skillId, byte level)
    {
        if (_templates.GuildSkillTypes.TryGetValue(skillId, out byte type) && type != 0 && ch.GuildId != 0)
            SendUpdateGuildCooldown(ch.GuildId, skillId, level, renew: false, use: true);
    }
}

/// <summary>A guild skill the character holds (C++ <c>GUILDSKILL</c>): its type (the duty it needs), level and end.</summary>
public sealed record GuildSkillHeld(ushort SkillId, byte Type, byte Level, long EndTime);
