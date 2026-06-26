using TWorld.Data;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 2b — the guild long-tail (cabinet, contribution, articles, fame, wanted/volunteer boards) and
/// the tactics (sub-guild / mercenary) subsystem. Ported from SSHandler.cpp/SSSender.cpp. State mutates
/// in-memory and persists best-effort via the guild DB (writes degrade gracefully if a proc is absent).
/// Senders for the looped/list responses follow the captured C++ field order; where the exact item
/// serialization could not be fully verified it is noted and should be byte-checked against SSSender
/// before real-cluster integration.
/// </summary>
public sealed partial class WorldService
{
    private async Task Persist(string what, Func<GuildDatabase, Task> op)
    {
        if (_guildDb is null) return;
        try { await op(_guildDb); }
        catch (Exception ex) { _log.LogWarning(ex, "{What} persist failed.", what); }
    }

    /// <summary>Dispatch the Phase-2b guild messages. Returns true if handled.</summary>
    private async Task<bool> DispatchGuild2bAsync(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.MW_GUILDCABINETLIST_ACK: OnGuildCabinetList(r); return true;
            case Msg.MW_GUILDCABINETPUTIN_ACK: OnGuildCabinetList(r); return true;   // re-send list (item move is map-side)
            case Msg.MW_GUILDCABINETTAKEOUT_ACK: OnGuildCabinetList(r); return true;
            case Msg.MW_GUILDCONTRIBUTION_ACK: await OnGuildContribution(r); return true;
            case Msg.MW_GUILDARTICLELIST_ACK: OnGuildArticleList(r); return true;
            case Msg.MW_GUILDARTICLEADD_ACK: await OnGuildArticleAdd(r); return true;
            case Msg.MW_GUILDARTICLEDEL_ACK: await OnGuildArticleDel(r); return true;
            case Msg.MW_GUILDARTICLEUPDATE_ACK: await OnGuildArticleUpdate(r); return true;
            case Msg.MW_GUILDFAME_ACK: await OnGuildFame(r); return true;
            case Msg.MW_GUILDWANTEDADD_ACK: await OnGuildWantedAdd(r); return true;
            case Msg.MW_GUILDWANTEDDEL_ACK: await OnGuildWantedDel(r); return true;
            case Msg.MW_GUILDWANTEDLIST_ACK: OnGuildWantedList(r); return true;
            case Msg.MW_GUILDVOLUNTEERING_ACK: await OnGuildVolunteering(r); return true;
            case Msg.MW_GUILDVOLUNTEERINGDEL_ACK: await OnGuildVolunteeringDel(r); return true;
            case Msg.MW_GUILDVOLUNTEERLIST_ACK: OnGuildVolunteerList(r); return true;
            case Msg.MW_GUILDVOLUNTEERREPLY_ACK: await OnGuildVolunteerReply(r); return true;
            case Msg.MW_GUILDPOINTLOG_ACK: OnGuildPointLog(r); return true;
            case Msg.MW_GUILDPVPRECORD_ACK: OnGuildPvpRecord(r); return true;
            case Msg.MW_GUILDMONEYRECOVER_ACK: return true; // recovery ack: no-op in this build
            case Msg.MW_GUILDSKILLACTION_REQ: OnGuildSkillAction(session, packet); return true;
            case Msg.MW_UPDATEGUILDCOOLDOWN_ACK: return true; // cooldown sync: map-driven

            // --- tactics (sub-guild) ---
            case Msg.MW_GUILDTACTICSLIST_ACK: OnGuildTacticsList(r); return true;
            case Msg.MW_GUILDTACTICSINVITE_ACK: OnGuildTacticsInvite(r); return true;
            case Msg.MW_GUILDTACTICSANSWER_ACK: await OnGuildTacticsAnswer(r); return true;
            case Msg.MW_GUILDTACTICSKICKOUT_ACK: await OnGuildTacticsKickout(r); return true;
            case Msg.MW_GUILDTACTICSWANTEDADD_ACK: await OnGuildTacticsWantedAdd(r); return true;
            case Msg.MW_GUILDTACTICSWANTEDDEL_ACK: await OnGuildTacticsWantedDel(r); return true;
            case Msg.MW_GUILDTACTICSWANTEDLIST_ACK: OnGuildTacticsWantedList(r); return true;
            case Msg.MW_GUILDTACTICSVOLUNTEERING_ACK: await OnGuildTacticsVolunteering(r); return true;
            case Msg.MW_GUILDTACTICSVOLUNTEERINGDEL_ACK: await OnGuildTacticsVolunteeringDel(r); return true;
            case Msg.MW_GUILDTACTICSVOLUNTEERLIST_ACK: OnGuildTacticsVolunteerList(r); return true;
            case Msg.MW_GUILDTACTICSREPLY_ACK: await OnGuildTacticsReply(r); return true;

            default: return false;
        }
    }

    // ===== cabinet =====
    private void OnGuildCabinetList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var g = _state.GetCurGuild(charId);
        if (g is null) return;
        var w = new PacketWriter(Msg.MW_GUILDCABINETLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteByte(g.MaxCabinet);
        w.WriteByte((byte)g.Cabinet.Count);   // GetCabinetSize() is BYTE
        foreach (var it in g.Cabinet) WriteGuildItem(w, it);
        SendToCharId(charId, key, w.ToArray());
    }

    // Per cabinet entry: dwItemID then CTServer::WrapItem (TServer.cpp:16). bGem/wMoggItemID and the
    // IEV_ELD/WRAP/COLOR/GUILD ext-values are not loaded by Phase-2b's cabinet query, so they are 0 here
    // (verify vs WrapItem if cabinet content is ever populated; the empty-list path is exact).
    private static void WriteGuildItem(PacketWriter w, GuildItem it)
    {
        w.WriteUInt32(it.StorageId);    // m_dwItemID (slot)
        w.WriteInt64(it.ItemDbId);      // m_dlID
        w.WriteByte(0);                 // m_bItemID (not modeled)
        w.WriteUInt16(it.ItemId);       // m_wItemID
        w.WriteByte(it.Level);
        w.WriteByte(0);                 // m_bGem (not modeled)
        w.WriteUInt16(0);               // m_wMoggItemID (not modeled)
        w.WriteByte(it.Count);
        w.WriteByte(it.GLevel);
        w.WriteUInt32(it.DuraMax);
        w.WriteUInt32(it.DuraCur);
        w.WriteByte(it.RefineCur);
        w.WriteInt64(it.EndTime);
        w.WriteByte(it.GradeEffect);
        w.WriteUInt32(0);               // extValue[IEV_ELD]
        w.WriteUInt32(0);               // extValue[IEV_WRAP]
        w.WriteUInt32(0);               // extValue[IEV_COLOR]
        w.WriteUInt32(0);               // extValue[IEV_GUILD]
        byte magicCount = (byte)it.Magic.Count(m => m != 0);
        w.WriteByte(magicCount);
        for (int i = 0; i < 6; i++)
            if (it.Magic[i] != 0) { w.WriteByte(it.Magic[i]); w.WriteUInt16(it.Value[i]); }
    }

    // ===== contribution =====
    private async Task OnGuildContribution(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        uint exp = r.ReadUInt32(); uint gold = r.ReadUInt32(); uint silver = r.ReadUInt32(); uint cooper = r.ReadUInt32();
        _ = r.ReadUInt32(); // pvpoint
        var g = _state.FindGuildByChar(charId);
        if (g is null) return;
        g.Exp += exp; g.Gold += gold; g.Silver += silver; g.Cooper += cooper;
        await Persist("contribution", db => db.ContributionAsync(g.Id, charId, exp, gold, silver, cooper));
        var w = new PacketWriter(Msg.MW_GUILDCONTRIBUTION_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)GuildResult.Success);
        w.WriteUInt32(g.Exp); w.WriteUInt32(g.Gold); w.WriteUInt32(g.Silver); w.WriteUInt32(g.Cooper); w.WriteUInt32(g.PvPTotalPoint);
        BroadcastToGuild(g, () => Clone(w));
    }

    // ===== articles =====
    private void OnGuildArticleList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var g = _state.GetCurGuild(charId);
        if (g is null) return;
        var w = new PacketWriter(Msg.MW_GUILDARTICLELIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)g.Articles.Count); // GetArticleSize() is BYTE
        foreach (var a in g.Articles.Values)
        {
            w.WriteUInt32(a.Id); w.WriteByte(a.Duty); w.WriteString(a.Writer); w.WriteString(a.Title); w.WriteString(a.Article); w.WriteString(a.Date);
        }
        SendToCharId(charId, key, w.ToArray());
    }

    private async Task OnGuildArticleAdd(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        string title = r.ReadString(); string text = r.ReadString();
        var g = _state.FindGuildByChar(charId);
        if (g is null) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEADD_REQ, charId, key, GuildResult.NotFound)); return; }
        uint id = ++g.ArticleSeq;
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var mem = g.FindMember(charId);
        g.Articles[id] = new GuildArticle { Id = id, Duty = mem?.Duty ?? 0, Writer = mem?.Name ?? "", Title = title, Article = text, Date = now.ToString() };
        await Persist("articleAdd", db => db.ArticleAddAsync(g.Id, id, mem?.Duty ?? 0, mem?.Name ?? "", title, text, now));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEADD_REQ, charId, key, GuildResult.Success));
    }

    private async Task OnGuildArticleDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint id = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        if (g is null || !g.Articles.Remove(id)) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEDEL_REQ, charId, key, GuildResult.Fail)); return; }
        await Persist("articleDel", db => db.ArticleDelAsync(g.Id, id));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEDEL_REQ, charId, key, GuildResult.Success));
    }

    private async Task OnGuildArticleUpdate(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint id = r.ReadUInt32();
        string title = r.ReadString(); string text = r.ReadString();
        var g = _state.FindGuildByChar(charId);
        if (g is null || !g.Articles.TryGetValue(id, out var a)) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEUPDATE_REQ, charId, key, GuildResult.Fail)); return; }
        a.Title = title; a.Article = text;
        await Persist("articleUpdate", db => db.ArticleUpdateAsync(g.Id, id, title, text));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEUPDATE_REQ, charId, key, GuildResult.Success));
    }

    // ===== fame =====
    private async Task OnGuildFame(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        uint fame = r.ReadUInt32(); uint fameColor = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        if (g is null || !g.IsChief(charId)) return;
        g.Fame = fame; g.FameColor = fameColor;
        await Persist("fame", db => db.FameAsync(g.Id, fame, fameColor));
        var w = new PacketWriter(Msg.MW_GUILDFAME_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)GuildResult.Success);
        w.WriteUInt32(charId); w.WriteUInt32(fame); w.WriteUInt32(fameColor);
        BroadcastToGuild(g, () => Clone(w));
    }

    // ===== wanted =====
    private async Task OnGuildWantedAdd(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        _ = r.ReadUInt32(); // id (per guild, keyed by guildId)
        string title = r.ReadString(); string text = r.ReadString();
        byte minLvl = r.ReadByte(); byte maxLvl = r.ReadByte();
        var g = _state.FindGuildByChar(charId);
        if (g is null) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDWANTEDADD_REQ, charId, key, GuildResult.NotFound)); return; }
        long end = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeSeconds();
        _state.GuildWanted[g.Id] = new GuildWanted { GuildId = g.Id, Name = g.Name, Title = title, Text = text, MinLevel = minLvl, MaxLevel = maxLvl, Country = g.Country, EndTime = end };
        await Persist("wantedAdd", db => db.WantedAddAsync(g.Id, minLvl, maxLvl, end, title, text));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDWANTEDADD_REQ, charId, key, GuildResult.Success));
    }

    private async Task OnGuildWantedDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); _ = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        if (g is null) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDWANTEDDEL_REQ, charId, key, GuildResult.NotFound)); return; }
        _state.GuildWanted.Remove(g.Id);
        await Persist("wantedDel", db => db.WantedDelAsync(g.Id));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDWANTEDDEL_REQ, charId, key, GuildResult.Success));
    }

    private void OnGuildWantedList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var w = new PacketWriter(Msg.MW_GUILDWANTEDLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)_state.GuildWanted.Count);
        foreach (var ad in _state.GuildWanted.Values)
        {
            // SSSender order: guildId, name, title, text, minLevel(BYTE), maxLevel(BYTE), endTime(INT64), appliedFlag(BYTE)
            w.WriteUInt32(ad.GuildId); w.WriteString(ad.Name); w.WriteString(ad.Title); w.WriteString(ad.Text);
            w.WriteByte(ad.MinLevel); w.WriteByte(ad.MaxLevel); w.WriteInt64(ad.EndTime); w.WriteByte(ad.Apps.ContainsKey(charId) ? (byte)1 : (byte)0);
        }
        SendToCharId(charId, key, w.ToArray());
    }

    // ===== volunteer (apply to a guild wanted) =====
    private async Task OnGuildVolunteering(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint wantedId = r.ReadUInt32();
        var ch = _state.Characters.TryGetValue(charId, out var c) ? c : null;
        if (_state.GuildWanted.TryGetValue(wantedId, out var ad) && ch is not null)
            ad.Apps[charId] = new GuildWantedApp { CharId = charId, WantedId = wantedId, Class = ch.Class, Level = ch.Level, Name = ch.Name, Region = ch.Region };
        await Persist("volunteering", db => db.VolunteeringAsync((byte)GuildRelation.Alliance, charId, wantedId));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDVOLUNTEERING_REQ, charId, key, GuildResult.Success));
    }

    private async Task OnGuildVolunteeringDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        foreach (var ad in _state.GuildWanted.Values) ad.Apps.Remove(charId);
        await Persist("volunteeringDel", db => db.VolunteeringDelAsync((byte)GuildRelation.Alliance, charId));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDVOLUNTEERINGDEL_REQ, charId, key, GuildResult.Success));
    }

    private void OnGuildVolunteerList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        var ad = g is not null && _state.GuildWanted.TryGetValue(g.Id, out var a) ? a : null;
        var w = new PacketWriter(Msg.MW_GUILDVOLUNTEERLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)(ad?.Apps.Count ?? 0));
        if (ad is not null)
            foreach (var app in ad.Apps.Values)
            { w.WriteUInt32(app.CharId); w.WriteString(app.Name); w.WriteByte(app.Level); w.WriteByte(app.Class); w.WriteUInt32(app.Region); }
        SendToCharId(charId, key, w.ToArray());
    }

    private async Task OnGuildVolunteerReply(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        uint targetId = r.ReadUInt32(); byte reply = r.ReadByte();
        var g = _state.FindGuildByChar(charId);
        if (g is null) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDVOLUNTEERREPLY_REQ, charId, key, GuildResult.NotFound)); return; }
        if (reply == Ask.Yes && _state.GuildWanted.TryGetValue(g.Id, out var ad) && ad.Apps.TryGetValue(targetId, out var app)
            && _state.FindGuildByChar(targetId) is null && g.Members.Count < g.MaxMembers)
        {
            var tgt = _state.Characters.TryGetValue(targetId, out var tc) ? tc : null;
            g.Members[targetId] = new GuildMember { CharId = targetId, Name = app.Name, Level = app.Level, Class = app.Class, Duty = (byte)GuildDuty.None, OnlineChar = tgt };
            _state.CharGuild[targetId] = g.Id;
            if (tgt is not null) tgt.Guild = g;
            ad.Apps.Remove(targetId);
            await Persist("volunteerReply", db => db.MemberAddAsync(g.Id, targetId, app.Level, (byte)GuildDuty.None));
        }
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDVOLUNTEERREPLY_REQ, charId, key, GuildResult.Success));
    }

    // ===== point log / pvp record / skill (read / relay) =====
    private void OnGuildPointLog(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        var w = new PacketWriter(Msg.MW_GUILDPOINTLOG_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt16((ushort)(g?.PointRewards.Count ?? 0));  // count is WORD
        if (g is not null)
            foreach (var pr in g.PointRewards) { w.WriteInt64(pr.Date); w.WriteString(pr.Name); w.WriteUInt32(pr.Point); } // date, name, point
        SendToCharId(charId, key, w.ToArray());
    }

    private void OnGuildPvpRecord(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        var w = new PacketWriter(Msg.MW_GUILDPVPRECORD_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt16((ushort)(g?.Members.Count ?? 0)); // member count is WORD
        if (g is not null)
            foreach (var mem in g.Members.Values)
            {
                // per member: id, weekKill(WORD), weekDie(WORD), points[PVPE_KILL_H..PVPE_WIN) = indices 1..5 (5 DWORD),
                // then the most-recent record (kill, die, 5 points) or zeros. Week-aggregate isn't tracked → zeros.
                w.WriteUInt32(mem.CharId);
                w.WriteUInt16(0); w.WriteUInt16(0);
                for (int i = 1; i <= 5; i++) w.WriteUInt32(0);
                var rec = mem.Records.Count > 0 ? mem.Records[^1] : null;
                if (rec is not null)
                {
                    w.WriteUInt16(rec.KillCount); w.WriteUInt16(rec.DieCount);
                    for (int i = 1; i <= 5; i++) w.WriteUInt32(rec.Point[i]);
                }
                else
                {
                    w.WriteUInt16(0); w.WriteUInt16(0);
                    for (int i = 1; i <= 5; i++) w.WriteUInt32(0);
                }
            }
        SendToCharId(charId, key, w.ToArray());
    }

    private void OnGuildSkillAction(ServerSession session, byte[] packet)
    {
        // Relay the guild-skill action to the acting char's guild (map applies the effect).
        var r = new PacketReader(packet);
        uint charId = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        if (g is not null) BroadcastToGuild(g, () => packet);
    }

    // ===== tactics (sub-guild / mercenary) =====
    private void OnGuildTacticsList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var g = _state.GetCurGuild(charId);
        if (g is null) return;
        var w = new PacketWriter(Msg.MW_GUILDTACTICSLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)g.Tactics.Count);
        foreach (var t in g.Tactics.Values)
        {
            w.WriteUInt32(t.CharId); w.WriteString(t.Name); w.WriteByte(t.OnlineChar?.Level ?? t.Level); w.WriteByte(t.Class);
            w.WriteByte(t.Day); w.WriteUInt32(t.RewardPoint); w.WriteInt64(t.RewardMoney); w.WriteInt64(t.EndTime);
            w.WriteUInt32(t.GainPoint); w.WriteUInt32(t.OnlineChar?.Region ?? 0); w.WriteUInt16(t.Castle); w.WriteByte(t.Camp);
        }
        SendToCharId(charId, key, w.ToArray());
    }

    private void OnGuildTacticsInvite(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        string name = r.ReadString(); byte day = r.ReadByte();
        uint point = r.ReadUInt32(); uint gold = r.ReadUInt32(); uint silver = r.ReadUInt32(); uint cooper = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        if (g is null || !_state.CharactersByName.TryGetValue(name, out var tgt)) return;
        string inviter = _state.Characters.TryGetValue(charId, out var ic) ? ic.Name : "";
        // SSSender: charId, key, guildName, strNAME (inviter name), day, point, gold, silver, cooper
        var w = new PacketWriter(Msg.MW_GUILDTACTICSINVITE_REQ);
        w.WriteUInt32(tgt.CharId); w.WriteUInt32(tgt.Key); w.WriteString(g.Name); w.WriteString(inviter);
        w.WriteByte(day); w.WriteUInt32(point); w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
        SendToChar(tgt, w.ToArray());
    }

    private async Task OnGuildTacticsAnswer(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); byte answer = r.ReadByte();
        string name = r.ReadString(); byte day = r.ReadByte();
        uint point = r.ReadUInt32(); uint gold = r.ReadUInt32(); uint silver = r.ReadUInt32(); uint cooper = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        var inviter = _state.CharactersByName.TryGetValue(name, out var inv) ? inv : null;
        var g = inviter is not null ? _state.FindGuildByChar(inviter.CharId) : null;
        if (ch is null || g is null) { return; }
        if (answer == Ask.Yes && _state.FindTacticsGuild(charId) is null)
        {
            long end = DateTimeOffset.UtcNow.AddDays(day).ToUnixTimeSeconds();
            long money = ((long)gold << 40) | ((long)silver << 20) | cooper; // packed money (best-effort)
            g.Tactics[charId] = new TacticsMember { CharId = charId, Name = ch.Name, Level = ch.Level, Class = ch.Class, RewardPoint = point, RewardMoney = money, Day = day, EndTime = end, OnlineChar = ch };
            _state.CharTactics[charId] = g.Id;
            await Persist("tacticsAdd", async db => await db.TacticsAddAsync(g.Id, charId, point, money, day, end));
        }
        // SSSender: charId, key, bResult, guildId, guildName, memberId, memberName, gold, silver, cooper
        var w = new PacketWriter(Msg.MW_GUILDTACTICSANSWER_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)GuildResult.Success);
        w.WriteUInt32(g.Id); w.WriteString(g.Name); w.WriteUInt32(charId); w.WriteString(ch.Name);
        w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
        SendToChar(ch, w.ToArray());
    }

    private async Task OnGuildTacticsKickout(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint targetId = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        if (g is null || !g.Tactics.Remove(targetId)) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSKICKOUT_REQ, charId, key, GuildResult.Fail)); return; }
        _state.CharTactics.Remove(targetId);
        if (_state.Characters.TryGetValue(targetId, out var tc)) tc.Guild = null;
        await Persist("tacticsDel", async db => await db.TacticsDelAsync(targetId));
        var w = new PacketWriter(Msg.MW_GUILDTACTICSKICKOUT_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)GuildResult.Success); w.WriteUInt32(targetId); w.WriteByte(1);
        SendToCharId(charId, key, w.ToArray());
    }

    private async Task OnGuildTacticsWantedAdd(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); _ = r.ReadUInt32();
        string title = r.ReadString(); string text = r.ReadString();
        byte day = r.ReadByte(); byte minLvl = r.ReadByte(); byte maxLvl = r.ReadByte();
        uint point = r.ReadUInt32(); uint gold = r.ReadUInt32(); uint silver = r.ReadUInt32(); uint cooper = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        if (g is null) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSWANTEDADD_REQ, charId, key, GuildResult.NotFound)); return; }
        uint id = ++_state.TacticsWantedSeq;
        long end = DateTimeOffset.UtcNow.AddDays(day == 0 ? 7 : day).ToUnixTimeSeconds();
        _state.TacticsWanted[id] = new GuildTacticsWanted { Id = id, GuildId = g.Id, Name = g.Name, Title = title, Text = text, Day = day, MinLevel = minLvl, MaxLevel = maxLvl, Point = point, Gold = gold, Silver = silver, Cooper = cooper, Country = g.Country, EndTime = end };
        await Persist("tacticsWantedAdd", db => db.TacticsWantedAddAsync(id, g.Id, point, gold, silver, cooper, day, minLvl, maxLvl, end, title, text));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSWANTEDADD_REQ, charId, key, GuildResult.Success));
    }

    private async Task OnGuildTacticsWantedDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint id = r.ReadUInt32();
        _state.TacticsWanted.Remove(id);
        await Persist("tacticsWantedDel", db => db.TacticsWantedDelAsync(id));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSWANTEDDEL_REQ, charId, key, GuildResult.Success));
    }

    private void OnGuildTacticsWantedList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var w = new PacketWriter(Msg.MW_GUILDTACTICSWANTEDLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)_state.TacticsWanted.Count);
        foreach (var ad in _state.TacticsWanted.Values)
        {
            // SSSender order: id, guildId, name, title, text, day, minLevel, maxLevel, point, gold, silver, cooper, endTime(INT64), appliedFlag(BYTE)
            w.WriteUInt32(ad.Id); w.WriteUInt32(ad.GuildId); w.WriteString(ad.Name); w.WriteString(ad.Title); w.WriteString(ad.Text);
            w.WriteByte(ad.Day); w.WriteByte(ad.MinLevel); w.WriteByte(ad.MaxLevel);
            w.WriteUInt32(ad.Point); w.WriteUInt32(ad.Gold); w.WriteUInt32(ad.Silver); w.WriteUInt32(ad.Cooper);
            w.WriteInt64(ad.EndTime); w.WriteByte(ad.Apps.ContainsKey(charId) ? (byte)1 : (byte)0);
        }
        SendToCharId(charId, key, w.ToArray());
    }

    private async Task OnGuildTacticsVolunteering(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        uint guildId = r.ReadUInt32(); uint tacticsId = r.ReadUInt32();
        var ch = _state.Characters.TryGetValue(charId, out var c) ? c : null;
        if (_state.TacticsWanted.TryGetValue(tacticsId, out var ad) && ch is not null)
            ad.Apps[charId] = new GuildTacticsWantedApp { CharId = charId, WantedGuildId = guildId, WantedId = tacticsId, Class = ch.Class, Level = ch.Level, Name = ch.Name, Region = ch.Region };
        await Persist("tacticsVolunteering", db => db.VolunteeringAsync((byte)GuildRelation.Enemy, charId, tacticsId));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSVOLUNTEERING_REQ, charId, key, GuildResult.Success));
    }

    private async Task OnGuildTacticsVolunteeringDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        foreach (var ad in _state.TacticsWanted.Values) ad.Apps.Remove(charId);
        await Persist("tacticsVolunteeringDel", db => db.VolunteeringDelAsync((byte)GuildRelation.Enemy, charId));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSVOLUNTEERINGDEL_REQ, charId, key, GuildResult.Success));
    }

    private void OnGuildTacticsVolunteerList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        var ad = g is not null ? _state.TacticsWanted.Values.FirstOrDefault(a => a.GuildId == g.Id) : null;
        var w = new PacketWriter(Msg.MW_GUILDTACTICSVOLUNTEERLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)(ad?.Apps.Count ?? 0));
        if (ad is not null)
            foreach (var app in ad.Apps.Values)
            {
                // SSSender: charId, name, level, class, region, day(BYTE), point, gold, silver, cooper
                w.WriteUInt32(app.CharId); w.WriteString(app.Name); w.WriteByte(app.Level); w.WriteByte(app.Class); w.WriteUInt32(app.Region);
                w.WriteByte(app.Day); w.WriteUInt32(app.Point); w.WriteUInt32(app.Gold); w.WriteUInt32(app.Silver); w.WriteUInt32(app.Cooper);
            }
        SendToCharId(charId, key, w.ToArray());
    }

    private async Task OnGuildTacticsReply(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        uint targetId = r.ReadUInt32(); byte reply = r.ReadByte();
        var g = _state.FindGuildByChar(charId);
        if (g is null) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSREPLY_REQ, charId, key, GuildResult.NotFound)); return; }
        var ad = _state.TacticsWanted.Values.FirstOrDefault(a => a.GuildId == g.Id);
        string memberName = ""; uint gold = 0, silver = 0, cooper = 0;
        if (reply == Ask.Yes && ad is not null && ad.Apps.TryGetValue(targetId, out var app) && _state.FindTacticsGuild(targetId) is null)
        {
            long end = DateTimeOffset.UtcNow.AddDays(app.Day == 0 ? 7 : app.Day).ToUnixTimeSeconds();
            long money = ((long)app.Gold << 40) | ((long)app.Silver << 20) | app.Cooper;
            var tgt = _state.Characters.TryGetValue(targetId, out var tc) ? tc : null;
            g.Tactics[targetId] = new TacticsMember { CharId = targetId, Name = app.Name, Level = app.Level, Class = app.Class, RewardPoint = app.Point, RewardMoney = money, Day = app.Day, EndTime = end, OnlineChar = tgt };
            _state.CharTactics[targetId] = g.Id;
            memberName = app.Name; gold = app.Gold; silver = app.Silver; cooper = app.Cooper;
            ad.Apps.Remove(targetId);
            await Persist("tacticsReply", async db => await db.TacticsAddAsync(g.Id, targetId, app.Point, money, app.Day, end));
        }
        // SSSender: charId, key, bResult, guildId, guildName, memberId, memberName, gold, silver, cooper
        var w = new PacketWriter(Msg.MW_GUILDTACTICSREPLY_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)GuildResult.Success);
        w.WriteUInt32(g.Id); w.WriteString(g.Name); w.WriteUInt32(targetId); w.WriteString(memberName);
        w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
        SendToCharId(charId, key, w.ToArray());
    }

    // ----- small helpers -----
    private static byte[] BuildResult(ushort id, uint charId, uint key, GuildResult ret)
    { var w = new PacketWriter(id); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)ret); return w.ToArray(); }

    private static byte[] Clone(PacketWriter w) => w.ToArray();
}
