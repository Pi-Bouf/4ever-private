using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Guilds, batch G1 — the map's side of the guild relay (C++ CSHandler.cpp:3693–4076, SSHandler.cpp:8094–8633). The guilds
/// live in the world (TWorldSvr.Net); the map checks what it can on the asking player and passes the request on
/// (<c>MW_GUILD*_ACK</c>), then applies the world's answer (<c>MW_GUILD*_REQ</c>) to its players and shows the change around
/// (<c>CS_GUILDATTR_ACK</c>):
/// <list type="bullet">
/// <item><b>Founding</b> (<c>CS_GUILDESTABLISH_REQ</c>): a valid name, level 20, no guild, and not too soon after leaving one
/// (both delays are 0 days in this build). The founder becomes its chief.</item>
/// <item><b>Inviting</b> (a vice-chief or the chief) and <b>answering</b>: the world finds the invitee; it must have no guild and
/// not have just left one; on yes, every member online is told who joined (<c>CS_GUILDJOIN_ACK</c>).</item>
/// <item><b>Leaving</b> (anyone but the chief), <b>kicking out</b> (a vice-chief or the chief), <b>disbanding</b> (the chief):
/// each member online is told (<c>CS_GUILDLEAVE_ACK</c>); the one who left loses the guild, its castle sign-up (unless it fights
/// as a tactics member), and is sent out of a castle it stands in.</item>
/// <item><b>Duties</b> (vice-chief…) and <b>peerages</b> (titles), given by the chief.</item>
/// <item><b>The guild window</b>: its info (with the player's own stat level, points and exp) and the member list, as the world
/// sends them.</item>
/// </list>
/// <para>The guild skills are empty in this database (see the guild-skills memo): <c>CS_GUILDSKILLUPDATE_ACK</c> goes out with
/// none, as the C++ does then. <b>Not ported:</b> the protected list (an invite is never refused by it), the guild cloak reset on
/// leaving, the security-code lock on leave / kick, the UDP guild log.</para>
/// </summary>
public sealed partial class MapService
{
    // TGUILD_RESULT (NetCode.h:434), GUILD_DUTY, the founding level and the waits after leaving (NetCode.h:64)
    private const byte GuildSuccess = 0, GuildJoinDeny = 1, GuildHaveGuild = 8, GuildEstablishErr = 10, GuildLeaveSelf = 12,
        GuildLeaveDisorganization = 14, GuildNoDuty = 16;
    private const byte GuildDutyNone = 0, GuildDutyViceChief = 1, GuildPeerNone = 0;
    private const byte GuildEstablishLevel = 20;
    private const uint GuildLeaveDuration = 0, GuildDisDuration = 0;

    private static bool CheckGuildDuty(Character ch, byte duty) => ch.GuildDuty >= duty;

    /// <summary>The waits after leaving or disbanding a guild (C++ <c>m_bGuildLeave</c> / <c>m_dwGuildLeaveTime</c>): the result
    /// to refuse with, or 0.</summary>
    private byte GuildLeaveWait(Character ch)
    {
        uint since = (uint)UnixNow() - ch.Persist.GuildLeaveTime;
        if (ch.Persist.GuildLeave == GuildLeaveSelf && since < GuildLeaveDuration) return GuildLeaveSelf;
        if (ch.Persist.GuildLeave == GuildLeaveDisorganization && since < GuildDisDuration) return GuildLeaveDisorganization;
        return 0;
    }

    private void SendToWorld(ushort id, ClientSession s, Action<PacketWriter>? body = null)
    {
        var w = new PacketWriter(id);
        w.WriteUInt32(s.CharId); w.WriteUInt32(s.Key);
        body?.Invoke(w);
        _world.Send(w);
    }

    private static void SendGuildResult(ClientSession s, ushort id, byte result, Action<PacketWriter> rest)
    {
        var w = new PacketWriter(id);
        w.WriteByte(result);
        rest(w);
        s.Send(w);
    }

    // ================================ the client's requests ================================

    /// <summary>C++ <c>OnCS_GUILDESTABLISH_REQ</c> (CSHandler.cpp:3693).</summary>
    private void OnCS_GUILDESTABLISH_REQ(ClientSession s, PacketReader r)
    {
        string name = r.ReadString().Trim(' ');
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        void Fail(byte result) => SendGuildResult(s, Msg.CS_GUILDESTABLISH_ACK, result, w => { w.WriteUInt32(0); w.WriteString(""); });
        if (name.Length == 0 || Cp949Length(name) > MaxName || !CheckCharName(name)) { Fail(GuildEstablishErr); return; }
        if (ch.GuildId != 0) { Fail(GuildHaveGuild); return; }
        if (ch.Level < GuildEstablishLevel) { Fail(GuildEstablishErr); return; }
        if (GuildLeaveWait(ch) is var wait and not 0) { Fail(wait); return; }
        ch.Persist.GuildLeave = 0;
        ch.Persist.GuildLeaveTime = 0;
        SendToWorld(Msg.MW_GUILDESTABLISH_ACK, s, w => w.WriteString(name));
    }

    /// <summary>C++ <c>OnCS_GUILDDISORGANIZATION_REQ</c> (CSHandler.cpp:3772): the chief disbands (or takes back the disbanding).</summary>
    private void OnCS_GUILDDISORGANIZATION_REQ(ClientSession s, PacketReader r)
    {
        byte disorg = r.ReadByte();
        if (!s.IsMain || s.Char is not { GuildId: not 0 } ch || IsTutorial(ch)) return;
        if (!CheckGuildDuty(ch, GuildDutyChief)) { SendGuildResult(s, Msg.CS_GUILDDISORGANIZATION_ACK, GuildNoDuty, _ => { }); return; }
        SendToWorld(Msg.MW_GUILDDISORGANIZATION_ACK, s, w => w.WriteByte(disorg));
    }

    /// <summary>C++ <c>OnCS_GUILDINVITE_REQ</c> (CSHandler.cpp:3805): a vice-chief or the chief invites someone by name.</summary>
    private void OnCS_GUILDINVITE_REQ(ClientSession s, PacketReader r)
    {
        string target = r.ReadString();
        if (!s.IsMain || s.Char is not { GuildId: not 0 } ch || IsTutorial(ch) || ch.Name == target) return;
        if (!CheckGuildDuty(ch, GuildDutyViceChief))
        {
            SendGuildResult(s, Msg.CS_GUILDINVITE_ACK, GuildNoDuty, w => { w.WriteString(""); w.WriteUInt32(0); w.WriteString(""); });
            return;
        }
        SendToWorld(Msg.MW_GUILDINVITE_ACK, s, w => w.WriteString(target));
    }

    /// <summary>C++ <c>OnCS_GUILDINVITEANSWER_REQ</c> (CSHandler.cpp:3837).</summary>
    private void OnCS_GUILDINVITEANSWER_REQ(ClientSession s, PacketReader r)
    {
        byte answer = r.ReadByte();
        uint inviter = r.ReadUInt32();
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        ch.Persist.GuildLeave = 0;
        ch.Persist.GuildLeaveTime = 0;
        SendToWorld(Msg.MW_GUILDINVITEANSWER_ACK, s, w => { w.WriteByte(answer); w.WriteUInt32(inviter); });
    }

    /// <summary>C++ <c>OnCS_GUILDLEAVE_REQ</c> (CSHandler.cpp:3866): anyone but the chief may leave.</summary>
    private void OnCS_GUILDLEAVE_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { GuildId: not 0 } ch || IsTutorial(ch)) return;
        if (CheckGuildDuty(ch, GuildDutyChief))
        {
            SendGuildResult(s, Msg.CS_GUILDLEAVE_ACK, GuildNoDuty, w => { w.WriteString(""); w.WriteByte(0); });
            return;
        }
        SendToWorld(Msg.MW_GUILDLEAVE_ACK, s);
    }

    /// <summary>C++ <c>OnCS_GUILDDUTY_REQ</c> (CSHandler.cpp:3918): the chief gives a member a duty.</summary>
    private void OnCS_GUILDDUTY_REQ(ClientSession s, PacketReader r)
    {
        string target = r.ReadString();
        byte duty = r.ReadByte();
        if (!s.IsMain || s.Char is not { GuildId: not 0 } ch || IsTutorial(ch) || ch.Name == target) return;
        if (!CheckGuildDuty(ch, GuildDutyChief))
        {
            SendGuildResult(s, Msg.CS_GUILDDUTY_ACK, GuildNoDuty, w => { w.WriteString(""); w.WriteByte(0); });
            return;
        }
        SendToWorld(Msg.MW_GUILDDUTY_ACK, s, w => { w.WriteString(target); w.WriteByte(duty); });
    }

    /// <summary>C++ <c>OnCS_GUILDPEER_REQ</c> (CSHandler.cpp:3953): the chief gives a member (or itself) a peerage.</summary>
    private void OnCS_GUILDPEER_REQ(ClientSession s, PacketReader r)
    {
        string target = r.ReadString();
        byte peer = r.ReadByte();
        if (!s.IsMain || s.Char is not { GuildId: not 0 } ch || IsTutorial(ch)) return;
        if (!CheckGuildDuty(ch, GuildDutyChief))
        {
            SendGuildResult(s, Msg.CS_GUILDPEER_ACK, GuildNoDuty, w => { w.WriteString(""); w.WriteByte(0); w.WriteByte(0); });
            return;
        }
        SendToWorld(Msg.MW_GUILDPEER_ACK, s, w => { w.WriteString(target); w.WriteByte(peer); });
    }

    /// <summary>C++ <c>OnCS_GUILDKICKOUT_REQ</c> (CSHandler.cpp:3988): a vice-chief or the chief puts a member out.</summary>
    private void OnCS_GUILDKICKOUT_REQ(ClientSession s, PacketReader r)
    {
        string target = r.ReadString();
        if (!s.IsMain || s.Char is not { GuildId: not 0 } ch || IsTutorial(ch)) return;
        if (!CheckGuildDuty(ch, GuildDutyViceChief) || ch.Name == target) return;
        SendToWorld(Msg.MW_GUILDKICKOUT_ACK, s, w => w.WriteString(target));
    }

    /// <summary>C++ <c>OnCS_GUILDMEMBERLIST_REQ</c> / <c>OnCS_GUILDINFO_REQ</c> (CSHandler.cpp:4039/4057).</summary>
    private void OnCS_GUILDMEMBERLIST_REQ(ClientSession s, PacketReader r)
    {
        if (s.IsMain && s.Char is { } ch && GuildOf(ch) != 0) SendToWorld(Msg.MW_GUILDMEMBERLIST_ACK, s);
    }

    private void OnCS_GUILDINFO_REQ(ClientSession s, PacketReader r)
    {
        if (s.IsMain && s.Char is { } ch && GuildOf(ch) != 0) SendToWorld(Msg.MW_GUILDINFO_ACK, s);
    }

    // ================================ the world's answers ================================

    /// <summary>C++ <c>SendCS_GUILDATTR_ACK</c> to the player and those around it: its guild, fame, peerage and tactics guild.</summary>
    private void ShowGuildAttr(ClientSession s, Character ch, byte peer)
    {
        var w = new PacketWriter(Msg.CS_GUILDATTR_ACK, capacity: 48);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(ch.GuildId); w.WriteUInt32(ch.Fame); w.WriteUInt32(ch.FameColor);
        w.WriteString(ch.GuildName); w.WriteByte(peer); w.WriteUInt32(ch.TacticsId); w.WriteString(ch.TacticsName);
        var msg = w.ToArray();
        if (!s.IsMain) return;
        s.Send(msg);
        foreach (var p in _state.Neighbors(s)) p.Send(msg);
    }

    /// <summary>C++ <c>SendCS_GUILDSKILLUPDATE_ACK</c> — the guild skills (none in this database).</summary>
    private static void SendCS_GUILDSKILLUPDATE_ACK(ClientSession s) => s.Send(new PacketWriter(Msg.CS_GUILDSKILLUPDATE_ACK, capacity: 1).WriteByte(0));

    /// <summary>C++ <c>OnMW_GUILDESTABLISH_REQ</c> (SSHandler.cpp:8094): the founder learns the result and becomes chief.</summary>
    private void OnMW_GUILDESTABLISH_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte result = r.ReadByte();
        uint guildId = r.ReadUInt32();
        string name = r.ReadString();
        byte establish = r.ReadByte();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        if (establish != 0) SendGuildResult(s, Msg.CS_GUILDESTABLISH_ACK, result, w => { w.WriteUInt32(guildId); w.WriteString(name); });
        if (result != GuildSuccess) return;
        ch.GuildId = guildId; ch.GuildDuty = GuildDutyChief; ch.GuildPeer = GuildPeerNone; ch.GuildName = name;
        SendCS_GUILDSKILLUPDATE_ACK(s);
        ch.Fame = ch.FameColor = 0;
        ShowGuildAttr(s, ch, GuildPeerNone);
    }

    /// <summary>C++ <c>OnMW_GUILDDISORGANIZATION_REQ</c> (SSHandler.cpp:8167).</summary>
    private void OnMW_GUILDDISORGANIZATION_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte disorg = r.ReadByte();
        if (FindPlayer(charId, key) is { } s) SendGuildResult(s, Msg.CS_GUILDDISORGANIZATION_ACK, disorg, _ => { });
    }

    /// <summary>C++ <c>OnMW_GUILDINVITE_REQ</c> (SSHandler.cpp:8189): the invitee is asked — unless it already has a guild or just
    /// left one (the inviter is told why).</summary>
    private void OnMW_GUILDINVITE_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        string guildName = r.ReadString();
        uint inviter = r.ReadUInt32();
        string inviterName = r.ReadString();
        if (FindPlayer(charId, key) is not { IsMain: true, Char: { } ch } s) return;
        var who = FindByName(inviterName);
        void Refuse(byte result)
        {
            if (who is not null) SendGuildResult(who, Msg.CS_GUILDINVITE_ACK, result, w => { w.WriteString(guildName); w.WriteUInt32(0); w.WriteString(inviterName); });
        }
        if (ch.GuildId != 0) { Refuse(GuildHaveGuild); return; }
        if (GuildLeaveWait(ch) is var wait and not 0) { Refuse(wait); return; }
        SendGuildResult(s, Msg.CS_GUILDINVITE_ACK, GuildSuccess, w => { w.WriteString(guildName); w.WriteUInt32(inviter); w.WriteString(inviterName); });
    }

    /// <summary>C++ <c>OnMW_GUILDJOIN_REQ</c> (SSHandler.cpp:8259): every member online is told who joined; the new member takes
    /// the guild and shows it around.</summary>
    private void OnMW_GUILDJOIN_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte result = r.ReadByte();
        uint guildId = r.ReadUInt32(), fame = r.ReadUInt32(), fameColor = r.ReadUInt32();
        string guildName = r.ReadString();
        uint newId = r.ReadUInt32();
        string newName = r.ReadString();
        byte max = r.ReadByte();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        SendGuildResult(s, Msg.CS_GUILDJOIN_ACK, result, w =>
        {
            w.WriteUInt32(guildId); w.WriteString(guildName); w.WriteUInt32(newId); w.WriteString(newName); w.WriteByte(max);
        });
        SendCS_GUILDSKILLUPDATE_ACK(s);
        if (charId != newId) return;
        (ch.GuildId, ch.GuildName, ch.Fame, ch.FameColor, ch.GuildDuty, ch.GuildPeer) = (guildId, guildName, fame, fameColor, GuildDutyNone, GuildPeerNone);
        ch.Persist.GuildLeave = 0;
        ch.Persist.GuildLeaveTime = 0;
        ShowGuildAttr(s, ch, GuildPeerNone);
    }

    /// <summary>C++ <c>OnMW_GUILDDUTY_REQ</c> (SSHandler.cpp:8379).</summary>
    private void OnMW_GUILDDUTY_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        string target = r.ReadString();
        byte duty = r.ReadByte();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        SendGuildResult(s, Msg.CS_GUILDDUTY_ACK, GuildSuccess, w => { w.WriteString(target); w.WriteByte(duty); });
        if (ch.Name != target) return;
        ch.GuildDuty = duty;
        SendCS_GUILDSKILLUPDATE_ACK(s);
    }

    /// <summary>C++ <c>OnMW_GUILDPEER_REQ</c> (SSHandler.cpp:8450): the new peerage, shown around for the one who got it.</summary>
    private void OnMW_GUILDPEER_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte result = r.ReadByte();
        string target = r.ReadString();
        byte peer = r.ReadByte(), oldPeer = r.ReadByte();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        SendGuildResult(s, Msg.CS_GUILDPEER_ACK, result, w => { w.WriteString(target); w.WriteByte(peer); w.WriteByte(oldPeer); });
        if (result != GuildSuccess || ch.Name != target) return;
        ch.GuildPeer = peer;
        ShowGuildAttr(s, ch, peer);
    }

    /// <summary>C++ <c>OnMW_GUILDLEAVE_REQ</c> (SSHandler.cpp:8507): someone left (or was put out, or the guild was disbanded); the
    /// one who left loses the guild and its castle sign-up, shows it around, and is sent out of a castle.</summary>
    private void OnMW_GUILDLEAVE_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        string target = r.ReadString();
        byte reason = r.ReadByte();
        uint time = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        SendGuildResult(s, Msg.CS_GUILDLEAVE_ACK, GuildSuccess, w => { w.WriteString(target); w.WriteByte(reason); });
        if (ch.Name != target) return;
        if (reason == GuildLeaveSelf || (reason == GuildLeaveDisorganization && ch.GuildDuty == GuildDutyChief))
        {
            ch.Persist.GuildLeave = reason;
            ch.Persist.GuildLeaveTime = time;
        }
        (ch.GuildId, ch.GuildDuty, ch.GuildPeer, ch.GuildName, ch.Fame, ch.FameColor) = (0, 0, 0, "", 0, 0);
        if (ch.TacticsId == 0) { ch.Castle = 0; ch.Camp = 0; }
        if (!s.IsMain) return;
        ShowGuildAttr(s, ch, GuildPeerNone);
        if (IsInCastle(ch)) Teleport(s, ch, ch.Persist.LastSpawnId != 0 ? ch.Persist.LastSpawnId : ch.Persist.SpawnId);
    }

    /// <summary>C++ <c>OnMW_GUILDMEMBERLIST_REQ</c> / <c>OnMW_GUILDINFO_REQ</c> (SSHandler.cpp:8601/8616): the world's list / info
    /// goes to the client as it is — the info with the player's own stat level, points and exp after it.</summary>
    private void OnMW_GUILDMEMBERLIST_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { } s) return;
        s.Send(new PacketWriter(Msg.CS_GUILDMEMBERLIST_ACK).WriteRaw(r.ReadBytes(r.Remaining)));
    }

    private void OnMW_GUILDINFO_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        var w = new PacketWriter(Msg.CS_GUILDINFO_ACK).WriteRaw(r.ReadBytes(r.Remaining));
        w.WriteByte(ch.Persist.StatLevel); w.WriteByte(ch.Persist.StatPoint); w.WriteUInt32(ch.Persist.StatExp);
        s.Send(w);
        SendCS_GUILDSKILLUPDATE_ACK(s);
    }
}
