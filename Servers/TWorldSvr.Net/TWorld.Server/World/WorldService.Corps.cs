using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 2b — corps (squad-of-parties) command-relay and party move/recall. Ported from SSHandler.cpp.
/// Corps are in-memory (like parties). The squad/unit broadcast packet bodies (ADDSQUAD/DELSQUAD/
/// CORPSJOIN/PARTYATTR/etc.) follow the captured C++ field order; the squad/unit list bodies are
/// best-effort and should be byte-verified against SSSender before real-cluster integration.
/// Result codes use the CORPS_* family (here mapped onto small byte constants).
/// </summary>
public sealed partial class WorldService
{
    private const byte CorpsSuccess = 0, CorpsNoParty = 1, CorpsNotCommander = 2, CorpsWrongTarget = 3, CorpsTargetNoParty = 4, CorpsChgCommander = 5;

    private async Task<bool> DispatchCorpsAsync(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.MW_CORPSASK_ACK: OnCorpsAsk(r); break;
            case Msg.MW_CORPSREPLY_ACK: OnCorpsReply(r); break;
            case Msg.MW_CORPSLEAVE_ACK: OnCorpsLeave(r); break;
            case Msg.MW_CORPSCMD_ACK: OnCorpsCmd(r); break;
            case Msg.MW_CHGCORPSCOMMANDER_ACK: OnChgCorpsCommander(r); break;
            case Msg.MW_CORPSENEMYLIST_ACK: OnCorpsEnemyList(r); break;
            case Msg.MW_CORPSHP_ACK: OnCorpsHp(r); break;
            case Msg.MW_PARTYMOVE_ACK: OnPartyMove(r); break;
            case Msg.MW_PARTYMEMBERRECALL_ACK: OnPartyMemberRecall(r); break;
            case Msg.MW_PARTYMEMBERRECALLANS_ACK: OnPartyMemberRecallAns(r); break;
            default: return false;
        }
        await Task.CompletedTask;
        return true;
    }

    // ----- corps formation -----
    private void OnCorpsAsk(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); string target = r.ReadString();
        if (!_state.CharactersByName.TryGetValue(target, out var tgt)) return;
        var w = new PacketWriter(Msg.MW_CORPSASK_REQ);
        w.WriteUInt32(tgt.CharId); w.WriteUInt32(tgt.Key); w.WriteUInt32(charId);
        SendToChar(tgt, w.ToArray());
    }

    private void OnCorpsReply(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); byte reply = r.ReadByte(); string reqName = r.ReadString();
        var ch = _state.Characters.TryGetValue(charId, out var c) ? c : null;
        var req = _state.CharactersByName.TryGetValue(reqName, out var rc) ? rc : null;
        if (reply != Ask.Yes || ch?.Party is null || req?.Party is null) return;

        var reqParty = req.Party;
        Corps corps;
        if (reqParty.CorpsId != 0 && _state.FindCorps(reqParty.CorpsId) is { } existing)
        {
            corps = existing;
        }
        else
        {
            ushort id = _state.PartyIds.Alloc();
            corps = new Corps { Id = id, Commander = reqParty.Id, GeneralId = reqParty.ChiefId };
            _state.CorpsMap[id] = corps;
            EnterCorps(corps, reqParty);
        }
        // announce the new squad to existing parties, then add it
        foreach (var p in corps.Parties.Values) AddSquad(p, ch.Party);
        EnterCorps(corps, ch.Party);
        CorpsJoin(ch.Party, corps.Commander);
        CorpsJoin(reqParty, corps.Commander);
    }

    private void OnCorpsLeave(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); ushort squadId = r.ReadUInt16();
        var party = _state.FindParty(squadId);
        var ch = _state.Characters.TryGetValue(charId, out var c) ? c : null;
        if (party is null || ch is null || party.CorpsId == 0) return;
        var corps = _state.FindCorps(party.CorpsId);
        if (corps is null) return;
        // chief of the squad or the corps general may remove it
        if (!party.IsChief(charId) && corps.GeneralId != charId) return;
        NotifyCorpsLeave(corps, party);
    }

    private void OnCorpsCmd(PacketReader r)
    {
        uint general = r.ReadUInt32(); uint key = r.ReadUInt32();
        ushort mapId = r.ReadUInt16(); ushort squadId = r.ReadUInt16();
        uint charId = r.ReadUInt32(); byte cmd = r.ReadByte();
        uint targetId = r.ReadUInt32(); byte targetType = r.ReadByte();
        ushort posX = r.ReadUInt16(); ushort posZ = r.ReadUInt16();

        var ch = _state.Characters.TryGetValue(charId, out var c) ? c : null;
        if (ch?.Party is null || !ch.Party.IsChief(charId) || ch.Party.CorpsId == 0) return;
        var corps = _state.FindCorps(ch.Party.CorpsId);
        if (corps is null) return;

        // broadcast the squad chief's order to every member of every party in the corps
        foreach (var p in corps.Parties.Values)
            foreach (var m in p.Members)
            {
                var w = new PacketWriter(Msg.MW_CORPSCMD_REQ);
                w.WriteUInt32(m.CharId); w.WriteUInt32(m.Key); w.WriteUInt16(squadId); w.WriteUInt32(charId);
                w.WriteUInt16(mapId); w.WriteByte(cmd); w.WriteUInt32(targetId); w.WriteByte(targetType); w.WriteUInt16(posX); w.WriteUInt16(posZ);
                SendToChar(m, w.ToArray());
            }
    }

    private void OnChgCorpsCommander(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); ushort partyId = r.ReadUInt16();
        var ch = _state.Characters.TryGetValue(charId, out var c) ? c : null;
        if (ch?.Party is null || ch.Party.CorpsId == 0) { SendToCharId(charId, key, BuildChgCorpsCommander(charId, key, CorpsNoParty)); return; }
        var corps = _state.FindCorps(ch.Party.CorpsId);
        if (corps is null || corps.GeneralId != charId || corps.Commander != ch.Party.Id)
        { SendToCharId(charId, key, BuildChgCorpsCommander(charId, key, CorpsNotCommander)); return; }
        if (!corps.Parties.TryGetValue(partyId, out var target) || partyId == corps.Commander)
        { SendToCharId(charId, key, BuildChgCorpsCommander(charId, key, CorpsWrongTarget)); return; }

        corps.Commander = partyId;
        corps.GeneralId = target.ChiefId;
        foreach (var p in corps.Parties.Values) CorpsJoin(p, corps.Commander);
        SendToCharId(charId, key, BuildChgCorpsCommander(charId, key, CorpsChgCommander));
    }

    private void OnCorpsEnemyList(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        // Per-corps enemy markers are not tracked in this build; reply an empty list.
        var w = new PacketWriter(Msg.MW_CORPSENEMYLIST_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(0);
        SendToCharId(charId, key, w.ToArray());
    }

    private void OnCorpsHp(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        var w = new PacketWriter(Msg.MW_CORPSHP_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(0);
        SendToCharId(charId, key, w.ToArray());
    }

    // ----- party move / recall -----
    private void OnPartyMove(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32();
        string target = r.ReadString(); string destName = r.ReadString(); ushort targetParty = r.ReadUInt16();

        var general = _state.Characters.TryGetValue(charId, out var c) ? c : null;
        if (general?.Party is null) { SendToCharId(charId, key, BuildPartyMove(charId, key, CorpsNotCommander)); return; }
        var tgt = _state.CharactersByName.TryGetValue(target, out var t) ? t : null;
        if (tgt is null) { SendToCharId(charId, key, BuildPartyMove(charId, key, CorpsWrongTarget)); return; }

        if (!string.IsNullOrEmpty(destName) && _state.CharactersByName.TryGetValue(destName, out var dest) && dest.Party is not null && tgt.Party is not null)
        {
            // swap the two members between their parties
            var tp = tgt.Party; var dp = dest.Party;
            tp.DelMember(tgt.CharId); dp.DelMember(dest.CharId);
            dp.AddMember(tgt); tp.AddMember(dest);
        }
        else if (_state.FindParty(targetParty) is { } destParty && tgt.Party is not null && destParty != tgt.Party && !destParty.IsFull)
        {
            tgt.Party.DelMember(tgt.CharId);
            destParty.AddMember(tgt);
        }
        else { SendToCharId(charId, key, BuildPartyMove(charId, key, CorpsWrongTarget)); return; }
        SendToCharId(charId, key, BuildPartyMove(charId, key, CorpsSuccess));
    }

    private void OnPartyMemberRecall(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); string target = r.ReadString();
        if (!_state.CharactersByName.TryGetValue(target, out var tgt)) return;
        var w = new PacketWriter(Msg.MW_PARTYMEMBERRECALL_REQ);
        w.WriteUInt32(tgt.CharId); w.WriteUInt32(tgt.Key); w.WriteUInt32(charId);
        SendToChar(tgt, w.ToArray());
    }

    private void OnPartyMemberRecallAns(PacketReader r)
    {
        uint charId = r.ReadUInt32(); uint key = r.ReadUInt32(); byte answer = r.ReadByte(); string name = r.ReadString();
        if (!_state.CharactersByName.TryGetValue(name, out var origin)) return;
        var w = new PacketWriter(Msg.MW_PARTYMEMBERRECALLANS_REQ);
        w.WriteUInt32(origin.CharId); w.WriteUInt32(origin.Key); w.WriteByte(answer); w.WriteUInt32(charId);
        SendToChar(origin, w.ToArray());
    }

    // ----- corps helpers -----
    private void EnterCorps(Corps corps, Party party)
    {
        corps.Parties[party.Id] = party;
        party.CorpsId = corps.Id;
    }

    private void NotifyCorpsLeave(Corps corps, Party party)
    {
        foreach (var other in corps.Parties.Values.Where(p => p.Id != party.Id))
            DelSquad(other, party);
        corps.Parties.Remove(party.Id);
        party.CorpsId = 0;
        CorpsJoin(party, 0);

        if (corps.Parties.Count <= 1)
        {
            foreach (var last in corps.Parties.Values.ToList())
            {
                last.CorpsId = 0;
                CorpsJoin(last, 0);
            }
            corps.Parties.Clear();
            _state.CorpsMap.Remove(corps.Id);
            _state.PartyIds.Free(corps.Id);
        }
        else if (corps.Commander == party.Id)
        {
            corps.Commander = corps.Parties.Keys.First();
            corps.GeneralId = corps.Parties[corps.Commander].ChiefId;
            foreach (var p in corps.Parties.Values) CorpsJoin(p, corps.Commander);
        }
    }

    /// <summary>CorpsJoin — tell every member of a party its corps id + commander, then sync PartyAttr.</summary>
    private void CorpsJoin(Party party, ushort commander)
    {
        foreach (var m in party.Members)
        {
            var w = new PacketWriter(Msg.MW_CORPSJOIN_REQ);
            w.WriteUInt32(m.CharId); w.WriteUInt32(m.Key); w.WriteUInt16(party.CorpsId); w.WriteUInt16(commander);
            SendToChar(m, w.ToArray());
            PartyAttr(m);
        }
        RelayCorpsJoin(party.Id, party.CorpsId, commander); // relay visibility index (no-op without a relay peer)
    }

    /// <summary>PartyAttr — sync a character's party id/type/chief/commander to its map.</summary>
    private void PartyAttr(Character ch)
    {
        var p = ch.Party;
        ushort commander = 0;
        if (p is not null && p.CorpsId != 0 && _state.FindCorps(p.CorpsId) is { } corps) commander = corps.Commander;
        var w = new PacketWriter(Msg.MW_PARTYATTR_REQ);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key);
        w.WriteUInt16(p?.Id ?? 0); w.WriteByte(p?.ObtainType ?? 0); w.WriteUInt32(p?.ChiefId ?? 0); w.WriteUInt16(commander);
        SendToChar(ch, w.ToArray());
    }

    // ADDSQUAD body matches SSSender: chiefId, partyId(WORD), size(BYTE), then a member loop.
    private void AddSquad(Party to, Party newSquad)
    {
        foreach (var m in to.Members)
        {
            var w = new PacketWriter(Msg.MW_ADDSQUAD_REQ);
            w.WriteUInt32(m.CharId); w.WriteUInt32(m.Key);
            w.WriteUInt32(newSquad.ChiefId); w.WriteUInt16(newSquad.Id); w.WriteByte(newSquad.Size);
            foreach (var sm in newSquad.Members)
            {
                w.WriteUInt32(sm.CharId); w.WriteString(sm.Name); w.WriteFloat(1.0f);
                w.WriteUInt32(0);                   // command target obj id
                w.WriteUInt32(sm.MaxHP); w.WriteUInt32(sm.HP);
                w.WriteUInt16(0); w.WriteUInt16(0); // command target pos X/Z
                w.WriteUInt16(sm.MapId);
                w.WriteUInt16((ushort)sm.PosX); w.WriteUInt16((ushort)sm.PosZ);
                w.WriteByte(0);                     // MOVE_NONE
                w.WriteByte(0);                     // command target type
                w.WriteByte(sm.Level); w.WriteByte(sm.Class); w.WriteByte(sm.Race); w.WriteByte(sm.Sex); w.WriteByte(sm.Face); w.WriteByte(sm.Hair);
                w.WriteByte(0);                     // command
            }
            SendToChar(m, w.ToArray());
        }
    }

    private void DelSquad(Party to, Party gone)
    {
        foreach (var m in to.Members)
        {
            var w = new PacketWriter(Msg.MW_DELSQUAD_REQ);
            w.WriteUInt32(m.CharId); w.WriteUInt32(m.Key); w.WriteUInt16(gone.Id);
            SendToChar(m, w.ToArray());
        }
    }

    private static byte[] BuildChgCorpsCommander(uint charId, uint key, byte ret)
    { var w = new PacketWriter(Msg.MW_CHGCORPSCOMMANDER_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); return w.ToArray(); }

    private static byte[] BuildPartyMove(uint charId, uint key, byte ret)
    { var w = new PacketWriter(Msg.MW_PARTYMOVE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); return w.ToArray(); }
}
