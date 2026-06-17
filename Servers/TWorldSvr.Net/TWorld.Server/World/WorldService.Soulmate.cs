using Microsoft.Extensions.Logging;
using TWorld.Protocol;

namespace TWorld.Server.World;

/// <summary>
/// Phase 3 — the soulmate subsystem, ported from <c>SSHandler.cpp</c> (OnMW_SOULMATE* + the OnDM_SOULMATE*
/// DB steps folded inline) + <c>TWorldSvr.cpp</c> (RegSoulmate/SoulmateEnd/SoulmateDel/CheckSoulmateEnd/
/// LeaveSoulmate). The C++ DB-thread round-trips are collapsed into a single best-effort DB call here.
/// </summary>
public sealed partial class WorldService
{
    private static uint Now() => (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private async Task<bool> DispatchSoulmateAsync(ServerSession session, PacketReader r)
    {
        switch (r.Id)
        {
            case Msg.MW_SOULMATESEARCH_ACK: await OnSoulmateSearch(session, r); return true;
            case Msg.MW_SOULMATEREG_ACK: await OnSoulmateReg(session, r); return true;
            case Msg.MW_SOULMATEEND_ACK: await OnSoulmateEnd(session, r); return true;
            default: return false;
        }
    }

    // ===== startup load (mirrors OnDM_SOULMATELIST_ACK) =====

    private async Task LoadSoulmatesAsync(Character player)
    {
        if (_socialDb is null) return;
        List<Data.SoulmateRow> rows;
        try { rows = await _socialDb.LoadSoulmatesAsync(player.CharId); }
        catch (Exception ex) { _log.LogWarning(ex, "Soulmate list load failed for {Char}.", player.CharId); return; }

        uint now = Now();
        foreach (var row in rows)
        {
            var sm = new Soulmate { CharId = row.CharId, Target = row.Target, Name = row.Name, Level = row.Level, Class = row.Class, Time = row.Time };
            bool silence = sm.Time != 0 && now - sm.Time < Proto.SoulmateSilenceDuration;

            // A finished break (time set, window elapsed) is purged from the DB and dropped.
            if (sm.Time != 0 && !silence)
            {
                await Persist(() => _socialDb!.SoulmateDelAsync(sm.CharId, sm.Target), "TSoulmateDel");
                continue;
            }

            if (!silence)
            {
                player.Soulmates[sm.CharId] = sm;
                Character? other = null;
                Soulmate? mine = null;
                if (sm.CharId == player.CharId)
                {
                    other = player; mine = sm;
                    if (_state.Characters.TryGetValue(sm.Target, out var tc)) { sm.Connected = true; sm.Region = tc.Region; }
                }
                else if (_state.Characters.TryGetValue(sm.CharId, out var oc))
                {
                    other = oc; sm.Connected = true; sm.Region = oc.Region;
                    if (oc.Soulmates.TryGetValue(oc.CharId, out var their)) { mine = their; their.Connected = true; their.Region = player.Region; }
                }
                CheckSoulmateEnd(other, mine);
            }

            if (sm.CharId == player.CharId)
            {
                if (silence) player.SoulSilence = sm.Time;
                SendToChar(player, BuildSoulmate(player.CharId, player.Key,
                    silence ? 0 : sm.Target, silence ? "" : sm.Name, silence ? sm.Time : 0));
            }
        }
    }

    // ===== leave (mirrors LeaveSoulmate) =====

    private void LeaveSoulmate(Character ch)
    {
        foreach (var sm in ch.Soulmates.Values)
        {
            if (sm.Connected && sm.CharId != ch.CharId && _state.Characters.TryGetValue(sm.CharId, out var tgt)
                && tgt.Soulmates.TryGetValue(tgt.CharId, out var their))
            {
                their.Connected = false;
                their.Region = 0;
            }
        }
        ch.Soulmates.Clear();
    }

    // ===== handlers =====

    private async Task OnSoulmateSearch(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        byte minLevel = r.ReadByte();
        byte npcInven = r.ReadByte();
        byte npcItem = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null) return;

        byte bestLevel = minLevel;
        var candidates = new List<Character>();
        foreach (var other in _state.Characters.Values)
        {
            if (other.CharId == charId || other.Country != ch.Country) continue;
            if (Math.Abs(ch.Level - other.Level) >= Proto.SoulmateLevel) continue;
            if (bestLevel > other.Level) { candidates.Clear(); candidates.Add(other); bestLevel = other.Level; }
            else if (bestLevel == other.Level) candidates.Add(other);
        }

        if (candidates.Count == 0)
        {
            session.Send(BuildSoulmateSearch(charId, key, (byte)SoulmateResult.NotFound, 0, "", 0, npcInven, npcItem));
            return;
        }

        // Narrow by opposite real sex, then by availability, then by opposite displayed sex.
        Narrow(candidates, c => ch.RealSex != c.RealSex);
        Narrow(candidates, c => !c.Soulmates.TryGetValue(c.CharId, out var s) || s.Target == 0);
        Narrow(candidates, c => ch.Sex != c.Sex);

        await RegSoulmate(ch, candidates[0], search: true, npcInven, npcItem);

        static void Narrow(List<Character> list, Func<Character, bool> pred)
        {
            if (list.Count == 1) return;
            var filtered = list.Where(pred).ToList();
            if (filtered.Count > 0) { list.Clear(); list.AddRange(filtered); }
        }
    }

    private async Task OnSoulmateReg(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        string name = r.ReadString();
        byte reg = r.ReadByte();
        byte npcInven = r.ReadByte();
        byte npcItem = r.ReadByte();

        var ch = _state.FindChar(charId, key);
        if (ch is null) return;

        if (!_state.CharactersByName.TryGetValue(name, out var tgt))
        { session.Send(BuildSoulmateReg(charId, key, (byte)SoulmateResult.NotFound, reg, npcInven, npcItem)); return; }
        if (tgt.Country != ch.Country || Math.Abs(tgt.Level - ch.Level) > Proto.SoulmateLevel)
        { session.Send(BuildSoulmateReg(charId, key, (byte)SoulmateResult.Fail, reg, npcInven, npcItem)); return; }

        if (reg != 0)
        {
            await Persist(() => _socialDb!.SoulmateRegAsync(ch.CharId, tgt.CharId), "TSoulmateReg");
            await RegSoulmate(ch, tgt, search: false, npcInven, npcItem);
        }
        else
        {
            session.Send(BuildSoulmateReg(charId, key, (byte)SoulmateResult.Success, reg, npcInven, npcItem, tgt.CharId, tgt.Name, tgt.Region));
        }
    }

    private async Task OnSoulmateEnd(ServerSession session, PacketReader r)
    {
        uint charId = r.ReadUInt32();
        uint key = r.ReadUInt32();
        var ch = _state.FindChar(charId, key);
        if (ch is null) return;
        if (!ch.Soulmates.ContainsKey(charId))
        { session.Send(BuildSoulmateEnd(charId, key, (byte)SoulmateResult.Fail, 0)); return; }

        uint time = Now();
        await Persist(() => _socialDb!.SoulmateEndAsync(charId, time), "TSoulmateEnd");
        SoulmateEnd(ch, time);
    }

    // ===== helpers (RegSoulmate / SoulmateEnd / SoulmateDel / CheckSoulmateEnd) =====

    private async Task RegSoulmate(Character ch, Character target, bool search, byte npcInven, byte npcItem)
    {
        Soulmate mine;
        if (ch.Soulmates.TryGetValue(ch.CharId, out var existing))
        {
            mine = existing;
            // tear down any prior partner's inbound link
            if (_state.CharactersByName.TryGetValue(mine.Name, out var prev)) prev.Soulmates.Remove(ch.CharId);
        }
        else
        {
            mine = new Soulmate { CharId = ch.CharId };
            ch.Soulmates[ch.CharId] = mine;
        }

        ch.SoulSilence = 0;
        mine.Target = target.CharId; mine.Name = target.Name; mine.Level = target.Level;
        mine.Connected = true; mine.Class = target.Class; mine.Region = target.Region; mine.Time = 0;

        target.Soulmates[ch.CharId] = new Soulmate
        {
            CharId = ch.CharId, Target = ch.CharId, Name = ch.Name, Level = ch.Level,
            Connected = true, Class = ch.Class, Region = ch.Region, Time = 0,
        };

        if (search)
            SendToChar(ch, BuildSoulmateSearch(ch.CharId, ch.Key, (byte)SoulmateResult.Success, target.CharId, target.Name, target.Region, npcInven, npcItem));
        else
            SendToChar(ch, BuildSoulmateReg(ch.CharId, ch.Key, (byte)SoulmateResult.Success, 1, npcInven, npcItem, target.CharId, target.Name, target.Region));

        await Task.CompletedTask;
    }

    private void SoulmateEnd(Character ch, uint time)
    {
        if (!ch.Soulmates.TryGetValue(ch.CharId, out var mine)) return;
        ch.SoulSilence = time;
        if (_state.CharactersByName.TryGetValue(mine.Name, out var soul)) soul.Soulmates.Remove(ch.CharId);
        SendToChar(ch, BuildSoulmateEnd(ch.CharId, ch.Key, (byte)SoulmateResult.Success, time));
        ch.Soulmates.Remove(ch.CharId);
    }

    private async Task SoulmateDel(Character ch, uint soul)
    {
        if (!ch.Soulmates.TryGetValue(ch.CharId, out var mine) || mine.Target != soul) return;
        if (_state.CharactersByName.TryGetValue(mine.Name, out var partner)) partner.Soulmates.Remove(ch.CharId);
        ch.Soulmates.Remove(ch.CharId);
        SendToChar(ch, BuildSoulmateEnd(ch.CharId, ch.Key, (byte)SoulmateResult.Success, 0));
        await Task.CompletedTask;
    }

    /// <summary>If an active pair drifted past the level gap, kick off the break (mirrors CheckSoulmateEnd).</summary>
    private void CheckSoulmateEnd(Character? ch, Soulmate? mine)
    {
        if (ch is null || mine is null || mine.Time != 0) return;
        if (Math.Abs(ch.Level - mine.Level) > Proto.SoulmateLevel)
        {
            uint time = Now();
            _ = Persist(() => _socialDb!.SoulmateEndAsync(ch.CharId, time), "TSoulmateEnd");
            SoulmateEnd(ch, time);
        }
    }

    // ===== senders =====

    private static byte[] BuildSoulmate(uint charId, uint key, uint soulId, string name, uint time)
    { var w = new PacketWriter(Msg.MW_SOULMATE_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteUInt32(soulId); w.WriteString(name); w.WriteUInt32(time); return w.ToArray(); }

    private static byte[] BuildSoulmateSearch(uint charId, uint key, byte result, uint soulId, string name, uint region, byte npcInven, byte npcItem)
    {
        var w = new PacketWriter(Msg.MW_SOULMATESEARCH_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(result); w.WriteUInt32(soulId);
        w.WriteString(name); w.WriteUInt32(region); w.WriteByte(npcInven); w.WriteByte(npcItem);
        return w.ToArray();
    }

    private static byte[] BuildSoulmateReg(uint charId, uint key, byte result, byte reg, byte npcInven, byte npcItem,
        uint soulId = 0, string? name = null, uint region = 0)
    {
        var w = new PacketWriter(Msg.MW_SOULMATEREG_REQ);
        w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(result); w.WriteByte(reg);
        w.WriteByte(npcInven); w.WriteByte(npcItem);
        w.WriteUInt32(soulId); w.WriteString(name ?? ""); w.WriteUInt32(region);
        return w.ToArray();
    }

    private static byte[] BuildSoulmateEnd(uint charId, uint key, byte result, uint time)
    { var w = new PacketWriter(Msg.MW_SOULMATEEND_REQ); w.WriteUInt32(charId); w.WriteUInt32(key); w.WriteByte(result); w.WriteUInt32(time); return w.ToArray(); }
}
