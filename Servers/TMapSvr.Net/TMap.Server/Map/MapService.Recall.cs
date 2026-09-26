using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Summons — the recall-monster core (C++ <c>CTRecallMon</c>). Every summon is created through the world, which owns
/// the cluster-wide id: the map sends <c>MW_CREATERECALLMON_ACK</c> (id 0), the world allocates one and sends
/// <c>MW_CREATERECALLMON_REQ</c> back (SSHandler.cpp:1632), and only then does the summon exist
/// (<c>CreateRecallMon</c>, TMapSvr.cpp:7460) and get shown with <c>CS_ADDRECALLMON_ACK</c>. Removal takes the same
/// road (<c>MW_RECALLMONDEL_ACK</c> → <c>MW_RECALLMONDEL_REQ</c>, SSHandler.cpp:1789).
///
/// <para>The owner's client drives its summons: it moves them (<c>CS_MONMOVE_REQ</c> with <c>OT_RECALL</c>, only the
/// owner is accepted), sets their mode (<c>CS_CHGMODERECALLMON_REQ</c>) and dismisses them
/// (<c>CS_DELRECALLMON_REQ</c>). A summon expires when its life runs out (<c>CheckTimeRecallMon</c>, TPlayer.cpp:4193),
/// dies with its owner, and follows the owner through a teleport — except a mount nobody is riding, which is sent
/// away (InitMap, TMapSvr.cpp:8163).</para>
///
/// <para><b>Faithful, flagged:</b> a new summon always starts at full HP/MP (the C++ tests the fresh object's HP, so
/// passed values are dropped). <b>Not ported yet:</b> summon combat (attacking, being attacked, owner credit),
/// self-objects (<c>OT_SELF</c>), the summon skills themselves, saving summons across logout
/// (<c>TRECALLMONTABLE</c>), the owner's item bonus on a summon's attack power, and multi-server handoff
/// (<c>MW_RECALLMONDATA</c>). The class/race chart checks <c>CreateRecallMon</c> also makes are skipped.</para>
/// </summary>
public sealed partial class MapService
{
    private const int MaxRecallMon = 5;                                              // MAX_RECALLMON
    private const byte TrecallMain = 1, TrecallAutoAi = 2, TrecallMine = 3;          // TRECALL_TYPE
    private const byte OtSelf = 11;

    /// <summary>The creature record <c>MW_CREATERECALLMON_ACK/_REQ</c> carry (SSSender.cpp:552-622).</summary>
    internal sealed record RecallRecord(uint CharId, uint Key, uint MonId, ushort Mon, uint Attr, ushort PetId, byte Effect,
        string Name, byte Level, byte Class, byte Race, byte Action, byte Status, byte Mode, uint MaxHp, uint MaxMp,
        uint Hp, uint Mp, byte Hit, byte SkillLevel, float X, float Y, float Z, ushort Dir, uint Time, byte RecallAuto,
        uint TargetId, byte TargetType, IReadOnlyList<ushort> Skills);

    private static RecallRecord ReadRecallRecord(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32(), monId = r.ReadUInt32();
        ushort mon = r.ReadUInt16(); uint attr = r.ReadUInt32(); ushort petId = r.ReadUInt16(); byte effect = r.ReadByte();
        string name = r.ReadString();
        byte level = r.ReadByte(), cls = r.ReadByte(), race = r.ReadByte(), action = r.ReadByte(), status = r.ReadByte(),
            mode = r.ReadByte();
        uint maxHp = r.ReadUInt32(), maxMp = r.ReadUInt32(), hp = r.ReadUInt32(), mp = r.ReadUInt32();
        byte hit = r.ReadByte(), skillLevel = r.ReadByte();
        float x = r.ReadFloat(), y = r.ReadFloat(), z = r.ReadFloat();
        ushort dir = r.ReadUInt16(); uint time = r.ReadUInt32();
        byte auto = r.ReadByte(); uint targetId = r.ReadUInt32(); byte targetType = r.ReadByte();
        byte n = r.ReadByte();
        var skills = new List<ushort>(n);
        for (int i = 0; i < n; i++) skills.Add(r.ReadUInt16());
        return new RecallRecord(charId, key, monId, mon, attr, petId, effect, name, level, cls, race, action, status, mode,
            maxHp, maxMp, hp, mp, hit, skillLevel, x, y, z, dir, time, auto, targetId, targetType, skills);
    }

    /// <summary>C++ <c>SendMW_CREATERECALLMON_ACK</c> (SSSender.cpp:552) — note <c>dwTime</c> goes after <c>wDir</c>.</summary>
    private void SendMW_CREATERECALLMON_ACK(RecallRecord c)
    {
        var w = new PacketWriter(Msg.MW_CREATERECALLMON_ACK, capacity: 96);
        w.WriteUInt32(c.CharId); w.WriteUInt32(c.Key); w.WriteUInt32(c.MonId); w.WriteUInt16(c.Mon); w.WriteUInt32(c.Attr);
        w.WriteUInt16(c.PetId); w.WriteByte(c.Effect); w.WriteString(c.Name);
        w.WriteByte(c.Level); w.WriteByte(c.Class); w.WriteByte(c.Race); w.WriteByte(c.Action); w.WriteByte(c.Status);
        w.WriteByte(c.Mode);
        w.WriteUInt32(c.MaxHp); w.WriteUInt32(c.MaxMp); w.WriteUInt32(c.Hp); w.WriteUInt32(c.Mp);
        w.WriteByte(c.Hit); w.WriteByte(c.SkillLevel);
        w.WriteFloat(c.X); w.WriteFloat(c.Y); w.WriteFloat(c.Z); w.WriteUInt16(c.Dir); w.WriteUInt32(c.Time);
        w.WriteByte(c.RecallAuto); w.WriteUInt32(c.TargetId); w.WriteByte(c.TargetType);
        w.WriteByte((byte)c.Skills.Count);
        foreach (var id in c.Skills) w.WriteUInt16(id);
        _world.Send(w);
    }

    /// <summary>C++ <c>SendMW_RECALLMONDEL_ACK</c> — ask the world to remove a summon everywhere.</summary>
    private void SendMW_RECALLMONDEL_ACK(uint charId, uint key, uint monId, bool forever = true)
    {
        var w = new PacketWriter(Msg.MW_RECALLMONDEL_ACK, capacity: 16);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(monId); w.WriteByte((byte)(forever ? 1 : 0));
        _world.Send(w);
    }

    // ================================ world → map ================================

    private void OnMW_CREATERECALLMON_REQ(PacketReader r)
    {
        var rec = ReadRecallRecord(r);
        if (FindPlayer(rec.CharId, rec.Key) is not { Char: { } ch } s) return;

        if (s.IsMain && ch.Hp == 0)                                                   // OS_DEAD
        {
            if (rec.PetId != 0) SendCS_PETRECALL_ACK(s, PetFail);
            SendMW_RECALLMONDEL_ACK(rec.CharId, rec.Key, rec.MonId);
            return;
        }
        if (!_templates.MonsterTemplates.TryGetValue(rec.Mon, out var tpl)) return;
        if (rec.RecallAuto == 0) CheckRecallMon(s, ch, tpl);

        var mon = CreateRecallMon(s, ch, tpl, rec);
        if (mon is not null && s.State == EnterState.InGame && !mon.InMap) EnterRecall(mon);
    }

    /// <summary>C++ <c>CreateRecallMon</c> (TMapSvr.cpp:7460).</summary>
    private RecallMon? CreateRecallMon(ClientSession s, Character ch, MonsterTemplate tpl, RecallRecord rec)
    {
        if (ch.Recalls.TryGetValue(rec.MonId, out var existing)) return existing;
        if (ch.Recalls.Count >= MaxRecallMon) return null;
        if (_templates.MonAttr((ushort)(rec.Attr & 0xFFFF), (byte)(rec.Attr >> 16)) is not { } attr) return null;

        var mon = new RecallMon
        {
            Id = rec.MonId, OwnerId = ch.CharId, ChartId = tpl.Id, Template = tpl, Attr = attr,
            PetId = rec.PetId, Effect = rec.Effect, Name = rec.Name, Level = rec.Level,
            AtkLevel = ch.Level, AtkSkillLevel = rec.SkillLevel, Hit = rec.Hit,
            Action = rec.Action, Status = rec.Status, Mode = rec.Mode,
            RecallType = rec.RecallAuto != 0 ? TrecallAutoAi : tpl.RecallType,
            TargetId = rec.RecallAuto != 0 ? rec.TargetId : 0, TargetType = rec.RecallAuto != 0 ? rec.TargetType : (byte)0,
            MaxHp = rec.MaxHp != 0 ? rec.MaxHp : attr.MaxHp, MaxMp = rec.MaxMp != 0 ? rec.MaxMp : attr.MaxMp,
            Channel = s.Channel, MapId = ch.MapId, Country = ch.Country, AidCountry = ch.AidCountry, Region = ch.RegionId,
            PosX = rec.X, PosY = rec.Y, PosZ = rec.Z, Dir = rec.Dir,
        };
        mon.Hp = mon.MaxHp;                                                              // see the class remarks
        mon.Mp = mon.MaxMp;
        AddSummonSkills(mon, rec.Skills, rec.SkillLevel);
        if (rec.Time != 0) { mon.RecallTickMs = NowMs; mon.DurationMs = rec.Time; }

        ch.Recalls[mon.Id] = mon;
        return mon;
    }

    /// <summary>C++ <c>CTPlayer::CheckRecallMon</c> (TPlayer.cpp:3552): a new main / mine / auto summon sends away the
    /// owner's current main or mine summon — one of those at a time.</summary>
    private void CheckRecallMon(ClientSession s, Character ch, MonsterTemplate tpl)
    {
        if (tpl.RecallType is TrecallMain or TrecallMine or TrecallAutoAi)
        {
            foreach (var m in ch.Recalls.Values)
                if (m.RecallType is TrecallMain or TrecallMine) SendMW_RECALLMONDEL_ACK(ch.CharId, s.Key, m.Id);
            foreach (var m in ch.SelfObjs.Values.ToList())
                if (m.RecallType == TrecallAutoAi) DeleteSelfObj(ch, m.Id);
        }
        else if (tpl.RecallType == TrecallMaintain)
        {
            // Keep at most max(GetRecallCount, 1) - 1 older ones, newest first (C++ reverse walk): with no passive
            // bonus, casting a new one removes every existing one.
            int cap = 1, current = 0;
            foreach (var m in ch.SelfObjs.Values.Reverse().ToList())
                if (m.RecallType == TrecallMaintain && cap <= ++current) DeleteSelfObj(ch, m.Id);
        }
    }

    private void OnMW_RECALLMONDEL_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        r.ReadUInt32();                                                                  // dwKey — the C++ finds by id only
        uint monId = r.ReadUInt32();
        bool forever = r.ReadByte() != 0;
        if (_state.FindByChar(charId) is not { Char: { } ch } s) return;

        if (!forever)
        {
            if (s.IsMain) SendCS_DELRECALLMON_ACK(s, charId, monId, exitMap: true, forever: false);
            return;
        }
        DeleteRecallMon(s, ch, monId, forever: true);
    }

    /// <summary>C++ <c>CTPlayer::DeleteRecallMon</c> (TPlayer.cpp:3176): <c>CTRecallMon::OnDie</c> (a ridden mount
    /// drops its rider) then off the map and out of the owner's list.</summary>
    private void DeleteRecallMon(ClientSession s, Character ch, uint monId, bool forever)
    {
        if (!ch.Recalls.TryGetValue(monId, out var mon)) return;
        if (ch.Riding == mon.Id) PetRiding(s, ch, 0);
        SummonOnDie(mon);
        if (mon.InMap) LeaveRecall(mon, exitMap: true, forever);
        ch.Recalls.Remove(monId);
    }

    // ================================ client → map ================================

    private void OnCS_DELRECALLMON_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.State != EnterState.InGame || s.Char is not { } ch) return;
        uint monId = r.ReadUInt32();
        byte type = r.ReadByte();
        if (type == RecallMon.OtRecall) SendMW_RECALLMONDEL_ACK(ch.CharId, s.Key, monId);
        else if (type == RecallMon.OtSelf) DeleteSelfObj(ch, monId);   // local: no world round trip
    }

    private void OnCS_CHGMODERECALLMON_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.State != EnterState.InGame || s.Char is not { } ch) return;
        uint monId = r.ReadUInt32();
        byte mode = r.ReadByte();
        if (ch.Recalls.TryGetValue(monId, out var mon)) mon.Mode = mode;               // OT_RECALL only, no reply
    }

    // ================================ presence ================================

    /// <summary>C++ <c>CTMap::EnterMAP(recall)</c> → <c>CTCell::EnterMonster</c>: shown to everyone around as new.</summary>
    private void EnterRecall(RecallMon mon)
    {
        _state.AddRecall(mon);
        _log.LogDebug("[summon] ENTER {Type}:{Id} tpl {Tpl} recallType {RecallType} owner {Owner} at ({X:F1},{Z:F1}) life {Life} ms, viewers [{Viewers}].",
            mon.ObjType, mon.Id, mon.ChartId, mon.RecallType, mon.OwnerId, mon.PosX, mon.PosZ, mon.DurationMs,
            string.Join(",", _state.PlayersAround(mon).Select(p => p.CharId)));
        foreach (var p in _state.PlayersAround(mon)) ShowSummon(p, mon, newMember: true);
    }

    private void LeaveRecall(RecallMon mon, bool exitMap, bool forever)
    {
        _log.LogDebug("[summon] LEAVE {Type}:{Id} owner {Owner} (exitMap {Exit}, forever {Forever}).", mon.ObjType, mon.Id, mon.OwnerId, exitMap, forever);
        foreach (var p in _state.PlayersAround(mon)) HideSummon(p, mon, exitMap, forever);
        _state.RemoveRecall(mon);
    }

    /// <summary>The summons a just-arrived player can see (C++ <c>CTCell::EnterPlayer</c> — sent before the players'
    /// <c>CS_ENTER_ACK</c>, so a rider's mount exists on the client when its rider appears).</summary>
    private void SendRecallsInView(ClientSession s)
    {
        foreach (var m in _state.RecallsInView(s)) ShowSummon(s, m, newMember: false);
    }

    /// <summary>A summon moved by its owner's client: re-bucket and exchange it with players it came into or out of
    /// view of (C++ <c>CTMap::OnMove</c>).</summary>
    private void ApplyRecallMove(RecallMon mon, float x, float z)
    {
        var diff = _state.MoveRecall(mon, x, z);
        if (!diff.CellChanged) return;
        foreach (var p in diff.Left) HideSummon(p, mon, exitMap: false, forever: true);
        foreach (var p in diff.Entered) ShowSummon(p, mon, newMember: false);
    }

    /// <summary>C++ <c>ExitMAP</c> (TMapSvr.cpp:7419): off the mount, and every summon leaves the map with its owner —
    /// kept in the list, to come back at the destination.</summary>
    private void RecallsExitMap(ClientSession s, Character ch)
    {
        PetRiding(s, ch, 0);
        ClearSelfObjs(ch);                                              // C++ ClearSelfMon(FALSE): placed objects stay behind
        foreach (var m in ch.Recalls.Values)
            if (m.InMap) LeaveRecall(m, exitMap: true, forever: false);
    }

    /// <summary>C++ <c>InitMap</c> (TMapSvr.cpp:8163-8200), on arrival: a mount nobody rides is sent away; every other
    /// summon stands up 2 units behind its owner, in its owner's mode, on the new map.</summary>
    private void RecallsEnterMap(ClientSession s, Character ch)
    {
        foreach (var m in ch.Recalls.Values.ToList())
        {
            if (m.InMap) continue;
            if (m.RecallType == RecallMon.TypePet && ch.Riding == 0)
            {
                SendMW_RECALLMONDEL_ACK(ch.CharId, s.Key, m.Id);
                continue;
            }
            float rad = ch.Dir * MathF.PI / 900f;
            m.Action = TaStand; m.Status = 1; m.Mode = ch.Mode;
            m.PosX = ch.PosX - 2f * MathF.Sin(rad); m.PosY = ch.PosY; m.PosZ = ch.PosZ - 2f * MathF.Cos(rad);
            m.MapId = ch.MapId; m.Channel = s.Channel;
            EnterRecall(m);
        }
    }

    /// <summary>A player leaving for good (logout / disconnect): its summons go with it (C++ <c>ClearRecallMon</c>).</summary>
    private void ClearRecalls(ClientSession s, Character ch)
    {
        ch.Riding = 0;
        ClearSelfObjs(ch);
        foreach (var m in ch.Recalls.Values)
            if (m.InMap) LeaveRecall(m, exitMap: true, forever: true);
        ch.Recalls.Clear();
    }

    /// <summary>C++ <c>CTPlayer::OnDie</c> (TPlayer.cpp:2083): every summon dies with its owner.</summary>
    private void RecallsOwnerDied(ClientSession s, Character ch)
    {
        foreach (var m in ch.Recalls.Values.ToList())
        {
            if (ch.Riding == m.Id) PetRiding(s, ch, 0);
            SendMW_RECALLMONDEL_ACK(ch.CharId, s.Key, m.Id);
        }
        ClearSelfObjs(ch);
    }

    /// <summary>C++ <c>CheckTimeRecallMon</c> (TPlayer.cpp:4193), from the map timer: a summon whose life ran out is
    /// sent away. Asked once — the world round trip removes it.</summary>
    private void RunRecallTimers()
    {
        long now = NowMs;
        foreach (var s in _state.AllInGame())
        {
            if (s.Char is not { Recalls.Count: > 0 } ch) continue;
            foreach (var m in ch.Recalls.Values)
                if (!m.DeleteAsked && m.Expired(now))
                {
                    m.DeleteAsked = true;
                    SendMW_RECALLMONDEL_ACK(ch.CharId, s.Key, m.Id);
                }
        }
    }

    // ================================ senders ================================

    /// <summary>Shows a summon to one player — a companion with <c>CS_ADDSPOLECNIKMON_ACK</c>, anything else with
    /// <c>CS_ADDRECALLMON_ACK</c> (C++ <c>CTCell</c> keeps them in separate maps and calls the matching sender).</summary>
    private void ShowSummon(ClientSession p, RecallMon m, bool newMember)
    {
        if (m.IsSelf) SendCS_ADDSELFOBJ_ACK(p, m, newMember);
        else if (m.IsCompanion) SendCS_ADDSPOLECNIKMON_ACK(p, m, newMember);
        else SendCS_ADDRECALLMON_ACK(p, m, newMember);
    }

    private void HideSummon(ClientSession p, RecallMon m, bool exitMap, bool forever)
    {
        if (m.IsSelf) SendCS_DELSELFOBJ_ACK(p, m.Id, exitMap);
        else if (m.IsCompanion) SendCS_DELSPOLECNIKMON_ACK(p, m.OwnerId, m.Id, exitMap);
        else SendCS_DELRECALLMON_ACK(p, m.OwnerId, m.Id, exitMap, forever);
    }

    /// <summary>C++ <c>SendCS_ADDRECALLMON_ACK</c> (CSSender.cpp:769) — field-exact.</summary>
    private void SendCS_ADDRECALLMON_ACK(ClientSession s, RecallMon m, bool newMember)
    {
        var w = new PacketWriter(Msg.CS_ADDRECALLMON_ACK, capacity: 128);
        w.WriteUInt32(m.OwnerId); w.WriteUInt32(m.Id); w.WriteUInt16(m.ChartId); w.WriteUInt16(m.PetId); w.WriteByte(m.Effect);
        w.WriteString(m.Name); w.WriteByte(m.Country); w.WriteByte(m.AidCountry);
        w.WriteByte(0);                                                                  // GetColor — deferred, as for monsters
        w.WriteByte(m.Level);
        w.WriteUInt32(m.MaxHp); w.WriteUInt32(m.Hp); w.WriteUInt32(m.MaxMp); w.WriteUInt32(m.Mp);
        w.WriteFloat(m.PosX); w.WriteFloat(m.PosY); w.WriteFloat(m.PosZ);
        w.WriteUInt16(m.Pitch); w.WriteUInt16(m.Dir); w.WriteByte(m.MouseDir); w.WriteByte(m.KeyDir);
        w.WriteByte(m.Action); w.WriteByte(m.Mode); w.WriteByte((byte)(newMember ? 1 : 0));
        w.WriteUInt32(m.Region); w.WriteByte(m.RecallType); w.WriteByte(m.Hit); w.WriteByte(m.AtkSkillLevel);
        w.WriteUInt16(m.Attr?.AttackLevel ?? 0); w.WriteByte(m.AtkLevel);
        w.WriteUInt32(m.Attr?.AtkMin ?? 0); w.WriteUInt32(m.Attr?.AtkMax ?? 0);          // GetMinAP/GetMaxAP (owner bonus deferred)
        w.WriteUInt32(0); w.WriteUInt32(0);                                              // GetMin/MaxMagicAP (wMAP unloaded)
        w.WriteUInt32(m.LifeLeft(NowMs)); w.WriteUInt32(m.TargetId); w.WriteByte(m.TargetType);
        w.WriteByte((byte)m.MaintainSkills.Count);
        foreach (var buff in m.MaintainSkills) WriteMaintainSkill(w, buff, NowMs);
        s.Send(w);
    }

    /// <summary>C++ <c>SendCS_DELRECALLMON_ACK</c> (CSSender.cpp:1033).</summary>
    private static void SendCS_DELRECALLMON_ACK(ClientSession s, uint hostId, uint monId, bool exitMap, bool forever)
    {
        var w = new PacketWriter(Msg.CS_DELRECALLMON_ACK, capacity: 16);
        w.WriteUInt32(hostId); w.WriteUInt32(monId); w.WriteByte((byte)(exitMap ? 1 : 0)); w.WriteByte((byte)(forever ? 1 : 0));
        s.Send(w);
    }
}
