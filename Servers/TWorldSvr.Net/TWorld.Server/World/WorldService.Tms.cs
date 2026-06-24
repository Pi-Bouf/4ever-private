using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5i — TMS, the multi-person private-chat ("whisper conversation") subsystem, ported from
/// <c>SSHandler.cpp</c> (OnMW_TMSSEND/TMSINVITEASK/TMSINVITE/TMSOUT) + <c>SSSender.cpp</c>. A conversation
/// (<see cref="Tms"/>) holds a member set; invites require the same war-country; a fresh 1:1 invite to
/// someone who already opened an empty conversation toward you merges into it. The "no receiver" system
/// message normally comes from the server-message table (not yet loaded here), so it's emitted as an empty
/// net-string and noted.
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchTms(ServerSession session, PacketReader r)
    {
        switch (r.Id)
        {
            case Msg.MW_TMSSEND_ACK: OnMW_TMSSEND_ACK(r); return true;
            case Msg.MW_TMSINVITEASK_ACK: OnMW_TMSINVITEASK_ACK(r); return true;
            case Msg.MW_TMSINVITE_ACK: OnMW_TMSINVITE_ACK(r); return true;
            case Msg.MW_TMSOUT_ACK: OnMW_TMSOUT_ACK(r); return true;
        }
        return false;
    }

    /// <summary>Send a message into a conversation. A still-one-member conversation first prompts the saved
    /// peer to join (TMSINVITEASK); otherwise the message is delivered to all members. C++ OnMW_TMSSEND_ACK.</summary>
    private void OnMW_TMSSEND_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint tmsId = r.ReadUInt32();
        string message = r.ReadString();

        var ch = _state.FindChar(charId, key);
        if (ch is null || !ch.TmsIds.Contains(tmsId)) return;
        if (!_state.TmsMap.TryGetValue(tmsId, out var tms)) return;

        if (tms.Members.Count == 1)
        {
            var target = FindByName(tms.LastMember);
            if (target is null) message = NoReceiverMessage();
            else
            {
                var w = new PacketWriter(Msg.MW_TMSINVITEASK_REQ);
                w.WriteUInt32(target.CharId); w.WriteUInt32(target.Key);
                w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.Key); w.WriteUInt32(tmsId); w.WriteString(message);
                SendToChar(target, w.ToArray());
                return;
            }
        }

        foreach (var m in tms.Members.Values.OrderBy(c => c.CharId))
            SendToChar(m, BuildTmsRecv(m, tmsId, ch.Name, message));
    }

    /// <summary>The prompted peer accepted (or declined) the invite. On accept, add them and notify all members
    /// (TMSINVITE); then relay the original message to everyone. C++ OnMW_TMSINVITEASK_ACK.</summary>
    private void OnMW_TMSINVITEASK_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint targetId = r.ReadUInt32();
        uint targetKey = r.ReadUInt32();
        byte result = r.ReadByte();
        uint tmsId = r.ReadUInt32();
        string message = r.ReadString();

        var ch = _state.FindChar(charId, key);
        if (ch is null || !ch.TmsIds.Contains(tmsId)) return;
        if (!_state.TmsMap.TryGetValue(tmsId, out var tms)) return;

        var target = _state.FindChar(targetId, targetKey);
        if (target is not null && result != 0)
        {
            tms.Members[target.CharId] = target;
            target.TmsIds.Add(tms.Id);
            foreach (var m in tms.Members.Values.OrderBy(c => c.CharId))
                SendToChar(m, BuildTmsInvite(target.CharId, m.Key, m.CharId, tms));
        }
        else
        {
            message = NoReceiverMessage();
        }

        foreach (var m in tms.Members.Values.OrderBy(c => c.CharId))
            SendToChar(m, BuildTmsRecv(m, tmsId, ch.Name, message));
    }

    /// <summary>Invite one or more chars into a (new or existing) conversation — same war-country only.
    /// A 1:1 invite to someone who already opened an empty conversation toward me merges into it.
    /// C++ OnMW_TMSINVITE_ACK.</summary>
    private void OnMW_TMSINVITE_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint tmsId = r.ReadUInt32();
        byte count = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null || count == 0) { for (byte i = 0; i < count; i++) r.ReadUInt32(); return; }

        Tms? tms = null;
        if (tmsId != 0)
        {
            if (!ch.TmsIds.Contains(tmsId) || !_state.TmsMap.TryGetValue(tmsId, out tms)) { for (byte i = 0; i < count; i++) r.ReadUInt32(); return; }
        }

        var targets = new List<Character>();
        for (byte i = 0; i < count; i++)
        {
            uint targetId = r.ReadUInt32();
            if (!_state.Characters.TryGetValue(targetId, out var t)) continue;
            if (GetWarCountry(ch) != GetWarCountry(t)) continue;
            targets.Add(t);
        }
        if (targets.Count == 0) return;

        // 1:1 to someone who already opened an empty conversation toward me -> join theirs.
        if (tms is null && targets.Count == 1)
        {
            var target = targets[0];
            foreach (var existingId in target.TmsIds)
            {
                if (!_state.TmsMap.TryGetValue(existingId, out var existing)) continue;
                if (existing.Members.Count == 1 && string.Equals(existing.LastMember, ch.Name, StringComparison.OrdinalIgnoreCase))
                {
                    existing.Members[ch.CharId] = ch;
                    ch.TmsIds.Add(existing.Id);
                    SendToChar(ch, BuildTmsInvite(ch.CharId, ch.Key, ch.CharId, existing));
                    return;
                }
            }
        }

        if (tms is null)
        {
            tms = new Tms { Id = ++_state.TmsIndex };
            tms.Members[ch.CharId] = ch;
            ch.TmsIds.Add(tms.Id);
            _state.TmsMap[tms.Id] = tms;
        }

        foreach (var t in targets)
        {
            tms.Members[t.CharId] = t;
            t.TmsIds.Add(tms.Id);
        }

        foreach (var m in tms.Members.Values.OrderBy(c => c.CharId))
            SendToChar(m, BuildTmsInvite(m.CharId, m.Key, ch.CharId, tms));
    }

    /// <summary>Leave a conversation: notify all members, remove the leaver, and dissolve an empty conversation.
    /// C++ OnMW_TMSOUT_ACK.</summary>
    private void OnMW_TMSOUT_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint tmsId = r.ReadUInt32();

        var ch = _state.FindChar(charId, key);
        if (ch is null || !ch.TmsIds.Contains(tmsId)) return;
        if (!_state.TmsMap.TryGetValue(tmsId, out var tms)) return;

        foreach (var m in tms.Members.Values.OrderBy(c => c.CharId))
        {
            var w = new PacketWriter(Msg.MW_TMSOUT_REQ);
            w.WriteUInt32(m.CharId); w.WriteUInt32(m.Key); w.WriteUInt32(tmsId); w.WriteString(ch.Name);
            SendToChar(m, w.ToArray());
        }

        tms.Members.Remove(charId);
        tms.LastMember = ch.Name;
        ch.TmsIds.Remove(tmsId);
        if (tms.Members.Count == 0) _state.TmsMap.Remove(tmsId);
    }

    /// <summary>Drop a logging-out char from every conversation it's in (and dissolve any that empty). Keeps
    /// TMS member lists free of stale references — called from CloseChar.</summary>
    private void TmsLeaveAll(Character ch)
    {
        foreach (var tmsId in ch.TmsIds.ToList())
        {
            if (_state.TmsMap.TryGetValue(tmsId, out var tms))
            {
                tms.Members.Remove(ch.CharId);
                tms.LastMember = ch.Name;
                if (tms.Members.Count == 0) _state.TmsMap.Remove(tmsId);
            }
        }
        ch.TmsIds.Clear();
    }

    // ===== senders / helpers =====

    private static byte[] BuildTmsRecv(Character to, uint tmsId, string sender, string message)
    {
        var w = new PacketWriter(Msg.MW_TMSRECV_REQ);
        w.WriteUInt32(to.CharId); w.WriteUInt32(to.Key); w.WriteUInt32(tmsId); w.WriteString(sender); w.WriteString(message);
        return w.ToArray();
    }

    /// <summary>MW_TMSINVITE_REQ — recipient char/key, the inviter, the tms id, then the member roster.</summary>
    private static byte[] BuildTmsInvite(uint charId, uint key, uint inviter, Tms tms)
    {
        var w = new PacketWriter(Msg.MW_TMSINVITE_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(inviter); w.WriteUInt32(tms.Id);
        w.WriteByte((byte)tms.Members.Count);
        foreach (var m in tms.Members.Values.OrderBy(c => c.CharId))
        {
            w.WriteUInt32(m.CharId); w.WriteString(m.Name); w.WriteByte(m.Class); w.WriteByte(m.Level);
        }
        return w.ToArray();
    }

    private Character? FindByName(string name)
        => _state.CharactersByName.TryGetValue(name, out var c) ? c : null;

    /// <summary>C++ BuildNetString(header, body) = 4-hex header length + 4-hex body length + header + body.</summary>
    private static string BuildNetString(string header, string body)
        => $"{header.Length:X4}{body.Length:X4}{header}{body}";

    /// <summary>The "no receiver" system message (CTBLSvrMsg / TMS_NORECEIVER), wrapped as a net string.
    /// Empty body if the message table wasn't loaded.</summary>
    private string NoReceiverMessage() => BuildNetString("", _state.GetSvrMsg((uint)SvrMsg.TmsNoReceiver));
}
