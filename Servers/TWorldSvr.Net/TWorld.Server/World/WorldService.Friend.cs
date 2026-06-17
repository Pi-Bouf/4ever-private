using Microsoft.Extensions.Logging;
using TWorld.Data;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 3 — the friend subsystem, ported from <c>SSHandler.cpp</c> (OnMW_FRIEND*) + <c>SSSender.cpp</c>
/// (SendMW_FRIEND*) + <c>TWorldSvr.cpp</c> (LeaveFriend/EraseFriend). Friend state lives on
/// <see cref="Character"/> and is loaded once on enter; DB persistence is best-effort.
/// </summary>
public sealed partial class WorldService
{
    private async Task<bool> DispatchFriendAsync(ServerSession session, PacketReader r)
    {
        switch (r.Id)
        {
            case Msg.MW_FRIENDLIST_ACK: OnFriendList(r); return true;
            case Msg.MW_FRIENDASK_ACK: await OnFriendAsk(session, r); return true;
            case Msg.MW_FRIENDREPLY_ACK: await OnFriendReply(r); return true;
            case Msg.MW_FRIENDERASE_ACK: await OnFriendErase(r); return true;
            case Msg.MW_FRIENDPROTECTEDASK_ACK: OnFriendProtectedAsk(r); return true;
            case Msg.MW_FRIENDGROUPMAKE_ACK: await OnFriendGroupMake(r); return true;
            case Msg.MW_FRIENDGROUPDELETE_ACK: await OnFriendGroupDelete(r); return true;
            case Msg.MW_FRIENDGROUPCHANGE_ACK: await OnFriendGroupChange(r); return true;
            case Msg.MW_FRIENDGROUPNAME_ACK: await OnFriendGroupName(r); return true;
            default: return false;
        }
    }

    /// <summary>Number of friends excluding one-way "target" entries (matches the C++ count loops).</summary>
    private static int VisibleFriendCount(Character ch)
        => ch.Friends.Values.Count(f => f.Type != FriendType.Target);

    // ===== startup load (mirrors OnDM_FRIENDLIST_ACK) =====

    private async Task LoadFriendsAsync(Character player)
    {
        if (_socialDb is null) return;
        FriendLoad load;
        try { load = await _socialDb.LoadFriendsAsync(player.CharId); }
        catch (Exception ex) { _log.LogWarning(ex, "Friend list load failed for {Char}.", player.CharId); return; }

        player.DbLoading = true;
        player.FriendGroups.Clear();
        foreach (var g in load.Groups) player.FriendGroups[g.Group] = g.Name;

        foreach (var fr in load.Friends)
        {
            var f = new Friend { Id = fr.FriendId, Name = fr.Name, Level = fr.Level, Group = fr.Group, Class = fr.Class, Type = FriendType.Friend };
            // bRet (protected/blacklist) is treated as not-protected here — the connection path proceeds.
            if (_state.CharactersByName.TryGetValue(fr.Name, out var fc) && fc.DbLoading)
            {
                f.Connected = true;
                f.Region = fc.Region;
                if (fc.Friends.TryGetValue(player.CharId, out var back)) { back.Connected = true; back.Region = player.Region; }
            }
            player.Friends[f.Id] = f;
        }

        // Inbound "target" entries — people who added me. Mirrors the C++ tail loop, including the
        // FRIENDCONNECTION notify to an already-online partner.
        foreach (var tg in load.Targets)
        {
            Character? partner = null;
            if (player.Friends.TryGetValue(tg.FriendId, out var existing))
            {
                existing.Type = FriendType.FriendFriend;
                _state.CharactersByName.TryGetValue(existing.Name, out partner);
            }
            else
            {
                var f = new Friend { Id = tg.FriendId, Name = tg.Name, Type = FriendType.Target };
                if (_state.CharactersByName.TryGetValue(tg.Name, out var fc) && fc.DbLoading) { f.Connected = true; partner = fc; }
                player.Friends[f.Id] = f;
                if (partner is not null && partner.Friends.TryGetValue(player.CharId, out var back))
                {
                    back.Connected = true;
                    back.Region = player.Region;
                }
            }

            if (partner is not null)
                SendToChar(partner, BuildFriendConnection(partner.CharId, partner.Key, (byte)FriendConnState.Connection, player.Name, player.Region));
        }
    }

    // ===== leave (mirrors LeaveFriend) =====

    private void LeaveFriend(Character ch)
    {
        foreach (var f in ch.Friends.Values)
        {
            if (!f.Connected) continue;
            if (!_state.CharactersByName.TryGetValue(f.Name, out var tgt)) continue;
            if (tgt.Friends.TryGetValue(ch.CharId, out var back)) back.Connected = false;
            if (f.Type != FriendType.Friend)
                SendToChar(tgt, BuildFriendConnection(tgt.CharId, tgt.Key, (byte)FriendConnState.Disconnection, ch.Name, 0));
        }
        ch.Friends.Clear();
    }

    // ===== handlers =====

    private void OnFriendList(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        SendToChar(ch, BuildFriendList(ch));
    }

    private async Task OnFriendAsk(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string target = r.ReadString();

        var ch = _state.FindChar(charId, key);
        if (ch is null) return;

        if (!_state.CharactersByName.TryGetValue(target, out var tgt) || tgt.Country != ch.Country)
        {
            session.Send(BuildFriendAdd(charId, key, (byte)FriendResult.NotFound, 0, target));
            return;
        }

        ch.Friends.TryGetValue(tgt.CharId, out var existing);
        if (existing is not null && existing.Type != FriendType.Target)
        {
            session.Send(BuildFriendAdd(charId, key, (byte)FriendResult.Already, 0, target));
            return;
        }
        tgt.Friends.TryGetValue(charId, out var reciprocal);

        if (VisibleFriendCount(ch) >= Proto.MaxFriend)
        {
            session.Send(BuildFriendAdd(charId, key, (byte)FriendResult.Max, 0, target));
            return;
        }

        // Already a mutual "target" pair on both sides → upgrade to friends immediately.
        if (existing is not null && reciprocal is not null)
        {
            existing.Type = FriendType.FriendFriend; reciprocal.Type = FriendType.FriendFriend;
            existing.Connected = true; existing.Region = tgt.Region; existing.Group = 0;
            reciprocal.Connected = true; reciprocal.Region = ch.Region;
            await Persist(() => _socialDb!.FriendInsertAsync(ch.CharId, tgt.CharId), "TFriendInsert");
            session.Send(BuildFriendAdd(charId, key, (byte)FriendResult.Success, tgt.CharId, tgt.Name, tgt.Level, 0, tgt.Class, tgt.Region));
            return;
        }

        if (TargetFriendCount(tgt, ch.CharId) >= Proto.MaxFriend)
        {
            session.Send(BuildFriendAdd(charId, key, (byte)FriendResult.Refuse, 0, target));
            return;
        }

        SendToChar(tgt, BuildFriendAsk(tgt.CharId, tgt.Key, ch.Name, ch.CharId));
    }

    /// <summary>Friend slots used by a target, excluding their own targets and an existing link to me.</summary>
    private static int TargetFriendCount(Character tgt, uint myId)
        => tgt.Friends.Values.Count(f => f.Type != FriendType.Target && f.Id != myId);

    private async Task OnFriendReply(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string inviter = r.ReadString();
        byte reply = r.ReadByte();

        _state.CharactersByName.TryGetValue(inviter, out var player);   // the original asker
        var friend = _state.FindChar(charId, key);                      // the one replying
        if (player is null && friend is null) return;

        if (player is null && friend is not null) { SendToChar(friend, BuildFriendAdd(friend.CharId, friend.Key, (byte)FriendResult.NotFound, 0, inviter)); return; }
        if (player is not null && friend is null) { SendToChar(player, BuildFriendAdd(player.CharId, player.Key, (byte)FriendResult.NotFound, 0, "")); return; }

        if (reply == Ask.Yes)
        {
            Upsert(player!, friend!);
            Upsert(friend!, player!);
            await Persist(() => _socialDb!.FriendInsertAsync(friend!.CharId, player!.CharId), "TFriendInsert");
            await Persist(() => _socialDb!.FriendInsertAsync(player!.CharId, friend!.CharId), "TFriendInsert");
            SendToChar(player!, BuildFriendAdd(player!.CharId, player.Key, (byte)FriendResult.Success, friend!.CharId, friend.Name, friend.Level, 0, friend.Class, friend.Region));
            SendToChar(friend!, BuildFriendAdd(friend.CharId, friend.Key, (byte)FriendResult.Success, player.CharId, player.Name, player.Level, 0, player.Class, player.Region));
        }
        else
        {
            SendToChar(player!, BuildFriendAdd(player!.CharId, player.Key, reply, friend!.CharId, friend.Name));
        }

        static void Upsert(Character owner, Character other)
        {
            if (owner.Friends.TryGetValue(other.CharId, out var f))
            {
                f.Type = FriendType.FriendFriend; f.Connected = true; f.Region = other.Region;
            }
            else
            {
                owner.Friends[other.CharId] = new Friend
                {
                    Id = other.CharId, Name = other.Name, Level = other.Level, Group = 0,
                    Class = other.Class, Type = FriendType.FriendFriend, Connected = true, Region = other.Region,
                };
            }
        }
    }

    private async Task OnFriendErase(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint target = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        await EraseFriend(ch, target);
    }

    /// <summary>Mirrors EraseFriend: removes (or downgrades) a friend on both sides + persists.</summary>
    private async Task EraseFriend(Character ch, uint target)
    {
        if (!ch.Friends.TryGetValue(target, out var f))
        {
            SendToChar(ch, BuildFriendErase(ch.CharId, ch.Key, (byte)FriendResult.NotFound, target));
            return;
        }

        if (f.Type == FriendType.FriendFriend)
        {
            if (f.Connected && _state.CharactersByName.TryGetValue(f.Name, out var tgt) && tgt.Friends.TryGetValue(ch.CharId, out var back))
                back.Type = FriendType.Friend;     // the other side keeps me as a one-way friend
            f.Type = FriendType.Target;            // I keep them only as an inbound target
        }
        else if (f.Type == FriendType.Friend)
        {
            if (f.Connected && _state.CharactersByName.TryGetValue(f.Name, out var tgt))
                tgt.Friends.Remove(ch.CharId);
            ch.Friends.Remove(target);
        }

        await Persist(() => _socialDb!.FriendEraseAsync(ch.CharId, target), "TFriendDelete");
        SendToChar(ch, BuildFriendErase(ch.CharId, ch.Key, (byte)FriendResult.Success, target));
    }

    private void OnFriendProtectedAsk(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string target = r.ReadString();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        if (_state.CharactersByName.TryGetValue(target, out var tgt))
            SendToChar(tgt, BuildFriendAdd(tgt.CharId, tgt.Key, (byte)FriendResult.Refuse, 0, ch.Name));
    }

    private async Task OnFriendGroupMake(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte group = r.ReadByte();
        string name = r.ReadString();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;

        if (group == 0 || ch.FriendGroups.Count >= Proto.MaxFriendGroup)
        { SendToChar(ch, BuildFriendGroupMake(charId, key, (byte)FriendResult.Max, 0, name)); return; }
        if (name.Length > Proto.MaxGroupName) return;
        if (ch.FriendGroups.ContainsKey(group) || ch.FriendGroups.Values.Contains(name))
        { SendToChar(ch, BuildFriendGroupMake(charId, key, (byte)FriendResult.Already, 0, name)); return; }

        ch.FriendGroups[group] = name;
        SendToChar(ch, BuildFriendGroupMake(charId, key, (byte)FriendResult.Success, group, name));
        await Persist(() => _socialDb!.FriendGroupMakeAsync(charId, group, name), "TFriendGroupMake");
    }

    private async Task OnFriendGroupDelete(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte group = r.ReadByte();
        var ch = _state.FindChar(charId, key);
        if (ch is null || !ch.FriendGroups.ContainsKey(group)) return;

        if (ch.Friends.Values.Any(f => f.Type != FriendType.Target && f.Group == group))
        { SendToChar(ch, BuildFriendGroupDelete(charId, key, (byte)FriendResult.Refuse, group)); return; }

        ch.FriendGroups.Remove(group);
        SendToChar(ch, BuildFriendGroupDelete(charId, key, (byte)FriendResult.Success, group));
        await Persist(() => _socialDb!.FriendGroupDeleteAsync(charId, group), "TFriendGroupDelete");
    }

    private async Task OnFriendGroupChange(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        uint friendId = r.ReadUInt32();
        byte group = r.ReadByte();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        if (group != 0 && !ch.FriendGroups.ContainsKey(group)) return;
        if (!ch.Friends.TryGetValue(friendId, out var f)) return;

        f.Group = group;
        SendToChar(ch, BuildFriendGroupChange(charId, key, (byte)FriendResult.Success, group, friendId));
        await Persist(() => _socialDb!.FriendGroupChangeAsync(charId, group, friendId), "TFriendGroupChange");
    }

    private async Task OnFriendGroupName(PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte group = r.ReadByte();
        string name = r.ReadString();
        var ch = _state.FindChar(charId, key);
        if (ch is null || name.Length > Proto.MaxGroupName) return;

        if (ch.FriendGroups.Values.Contains(name))
        { SendToChar(ch, BuildFriendGroupName(charId, key, (byte)FriendResult.Refuse, group, name)); return; }
        if (!ch.FriendGroups.ContainsKey(group))
        { SendToChar(ch, BuildFriendGroupName(charId, key, (byte)FriendResult.NotFound, group, name)); return; }

        ch.FriendGroups[group] = name;
        SendToChar(ch, BuildFriendGroupName(charId, key, 0, group, name));
        await Persist(() => _socialDb!.FriendGroupNameAsync(charId, group, name), "TFriendGroupName");
    }

    private async Task Persist(Func<Task> op, string what)
    {
        if (_socialDb is null) return;
        try { await op(); } catch (Exception ex) { _log.LogWarning(ex, "{Proc} failed.", what); }
    }

    // ===== senders =====

    /// <summary>SendMW_FRIENDLIST_REQ: the combined soulmate + group + friend snapshot.</summary>
    private byte[] BuildFriendList(Character ch)
    {
        var w = new PacketWriter(Msg.MW_FRIENDLIST_REQ);
        w.WriteUInt32(ch.CharId);
        w.WriteUInt32(ch.Key);

        if (ch.Soulmates.TryGetValue(ch.CharId, out var soul))
        {
            w.WriteUInt32(soul.Target);
            w.WriteString(soul.Name);
            w.WriteByte(soul.Level);
            w.WriteByte(soul.Class);
            w.WriteBool(soul.Connected);
            w.WriteUInt32(soul.Connected ? soul.Region : 0);
        }
        else
        {
            w.WriteUInt32(0);
        }

        w.WriteByte((byte)ch.FriendGroups.Count);
        foreach (var (g, name) in ch.FriendGroups) { w.WriteByte(g); w.WriteString(name); }

        w.WriteByte((byte)VisibleFriendCount(ch));
        foreach (var f in ch.Friends.Values)
        {
            if (f.Type == FriendType.Target) continue;
            w.WriteUInt32(f.Id);
            w.WriteString(f.Name);
            w.WriteByte(f.Level);
            w.WriteByte(f.Group);
            w.WriteByte(f.Class);
            w.WriteBool(f.Connected);
            w.WriteUInt32(f.Connected ? f.Region : 0);
        }
        return w.ToArray();
    }

    private static byte[] BuildFriendAdd(uint charId, uint key, byte result, uint friendId, string name,
        byte level = 0, byte group = 0, byte cls = 0, uint region = 0)
    {
        var w = new PacketWriter(Msg.MW_FRIENDADD_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(result); w.WriteUInt32(friendId);
        w.WriteString(name); w.WriteByte(level); w.WriteByte(group); w.WriteByte(cls); w.WriteUInt32(region);
        return w.ToArray();
    }

    private static byte[] BuildFriendAsk(uint charId, uint key, string inviter, uint inviterId)
    { var w = new PacketWriter(Msg.MW_FRIENDASK_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteString(inviter); w.WriteUInt32(inviterId); return w.ToArray(); }

    private static byte[] BuildFriendErase(uint charId, uint key, byte ret, uint target)
    { var w = new PacketWriter(Msg.MW_FRIENDERASE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteUInt32(target); return w.ToArray(); }

    private static byte[] BuildFriendConnection(uint charId, uint key, byte ret, string name, uint region)
    { var w = new PacketWriter(Msg.MW_FRIENDCONNECTION_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteString(name); w.WriteUInt32(region); return w.ToArray(); }

    private static byte[] BuildFriendGroupMake(uint charId, uint key, byte ret, byte group, string name)
    { var w = new PacketWriter(Msg.MW_FRIENDGROUPMAKE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteByte(group); w.WriteString(name); return w.ToArray(); }

    private static byte[] BuildFriendGroupDelete(uint charId, uint key, byte ret, byte group)
    { var w = new PacketWriter(Msg.MW_FRIENDGROUPDELETE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteByte(group); return w.ToArray(); }

    private static byte[] BuildFriendGroupChange(uint charId, uint key, byte ret, byte group, uint friendId)
    { var w = new PacketWriter(Msg.MW_FRIENDGROUPCHANGE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteByte(group); w.WriteUInt32(friendId); return w.ToArray(); }

    private static byte[] BuildFriendGroupName(uint charId, uint key, byte ret, byte group, string name)
    { var w = new PacketWriter(Msg.MW_FRIENDGROUPNAME_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(ret); w.WriteByte(group); w.WriteString(name); return w.ToArray(); }
}
