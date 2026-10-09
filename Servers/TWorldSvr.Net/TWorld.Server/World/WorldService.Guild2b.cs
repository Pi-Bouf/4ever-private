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
            case Msg.MW_GUILDCABINETPUTIN_ACK: OnGuildCabinetPutIn(r); return true;
            case Msg.MW_GUILDCABINETTAKEOUT_ACK: OnGuildCabinetTakeOut(r); return true;
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
            case Msg.MW_GUILDMONEYRECOVER_ACK: await OnGuildMoneyRecover(r); return true;
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
            case Msg.MW_GUILDTACTICSVOLUNTEERINGDEL_ACK: OnGuildTacticsVolunteeringDel(r); return true;
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
        SendCabinetList(charId, key, g);
    }

    /// <summary>C++ <c>SendMW_GUILDCABINETLIST_REQ</c>: the cabinet by slot (C++ <c>m_mapTCabinet</c> order).</summary>
    private void SendCabinetList(uint charId, uint key, Guild g)
    {
        var w = new PacketWriter(Msg.MW_GUILDCABINETLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteByte(g.MaxCabinet);
        w.WriteByte((byte)g.Cabinet.Count);   // GetCabinetSize() is BYTE
        foreach (var it in g.Cabinet.OrderBy(i => i.StorageId)) WriteGuildItem(w, it);
        SendToCharId(charId, key, w.ToArray());
    }

    /// <summary>C++ <c>OnMW_GUILDCABINETPUTIN_ACK</c>: the map stored an item — into the cabinet's slot (a stack grows), and
    /// the list again.</summary>
    private void OnGuildCabinetPutIn(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint slot = r.ReadUInt32();
        var it = ReadGuildItem(r, slot);
        var ch = _state.FindChar(charId, key);
        if (ch?.Guild is not { } g) return;
        if (g.Cabinet.FirstOrDefault(i => i.StorageId == slot) is { } have) have.Count = (byte)(have.Count + it.Count);
        else g.Cabinet.Add(it);
        SendCabinetList(charId, key, g);
    }

    /// <summary>C++ <c>OnMW_GUILDCABINETTAKEOUT_ACK</c>: the map took items out of a slot — lessened or gone, and the list again.</summary>
    private void OnGuildCabinetTakeOut(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint slot = r.ReadUInt32(); byte count = r.ReadByte();
        var ch = _state.FindChar(charId, key);
        if (ch?.Guild is not { } g) return;
        if (g.Cabinet.FirstOrDefault(i => i.StorageId == slot) is { } have)
        {
            have.Count = (byte)(have.Count - count);
            if (have.Count == 0) g.Cabinet.Remove(have);
        }
        SendCabinetList(charId, key, g);
    }

    /// <summary>C++ <c>CTWorldSvrModule::CreateItem</c> (TWorldSvr.cpp:5433), the layout of <c>CTServer::WrapItem</c>.</summary>
    private static GuildItem ReadGuildItem(PacketReader r, uint slot)
    {
        var it = new GuildItem { ItemDbId = r.ReadInt64(), StorageId = slot };
        it.ItemSlot = r.ReadByte(); it.ItemId = r.ReadUInt16(); it.Level = r.ReadByte(); it.Gem = r.ReadByte(); it.MoggItemId = r.ReadUInt16();
        it.Count = r.ReadByte(); it.GLevel = r.ReadByte(); it.DuraMax = r.ReadUInt32(); it.DuraCur = r.ReadUInt32(); it.RefineCur = r.ReadByte();
        it.EndTime = r.ReadInt64(); it.GradeEffect = r.ReadByte();
        for (int e = 0; e < 4; e++) it.ExtValue[e] = r.ReadUInt32();
        byte n = r.ReadByte();
        for (int i = 0; i < n; i++)
        {
            byte m = r.ReadByte(); ushort v = r.ReadUInt16();
            if (i < 6) { it.Magic[i] = m; it.Value[i] = v; }
        }
        return it;
    }

    // Per cabinet entry: dwItemID then CTServer::WrapItem (TServer.cpp:16). bGem/wMoggItemID and the
    // IEV_ELD/WRAP/COLOR/GUILD ext-values are not loaded by Phase-2b's cabinet query, so they are 0 here
    // (verify vs WrapItem if cabinet content is ever populated; the empty-list path is exact).
    private static void WriteGuildItem(PacketWriter w, GuildItem it)
    {
        w.WriteUInt32(it.StorageId);    // m_dwItemID (slot)
        w.WriteInt64(it.ItemDbId);      // m_dlID
        w.WriteByte(it.ItemSlot);       // m_bItemID
        w.WriteUInt16(it.ItemId);       // m_wItemID
        w.WriteByte(it.Level);
        w.WriteByte(it.Gem);            // m_bGem
        w.WriteUInt16(it.MoggItemId);   // m_wMoggItemID
        w.WriteByte(it.Count);
        w.WriteByte(it.GLevel);
        w.WriteUInt32(it.DuraMax);
        w.WriteUInt32(it.DuraCur);
        w.WriteByte(it.RefineCur);
        w.WriteInt64(it.EndTime);
        w.WriteByte(it.GradeEffect);
        for (int e = 0; e < 4; e++) w.WriteUInt32(it.ExtValue[e]);   // extValue[IEV_ELD / WRAP / COLOR / GUILD]
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
        uint pv = r.ReadUInt32();
        // C++ OnMW_GUILDCONTRIBUTION_ACK: the one who gave is told, with what it gave (the map takes that much) — never the
        // guild's totals, and never the whole guild.
        byte[] Answer(byte result, bool echo) => BuildContributionReq(charId, key, result, echo ? exp : 0, echo ? gold : 0, echo ? silver : 0, echo ? cooper : 0, echo ? pv : 0);
        var ch = _state.FindChar(charId, key);
        if (ch?.Guild is not { } g || g.Disorg != 0) return;
        if (g.Level == MaxGuildLevel && exp != 0) { SendToCharId(charId, key, Answer(2 /* GUILD_CONTRIBUTION_MAXGUILDLEVEL */, false)); return; }
        long money = cooper + (long)silver * Proto.MoneyMultiply + (long)gold * Proto.MoneyMultiply * Proto.MoneyMultiply;
        bool ok = g.FindMember(charId) is not null
                  && (money == 0 || (money <= (long)Proto.MoneyMultiply * Proto.MoneyMultiply && money >= Proto.MoneyMultiply));
        if (ok)                                                            // CTGuild::Contribution
        {
            if (money != 0) g.GainMoney(gold, silver, cooper);
            g.Exp += exp;
            if (pv != 0) g.GainPvPoint(pv, Proto.PvpUseable);
            await Persist("contribution", db => db.ContributionAsync(g.Id, charId, g.Exp, g.Gold, g.Silver, g.Cooper));   // totals, as the C++
            if (pv != 0) await SaveGuildPvPoint(g);
        }
        SendToCharId(charId, key, Answer(ok ? (byte)GuildResult.Success : (byte)GuildResult.NotFound, true));
    }

    private const byte MaxGuildLevel = 10;                                     // MAX_GUILD_LEVEL

    private static byte[] BuildContributionReq(uint charId, uint key, byte result, uint exp, uint gold, uint silver, uint cooper, uint pv)
    {
        var w = new PacketWriter(Msg.MW_GUILDCONTRIBUTION_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(result);
        w.WriteUInt32(exp); w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper); w.WriteUInt32(pv);
        return w.ToArray();
    }

    // ===== articles =====
    private void OnGuildArticleList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var g = _state.GetCurGuild(charId);
        if (g is null) return;
        SendArticleList(charId, key, g);
    }

    /// <summary>C++ <c>SendMW_GUILDARTICLELIST_REQ</c> — by article id (C++ <c>m_mapTArticle</c> order).</summary>
    private void SendArticleList(uint charId, uint key, Guild g)
    {
        var w = new PacketWriter(Msg.MW_GUILDARTICLELIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)g.Articles.Count); // GetArticleSize() is BYTE
        foreach (var a in g.Articles.Values.OrderBy(a => a.Id))
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
        g.Articles[id] = new GuildArticle { Id = id, Duty = mem?.Duty ?? 0, Writer = mem?.Name ?? "", Title = title, Article = text,
            Date = DateTimeOffset.FromUnixTimeSeconds(now).ToLocalTime().ToString("yyyy-MM-dd") };   // C++ CTime::Format("%Y-%m-%d")
        await Persist("articleAdd", db => db.ArticleAddAsync(g.Id, id, mem?.Duty ?? 0, mem?.Name ?? "", title, text, now));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEADD_REQ, charId, key, GuildResult.Success));
        SendArticleList(charId, key, g);                                   // C++: the list follows
    }

    private async Task OnGuildArticleDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint id = r.ReadUInt32();
        var g = _state.FindGuildByChar(charId);
        if (g is null || !g.Articles.Remove(id)) { SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEDEL_REQ, charId, key, GuildResult.Fail)); return; }
        await Persist("articleDel", db => db.ArticleDelAsync(g.Id, id));
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDARTICLEDEL_REQ, charId, key, GuildResult.Success));
        SendArticleList(charId, key, g);
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
        SendArticleList(charId, key, g);
    }

    // ===== fame =====
    private async Task OnGuildFame(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        uint fame = r.ReadUInt32(); uint fameColor = r.ReadUInt32();
        var g = _state.FindChar(charId, key)?.Guild;
        if (g is null || g.Disorg != 0) return;
        byte[] Fame(uint to, uint toKey, byte result)
        {
            var w = new PacketWriter(Msg.MW_GUILDFAME_REQ);
            w.WriteUInt32(to); w.WriteUInt32(toKey); w.WriteByte(result);
            w.WriteUInt32(charId); w.WriteUInt32(fame); w.WriteUInt32(fameColor);
            return w.ToArray();
        }
        const uint FameCost = 30000;                                       // C++ dwUsePoint
        if (g.PvPUseablePoint < FameCost) { SendToCharId(charId, key, Fame(charId, key, (byte)GuildResult.NoPoint)); return; }
        if (g.Fame == fame && g.FameColor == fameColor) return;
        g.UsePvPoint(FameCost, Proto.PvpUseable);
        await SaveGuildPvPoint(g);
        g.Fame = fame; g.FameColor = fameColor;
        foreach (var m in g.Members.Values)
            if (m.OnlineChar is { } mc) SendToChar(mc, Fame(mc.CharId, mc.Key, (byte)GuildResult.Success));
        await Persist("fame", db => db.FameAsync(g.Id, fame, fameColor));
    }

    // ===== wanted =====
    private async Task OnGuildWantedAdd(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        _ = r.ReadUInt32(); // id (per guild, keyed by guildId)
        string title = r.ReadString(); string text = r.ReadString();
        byte minLvl = r.ReadByte(); byte maxLvl = r.ReadByte();
        if (title.Length == 0) return;
        var g = _state.FindChar(charId, key)?.Guild;
        if (g is null || g.Disorg != 0) return;
        long end = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 14 * 86400;   // GUILDWANTED_PERIOD
        var ad = _state.GuildWanted.TryGetValue(g.Id, out var old) ? old : new GuildWanted { GuildId = g.Id };   // C++ keeps the applicants
        (ad.Name, ad.Title, ad.Text, ad.MinLevel, ad.MaxLevel, ad.Country, ad.EndTime) = (g.Name, title, text, minLvl, maxLvl, g.Country, end);
        _state.GuildWanted[g.Id] = ad;
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDWANTEDADD_REQ, charId, key, GuildResult.Success));
        SendWantedList(charId, key);                                       // C++ NotifyGuildWantedList
        await Persist("wantedAdd", db => db.WantedAddAsync(g.Id, minLvl, maxLvl, end, title, text));
    }

    private async Task OnGuildWantedDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); _ = r.ReadUInt32();
        var g = _state.FindChar(charId, key)?.Guild;
        if (g is null) return;
        bool had = _state.GuildWanted.Remove(g.Id);                        // C++ DelGuildWanted (its applicants go with it)
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDWANTEDDEL_REQ, charId, key, had ? GuildResult.Success : GuildResult.Fail));
        if (!had) return;
        SendWantedList(charId, key);
        await Persist("wantedDel", db => db.WantedDelAsync(g.Id));
    }

    private void OnGuildWantedList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        SendWantedList(charId, key);
    }

    /// <summary>C++ <c>NotifyGuildWantedList</c> → <c>SendMW_GUILDWANTEDLIST_REQ</c>: the posts of one's own country (by guild id,
    /// C++ map order), each marked when it is the one applied to.</summary>
    private void SendWantedList(uint charId, uint key)
    {
        if (_state.FindChar(charId, key) is not { } ch) return;
        var ads = _state.GuildWanted.Values.Where(a => a.Country == ch.Country).OrderBy(a => a.GuildId).ToList();
        var w = new PacketWriter(Msg.MW_GUILDWANTEDLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)ads.Count);
        foreach (var ad in ads)
        {
            // SSSender order: guildId, name, title, text, minLevel(BYTE), maxLevel(BYTE), endTime(INT64), appliedFlag(BYTE)
            w.WriteUInt32(ad.GuildId); w.WriteString(ad.Name); w.WriteString(ad.Title); w.WriteString(ad.Text);
            w.WriteByte(ad.MinLevel); w.WriteByte(ad.MaxLevel); w.WriteInt64(ad.EndTime); w.WriteByte(ad.Apps.ContainsKey(charId) ? (byte)1 : (byte)0);
        }
        SendToCharId(charId, key, w.ToArray());
    }

    private GuildWantedApp? FindWantedApp(uint charId)
        => _state.GuildWanted.Values.Select(a => a.Apps.GetValueOrDefault(charId)).FirstOrDefault(a => a is not null);

    // ===== volunteer (apply to a guild wanted) =====
    private async Task OnGuildVolunteering(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint wantedId = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null || ch.Guild is not null) return;
        GuildResult result;                                                // C++ AddGuildWantedApp
        if (FindWantedApp(charId) is { } mine) result = mine.WantedId == wantedId ? GuildResult.Same : GuildResult.AlreadyApply;
        else if (!_state.GuildWanted.TryGetValue(wantedId, out var ad) || ad.Country != ch.Country) result = GuildResult.Fail;
        else if (ad.EndTime < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) result = GuildResult.WantedEnd;
        else if (ad.MaxLevel < ch.Level || ad.MinLevel > ch.Level) result = GuildResult.MismatchLevel;
        else
        {
            ad.Apps[charId] = new GuildWantedApp { CharId = charId, WantedId = wantedId, Class = ch.Class, Level = ch.Level, Name = ch.Name };
            result = GuildResult.Success;
        }
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDVOLUNTEERING_REQ, charId, key, result));
        if (result != GuildResult.Success) return;
        SendWantedList(charId, key);
        await Persist("volunteering", db => db.VolunteeringAsync((byte)GuildRelation.Alliance, charId, wantedId));
    }

    private async Task OnGuildVolunteeringDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        if (_state.FindChar(charId, key) is null) return;
        bool had = false;
        foreach (var ad in _state.GuildWanted.Values) had |= ad.Apps.Remove(charId);
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDVOLUNTEERINGDEL_REQ, charId, key, had ? GuildResult.Success : GuildResult.Fail));
        if (!had) return;
        SendWantedList(charId, key);
        await Persist("volunteeringDel", db => db.VolunteeringDelAsync((byte)GuildRelation.Alliance, charId));
    }

    private void OnGuildVolunteerList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        SendVolunteerList(charId, key);
    }

    /// <summary>C++ <c>NotifyGuildVolunteerList</c>: one's guild's applicants, each with where it is now (0 when offline).</summary>
    private void SendVolunteerList(uint charId, uint key)
    {
        var g = _state.FindChar(charId, key)?.Guild;
        if (g is null) return;
        var ad = _state.GuildWanted.TryGetValue(g.Id, out var a) ? a : null;
        if (ad is not null)
            foreach (var app in ad.Apps.Values) app.Region = _state.CharactersByName.TryGetValue(app.Name, out var on) ? on.Region : 0;
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
        var g = _state.FindChar(charId, key)?.Guild;
        if (g is null) return;
        if (reply == 0)
        {
            foreach (var a in _state.GuildWanted.Values) a.Apps.Remove(targetId);   // C++ DelGuildWantedApp
            await Persist("volunteeringDel", db => db.VolunteeringDelAsync((byte)GuildRelation.Alliance, targetId));
        }
        else
        {
            var result = await ApplyGuildApp(targetId);
            if (result != GuildResult.Success) SendToCharId(charId, key, BuildResult(Msg.MW_GUILDVOLUNTEERREPLY_REQ, charId, key, result));
        }
        SendVolunteerList(charId, key);
    }

    /// <summary>C++ <c>ApplyGuildApp</c> (TWorldSvr.cpp:4892): the applicant joins the guild it applied to — the chief and the
    /// newcomer are told, as on an invite.</summary>
    private async Task<GuildResult> ApplyGuildApp(uint targetId)
    {
        if (FindWantedApp(targetId) is not { } app || _state.FindGuild(app.WantedId) is not { } g) return GuildResult.Fail;
        if (_state.FindGuildByChar(targetId) is not null) return GuildResult.HaveGuild;
        if (g.Members.Count >= g.MaxMembers) return GuildResult.MemberFull;
        if (g.FindMember(targetId) is not null) return GuildResult.AlreadyMember;
        if (g.FindMember(g.Chief) is null) return GuildResult.Fail;
        var tgt = _state.Characters.TryGetValue(targetId, out var tc) ? tc : null;
        g.Members[targetId] = new GuildMember { CharId = targetId, Name = app.Name, Level = app.Level, Class = app.Class, Duty = (byte)GuildDuty.None, OnlineChar = tgt };
        _state.CharGuild[targetId] = g.Id;
        if (tgt is not null) tgt.Guild = g;
        foreach (var a in _state.GuildWanted.Values) a.Apps.Remove(targetId);
        await Persist("volunteerReply", db => db.MemberAddAsync(g.Id, targetId, app.Level, (byte)GuildDuty.None));
        // C++ NotifyAddGuildMember: the newcomer (if online) and the chief.
        if (tgt is not null) SendToChar(tgt, BuildGuildJoinReq(tgt.CharId, tgt.Key, (byte)GuildResult.JoinSuccess, g.Id, g.Fame, g.FameColor, g.Name, targetId, app.Name, (byte)g.MaxMembers));
        if (g.FindMember(g.Chief)?.OnlineChar is { } chief)
            SendToChar(chief, BuildGuildJoinReq(chief.CharId, chief.Key, (byte)GuildResult.JoinSuccess, g.Id, g.Fame, g.FameColor, g.Name, targetId, app.Name, (byte)g.MaxMembers));
        return GuildResult.Success;
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

    // ===== tactics (mercenaries) =====
    // C++ m_mapTGuildTacticsWanted (guild → its ads, at most MAX_TACTICSWANTED) and m_mapTGuildTacticsWantedApp (one application
    // per character): here the ads are by id and each holds its applicants.
    private const int MaxTacticsWanted = 5;                                        // MAX_TACTICSWANTED
    private const long GuildWantedPeriod = 14 * 86400;                              // GUILDWANTED_PERIOD
    private const byte WptTacticsKick = 1, WptTacticsEnd = 2;                       // WORLDPOST_TYPE

    private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static long CalcMoney(uint gold, uint silver, uint cooper)
        => cooper + (long)silver * Proto.MoneyMultiply + (long)gold * Proto.MoneyMultiply * Proto.MoneyMultiply;

    /// <summary>The guild treasury saved as it now is (C++ <c>SendDM_GUILDCONTRIBUTION_REQ(id, 0, exp, gold, silver, cooper)</c> — the
    /// proc stores totals).</summary>
    private Task SaveGuildMoney(Guild g) => Persist("guildMoney", db => db.ContributionAsync(g.Id, 0, g.Exp, g.Gold, g.Silver, g.Cooper));
    private Task SaveGuildPvPoint(Guild g) => Persist("guildPvPoint", db => db.SaveGuildPvPointAsync(g.Id, g.PvPTotalPoint, g.PvPUseablePoint, g.PvPMonthPoint));

    private GuildTacticsWantedApp? FindTacticsApp(uint charId)
        => _state.TacticsWanted.Values.Select(a => a.Apps.GetValueOrDefault(charId)).FirstOrDefault(a => a is not null);

    /// <summary>C++ <c>DelGuildTacticsWantedApp</c>: the character's application goes (and from the database).</summary>
    private bool DelTacticsApp(uint charId)
    {
        bool had = false;
        foreach (var ad in _state.TacticsWanted.Values) had |= ad.Apps.Remove(charId);
        if (had) _ = Persist("tacticsVolunteeringDel", db => db.VolunteeringDelAsync((byte)GuildRelation.Enemy, charId));
        return had;
    }

    private void OnGuildTacticsList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        if (_state.FindChar(charId, key) is null || _state.GetCurGuild(charId) is not { } g) return;
        SendTacticsList(charId, key, g);
    }

    /// <summary>C++ <c>SendMW_GUILDTACTICSLIST_REQ</c> — by character id (C++ <c>m_mapTTactics</c> order).</summary>
    private void SendTacticsList(uint charId, uint key, Guild g)
    {
        var w = new PacketWriter(Msg.MW_GUILDTACTICSLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)g.Tactics.Count);
        foreach (var t in g.Tactics.Values.OrderBy(t => t.CharId))
        {
            w.WriteUInt32(t.CharId); w.WriteString(t.Name); w.WriteByte(t.OnlineChar?.Level ?? t.Level); w.WriteByte(t.Class);
            w.WriteByte(t.Day); w.WriteUInt32(t.RewardPoint); w.WriteInt64(t.RewardMoney); w.WriteInt64(t.EndTime);
            w.WriteUInt32(t.GainPoint); w.WriteUInt32(t.OnlineChar?.Region ?? 0); w.WriteUInt16(t.Castle); w.WriteByte(t.Camp);
        }
        SendToCharId(charId, key, w.ToArray());
    }

    /// <summary>C++ <c>OnMW_GUILDTACTICSINVITE_ACK</c>: a (vice-)chief offers a contract by name. The guild must be able to pay
    /// (points and money are only checked here — taken when the target says yes); a refusal goes back as an answer.</summary>
    private void OnGuildTacticsInvite(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        string name = r.ReadString(); byte day = r.ReadByte();
        uint point = r.ReadUInt32(); uint gold = r.ReadUInt32(); uint silver = r.ReadUInt32(); uint cooper = r.ReadUInt32();
        if (_state.FindChar(charId, key) is not { Guild: { } g } ch) return;
        var tgt = _state.CharactersByName.GetValueOrDefault(name);
        var tgtTactics = tgt is null ? null : _state.FindTacticsGuild(tgt.CharId);
        var result = GuildResult.Success;
        if (tgt is null) result = GuildResult.NotFound;
        else if (GetWarCountry(tgt) != GetWarCountry(ch)) result = GuildResult.Fail;
        else if (g.PvPUseablePoint < point) result = GuildResult.NoPoint;
        else if (!g.UseMoney(CalcMoney(gold, silver, cooper), use: false)) result = GuildResult.NoMoney;
        else if (tgtTactics is not null && tgtTactics.Id != g.Id) result = GuildResult.HaveGuild;
        else if (tgtTactics is null && !CanAddTactics(g)) result = GuildResult.MemberFull;
        else if (tgt.Guild?.Id == g.Id) result = GuildResult.SameGuildTactics;

        if (result == GuildResult.Success)
        {
            var w = new PacketWriter(Msg.MW_GUILDTACTICSINVITE_REQ);
            w.WriteUInt32(tgt!.CharId); w.WriteUInt32(tgt.Key); w.WriteString(g.Name); w.WriteString(ch.Name);
            w.WriteByte(day); w.WriteUInt32(point); w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
            SendToChar(tgt, w.ToArray());
        }
        else SendToCharId(charId, key, BuildTacticsAnswer(charId, key, result, 0, "", 0, name, gold, silver, cooper));
    }

    private static bool CanAddTactics(Guild g) => g.Tactics.Count < (g.LevelChart?.TacticsCnt ?? 0);   // CTGuild::CanAddTactics

    /// <summary>C++ <c>OnMW_GUILDTACTICSANSWER_ACK</c>: the invited one answers. On yes (and the guild still able), the guild pays
    /// the money and the points (a contract renewed keeps its end), the mercenary joins; both are told.</summary>
    private async Task OnGuildTacticsAnswer(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); byte answer = r.ReadByte();
        string name = r.ReadString(); byte day = r.ReadByte();
        uint point = r.ReadUInt32(); uint gold = r.ReadUInt32(); uint silver = r.ReadUInt32(); uint cooper = r.ReadUInt32();
        if (_state.FindChar(charId, key) is not { } ch) return;
        if (_state.CharactersByName.GetValueOrDefault(name) is not { Guild: { } g } origin) return;
        var mine = _state.FindTacticsGuild(charId);
        if (mine is not null && mine.Id != g.Id) return;

        long start = UnixNow();
        var result = GuildResult.Success;
        if (answer == Ask.Yes)
        {
            if (g.PvPUseablePoint < point) result = GuildResult.NoPoint;
            else if (GetWarCountry(origin) != GetWarCountry(ch)) result = GuildResult.Fail;
            else if (!g.UseMoney(CalcMoney(gold, silver, cooper), use: false)) result = GuildResult.NoMoney;
            else if (mine is null && !CanAddTactics(g)) result = GuildResult.MemberFull;
            else if (ch.Guild?.Id == g.Id) result = GuildResult.SameGuildTactics;

            if (result == GuildResult.Success)
            {
                if (g.FindTactics(charId) is { } old) { start = old.EndTime; await GuildTacticsDel(g, old, 2); }
                g.UseMoney(CalcMoney(gold, silver, cooper), use: true);
                g.UsePvPoint(point, Proto.PvpUseable);
                await SaveGuildMoney(g); await SaveGuildPvPoint(g);
                await GuildTacticsAdd(g, new TacticsMember
                {
                    CharId = charId, Name = ch.Name, Level = ch.Level, Class = ch.Class, RewardPoint = point,
                    RewardMoney = CalcMoney(gold, silver, cooper), Day = day, EndTime = start + day * 86400L, OnlineChar = ch,
                });
            }
        }
        else result = GuildResult.JoinDeny;

        SendToChar(ch, BuildTacticsAnswer(charId, key, result, g.Id, g.Name, charId, ch.Name, gold, silver, cooper));
        SendToChar(origin, BuildTacticsAnswer(origin.CharId, origin.Key, result, g.Id, g.Name, charId, ch.Name, gold, silver, cooper));
    }

    private static byte[] BuildTacticsAnswer(uint charId, uint key, GuildResult result, uint guildId, string guildName, uint memberId,
        string memberName, uint gold, uint silver, uint cooper)
    {
        var w = new PacketWriter(Msg.MW_GUILDTACTICSANSWER_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)result);
        w.WriteUInt32(guildId); w.WriteString(guildName); w.WriteUInt32(memberId); w.WriteString(memberName);
        w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
        return w.ToArray();
    }

    /// <summary>C++ <c>GuildTacticsAdd</c>: the contract saved and timed, the application gone, the mercenary's own guild's castle
    /// sign-up dropped.</summary>
    private async Task GuildTacticsAdd(Guild g, TacticsMember tm)
    {
        DelTacticsApp(tm.CharId);
        _state.CharTactics[tm.CharId] = g.Id;
        g.Tactics[tm.CharId] = tm;
        AddExpired(ExpiredType.GuildTactics, tm.EndTime, g.Id, tm.CharId);
        if (_state.FindGuildByChar(tm.CharId) is { } own && own.FindMember(tm.CharId) is { } om)
        {
            om.Tactics = g.Id;
            if (om.Castle != 0)
            {
                ushort castle = om.Castle;
                om.Castle = 0; om.Camp = 0;
                if (_gameDb is not null) _ = _gameDb.SaveCastleApplicantAsync(0, tm.CharId, 0);
                NotifyCastleApply(castle, own);
            }
        }
        await Persist("tacticsAdd", db => db.TacticsAddAsync(g.Id, tm.CharId, tm.RewardPoint, tm.RewardMoney, tm.Day, tm.EndTime));
    }

    /// <summary>C++ <c>GuildTacticsDel</c> + <c>CTGuild::DelTactics</c>. <paramref name="kick"/>: 0 the mercenary left (the guild gets
    /// its points and money back), 1 fired, 2 replaced by a new contract, 3 the contract ran out — then the mercenary is paid its
    /// points (when online) and mailed its money, and it goes in the guild's point log.</summary>
    private async Task GuildTacticsDel(Guild g, TacticsMember tm, byte kick)
    {
        if (kick != 0)
        {
            g.PointLog(tm.RewardPoint, tm.Name, UnixNow());
            await Persist("tacticsPointLog", db => db.SaveGuildPointRewardAsync(g.Id, tm.RewardPoint, tm.Name, g.PvPTotalPoint, g.PvPUseablePoint));
        }
        if (_state.Characters.TryGetValue(tm.CharId, out var tc) && _state.FindMapSvr(tc.MainId) is { } map)
        {
            var w = new PacketWriter(Msg.MW_GUILDTACTICSKICKOUT_REQ);
            w.WriteUInt32(tc.CharId); w.WriteUInt32(tc.Key); w.WriteByte((byte)GuildResult.Success); w.WriteUInt32(tm.CharId); w.WriteByte(kick);
            map.Send(w.ToArray());
            if (kick != 0 && tc.Save)
            {
                var p = new PacketWriter(Msg.MW_GAINPVPPOINT_REQ);
                p.WriteUInt32(tm.CharId); p.WriteUInt32(tm.RewardPoint); p.WriteByte(Proto.PvpeGuild); p.WriteByte(Proto.PvpUseable);
                p.WriteByte(1); p.WriteString(""); p.WriteByte(0); p.WriteByte(0);
                map.Send(p.ToArray());
            }
        }
        byte post = kick == 1 ? WptTacticsKick : kick >= 2 ? WptTacticsEnd : (byte)0;
        if (post != 0 && g.FindMember(g.Chief) is { } chief && _state.Servers.Values.FirstOrDefault() is { } any)
        {
            // C++ SendPost(type, value, sender id/name, receiver id/name, money) → MW_WORLDPOSTSEND_REQ on the first map.
            var w = new PacketWriter(Msg.MW_WORLDPOSTSEND_REQ);
            w.WriteByte(post); w.WriteUInt32(tm.RewardPoint); w.WriteUInt32(chief.CharId); w.WriteString(chief.Name);
            w.WriteUInt32(tm.CharId); w.WriteString(tm.Name); w.WriteInt64(tm.RewardMoney);
            any.Send(w.ToArray());
        }
        if (_state.FindGuildByChar(tm.CharId)?.FindMember(tm.CharId) is { } om) om.Tactics = 0;
        _state.CharTactics.Remove(tm.CharId);
        if (kick < 3) DelExpired(ExpiredType.GuildTactics, tm.EndTime, g.Id, tm.CharId);
        g.Tactics.Remove(tm.CharId);
        if (kick == 0)
        {
            long money = tm.RewardMoney;
            g.GainPvPoint(tm.RewardPoint, Proto.PvpUseable);
            g.GainMoney((uint)(money / Proto.MoneyMultiply / Proto.MoneyMultiply), (uint)(money / Proto.MoneyMultiply % Proto.MoneyMultiply), (uint)(money % Proto.MoneyMultiply));
            await SaveGuildPvPoint(g); await SaveGuildMoney(g);
        }
        await Persist("tacticsDel", async db => await db.TacticsDelAsync(tm.CharId));
    }

    /// <summary>C++ <c>OnMW_GUILDTACTICSKICKOUT_ACK</c>: a (vice-)chief fires a mercenary (it is told and paid), or a mercenary
    /// leaves its contract (the guild gets the pay back). The firing one gets the result and the list.</summary>
    private async Task OnGuildTacticsKickout(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint targetId = r.ReadUInt32();
        if (_state.FindChar(charId, key) is not { } ch) return;
        var g = charId != targetId ? ch.Guild : _state.FindTacticsGuild(targetId);
        if (g?.FindTactics(targetId) is not { } tm) return;
        byte kick = charId == targetId ? (byte)0 : (byte)1;
        await GuildTacticsDel(g, tm, kick);
        if (kick == 0) return;
        var w = new PacketWriter(Msg.MW_GUILDTACTICSKICKOUT_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)GuildResult.Success); w.WriteUInt32(targetId); w.WriteByte(kick);
        SendToCharId(charId, key, w.ToArray());
        SendTacticsList(charId, key, g);
    }

    /// <summary>C++ <c>OnMW_GUILDTACTICSWANTEDADD_ACK</c> → <c>AddGuildTacticsWanted</c>: a new ad (id 0) or an edit of one of the
    /// guild's; at most five a guild. The list follows.</summary>
    private async Task OnGuildTacticsWantedAdd(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint id = r.ReadUInt32();
        string title = r.ReadString(); string text = r.ReadString();
        byte day = r.ReadByte(); byte minLvl = r.ReadByte(); byte maxLvl = r.ReadByte();
        uint point = r.ReadUInt32(); uint gold = r.ReadUInt32(); uint silver = r.ReadUInt32(); uint cooper = r.ReadUInt32();
        if (title.Length == 0 || title.Length > 256 || text.Length > 2048) return;   // MAX_BOARD_TITLE / MAX_BOARD_TEXT
        if (_state.FindChar(charId, key) is not { Guild: { Disorg: 0 } g }) return;

        var result = GuildResult.Success;
        var ad = id != 0 && _state.TacticsWanted.TryGetValue(id, out var have) && have.GuildId == g.Id ? have : null;
        if (ad is null)
        {
            if (_state.TacticsWanted.Values.Count(a => a.GuildId == g.Id) >= MaxTacticsWanted) result = GuildResult.MaxWanted;
            else
            {
                if (id == 0 || _state.TacticsWanted.ContainsKey(id)) id = ++_state.TacticsWantedSeq;
                _state.TacticsWanted[id] = ad = new GuildTacticsWanted { Id = id, GuildId = g.Id };
            }
        }
        long end = UnixNow() + GuildWantedPeriod;
        if (ad is not null)
            (ad.MinLevel, ad.MaxLevel, ad.Country, ad.Name, ad.Title, ad.Text, ad.Day, ad.Gold, ad.Silver, ad.Cooper, ad.Point, ad.EndTime)
                = (minLvl, maxLvl, g.Country, g.Name, title, text, day, gold, silver, cooper, point, end);
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSWANTEDADD_REQ, charId, key, result));
        if (result != GuildResult.Success) return;
        SendTacticsWantedList(charId, key);
        await Persist("tacticsWantedAdd", db => db.TacticsWantedAddAsync(id, g.Id, point, gold, silver, cooper, day, minLvl, maxLvl, end, title, text));
    }

    /// <summary>C++ <c>DelGuildTacticsWanted</c>: one of the guild's ads, its applications with it. The list follows.</summary>
    private async Task OnGuildTacticsWantedDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); uint id = r.ReadUInt32();
        if (_state.FindChar(charId, key) is not { Guild: { } g }) return;
        bool had = _state.TacticsWanted.TryGetValue(id, out var ad) && ad.GuildId == g.Id;
        if (had)
        {
            foreach (var app in ad!.Apps.Keys.ToList()) DelTacticsApp(app);
            _state.TacticsWanted.Remove(id);
        }
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSWANTEDDEL_REQ, charId, key, had ? GuildResult.Success : GuildResult.Fail));
        if (!had) return;
        SendTacticsWantedList(charId, key);
        await Persist("tacticsWantedDel", db => db.TacticsWantedDelAsync(id));
    }

    private void OnGuildTacticsWantedList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        SendTacticsWantedList(charId, key);
    }

    /// <summary>C++ <c>NotifyGuildTacticsWantedList</c> → <c>SendMW_GUILDTACTICSWANTEDLIST_REQ</c>: the ads of one's war country (by
    /// guild, then id), the one applied to marked.</summary>
    private void SendTacticsWantedList(uint charId, uint key)
    {
        if (_state.FindChar(charId, key) is not { } ch) return;
        byte country = GetWarCountry(ch);
        uint applied = FindTacticsApp(charId)?.WantedId ?? 0;
        var ads = _state.TacticsWanted.Values.Where(a => a.Country == country).OrderBy(a => a.GuildId).ThenBy(a => a.Id).ToList();
        var w = new PacketWriter(Msg.MW_GUILDTACTICSWANTEDLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)ads.Count);
        foreach (var ad in ads)
        {
            w.WriteUInt32(ad.Id); w.WriteUInt32(ad.GuildId); w.WriteString(ad.Name); w.WriteString(ad.Title); w.WriteString(ad.Text);
            w.WriteByte(ad.Day); w.WriteByte(ad.MinLevel); w.WriteByte(ad.MaxLevel);
            w.WriteUInt32(ad.Point); w.WriteUInt32(ad.Gold); w.WriteUInt32(ad.Silver); w.WriteUInt32(ad.Cooper);
            w.WriteInt64(ad.EndTime); w.WriteByte(applied != 0 && ad.Id == applied ? (byte)1 : (byte)0);
        }
        SendToCharId(charId, key, w.ToArray());
    }

    /// <summary>C++ <c>OnMW_GUILDTACTICSVOLUNTEERING_ACK</c> → <c>AddGuildTacticsWantedApp</c>: applying to an ad (one application at a
    /// time; of one's war country, open, the level in range, not one's own guild). The list follows.</summary>
    private async Task OnGuildTacticsVolunteering(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        uint guildId = r.ReadUInt32(); uint id = r.ReadUInt32();
        if (_state.FindChar(charId, key) is not { } ch) return;
        GuildResult result;
        if (FindTacticsApp(charId) is { } mine) result = mine.WantedGuildId == guildId ? GuildResult.Same : GuildResult.AlreadyApply;
        else if (!_state.TacticsWanted.TryGetValue(id, out var ad) || ad.GuildId != guildId || ad.Country != GetWarCountry(ch)) result = GuildResult.Fail;
        else if (ad.EndTime < UnixNow()) result = GuildResult.WantedEnd;
        else if (ad.MaxLevel < ch.Level || ad.MinLevel > ch.Level) result = GuildResult.MismatchLevel;
        else if (ch.Guild?.Id == guildId) result = GuildResult.SameGuildTactics;
        else
        {
            ad.Apps[charId] = new GuildTacticsWantedApp
            {
                CharId = charId, WantedId = id, WantedGuildId = ad.GuildId, Class = ch.Class, Level = ch.Level, Name = ch.Name,
                Day = ad.Day, Point = ad.Point, Gold = ad.Gold, Silver = ad.Silver, Cooper = ad.Cooper,
            };
            result = GuildResult.Success;
        }
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSVOLUNTEERING_REQ, charId, key, result));
        if (result != GuildResult.Success) return;
        SendTacticsWantedList(charId, key);
        await Persist("tacticsVolunteering", db => db.VolunteeringAsync((byte)GuildRelation.Enemy, charId, id));   // GUILDAPP_TACTICS
    }

    private void OnGuildTacticsVolunteeringDel(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        if (_state.FindChar(charId, key) is null) return;
        bool had = DelTacticsApp(charId);
        SendToCharId(charId, key, BuildResult(Msg.MW_GUILDTACTICSVOLUNTEERINGDEL_REQ, charId, key, had ? GuildResult.Success : GuildResult.Fail));
        if (had) SendTacticsWantedList(charId, key);
    }

    private void OnGuildTacticsVolunteerList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        SendTacticsVolunteerList(charId, key);
    }

    /// <summary>C++ <c>NotifyGuildTacticsVolunteerList</c>: the applicants to the guild's ads (by character id), each with where it is
    /// now.</summary>
    private void SendTacticsVolunteerList(uint charId, uint key)
    {
        if (_state.FindChar(charId, key) is not { Guild: { } g }) return;
        var apps = _state.TacticsWanted.Values.Where(a => a.GuildId == g.Id).SelectMany(a => a.Apps.Values).OrderBy(a => a.CharId).ToList();
        foreach (var app in apps) app.Region = _state.CharactersByName.TryGetValue(app.Name, out var on) ? on.Region : 0;
        var w = new PacketWriter(Msg.MW_GUILDTACTICSVOLUNTEERLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key);
        w.WriteUInt32((uint)apps.Count);
        foreach (var app in apps)
        {
            w.WriteUInt32(app.CharId); w.WriteString(app.Name); w.WriteByte(app.Level); w.WriteByte(app.Class); w.WriteUInt32(app.Region);
            w.WriteByte(app.Day); w.WriteUInt32(app.Point); w.WriteUInt32(app.Gold); w.WriteUInt32(app.Silver); w.WriteUInt32(app.Cooper);
        }
        SendToCharId(charId, key, w.ToArray());
    }

    /// <summary>C++ <c>OnMW_GUILDTACTICSREPLY_ACK</c>: a (vice-)chief turns an applicant down (0), or takes it (non-zero —
    /// <c>ApplyGuildTacticsApp</c>: the new mercenary and the chief told). A failure is answered; the list follows.</summary>
    private async Task OnGuildTacticsReply(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        uint targetId = r.ReadUInt32(); byte reply = r.ReadByte();
        if (_state.FindChar(charId, key) is not { Guild: not null }) return;
        if (reply == 0) DelTacticsApp(targetId);
        else
        {
            var result = await ApplyTacticsApp(targetId);
            if (result != GuildResult.Success) SendToCharId(charId, key, BuildTacticsReply(charId, key, result, targetId, 0, "", "", 0, 0, 0));
        }
        SendTacticsVolunteerList(charId, key);
    }

    private async Task<GuildResult> ApplyTacticsApp(uint charId)
    {
        if (FindTacticsApp(charId) is not { } app || _state.FindGuild(app.WantedGuildId) is not { } g) return GuildResult.Fail;
        if (_state.CharTactics.ContainsKey(charId)) return GuildResult.HaveGuild;
        if (_state.FindGuildByChar(charId)?.FindMember(charId) is { } gm && gm.Duty >= (byte)GuildDuty.ViceChief) return GuildResult.NoDuty;
        if (g.FindTactics(charId) is not null) return GuildResult.AlreadyMember;
        if (g.FindMember(charId) is not null) return GuildResult.SameGuildTactics;
        if (g.PvPUseablePoint < app.Point) return GuildResult.NoPoint;
        if (!CanAddTactics(g)) return GuildResult.MemberFull;
        if (!g.UseMoney(CalcMoney(app.Gold, app.Silver, app.Cooper), use: true)) return GuildResult.NoMoney;
        g.UsePvPoint(app.Point, Proto.PvpUseable);
        await SaveGuildMoney(g); await SaveGuildPvPoint(g);

        var tm = new TacticsMember
        {
            CharId = charId, Name = app.Name, Level = app.Level, Class = app.Class, RewardPoint = app.Point,
            RewardMoney = CalcMoney(app.Gold, app.Silver, app.Cooper), Day = app.Day, EndTime = UnixNow() + app.Day * 86400L,
        };
        if (_state.CharactersByName.GetValueOrDefault(tm.Name) is { } on)
        {
            tm.OnlineChar = on; tm.Level = on.Level;
            SendToChar(on, BuildTacticsReply(on.CharId, on.Key, GuildResult.Success, charId, g.Id, g.Name, tm.Name, app.Gold, app.Silver, app.Cooper));
        }
        if (g.FindMember(g.Chief)?.OnlineChar is { } chief)
            SendToChar(chief, BuildTacticsReply(chief.CharId, chief.Key, GuildResult.Success, charId, g.Id, g.Name, tm.Name, app.Gold, app.Silver, app.Cooper));
        await GuildTacticsAdd(g, tm);
        return GuildResult.Success;
    }

    /// <summary>C++ <c>SendMW_GUILDTACTICSREPLY_REQ</c> (its arguments take the member id before the guild's; the wire has the guild
    /// first).</summary>
    private static byte[] BuildTacticsReply(uint charId, uint key, GuildResult result, uint memberId, uint guildId, string guildName,
        string memberName, uint gold, uint silver, uint cooper)
    {
        var w = new PacketWriter(Msg.MW_GUILDTACTICSREPLY_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)result);
        w.WriteUInt32(guildId); w.WriteString(guildName); w.WriteUInt32(memberId); w.WriteString(memberName);
        w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
        return w.ToArray();
    }

    /// <summary>C++ <c>OnMW_GUILDMONEYRECOVER_ACK</c>: a guard bought from the guild's money could not be put out — the money
    /// goes back.</summary>
    private async Task OnGuildMoneyRecover(PacketReader r)
    {
        uint guildId = r.ReadUInt32(); uint price = r.ReadUInt32();
        if (_state.FindGuild(guildId) is not { } g) return;
        g.GainMoney(0, 0, price);
        await SaveGuildMoney(g);
    }

    // ----- small helpers -----
    private static byte[] BuildResult(ushort id, uint charId, uint key, GuildResult ret)
    { var w = new PacketWriter(id); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte((byte)ret); return w.ToArray(); }

    private static byte[] Clone(PacketWriter w) => w.ToArray();
}
