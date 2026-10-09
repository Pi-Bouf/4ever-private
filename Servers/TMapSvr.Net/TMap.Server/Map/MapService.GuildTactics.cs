using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Guilds, batch G3 (C++ CSHandler.cpp / SSHandler.cpp, the <c>GUILDTACTICS*</c>, <c>GUILDPOINTREWARD</c> and <c>MONSTERBUY</c>
/// handlers):
/// <list type="bullet">
/// <item><b>Mercenaries</b> (tactics): a (vice-)chief posts mercenary ads, invites by name with a contract (days, PvP points,
/// money), reads the applicants and takes or turns them down, fires one; anyone applies to an ad (or withdraws), answers an
/// invite, and leaves a contract. The world checks and pays; here the player's tactics guild is set or cleared, shown around
/// (<c>CS_GUILDATTR_ACK</c>), and a player standing in a castle is sent out. A mercenary taken on by a reply gets a welcome
/// mail from the one who took it.</item>
/// <item><b>PvP point rewards</b>: the chief gives guild points to a member — the member is mailed (<c>MSG_GUILDPOINT_TAKE</c>)
/// and the world sends it the points.</item>
/// <item><b>The castle guards' shop</b> (<c>TNPC_MONSTER</c>, <c>TMONSTERSHOPCHART</c>): a guild officer buys a guard post from the
/// guild's money; a tower's guards only for the side holding that tower. The guards come out on the buyer's channel, of its
/// country, and stay until killed; knocking the ball off a tower removes its guards. Whatever stops the guards coming out
/// after the world took the money gives it back (<c>MW_GUILDMONEYRECOVER_ACK</c>).</item>
/// </list>
/// <para>The world's mails for a contract's end (<c>MW_WORLDPOSTSEND_REQ</c> <c>WPT_TACTICSKICK</c> / <c>WPT_TACTICSEND</c>) are
/// in MapService.SmallRequests.cs; the mail texts come from <c>TSVRMSGCHART</c>. <b>Not ported:</b> the security-code lock,
/// the UDP tactics log, the server's level cap on tower guards (<c>CSPGetLimitedLevel</c>), and guild skills
/// (<c>CS_GUILDSKILLACTION_REQ</c> — their chart is empty in this database).</para>
/// </summary>
public sealed partial class MapService
{
    private const byte TnpcMonster = 22;                                       // TNPC_TYPE TNPC_MONSTER
    private const byte MsbSuccess = 0, MsbInvalidNpc = 1, MsbNotFound = 2, MsbCampMismatch = 4, MsbAuthority = 5, MsbAlready = 6;   // MONSTERBUY_RESULT
    private const byte GprNoMember = 2;                                        // GUILDPOINTREWARD_RESULT
    private const byte GuildSameGuildTactics = 19;                             // TGUILD_RESULT
    private const byte WptTacticsKick = 1, WptTacticsEnd = 2;                  // WORLDPOST_TYPE
    // SERVER_MESSAGE (CTProtocol.h:53) — rows of TSVRMSGCHART
    private const uint MsgGuildPointTake = 8, MsgTacticsTitle = 30, MsgTacticsMessage = 31, MsgTacticsKickTitle = 32,
        MsgTacticsKickMessage = 33, MsgTacticsEndTitle = 34, MsgTacticsEndMessage = 35;

    /// <summary>C++ <c>GetSvrMsg</c>: a server message, empty when the chart has none.</summary>
    private string SvrMsg(uint id) => _templates.SvrMsgs.GetValueOrDefault(id, "");

    /// <summary>C++ <c>CString::Format</c> for the server messages: each <c>%s</c> / <c>%d</c> takes the next value.</summary>
    private static string FormatSvrMsg(string format, params object[] args)
    {
        var sb = new System.Text.StringBuilder();
        int next = 0;
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] == '%' && i + 1 < format.Length && format[i + 1] is 's' or 'd' && next < args.Length)
            { sb.Append(args[next++]); i++; }
            else sb.Append(format[i]);
        }
        return sb.ToString();
    }

    /// <summary>C++ <c>SendDM_POSTRECV_REQ</c> without an item: a mail saved, its receiver told.</summary>
    private async Task MailFrom(uint sendId, string sender, uint recvId, string recver, string title, string message, byte type,
        uint gold = 0, uint silver = 0, uint cooper = 0)
    {
        if (PostStore is not { } db) return;
        long now = UnixNow();
        (int saved, uint postId, _) = await Safe(() => db.SavePostAsync(sendId, recvId, recver, sender, title, message, 0, type,
            gold, silver, cooper, now), (PostInternal, 0u, 0u));
        if (saved == 0 && postId != 0) NotifyPostRecv(postId, sender, recver, title, type, now);
    }

    // ================================ mercenaries ================================

    private void OnCS_GUILDTACTICSWANTEDADD_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { GuildId: not 0 } ch || IsTutorial(ch)) return;
        if (!CheckGuildDuty(ch, GuildDutyViceChief)) { SendByte(s, Msg.CS_GUILDWANTEDADD_ACK, GuildNoDuty); return; }   // as the C++
        uint id = r.ReadUInt32();
        string title = r.ReadString(), text = r.ReadString();
        byte day = r.ReadByte(), min = r.ReadByte(), max = r.ReadByte();
        uint point = r.ReadUInt32(), gold = r.ReadUInt32(), silver = r.ReadUInt32(), cooper = r.ReadUInt32();
        if (title.Length == 0) return;
        SendToWorld(Msg.MW_GUILDTACTICSWANTEDADD_ACK, s, w =>
        {
            w.WriteUInt32(id); w.WriteString(title); w.WriteString(text); w.WriteByte(day); w.WriteByte(min); w.WriteByte(max);
            w.WriteUInt32(point); w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
        });
    }

    private void OnCS_GUILDTACTICSWANTEDDEL_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch) || !CheckGuildDuty(ch, GuildDutyViceChief)) return;
        uint id = r.ReadUInt32();
        SendToWorld(Msg.MW_GUILDTACTICSWANTEDDEL_ACK, s, w => w.WriteUInt32(id));
    }

    private void OnCS_GUILDTACTICSWANTEDLIST_REQ(ClientSession s)
    {
        if (s.IsMain && s.Char is not null) SendToWorld(Msg.MW_GUILDTACTICSWANTEDLIST_ACK, s);
    }

    /// <summary>C++ <c>OnCS_GUILDTACTICSVOLUNTEERING_REQ</c>: not while a mercenary, not to one's own guild.</summary>
    private void OnCS_GUILDTACTICSVOLUNTEERING_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        if (ch.TacticsId != 0) { SendByte(s, Msg.CS_GUILDTACTICSVOLUNTEERING_ACK, GuildHaveGuild); return; }
        uint guildId = r.ReadUInt32(), id = r.ReadUInt32();
        if (ch.GuildId == guildId) { SendByte(s, Msg.CS_GUILDTACTICSVOLUNTEERING_ACK, GuildSameGuildTactics); return; }
        SendToWorld(Msg.MW_GUILDTACTICSVOLUNTEERING_ACK, s, w => { w.WriteUInt32(guildId); w.WriteUInt32(id); });
    }

    private void OnCS_GUILDTACTICSVOLUNTEERINGDEL_REQ(ClientSession s)
    {
        if (s.IsMain && s.Char is { } ch && !IsTutorial(ch)) SendToWorld(Msg.MW_GUILDTACTICSVOLUNTEERINGDEL_ACK, s);
    }

    private void OnCS_GUILDTACTICSVOLUNTEERLIST_REQ(ClientSession s)
    {
        if (s.IsMain && s.Char is { } ch && CheckGuildDuty(ch, GuildDutyViceChief)) SendToWorld(Msg.MW_GUILDTACTICSVOLUNTEERLIST_ACK, s);
    }

    private void OnCS_GUILDTACTICSREPLY_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { GuildId: not 0, TacticsId: 0 } ch || IsTutorial(ch)) return;
        uint target = r.ReadUInt32();
        byte reply = r.ReadByte();
        if (!CheckGuildDuty(ch, GuildDutyViceChief))
        {
            s.Send(new PacketWriter(Msg.CS_GUILDTACTICSREPLY_ACK, capacity: 5).WriteByte(GuildNoDuty).WriteUInt32(target));
            return;
        }
        SendToWorld(Msg.MW_GUILDTACTICSREPLY_ACK, s, w => { w.WriteUInt32(target); w.WriteByte(reply); });
    }

    /// <summary>C++ <c>OnCS_GUILDTACTICSKICKOUT_REQ</c>: firing another needs a (vice-)chief who is no mercenary; oneself — leaving
    /// one's contract — anyone.</summary>
    private void OnCS_GUILDTACTICSKICKOUT_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        uint target = r.ReadUInt32();
        if (target != ch.CharId)
        {
            if (ch.GuildId == 0) return;
            if (!CheckGuildDuty(ch, GuildDutyViceChief) || ch.TacticsId != 0)
            {
                s.Send(new PacketWriter(Msg.CS_GUILDTACTICSKICKOUT_ACK, capacity: 5).WriteByte(GuildNoDuty).WriteUInt32(target));
                return;
            }
        }
        SendToWorld(Msg.MW_GUILDTACTICSKICKOUT_ACK, s, w => w.WriteUInt32(target));
    }

    private void OnCS_GUILDTACTICSINVITE_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        string name = r.ReadString();
        byte day = r.ReadByte();
        uint point = r.ReadUInt32(), gold = r.ReadUInt32(), silver = r.ReadUInt32(), cooper = r.ReadUInt32();
        if (ch.GuildId == 0 || !CheckGuildDuty(ch, GuildDutyViceChief) || ch.TacticsId != 0) return;   // (no tournament maps here)
        SendToWorld(Msg.MW_GUILDTACTICSINVITE_ACK, s, w =>
        {
            w.WriteString(name); w.WriteByte(day); w.WriteUInt32(point); w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
        });
    }

    private void OnCS_GUILDTACTICSANSWER_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        byte answer = r.ReadByte();
        string inviter = r.ReadString();
        byte day = r.ReadByte();
        uint point = r.ReadUInt32(), gold = r.ReadUInt32(), silver = r.ReadUInt32(), cooper = r.ReadUInt32();
        SendToWorld(Msg.MW_GUILDTACTICSANSWER_ACK, s, w =>
        {
            w.WriteByte(answer); w.WriteString(inviter); w.WriteByte(day);
            w.WriteUInt32(point); w.WriteUInt32(gold); w.WriteUInt32(silver); w.WriteUInt32(cooper);
        });
    }

    private void OnCS_GUILDTACTICSLIST_REQ(ClientSession s)
    {
        if (s.IsMain && s.Char is { } ch && (ch.GuildId != 0 || ch.TacticsId != 0)) SendToWorld(Msg.MW_GUILDTACTICSLIST_ACK, s);
    }

    /// <summary>C++ <c>OnMW_GUILDTACTICSINVITE_REQ</c>: the offer, to the invited player.</summary>
    private void OnMW_GUILDTACTICSINVITE_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        if (FindPlayer(charId, key) is { } s) s.Send(new PacketWriter(Msg.CS_GUILDTACTICSINVITE_ACK).WriteRaw(r.ReadBytes(r.Remaining)));
    }

    /// <summary>C++ <c>OnMW_GUILDTACTICSANSWER_REQ</c> / <c>OnMW_GUILDTACTICSREPLY_REQ</c> (the same body): the result to whom it
    /// is for; on success the new mercenary takes the guild's colours, and the one who took it on mails it a welcome.</summary>
    private void OnMW_GUILDTACTICSJOIN(PacketReader r, ushort csId)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte result = r.ReadByte();
        uint guildId = r.ReadUInt32();
        string guildName = r.ReadString();
        uint memberId = r.ReadUInt32();
        string memberName = r.ReadString();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        s.Send(new PacketWriter(csId, capacity: 5).WriteByte(result).WriteUInt32(memberId));
        if (result != GuildSuccess) return;
        if (charId == memberId)
        {
            (ch.TacticsId, ch.TacticsName, ch.Castle, ch.Camp) = (guildId, guildName, 0, 0);
            if (!s.IsMain) return;
            ShowGuildAttr(s, ch, ch.GuildPeer);
            if (IsInCastle(ch)) Teleport(s, ch, ch.Persist.LastSpawnId != 0 ? ch.Persist.LastSpawnId : ch.Persist.SpawnId);
        }
        else
            _ = MailFrom(charId, ch.Name, memberId, memberName, SvrMsg(MsgTacticsTitle),
                FormatSvrMsg(SvrMsg(MsgTacticsMessage), guildName), PostPackage);
    }

    /// <summary>C++ <c>OnMW_GUILDTACTICSKICKOUT_REQ</c>: the result; the mercenary itself loses its tactics guild.</summary>
    private void OnMW_GUILDTACTICSKICKOUT_REQ(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte result = r.ReadByte();
        uint target = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        s.Send(new PacketWriter(Msg.CS_GUILDTACTICSKICKOUT_ACK, capacity: 5).WriteByte(result).WriteUInt32(target));
        if (result != GuildSuccess || charId != target) return;
        (ch.TacticsId, ch.TacticsName, ch.Castle, ch.Camp) = (0, "", 0, 0);
        if (!s.IsMain) return;
        ShowGuildAttr(s, ch, ch.GuildPeer);
        if (IsInCastle(ch)) Teleport(s, ch, ch.Persist.LastSpawnId != 0 ? ch.Persist.LastSpawnId : ch.Persist.SpawnId);
    }

    // ================================ PvP point rewards ================================

    /// <summary>C++ <c>OnCS_GUILDPOINTREWARD_REQ</c>: the chief only; a name is needed.</summary>
    private void OnCS_GUILDPOINTREWARD_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        string target = r.ReadString();
        uint point = r.ReadUInt32();
        string message = r.ReadString();
        if (ch.GuildId == 0 || ch.GuildDuty != GuildDutyChief) return;
        if (target.Length == 0 || target.Length > MaxName)
        {
            s.Send(new PacketWriter(Msg.CS_GUILDPOINTREWARD_ACK, capacity: 5).WriteByte(GprNoMember).WriteUInt32(point));
            return;
        }
        if (message.Length > MaxBoardText) message = message[..MaxBoardText];
        SendToWorld(Msg.MW_GUILDPOINTREWARD_ACK, s, w => { w.WriteString(target); w.WriteUInt32(point); w.WriteString(message); });
    }

    /// <summary>C++ <c>OnMW_GUILDPOINTREWARD_REQ</c>: the chief told what is left; given, the member is mailed.</summary>
    private void OnMW_GUILDPOINTREWARD_REQ(PacketReader r)
    {
        byte ret = r.ReadByte();
        uint charId = r.ReadUInt32(), key = r.ReadUInt32(), remain = r.ReadUInt32();
        if (FindPlayer(charId, key) is not { Char: { } ch } s) return;
        if (ret == 0)
        {
            uint point = r.ReadUInt32(), targetId = r.ReadUInt32();
            string target = r.ReadString(), message = r.ReadString();
            _ = MailFrom(charId, ch.Name, targetId, target, FormatSvrMsg(SvrMsg(MsgGuildPointTake), ch.GuildName, point), message, PostNormal);
        }
        s.Send(new PacketWriter(Msg.CS_GUILDPOINTREWARD_ACK, capacity: 5).WriteByte(ret).WriteUInt32(remain));
    }

    /// <summary>C++ <c>OnMW_WORLDPOSTSEND_REQ</c> <c>WPT_TACTICSKICK</c> / <c>WPT_TACTICSEND</c>: a fired or finished mercenary is mailed
    /// by the chief, its pay attached.</summary>
    private async Task MailTacticsEnd(byte type, PacketReader r)
    {
        uint value = r.ReadUInt32(), senderId = r.ReadUInt32();
        string sender = r.ReadString();
        uint recvId = r.ReadUInt32();
        string recver = r.ReadString();
        long money = r.ReadInt64();
        bool kick = type == WptTacticsKick;
        string title = SvrMsg(kick ? MsgTacticsKickTitle : MsgTacticsEndTitle);
        string message = FormatSvrMsg(SvrMsg(kick ? MsgTacticsKickMessage : MsgTacticsEndMessage), value);
        var (gold, silver, cooper) = SplitMoney(money);
        await MailFrom(senderId, sender, recvId, recver, title, message, PostNormal, gold, silver, cooper);
    }

    private static (uint Gold, uint Silver, uint Cooper) SplitMoney(long money)
        => ((uint)(money / 1000 / 1000), (uint)(money / 1000 % 1000), (uint)(money % 1000));   // CalcMoney, MONEY_MULTIPLY

    // ================================ the castle guards' shop ================================

    /// <summary>The guard posts a <c>TNPC_MONSTER</c> sells (C++ <c>CTNpc::m_mapMon</c>): those whose spawn exists.</summary>
    private void InitMonsterShop(Npc npc)
    {
        foreach (var row in _templates.MonsterShops)
            if (row.NpcId == npc.Id && !npc.Monsters.ContainsKey(row.Id) && SpawnById(row.SpawnId) is not null) npc.Monsters[row.Id] = row;
    }

    /// <summary>The <c>TNPC_MONSTER</c> branch of <c>SendCS_NPCITEMLIST_ACK</c>: every guard post and its price.</summary>
    private void SendCS_NPCMONSTERLIST_ACK(ClientSession s, Character ch, Npc npc)
    {
        var list = new SortedDictionary<ushort, uint>();
        foreach (var (id, row) in npc.Monsters) list[id] = row.Price;
        SendNpcList(s, ch, npc, list);
    }

    /// <summary>C++ <c>OnCS_MONSTERBUY_REQ</c>: a guild officer, an NPC it may talk to, a post it sells — of a tower its side holds —
    /// whose guards are not out. The world takes the price from the guild's money.</summary>
    private void OnCS_MONSTERBUY_REQ(ClientSession s, PacketReader r)
    {
        if (!s.IsMain || s.Char is not { } ch || IsTutorial(ch)) return;
        ushort npcId = r.ReadUInt16(), id = r.ReadUInt16();
        byte ret = CheckMonsterBuy(s, ch, npcId, id, out var row);
        if (ret != MsbSuccess) { SendByte(s, Msg.CS_MONSTERBUY_ACK, ret); return; }
        SendToWorld(Msg.MW_MONSTERBUY_ACK, s, w => { w.WriteUInt16(npcId); w.WriteUInt16(id); w.WriteUInt32(row!.Price); });
    }

    private byte CheckMonsterBuy(ClientSession s, Character ch, ushort npcId, ushort id, out MonsterShopRow? row)
    {
        row = null;
        if (ch.GuildDuty == GuildDutyNone) return MsbAuthority;
        if (_state.FindNpc(npcId) is not { } npc || !CanTalk(npc, ch)) return MsbInvalidNpc;
        if (!npc.Monsters.TryGetValue(id, out row)) return MsbNotFound;
        if (row.TowerId != 0 && (CastleOfMap(ch.MapId)?.War?.Towers.GetValueOrDefault(row.TowerId)?.Ball is not { } ball || ball.Camp != ch.Camp))
            return MsbCampMismatch;
        var spawnId = row.SpawnId;
        if (_spawns.Any(p => p.Def.Spawn.Id == spawnId && p.Channel == s.Channel && p.Slots.Any(sl => sl.Live is not null))) return MsbAlready;
        return MsbSuccess;
    }

    /// <summary>C++ <c>OnMW_MONSTERBUY_REQ</c>: paid, the guards come out (of the buyer's country, for good); if they cannot, the
    /// money goes back to the guild.</summary>
    private void OnMW_MONSTERBUY_REQ(PacketReader r)
    {
        byte ret = r.ReadByte();
        uint charId = r.ReadUInt32(), key = r.ReadUInt32(), guildId = r.ReadUInt32();
        ushort npcId = r.ReadUInt16(), id = r.ReadUInt16();
        uint price = r.ReadUInt32();
        void Recover() => _world.Send(new PacketWriter(Msg.MW_GUILDMONEYRECOVER_ACK, capacity: 8).WriteUInt32(guildId).WriteUInt32(price));
        if (FindPlayer(charId, key) is not { Char: { } ch } s)
        {
            if (ret == MsbSuccess) Recover();
            return;
        }
        if (ret != MsbSuccess) { SendByte(s, Msg.CS_MONSTERBUY_ACK, ret); return; }
        byte check = CheckMonsterBuy(s, ch, npcId, id, out var row);
        if (check == MsbAuthority) check = MsbSuccess;                     // (the C++ does not ask again)
        if (check != MsbSuccess)
        {
            Recover();
            if (check != MsbNotFound) SendByte(s, Msg.CS_MONSTERBUY_ACK, check);
            return;
        }
        AddTimelimitedMon(row!.SpawnId, s.Channel, regenType: 0 /* RT_ETERNAL */, NowMs, ch.Country);
        SendByte(s, Msg.CS_MONSTERBUY_ACK, MsbSuccess);
    }

    /// <summary>C++ <c>TGODTOWER::m_vSpawnID</c>: the guards a tower's posts put out — removed when its ball is knocked off.</summary>
    private void DelTowerGuards(Territory c, ushort towerId, byte channel)
    {
        foreach (var npc in _state.AllNpcs())
            foreach (var row in npc.Monsters.Values)
                if (row.TowerId == towerId && SpawnById(row.SpawnId) is { } def && def.Spawn.MapId == c.Zone.MapId)
                    DelMonSpawn(row.SpawnId, channel);
    }
}
