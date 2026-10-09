using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Guilds, batch G4 — the guild's level and its skills (C++ TGuild.cpp <c>UpdateLevel</c> / <c>GainEXP</c>, SSHandler.cpp
/// <c>OnMW_GUILDSKILLACTION_REQ</c> / <c>OnMW_UPDATEGUILDCOOLDOWN_ACK</c>):
/// <list type="bullet">
/// <item><b>The level</b> is checked when the guild gains exp and when a member joins or goes: one level down when it has
/// fewer members than its level's minimum, one up when its exp reaches the level's and it has the next level's minimum (in
/// this database every level needs 0 exp — the member count decides). The cabinet grows to the level's size.</item>
/// <item><b>Buying a guild skill level</b>: an officer's skill (type 1/2) costs one of the guild's stat points, here; a
/// member's (type 0) one of the character's own, on its map. The answer goes back to the buyer.</item>
/// <item><b>A guild skill renewed, levelled or cast</b> by an officer: every member online re-reads the guild's skills; cast,
/// every map starts the guild's shared cooldown for it (<c>MW_ADDCOOLDOWN_REQ</c>).</item>
/// </list>
/// </summary>
public sealed partial class WorldService
{
    private const byte GsBuy = 0, GsRenew = 1, GsGainExp = 2, GsUseSkill = 3, GsIncreaseLv = 4;   // GUILDSKILL_ACTION

    /// <summary>C++ <c>CTGuild::UpdateLevel</c>: one level down below the minimum members, one up when the exp and the next
    /// level's members are there; the level saved (<c>TGuildLevel</c>) and the cabinet grown (<c>TGuildMaxCabinet</c>).</summary>
    private void UpdateGuildLevel(Guild g)
    {
        if (g.LevelChart is not { } cur) return;
        byte prev = g.Level;
        int members = g.Members.Count;
        if (cur.MinCnt > members) g.Level = (byte)Math.Max(1, g.Level - 1);
        else if (_state.GuildLevelOf((byte)Math.Min(MaxGuildLevel, g.Level + 1)) is { } next && g.Level < MaxGuildLevel
                 && g.Exp >= cur.Exp && members >= next.MinCnt)
            g.Level++;

        if (prev != g.Level)
        {
            if (_state.GuildLevelOf(g.Level) is { } now)
            {
                g.LevelChart = now;
                byte level = g.Level;
                _ = Persist("guildLevel", db => db.LevelAsync(g.Id, level));
            }
            else g.Level = prev;
        }
        if (g.MaxCabinet < g.LevelChart!.CabinetCnt)
        {
            g.MaxCabinet = g.LevelChart.CabinetCnt;
            byte max = g.MaxCabinet;
            _ = Persist("guildMaxCabinet", db => db.MaxCabinetAsync(g.Id, max));
        }
    }

    /// <summary>C++ <c>OnMW_GUILDSKILLACTION_REQ</c>.</summary>
    private async Task OnGuildSkillAction(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte action = r.ReadByte(), size = r.ReadByte();
        var param = new ushort[size];
        for (int i = 0; i < size; i++) param[i] = r.ReadUInt16();
        if (_state.FindChar(charId, key) is not { Guild: { } g } ch) return;

        bool all = action is GsIncreaseLv or GsUseSkill;
        if (action == GsBuy)
        {
            if (size < 2) return;
            if (param[1] != 0)                                                // an officer's skill: the guild's stat point
            {
                if (g.StatPoint == 0) return;
                g.StatPoint--;
            }
        }

        byte[] Ack(Character to)
        {
            var w = new PacketWriter(Msg.MW_GUILDSKILLACTION_ACK);
            w.WriteUInt32(to.CharId); w.WriteUInt32(to.Key); w.WriteByte(action); w.WriteByte(size);
            foreach (var p in param) w.WriteUInt16(p);
            return w.ToArray();
        }
        if (all) { foreach (var m in g.Members.Values) if (m.OnlineChar is { } mc) SendToChar(mc, Ack(mc)); }
        else SendToChar(ch, Ack(ch));
        await SaveGuildStats(g);
    }

    /// <summary>C++ <c>OnMW_UPDATEGUILDCOOLDOWN_ACK</c>: every member online re-reads the guild's skills; a skill cast (not a
    /// renewal) starts the guild's cooldown for it on every map.</summary>
    private void OnUpdateGuildCooldown(PacketReader r)
    {
        uint guildId = r.ReadUInt32();
        ushort skillId = r.ReadUInt16();
        byte level = r.ReadByte();
        uint renew = r.ReadUInt32(), use = r.ReadUInt32();                 // BOOL
        if (_state.FindGuild(guildId) is not { } g) return;
        foreach (var m in g.Members.Values)
        {
            if (m.OnlineChar is not { } mc) continue;
            var w = new PacketWriter(Msg.MW_UPDATEGUILDCOOLDOWN_REQ);
            w.WriteUInt32(mc.CharId); w.WriteUInt32(mc.Key); w.WriteUInt16(skillId); w.WriteByte(level); w.WriteUInt32(renew); w.WriteUInt32(use);
            SendToChar(mc, w.ToArray());
        }
        if (use != 0 && renew == 0)
        {
            var cd = new PacketWriter(Msg.MW_ADDCOOLDOWN_REQ);
            cd.WriteUInt32(guildId); cd.WriteUInt16(skillId);
            BroadcastServers(cd.ToArray());
        }
    }
}
