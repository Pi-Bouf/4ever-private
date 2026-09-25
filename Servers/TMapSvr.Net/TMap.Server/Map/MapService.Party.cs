using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Party management — the map's half of it. The world owns the parties; the map only relays the client's
/// requests up (<c>CS_*_REQ</c> → <c>MW_*_ACK</c>, CSHandler.cpp:3377-3506, 8412) and turns the world's answers
/// back into client packets (<c>MW_*_REQ</c> → <c>CS_*_ACK</c>, SSHandler.cpp:7808-8069, 10392-10511, 11944).
///
/// <para>The party state itself (<c>m_wPartyID</c>/<c>m_dwPartyChiefID</c>/<c>m_bPartyType</c>/<c>m_wCommanderID</c>)
/// is only ever written by <c>MW_PARTYJOIN_REQ</c> and <c>MW_PARTYATTR_REQ</c>. <c>MW_PARTYDEL_REQ</c> carries the
/// new values too but, in the C++, echoes the member's <b>current</b> ones to the client — the world follows it
/// with a <c>MW_PARTYATTR_REQ</c> that does the update. Ported as is.</para>
///
/// <para>Party HP bars: every <c>CS_HPMP_ACK</c> a player receives about <b>itself</b> is also sent to its party
/// through the world (CSSender.cpp:1334), as is every level-up.</para>
///
/// <para><b>Not ported:</b> the arena gates (no arenas), the <c>MAP_INDUN</c> kick on leaving a party (no dungeon
/// instances), the protected (block) list — every invite passes <c>CheckProtected</c> — the squad-chief
/// message (the world does not send it), and member recall.</para>
/// </summary>
public sealed partial class MapService
{
    private const byte AskNo = 1, AskBusy = 2;   // ASK_TYPE (NetCode.h:222)
    private const byte PartyAgree = 0;           // PARTY_AGREE (NetCode.h:392)

    private ClientSession? FindPlayer(uint charId, uint key)
        => _state.FindByChar(charId) is { } s && s.Key == key ? s : null;

    // ======================= client → world =======================

    private void OnCS_PARTYADD_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        string target = r.ReadString();
        byte obtainType = r.ReadByte();

        var w = new PacketWriter(Msg.MW_PARTYADD_ACK);
        w.WriteString(ch.Name); w.WriteString(target); w.WriteByte(obtainType);
        w.WriteUInt32(MaxHpFor(ch)); w.WriteUInt32(ch.Hp); w.WriteUInt32(MaxMpFor(ch)); w.WriteUInt32(ch.Mp);
        _world.Send(w);
    }

    private void OnCS_PARTYJOIN_REQ(ClientSession s, PacketReader r)
    {
        // The C++ gates this one on m_bMain only — no map check.
        if (!s.IsMain || s.Char is not { } ch) return;
        string origin = r.ReadString();
        byte obtainType = r.ReadByte();
        byte response = r.ReadByte();
        SendMW_PARTYJOIN_ACK(origin, ch.Name, obtainType, response, MaxHpFor(ch), ch.Hp, MaxMpFor(ch), ch.Mp);
    }

    private void OnCS_PARTYDEL_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        uint memberId = r.ReadUInt32();

        // Only the chief may remove someone else; anyone may leave.
        if (ch.GetPartyId() == 0 || (memberId != ch.CharId && ch.GetPartyChiefId() != ch.CharId)) return;

        var w = new PacketWriter(Msg.MW_PARTYDEL_ACK);
        w.WriteUInt16(ch.GetPartyId()); w.WriteUInt32(memberId); w.WriteByte((byte)(memberId != ch.CharId ? 1 : 0));
        _world.Send(w);
    }

    private void OnCS_CHGPARTYCHIEF_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        uint targetId = r.ReadUInt32();

        var w = new PacketWriter(Msg.MW_CHGPARTYCHIEF_ACK);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(s.Key); w.WriteUInt32(targetId);
        _world.Send(w);
    }

    private void OnCS_CHGPARTYTYPE_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        byte partyType = r.ReadByte();

        var w = new PacketWriter(Msg.MW_CHGPARTYTYPE_ACK);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(s.Key); w.WriteByte(partyType);
        _world.Send(w);
    }

    private void OnCS_PARTYMOVE_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain || s.Char is not { } ch) return;
        string targetName = r.ReadString();
        string destName = r.ReadString();
        ushort destParty = r.ReadUInt16();

        // A corps commander's chief only (moving a member between the corps' parties).
        if (ch.GetPartyChiefId() != ch.CharId || ch.GetPartyId() != ch.GetCommanderId()) return;

        var w = new PacketWriter(Msg.MW_PARTYMOVE_ACK);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(s.Key); w.WriteString(targetName); w.WriteString(destName);
        w.WriteUInt16(destParty);
        _world.Send(w);
    }

    private void SendMW_PARTYJOIN_ACK(string origin, string target, byte obtainType, byte response,
        uint maxHp, uint hp, uint maxMp, uint mp)
    {
        var w = new PacketWriter(Msg.MW_PARTYJOIN_ACK);
        w.WriteString(origin); w.WriteString(target); w.WriteByte(obtainType); w.WriteByte(response);
        w.WriteUInt32(maxHp); w.WriteUInt32(hp); w.WriteUInt32(maxMp); w.WriteUInt32(mp);
        _world.Send(w);
    }

    // ======================= world → client =======================

    /// <summary>C++ <c>OnMW_PARTYADD_REQ</c>: an invite reached its target (<c>bReply == PARTY_AGREE</c>) — ask
    /// it, unless it is busy — or the inviter is being told the invite failed.</summary>
    private void OnMW_PARTYADD_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        string origin = r.ReadString(), target = r.ReadString();
        byte obtainType = r.ReadByte(), reply = r.ReadByte();
        r.ReadUInt32();                                   // dwOrigin — only CheckProtected reads it

        var s = FindPlayer(charId, key);
        if (s is not { IsMain: true, State: EnterState.InGame, Char: { } ch })
        {
            SendMW_PARTYJOIN_ACK(origin, target, obtainType, AskBusy, 0, 0, 0, 0);
            return;
        }

        if (reply != PartyAgree)
        {
            var w = new PacketWriter(Msg.CS_PARTYADD_ACK);
            w.WriteString(origin); w.WriteString(target); w.WriteByte(reply);
            s.Send(w);
            return;
        }

        if (IsActionBlock(ch))
        {
            SendMW_PARTYJOIN_ACK(origin, ch.Name, obtainType, AskBusy, MaxHpFor(ch), ch.Hp, MaxMpFor(ch), ch.Mp);
            return;
        }

        var ask = new PacketWriter(Msg.CS_PARTYJOINASK_ACK);
        ask.WriteString(origin); ask.WriteByte(obtainType);
        s.Send(ask);
    }

    /// <summary>C++ <c>OnMW_PARTYJOIN_REQ</c>: a member joined — the receiver's own party fields are
    /// overwritten from the packet, then it is shown the member.</summary>
    private void OnMW_PARTYJOIN_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;

        ch.PartyId = r.ReadUInt16();
        string memberName = r.ReadString();
        uint memberId = r.ReadUInt32();
        ch.PartyChiefId = r.ReadUInt32();
        ch.CommanderId = r.ReadUInt16();
        string guild = r.ReadString();
        byte level = r.ReadByte();
        uint maxHp = r.ReadUInt32(), hp = r.ReadUInt32(), maxMp = r.ReadUInt32(), mp = r.ReadUInt32();
        byte race = r.ReadByte(), sex = r.ReadByte(), face = r.ReadByte(), hair = r.ReadByte();
        ch.PartyType = r.ReadByte();
        byte cls = r.ReadByte();

        var w = new PacketWriter(Msg.CS_PARTYJOIN_ACK);
        w.WriteUInt16(ch.GetPartyId()); w.WriteString(memberName); w.WriteUInt32(memberId);
        w.WriteUInt32(ch.GetPartyChiefId()); w.WriteUInt16(ch.GetCommanderId()); w.WriteString(guild);
        w.WriteByte(level); w.WriteUInt32(maxHp); w.WriteUInt32(hp); w.WriteUInt32(maxMp); w.WriteUInt32(mp);
        w.WriteByte(race); w.WriteByte(sex); w.WriteByte(face); w.WriteByte(hair);
        w.WriteByte(ch.PartyType); w.WriteByte(cls);
        s.Send(w);
    }

    /// <summary>C++ <c>OnMW_PARTYDEL_REQ</c>: a member left or was kicked. The packet's chief/commander/party
    /// are read and ignored; the receiver's current values are what the client is sent.</summary>
    private void OnMW_PARTYDEL_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;

        uint memberId = r.ReadUInt32();
        r.ReadUInt32(); r.ReadUInt16(); r.ReadUInt16();   // dwChiefID, wCommander, wPartyID — unused by the C++
        byte kicked = r.ReadByte();

        var w = new PacketWriter(Msg.CS_PARTYDEL_ACK);
        w.WriteUInt32(memberId); w.WriteUInt32(ch.GetPartyChiefId()); w.WriteUInt16(ch.GetCommanderId());
        w.WriteUInt16(ch.GetPartyId()); w.WriteByte(kicked);
        s.Send(w);
    }

    /// <summary>C++ <c>OnMW_PARTYATTR_REQ</c>: the world's word on a member's party fields. The member and every
    /// player around it are told (<c>CS_PARTYATTR_ACK</c>), so name plates update too.</summary>
    private void OnMW_PARTYATTR_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        ushort partyId = r.ReadUInt16();
        byte partyType = r.ReadByte();
        uint chiefId = r.ReadUInt32();
        ushort commander = r.ReadUInt16();

        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        ch.PartyChiefId = chiefId;
        ch.PartyId = partyId;
        ch.PartyType = partyType;
        ch.CommanderId = commander;

        if (!s.IsMain) return;
        var w = new PacketWriter(Msg.CS_PARTYATTR_ACK);
        w.WriteUInt32(ch.CharId); w.WriteUInt16(ch.GetPartyId()); w.WriteUInt32(ch.GetPartyChiefId());
        w.WriteUInt16(ch.GetCommanderId());
        var ack = w.ToArray();
        s.Send(ack);
        if (s.State == EnterState.InGame)
            foreach (var other in _state.Neighbors(s)) other.Send(ack);
    }

    private void OnMW_CHGPARTYCHIEF_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte ret = r.ReadByte();
        if (FindPlayer(charId, key) is not { } s) return;
        var w = new PacketWriter(Msg.CS_CHGPARTYCHIEF_ACK);
        w.WriteByte(ret);
        s.Send(w);
    }

    private void OnMW_CHGPARTYTYPE_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        byte ret = r.ReadByte(), partyType = r.ReadByte();
        if (ret == 0) ch.PartyType = partyType;
        var w = new PacketWriter(Msg.CS_CHGPARTYTYPE_ACK);
        w.WriteByte(ret); w.WriteByte(partyType);
        s.Send(w);
    }

    private void OnMW_PARTYMANSTAT_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        uint id = r.ReadUInt32();
        byte type = r.ReadByte(), level = r.ReadByte();
        uint maxHp = r.ReadUInt32(), hp = r.ReadUInt32(), maxMp = r.ReadUInt32(), mp = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { } s) return;

        var w = new PacketWriter(Msg.CS_PARTYMANSTAT_ACK);
        w.WriteUInt32(id); w.WriteByte(type); w.WriteByte(level);
        w.WriteUInt32(maxHp); w.WriteUInt32(hp); w.WriteUInt32(maxMp); w.WriteUInt32(mp);
        s.Send(w);
    }

    private void OnMW_PARTYMOVE_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte result = r.ReadByte();
        if (FindPlayer(charId, key) is not { } s) return;
        var w = new PacketWriter(Msg.CS_PARTYMOVE_ACK);
        w.WriteByte(result);
        s.Send(w);
    }

    // ======================= party HP bars =======================

    /// <summary>C++ <c>CTPlayer::SendCS_HPMP_ACK</c>'s tail (CSSender.cpp:1334): a player told about its own
    /// HP/MP passes it on to its party through the world (<c>MW_PARTYMANSTAT_ACK</c>). Also used on level-up.</summary>
    private void NotifyPartyManStat(ClientSession receiver, uint id, byte type, uint maxHp, uint hp, uint maxMp, uint mp)
    {
        if (receiver.Char is not { } ch || id != ch.CharId || ch.GetPartyId() == 0) return;
        var w = new PacketWriter(Msg.MW_PARTYMANSTAT_ACK);
        w.WriteUInt16(ch.GetPartyId()); w.WriteUInt32(id); w.WriteByte(type); w.WriteByte(ch.Level);
        w.WriteUInt32(maxHp); w.WriteUInt32(hp); w.WriteUInt32(maxMp); w.WriteUInt32(mp);
        _world.Send(w);
    }

    /// <summary>C++ <c>CTObjBase::IsActionBlock</c> (TObjBase.cpp:4545) — dead, transformed, or held/blocked by a
    /// status effect.</summary>
    private bool IsActionBlock(Character ch)
    {
        if (ch.Hp == 0) return true;
        foreach (var m in ch.MaintainSkills)
        {
            if (!_templates.Skills.TryGetValue(m.SkillId, out var tpl)) continue;
            foreach (var d in tpl.Data)
                if (d.Type == SdtTrans || (d.Type == SkillTemplate.SdtStatus && (d.Exec == SdtStatusBlock || d.Exec == SdtStatusHold)))
                    return true;
        }
        return false;
    }

    private const byte SdtTrans = 3, SdtStatusBlock = 3, SdtStatusHold = 8;   // SKILL_DATA_TYPE / SDT_STATUS_TYPE
}
