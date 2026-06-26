using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// SM server-to-server plane (the parts not already covered by the direct timer/disconnect ticks) plus the
/// guild auto-extinction timer it drives.
///
/// In C++ a disbanding guild's id+time is held in <c>m_mapTGuildEx</c> — seeded at startup and on the disorg
/// toggle, kept in sync across world instances by <c>SM_GUILDDISORGANIZATION_REQ</c> — and a per-tick
/// <c>CheckTGuildExtinction</c> deletes any guild whose disband grace (<c>GUILD_EXTINC_DURATION</c> = 7 days)
/// has elapsed (DB <c>TGuildDelete</c> + in-memory <c>DeleteTGuild</c>). This port drives the same extinction
/// directly off the live <see cref="Guild.Disorg"/>/<see cref="Guild.Time"/> fields (already maintained by the
/// disorg handler + startup load), so no separate map is needed; the SM handler still applies the
/// cross-instance disorg state for completeness.
/// </summary>
public sealed partial class WorldService
{
    private const long GuildExtincDuration = 86400L * 7; // GUILD_EXTINC_DURATION
    private int _extinctionTick;

    private bool DispatchSm(ServerSession session, PacketReader r)
    {
        switch (r.Id)
        {
            case Msg.SM_GUILDDISORGANIZATION_REQ: OnSM_GUILDDISORGANIZATION_REQ(r); return true;
            case Msg.SM_EVENTQUARTER_REQ: OnSM_EVENTQUARTER_REQ(r); return true;
            case Msg.SM_EVENTQUARTERNOTIFY_REQ: OnSM_EVENTQUARTERNOTIFY_REQ(r); return true;
            case Msg.SM_EVENTEXPIRED_REQ: OnSM_EVENTEXPIRED_REQ(r); return true;
            case Msg.SM_EVENTEXPIRED_ACK: OnSM_EVENTEXPIRED_ACK(r); return true;
        }
        return false;
    }

    /// <summary>Cross-instance guild-disband-timer sync: set/clear a guild's disband state. C++
    /// OnSM_GUILDDISORGANIZATION_REQ (maintains m_mapTGuildEx; here it updates the guild's live disorg fields).</summary>
    private void OnSM_GUILDDISORGANIZATION_REQ(PacketReader r)
    {
        uint guildId = r.ReadUInt32();
        uint time = r.ReadUInt32();
        byte disorg = r.ReadByte();

        if (_state.FindGuild(guildId) is not { } guild) return;
        guild.Disorg = disorg;
        guild.Time = disorg != 0 ? time : 0;
    }

    /// <summary>Auto-extinct guilds whose disband grace period has elapsed. C++ CheckTGuildExtinction, driven
    /// here off the live disorg fields. Throttled to ~once a minute (the C++ runs it each tick; the result is
    /// identical at 7-day granularity).</summary>
    public async Task CheckGuildExtinctionAsync()
    {
        if (++_extinctionTick < 60) return;
        _extinctionTick = 0;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var expired = _state.Guilds.Values
            .Where(g => g.Disorg != 0 && g.Time != 0 && now - g.Time > GuildExtincDuration)
            .ToList();
        foreach (var g in expired) await DeleteGuildAsync(g);
    }

    /// <summary>Disband a guild: notify + unlink every member (GUILD_LEAVE_DISORGANIZATION) and tactics member,
    /// remove it from state, forward to the relay, and best-effort delete it in the DB. C++ DeleteTGuild +
    /// OnDM_GUILDEXTINCTION (CSPGuildDelete).</summary>
    private async Task DeleteGuildAsync(Guild guild)
    {
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var mem in guild.Members.Values.ToList())
        {
            if (mem.OnlineChar is { } ch)
            {
                ch.Guild = null;
                SendToChar(ch, BuildGuildLeaveReq(ch.CharId, ch.Key, mem.Name, (byte)GuildResult.LeaveDisorganization, now));
            }
            _state.CharGuild.Remove(mem.CharId);
        }
        guild.Members.Clear();

        foreach (var tm in guild.Tactics.Values.ToList())
        {
            if (_state.Characters.TryGetValue(tm.CharId, out var tch))
            {
                var w = new PacketWriter(Msg.MW_GUILDTACTICSKICKOUT_REQ);
                w.WriteUInt32(tm.CharId); w.WriteUInt32(tch.Key); w.WriteByte((byte)GuildResult.Success);
                w.WriteUInt32(tm.CharId); w.WriteByte(1);
                SendToChar(tch, w.ToArray());
            }
            _state.CharTactics.Remove(tm.CharId);
        }
        guild.Tactics.Clear();

        _state.Guilds.Remove(guild.Id);
        RelayGuildDel(guild.Chief, guild.Id); // relay visibility index (no-op without a relay peer)

        if (_guildDb is not null)
        {
            try { await _guildDb.DeleteAsync(guild.Id); }
            catch (Exception ex) { _log.LogWarning(ex, "TGuildDelete failed for guild {Id}.", guild.Id); }
        }
        _log.LogInformation("Guild {Id} auto-extincted after disband grace period.", guild.Id);
    }
}
