using TWorld.Protocol;
using TWorld.Server.Net;

namespace TWorld.Server.World;

/// <summary>
/// Phase 5e — PvP scoring, ported from <c>SSHandler.cpp</c> (OnMW_GAINPVPPOINT_ACK / OnMW_LOCALRECORD_ACK)
/// and <c>TGuild.cpp</c> (GainPvPoint/UsePvPoint). GAINPVPPOINT either forwards a character's point gain to
/// its main, or accrues/spends a guild's PvP points. LOCALRECORD is the post-war per-member kill/die/point
/// report: it appends to each member's daily record (handling tactics mercenaries who score for their real
/// guild), refreshes the rolling week record, persists, and distributes the Broa "aid" bonus. These records
/// are what the castle-war scoreboard ranking and the week-record recompute consume.
/// </summary>
public sealed partial class WorldService
{
    private bool DispatchPvp(ServerSession session, PacketReader r, byte[] packet)
    {
        switch (r.Id)
        {
            case Msg.MW_GAINPVPPOINT_ACK: OnMW_GAINPVPPOINT_ACK(r); return true;
            case Msg.MW_LOCALRECORD_ACK: OnMW_LOCALRECORD_ACK(r); return true;
        }
        return false;
    }

    /// <summary>PvP points were gained: a character owner gets the gain forwarded to its main; a guild owner
    /// accrues (or spends) guild PvP points. C++ OnMW_GAINPVPPOINT_ACK.</summary>
    private void OnMW_GAINPVPPOINT_ACK(PacketReader r)
    {
        byte ownerType = r.ReadByte();
        uint ownerId = r.ReadUInt32();
        uint point = r.ReadUInt32();
        byte evt = r.ReadByte();
        byte type = r.ReadByte();
        byte gain = r.ReadByte();
        string name = r.ReadString();
        byte cls = r.ReadByte();
        byte level = r.ReadByte();

        if (ownerType == Proto.TownerChar)
        {
            if (_state.Characters.TryGetValue(ownerId, out var ch))
            {
                var w = new PacketWriter(Msg.MW_GAINPVPPOINT_REQ);
                w.WriteUInt32(ownerId); w.WriteUInt32(point); w.WriteByte(evt); w.WriteByte(type); w.WriteByte(gain);
                w.WriteString(name); w.WriteByte(cls); w.WriteByte(level);
                _state.FindMapSvr(ch.MainId)?.Send(w.ToArray());
            }
            return;
        }

        var guild = _state.FindGuild(ownerId);
        if (guild is null) return;
        if (gain != 0) GuildGainPvP(guild, point, type);
        else GuildUsePvP(guild, point, type);
    }

    /// <summary>Post-war per-member kill/die/point records. C++ OnMW_LOCALRECORD_ACK.</summary>
    private void OnMW_LOCALRECORD_ACK(PacketReader r)
    {
        uint winGuildId = r.ReadUInt32();
        uint guildPoint = r.ReadUInt32();
        ushort guildCount = r.ReadUInt16();
        uint date = CurrentDay();

        // NOTE: faithful to the original, aidGuild is declared once and not cleared between guilds, so a
        // later warring guild re-distributes earlier guilds' accumulated aid. In practice LOCALRECORD carries
        // a single warring guild, so this latent quirk is inert.
        var aidGuild = new Dictionary<uint, uint>();

        for (ushort gi = 0; gi < guildCount; gi++)
        {
            uint guildId = r.ReadUInt32();
            ushort recordCount = r.ReadUInt16();
            var guild = _state.FindGuild(guildId);

            for (ushort ri = 0; ri < recordCount; ri++)
            {
                uint charId = r.ReadUInt32();
                ushort kill = r.ReadUInt16();
                ushort die = r.ReadUInt16();

                bool isTactics = false;
                GuildMember? member = guild?.FindMember(charId);
                TacticsMember? tactics = null;
                Guild? memberGuild = member is not null ? guild : null;
                GuildPvpRecord? prec = null;

                if (member is not null)
                {
                    prec = TodayRecord(member, charId, date);
                    prec.KillCount = (ushort)(prec.KillCount + kill);
                    prec.DieCount = (ushort)(prec.DieCount + die);
                }
                else if (guild is not null)
                {
                    tactics = guild.FindTactics(charId);
                    if (tactics is not null)
                    {
                        isTactics = true;
                        if (tactics.OnlineChar?.Guild is { } realGuild)
                        {
                            member = realGuild.FindMember(charId);
                            if (member is not null)
                            {
                                memberGuild = realGuild;
                                prec = TodayRecord(member, charId, date);
                                prec.KillCount = (ushort)(prec.KillCount + kill);
                                prec.DieCount = (ushort)(prec.DieCount + die);
                            }
                        }
                    }
                }

                for (int e = 0; e < Proto.PvpeCount; e++)
                {
                    uint pt = r.ReadUInt32();
                    if (prec is not null && (e != Proto.PvpeEntry || _state.Characters.ContainsKey(charId)))
                    {
                        prec.Point[e] += pt;
                        if (tactics is not null) tactics.GainPoint += pt;
                    }
                }

                if (member is not null)
                {
                    bool recompute = isTactics
                        ? (tactics is not null && tactics.CharId == member.CharId && member.OnlineChar?.Guild is not null)
                        : guild is not null;
                    if (recompute) member.RecalcWeekRecord(date);
                    if (memberGuild is not null && prec is not null) PersistPvpRecord(memberGuild, member, prec);
                }

                if (winGuildId == guildId && tactics?.OnlineChar?.Guild is { Country: (byte)Contry.Broa } tg)
                    aidGuild[tg.Id] = aidGuild.TryGetValue(tg.Id, out var c) ? c + 1 : 1;
            }

            foreach (var (aidId, count) in aidGuild)
                if (_state.FindGuild(aidId) is { } tacticsGuild)
                    GuildGainPvP(tacticsGuild, Math.Min(guildPoint, guildPoint * count / 20), (byte)(Proto.PvpUseable | Proto.PvpTotal));
        }
    }

    // ===== helpers =====

    private void GuildGainPvP(Guild g, uint point, byte type)
    {
        g.GainPvPoint(point, type);
        RecalcGuildRanking();
        if (_guildDb is not null) _ = _guildDb.SaveGuildPvPointAsync(g.Id, g.PvPTotalPoint, g.PvPUseablePoint, g.PvPMonthPoint);
    }

    private void GuildUsePvP(Guild g, uint point, byte type)
    {
        g.UsePvPoint(point, type);
        RecalcGuildRanking();
        if (_guildDb is not null) _ = _guildDb.SaveGuildPvPointAsync(g.Id, g.PvPTotalPoint, g.PvPUseablePoint, g.PvPMonthPoint);
    }

    /// <summary>Recompute every guild's total/month PvP rank (1 + number of guilds with strictly more points;
    /// guilds with no points are unranked). C++ CTWorldSvrModule::CalcGuildRanking — run daily there (on
    /// SM_CHANGEDAY); here it's kept current on each PvP-point change so guild-info always shows the right rank.</summary>
    private void RecalcGuildRanking()
    {
        var guilds = _state.Guilds.Values;
        foreach (var g in guilds)
        {
            g.RankTotal = 0;
            g.RankMonth = 0;
            if (g.PvPTotalPoint == 0 && g.PvPMonthPoint == 0) continue;

            foreach (var c in guilds)
            {
                if (c.PvPTotalPoint == 0 && c.PvPMonthPoint == 0) continue;
                if (g.PvPTotalPoint != 0 && g.PvPTotalPoint < c.PvPTotalPoint) g.RankTotal++;
                if (g.PvPMonthPoint != 0 && g.PvPMonthPoint < c.PvPMonthPoint) g.RankMonth++;
            }

            if (g.PvPTotalPoint != 0) g.RankTotal++;
            if (g.PvPMonthPoint != 0) g.RankMonth++;
        }
    }

    private void PersistPvpRecord(Guild guild, GuildMember member, GuildPvpRecord rec)
    {
        if (_guildDb is not null)
            _ = _guildDb.SaveGuildPvPRecordAsync(guild.Id, member.CharId, rec.Date, rec.KillCount, rec.DieCount, rec.Point);
    }

    /// <summary>The member's record for today: extend the most recent if it's already today's, else append a
    /// fresh one. Mirrors the per-day record append in OnMW_LOCALRECORD_ACK.</summary>
    private static GuildPvpRecord TodayRecord(GuildMember member, uint charId, uint date)
    {
        if (member.Records.Count > 0 && member.Records[^1].Date == date) return member.Records[^1];
        var rec = new GuildPvpRecord { CharId = charId, Date = date };
        member.Records.Add(rec);
        return rec;
    }

    /// <summary>Current day number (unix seconds / 86400), matching C++ <c>m_timeCurrent / DAY_ONE</c>.</summary>
    private static uint CurrentDay() => (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Proto.DayOne);
}
