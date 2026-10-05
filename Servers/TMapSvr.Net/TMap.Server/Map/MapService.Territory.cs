using Microsoft.Extensions.Logging;
using TMap.Data;
using TMap.Protocol;

namespace TMap.Server.Map;

/// <summary>
/// Territory battles, the foundation (batch A) — the C++ <c>TLOCAL</c> model for the forts (locals), castles, mission areas
/// and the sky garden, and what every one of their wars rests on:
/// <list type="bullet">
/// <item><b>The territories</b> (C++ <c>LoadData</c>, TMapSvr.cpp:3052): built from <c>TBATTLEZONECHART</c> and the territory
/// tables at bring-up. All maps are hosted here, so castles, missions and the sky garden are valid; a fort only when its boss
/// spawn exists (C++ <c>InitEnvironment</c>).</item>
/// <item><b>Entering one</b> (<c>CS_REGION_REQ</c>, CSHandler.cpp:10133): the client names the territory it walks into. Its own
/// country gets the zone's two buffs there (they come off on leaving, and are not saved); the equipment is reworked at the
/// zone's item level cap (<c>EquipItemRevision</c>, <c>CS_ITEMLEVELREVISION_ACK</c>). The world learns the region.</item>
/// <item><b>Peace zones</b> (C++ <c>CheckPeaceZone</c>): no hostile skill from, or on a player in, a territory that cannot battle
/// (a castle outside its war, a fort in the 3 minutes after its war, the sky garden out of its war).</item>
/// <item><b>The war phases</b> the world sends (<c>MW_LOCALENABLE_REQ</c> / <c>CASTLEENABLE</c> / <c>MISSIONENABLE</c> /
/// <c>SKYGARDENENABLE</c>): each territory's status and whether it can battle, the next war times, and the war news to every
/// player (<c>CS_SYSTEMMSG_ACK</c>). On the world's first enable the castles' week of fort owners goes to its scoreboard
/// (<c>MW_CASTLEWARINFO_ACK</c>).</item>
/// <item><b>The war-info window</b> (<c>CS_GUILDLOCALLIST_REQ</c>, with the BoW / BR status from the world), and the world's castle
/// news it shows: the scoreboard (<c>MW_CASTLEWARINFO_REQ</c>), a GM's defender / attacker change, the applicant counts, the
/// heroes.</item>
/// <item><b>Castle and camp</b> (from the world's character info) in <c>CS_ENTER_ACK</c>, for a player inside a castle or the sky
/// garden. <b>Fixed C++ bug:</b> the C++ wrote the <i>receiver's</i> castle there; this writes the entering player's.</item>
/// </list>
/// <para><b>Not yet (the next batches):</b> what a war does on its map — spawns, gate switches, gatekeepers and bosses, captures
/// (<c>*OCCUPY</c>), rewards, PvP points and records, castle sign-up and entry, god balls and towers, the sky garden's capture
/// points — and the discounts. Only players' own territory is tracked (not summons' or monsters').</para>
/// </summary>
public sealed partial class MapService
{
    // BATTLE_STATUS (NetCode.h:2030), TSYSMSG_TYPE (NetCode.h:2277), CAMP
    private const byte BsNormal = 0, BsBattle = 1, BsPeace = 4, BsSkyGardenStart = 8;
    private const byte SmBattleNormal = 1, SmBattleStart = 2, SmBattleStartAlarm = 3, SmBattleEndAlarm = 4, SmBattlePeace = 5,
        SmCastleNormal = 11, SmCastleStart = 12, SmCastleStartAlarm = 13, SmCastleEndAlarm = 14, SmCastlePeace = 15,
        SmMissionStart = 21, SmMissionStartAlarm = 23, SmMissionEndAlarm = 24, SmMissionPeace = 25,
        SmSkyGardenNormal = 29, SmSkyGardenStart = 30, SmSkyGardenStartAlarm = 31, SmSkyGardenEndAlarm = 32, SmSkyGardenPeace = 33;
    private const byte CampDefend = 1, CampAttack = 2;
    private const long WeekOne = 7 * 86400;

    private readonly Dictionary<ushort, Territory> _territories = new();
    private readonly List<Territory> _occupations = new(), _castles = new(), _missions = new(), _skyGardens = new();
    // The C++ handlers' static bCurStatus / dwCurStart: the last phase each war type was in, for its news.
    private byte _localPhase = BsNormal, _castlePhase = BsNormal, _missionPhase = BsNormal, _skyPhase = BsNormal;
    private uint _missionStart;
    private bool _castleWarInfoSent;

    /// <summary>The territory a player stands in (C++ <c>m_pLocal</c>), or null.</summary>
    private Territory? LocalOf(Character ch) => ch.LocalId != 0 && _territories.TryGetValue(ch.LocalId, out var t) && t.Valid ? t : null;

    public IReadOnlyDictionary<ushort, Territory> Territories => _territories;

    /// <summary>C++ <c>LoadData</c> + <c>InitEnvironment</c>: the territories from their tables. Idempotent.</summary>
    public void InitTerritories()
    {
        _territories.Clear(); _occupations.Clear(); _castles.Clear(); _missions.Clear(); _skyGardens.Clear();
        var spawnIds = _templates.MonsterSpawns.Select(s => s.Spawn.Id).ToHashSet();
        foreach (var row in _templates.Territories)
        {
            if (!_templates.BattleZones.TryGetValue(row.Id, out var zone)) continue;
            var t = new Territory
            {
                Type = row.Type, Id = row.Id, Zone = zone, Country = row.Country, Guild = row.Guild, GuildName = row.GuildName,
                TimeOccupied = row.Occupied, NextDefend = row.NextDefend, Hero = row.Hero, HeroTime = row.HeroTime,
                CanBattle = row.Type != LocalType.Castle,
                Valid = row.Type != LocalType.Occupation || spawnIds.Contains(zone.BossSpawnId),
            };
            if (row.Type == LocalType.SkyGarden) t.LeftOwner = t.MiddleOwner = t.RightOwner = CampDefend;
            _territories[t.Id] = t;
            (row.Type switch
            {
                LocalType.Occupation => _occupations, LocalType.Castle => _castles, LocalType.Mission => _missions, _ => _skyGardens,
            }).Add(t);
            if (row.Type == LocalType.Occupation && _territories.TryGetValue(zone.Castle, out var castle) && castle.Type == LocalType.Castle)
                castle.Locals.Add(t);
        }
        foreach (var castle in _castles) castle.Locals.Sort((a, b) => a.Id.CompareTo(b.Id));   // C++ std::map order
        foreach (var o in _templates.LocalOccupy)
            if (o.Day > 0 && o.Day <= 7 && _territories.TryGetValue(o.LocalId, out var t))
            {
                t.OccupyGuild[o.Day - 1] = o.Guild;
                t.OccupyType[o.Day - 1] = o.Type;
            }
        if (_territories.Count > 0)
            _log.LogInformation("Initialized {Count} territories ({Forts} forts, {Castles} castles, {Missions} missions, {Sky} sky garden).",
                _territories.Count, _occupations.Count, _castles.Count, _missions.Count, _skyGardens.Count);
    }

    // ================================ entering a territory ================================

    /// <summary>C++ <c>OnCS_REGION_REQ</c> (CSHandler.cpp:10133), for the player itself: the new region, and on a new territory
    /// the old one's buffs and item cap undone, the new one's applied; then the world told the region.</summary>
    private void OnCS_REGION_REQ(ClientSession s, PacketReader r)
    {
        uint id = r.ReadUInt32();
        byte type = r.ReadByte();
        r.ReadByte();                                                   // bChannel
        r.ReadUInt16();                                                 // wMapID
        uint region = r.ReadUInt32();
        ushort localId = r.Remaining >= 2 ? r.ReadUInt16() : (ushort)0;
        if (type != OtPc || !s.IsMain || s.Char is not { } ch || id != ch.CharId) return;

        ch.RegionId = region;
        if (ch.LocalId != localId)
        {
            var next = localId != 0 && _territories.TryGetValue(localId, out var t) && t.Valid ? t : null;
            if (LocalOf(ch) is { } cur)
            {
                EraseMaintainById(s, ch, cur.Zone.Skill1);
                EraseMaintainById(s, ch, cur.Zone.Skill2);
                if (EquipItemRevision(s, ch, false, cur.Zone.ItemLevel)) SendCS_ITEMLEVELREVISION_ACK(s, 0);
            }
            if (next is not null)
            {
                if (next.Country == WarCountryOf(ch))
                {
                    ForceMaintain(s, ch, next.Zone.Skill1, ch.CharId, OtPc, ch.CharId, OtPc, 0);
                    ForceMaintain(s, ch, next.Zone.Skill2, ch.CharId, OtPc, ch.CharId, OtPc, 0);
                }
                else
                {
                    EraseMaintainById(s, ch, next.Zone.Skill1);
                    EraseMaintainById(s, ch, next.Zone.Skill2);
                }
                if (EquipItemRevision(s, ch, true, next.Zone.ItemLevel)) SendCS_ITEMLEVELREVISION_ACK(s, next.Zone.ItemLevel);
            }
            ch.LocalId = localId;
            ch.ZoneSkills = next is null ? default : (next.Zone.Skill1, next.Zone.Skill2);
        }
        var w = new PacketWriter(Msg.MW_REGION_ACK);
        w.WriteUInt32(ch.CharId); w.WriteUInt32(s.Key); w.WriteUInt32(region);
        _world.Send(w);
    }

    /// <summary>C++ <c>EquipItemRevision</c> (TMapSvr.cpp): every equipped item's attributes at <c>min(cap, its level)</c> (or back
    /// at its own level), then the new stat sheet. No cap ⇒ nothing.</summary>
    private bool EquipItemRevision(ClientSession s, Character ch, bool revise, byte cap)
    {
        if (cap == 0) return false;
        if (ch.Equipped is { } equip)
            foreach (var it in equip.Items) LinkItemAttrAt(it, revise ? Math.Min(cap, it.Level) : it.Level);
        SendCS_CHARSTATINFO_ACK(s, ch);
        return true;
    }

    private static void SendCS_ITEMLEVELREVISION_ACK(ClientSession s, byte level)
    {
        var w = new PacketWriter(Msg.CS_ITEMLEVELREVISION_ACK, capacity: 4);
        w.WriteByte(level);
        s.Send(w);
    }

    /// <summary>The zone buffs a player keeps only inside its territory (C++ <c>SendDM_SAVECHAR_REQ</c> leaves them out).</summary>
    public static bool IsZoneBuff(Character ch, ushort skillId)
        => skillId != 0 && (ch.ZoneSkills.Skill1 == skillId || ch.ZoneSkills.Skill2 == skillId);

    // ================================ peace zones ================================

    /// <summary>C++ <c>CheckPeaceZone</c> (TMapSvr.cpp:11602), the territory part: in a territory that cannot battle, or in the sky
    /// garden's map out of its war. (Tournament lounge, arena and meeting room are not ported.)</summary>
    private bool CheckPeaceZone(Character ch)
        => LocalOf(ch) is { CanBattle: false }
           || _skyGardens.Any(g => g.Valid && g.Zone.MapId == ch.MapId && !g.CanBattle);

    /// <summary>The defender side (CSHandler.cpp:1683 / 20552): a hostile skill does not land on a player standing in a territory
    /// that cannot battle (unless it is the caster itself).</summary>
    private bool InPeaceTerritory(Character target) => LocalOf(target) is { CanBattle: false };

    // ================================ the war phases ================================

    /// <summary>C++ <c>OnMW_LOCALENABLE_REQ</c> (SSHandler.cpp:11194): the forts' next war (and, on the first one, the castles'
    /// war day and next war, a fort's war moved off the castle's day); their status and whether they can battle; the news.</summary>
    private void OnMW_LOCALENABLE_REQ(PacketReader r)
    {
        byte status = r.ReadByte();
        uint second = r.ReadUInt32(), localStart = r.ReadUInt32();
        byte castleDay = r.ReadByte();
        uint castleStart = r.ReadUInt32();
        var now = LocalNow();
        uint clt = (uint)now.TimeOfDay.TotalSeconds;
        long unix = UnixNow();
        foreach (var t in _occupations)
        {
            if (localStart != 0)
                t.NextDefend = clt > localStart ? unix + (86400 - clt) + localStart : unix + (localStart - clt);
            if (castleDay != 0 && _territories.TryGetValue(t.Zone.Castle, out var castle) && castle.Type == LocalType.Castle)
            {
                castle.Day = castleDay;
                uint start = (uint)((castleDay - 1) * 86400) + castleStart;
                uint csw = (uint)((int)now.DayOfWeek * 86400) + clt;
                castle.NextDefend = csw > start ? unix + (WeekOne - csw) + start : unix + (start - csw);
                if (LocalDayOf(t.NextDefend) == LocalDayOf(castle.NextDefend)) t.NextDefend += 86400;
            }
            if (!t.Valid) { t.Status = status; continue; }
            if (t.Status == status) continue;
            t.Status = status;
            switch (status)
            {
                case BsNormal: t.CanBattle = true; break;
                case BsBattle: t.CanBattle = true; t.GateOpened = false; break;
                case BsPeace: t.Occupied = false; t.CanBattle = false; t.GateOpened = true; break;
            }
        }
        byte msg = status switch
        {
            BsNormal => _localPhase == BsNormal ? SmBattleStartAlarm : _localPhase == BsPeace ? SmBattleNormal : (byte)0,
            BsBattle => _localPhase == BsBattle ? SmBattleEndAlarm : _localPhase == BsNormal ? SmBattleStart : (byte)0,
            BsPeace => _localPhase == BsBattle ? SmBattlePeace : (byte)0,
            _ => 0,
        };
        NotifyLocalInfo(msg, 0, second);
        _localPhase = status;
        if (castleDay != 0) SendCastleWarInfo();
    }

    /// <summary>C++ <c>OnMW_CASTLEENABLE_REQ</c> (SSHandler.cpp:12048): a castle in peace waits for its normal; status and battle; news.</summary>
    private void OnMW_CASTLEENABLE_REQ(PacketReader r)
    {
        byte status = r.ReadByte();
        uint second = r.ReadUInt32();
        foreach (var c in _castles)
        {
            if (c.Status == BsPeace && status != BsNormal) continue;
            byte prev = c.Status;
            c.Status = status;
            if (!c.Valid || prev == status) continue;
            if (status == BsBattle)
            {
                c.CanBattle = true;
                if (c.DefGuildId == 0 || c.AtkGuildId == 0) EndWar(c);         // C++ EndWar(WIN_NOWAR): no war without both sides
            }
            else if (status == BsPeace) EndWar(c);                            // C++ EndWar(WIN_TIME)
        }
        byte msg = status switch
        {
            BsNormal => _castlePhase == BsNormal ? SmCastleStartAlarm : _castlePhase == BsPeace ? SmCastleNormal : (byte)0,
            BsBattle => _castlePhase == BsBattle ? SmCastleEndAlarm : _castlePhase == BsNormal ? SmCastleStart : (byte)0,
            BsPeace => _castlePhase == BsBattle ? SmCastlePeace : (byte)0,
            _ => 0,
        };
        NotifyLocalInfo(msg, 0, second);
        _castlePhase = status;
    }

    /// <summary>C++ <c>EndWar</c> (TMapSvr.cpp:10652), its state part: the castle cannot battle any more and is in peace. (The
    /// winner, <c>CS_ENDWAR_ACK</c>, the capture and the rewards come with the castle war itself.)</summary>
    private static void EndWar(Territory castle)
    {
        castle.CanBattle = false;
        castle.Status = BsPeace;
    }

    /// <summary>C++ <c>OnMW_MISSIONENABLE_REQ</c> (SSHandler.cpp:18596): the missions' next war (today at the start), status; news
    /// (with the start hour).</summary>
    private void OnMW_MISSIONENABLE_REQ(PacketReader r)
    {
        byte status = r.ReadByte();
        uint start = r.ReadUInt32(), second = r.ReadUInt32();
        if (_missionStart == 0) _missionStart = start;
        long midnight = UnixNow() - (long)LocalNow().TimeOfDay.TotalSeconds;
        foreach (var m in _missions)
        {
            if (m.NextDefend == 0 || (_missionPhase != BsNormal && status == BsNormal)) m.NextDefend = midnight + start;
            if (m.Status == BsPeace && status != BsNormal) continue;
            if (m.Status == status) continue;
            m.Status = status;
            if (m.Valid && status is BsNormal or BsPeace) m.Occupied = false;
        }
        byte msg = status switch
        {
            BsNormal => _missionPhase == BsNormal ? SmMissionStartAlarm : (byte)0,
            BsBattle => _missionPhase == BsBattle ? SmMissionEndAlarm : _missionPhase == BsNormal ? SmMissionStart : (byte)0,
            BsPeace => _missionPhase == BsBattle ? SmMissionPeace : (byte)0,
            _ => 0,
        };
        NotifyLocalInfo(msg, (byte)(_missionStart / 3600), second);
        _missionPhase = status;
        _missionStart = start;
    }

    /// <summary>C++ <c>OnMW_SKYGARDENENABLE_REQ</c> (SSHandler.cpp:12222): the next war (daily at the start), status; out of a peace
    /// the garden opens again; in battle it can be fought over; in peace not. News.</summary>
    private void OnMW_SKYGARDENENABLE_REQ(PacketReader r)
    {
        byte status = r.ReadByte();
        uint second = r.ReadUInt32();
        r.ReadByte();                                                   // bSkyGardenDay
        uint start = r.ReadUInt32();
        var now = LocalNow();
        uint clt = (uint)now.TimeOfDay.TotalSeconds;
        long midnight = UnixNow() - clt;
        foreach (var g in _skyGardens)
        {
            g.Day = (byte)((int)now.DayOfWeek + 1);
            g.NextDefend = clt < start ? midnight + start : midnight + start + 86400;
            bool reopen = g.Status == BsPeace && status == BsNormal;
            if (g.Status == BsPeace && status != BsNormal) continue;
            if (g.Status == status) continue;
            g.Status = status;
            if (!g.Valid) continue;
            switch (status)
            {
                case BsNormal: g.CanBattle = reopen; break;
                case BsBattle: g.CanBattle = true; g.LeftOwner = g.MiddleOwner = g.RightOwner = CampDefend; break;
                case BsPeace: g.CanBattle = false; break;
            }
        }
        byte msg = status switch
        {
            BsNormal => _skyPhase == BsPeace ? SmSkyGardenNormal : SmSkyGardenStartAlarm,
            BsBattle => _skyPhase == BsBattle ? SmSkyGardenEndAlarm : _skyPhase == BsNormal ? SmSkyGardenStart : (byte)0,
            BsPeace => _skyPhase == BsBattle ? SmSkyGardenPeace : (byte)0,
            BsSkyGardenStart => SmSkyGardenNormal,
            _ => 0,
        };
        NotifyLocalInfo(msg, 0, second);
        _skyPhase = status;
    }

    /// <summary>C++ <c>NotifyLocalInfo</c> → <c>SendCS_SYSTEMMSG_ACK</c> (CSSender.cpp:5056): the war news to every player in the game.
    /// SM_NONE sends nothing.</summary>
    private void NotifyLocalInfo(byte type, ushort localId, uint second)
    {
        if (type == 0) return;
        var w = new PacketWriter(Msg.CS_SYSTEMMSG_ACK, capacity: 12);
        w.WriteByte(type);
        switch (type)
        {
            case SmBattleNormal or SmBattleStart or SmCastleNormal or SmCastleStart or SmSkyGardenNormal or SmSkyGardenStart: break;
            case SmMissionStart: w.WriteUInt16(localId); break;
            case SmMissionStartAlarm or SmMissionEndAlarm or SmMissionPeace: w.WriteUInt16(localId); w.WriteUInt32(second); break;
            default: w.WriteUInt32(second); break;                     // the *_ALARM and *_PEACE news
        }
        var msg = w.ToArray();
        foreach (var s in _state.AllInGame())
            if (s.IsMain) s.Send(msg);
    }

    /// <summary>C++ <c>SendMW_CASTLEWARINFO_ACK</c> for every valid castle (or a bare 0 when none): its owner and, for each of its
    /// forts, who held it on each day of the week but the castle's own.</summary>
    private void SendCastleWarInfo()
    {
        bool sent = false;
        foreach (var c in _castles.Where(c => c.Valid && c.Day != 0))
        {
            var w = new PacketWriter(Msg.MW_CASTLEWARINFO_ACK);
            w.WriteUInt16(c.Id); w.WriteUInt32(c.Guild); w.WriteByte((byte)c.Locals.Count);
            foreach (var l in c.Locals)
            {
                w.WriteUInt16(l.Id);
                for (int i = 0; i < 7; i++)
                    if (i + 1 != c.Day) { w.WriteUInt32(l.OccupyGuild[i]); w.WriteByte(l.OccupyType[i]); }
            }
            _world.Send(w);
            sent = true;
        }
        if (!sent)
        {
            var w = new PacketWriter(Msg.MW_CASTLEWARINFO_ACK);
            w.WriteUInt16(0);
            _world.Send(w);
        }
        _castleWarInfoSent = true;
    }

    // ================================ the world's castle news ================================

    /// <summary>C++ <c>OnMW_CASTLEWARINFO_REQ</c> (SSHandler.cpp:14422): the world's scoreboard for a castle — its defender and
    /// attacker, the countries' points, every guild's points, and each country's top 3.</summary>
    private void OnMW_CASTLEWARINFO_REQ(PacketReader r)
    {
        ushort id = r.ReadUInt16();
        uint def = r.ReadUInt32(); string defName = r.ReadString(); byte defCountry = r.ReadByte(); ushort dPoint = r.ReadUInt16();
        uint atk = r.ReadUInt32(); string atkName = r.ReadString(); ushort cPoint = r.ReadUInt16();
        uint n = r.ReadUInt32();
        if (!_territories.TryGetValue(id, out var c) || c.Type != LocalType.Castle) return;
        (c.DefCountry, c.DefGuildId, c.DefName, c.DPoint, c.AtkGuildId, c.CPoint, c.AtkName) = (defCountry, def, defName, dPoint, atk, cPoint, atkName);
        c.Points.Clear();
        c.Top3[TcontryD].Clear(); c.Top3[TcontryC].Clear();
        for (uint i = 0; i < n; i++) { uint g = r.ReadUInt32(); c.Points[g] = r.ReadUInt32(); }
        byte top = r.ReadByte();
        for (int i = 0; i < top; i++)
        {
            byte country = r.ReadByte();
            string name = r.ReadString();
            ushort point = r.ReadUInt16();
            if (country <= TcontryC) c.Top3[country].Add((name, point));
        }
    }

    /// <summary>C++ <c>OnMW_CASTLEGUILDCHG_REQ</c> (SSHandler.cpp:15827): a GM's new defender / attacker and next war.</summary>
    private void OnMW_CASTLEGUILDCHG_REQ(PacketReader r)
    {
        ushort id = r.ReadUInt16();
        uint def = r.ReadUInt32(); string defName = r.ReadString();
        uint atk = r.ReadUInt32(); string atkName = r.ReadString();
        long next = r.ReadInt64();
        if (!_territories.TryGetValue(id, out var c) || c.Type != LocalType.Castle) return;
        (c.NextDefend, c.DefGuildId, c.DefName, c.AtkGuildId, c.AtkName) = (next, def, defName, atk, atkName);
    }

    /// <summary>C++ <c>OnMW_CASTLEAPPLICANTCOUNT_REQ</c> (SSHandler.cpp:17201): how many signed up on each side.</summary>
    private void OnMW_CASTLEAPPLICANTCOUNT_REQ(PacketReader r)
    {
        ushort id = r.ReadUInt16();
        r.ReadUInt32();                                                 // dwGuildID
        byte camp = r.ReadByte(), count = r.ReadByte();
        if (!_territories.TryGetValue(id, out var c) || c.Type != LocalType.Castle) return;
        if (camp == CampAttack) c.AtkCount = count; else c.DefCount = count;
    }

    /// <summary>C++ <c>OnMW_HEROSELECT_REQ</c> (SSHandler.cpp:15283): a territory's hero (and when, unless cleared).</summary>
    private void OnMW_HEROSELECT_REQ(PacketReader r)
    {
        ushort id = r.ReadUInt16();
        string hero = r.ReadString();
        long time = r.ReadInt64();
        if (!_territories.TryGetValue(id, out var t)) return;
        t.Hero = hero;
        if (hero.Length != 0) t.HeroTime = time;
    }

    // ================================ the war-info window ================================

    /// <summary>C++ <c>OnCS_GUILDLOCALLIST_REQ</c>: the BoW / BR status is asked of the world first.</summary>
    private void OnCS_GUILDLOCALLIST_REQ(ClientSession s, PacketReader r)
    {
        if (s.State != EnterState.InGame || !s.IsMain) return;
        var w = new PacketWriter(Msg.MW_BATTLEMODESTATUS_REQ);
        w.WriteUInt32(s.CharId); w.WriteUInt32(s.Key);
        _world.Send(w);
    }

    /// <summary>C++ <c>OnMW_BATTLEMODESTATUS_ACK</c> → <c>SendCS_GUILDLOCALLIST_ACK</c> (CSSender.cpp:2107).</summary>
    private void OnMW_BATTLEMODESTATUS_ACK(PacketReader r)
    {
        uint charId = r.ReadUInt32(), key = r.ReadUInt32();
        byte bowStatus = r.ReadByte(); uint nextBow = r.ReadUInt32(); byte bowWinner = r.ReadByte();
        byte brStatus = r.ReadByte(); uint nextBr = r.ReadUInt32(); byte brType = r.ReadByte();
        if (_state.FindByChar(charId) is not { Char: { } ch } s || s.Key != key) return;
        s.Send(BuildCS_GUILDLOCALLIST_ACK(ch, bowStatus, nextBow, bowWinner, brStatus, nextBr, brType));
    }

    /// <summary>The window's status of a territory: in battle until taken, peace once taken or after, else normal.</summary>
    private static byte ShownStatus(Territory t)
        => t.Status == BsBattle && !t.Occupied ? BsBattle : t.Status == BsPeace || (t.Status == BsBattle && t.Occupied) ? BsPeace : BsNormal;

    /// <summary>C++ <c>CanApplyCastle</c>: signing up is open until the castle's war, and only while its forts' wars come later.</summary>
    private bool CanApplyCastle(Territory c)
        => UnixNow() < c.NextDefend && (c.Locals.Count == 0 || c.Locals[0].NextDefend >= c.NextDefend);

    private byte[] BuildCS_GUILDLOCALLIST_ACK(Character ch, byte bowStatus, uint nextBow, byte bowWinner, byte brStatus, uint nextBr, byte brType)
    {
        var w = new PacketWriter(Msg.CS_GUILDLOCALLIST_ACK, capacity: 1024);
        w.WriteUInt16((ushort)_castles.Count);
        foreach (var c in _castles)
        {
            w.WriteUInt16(c.Id); w.WriteString(c.Zone.Name); w.WriteByte((byte)(CanApplyCastle(c) ? 0 : 1));
            w.WriteUInt32(c.Guild); w.WriteString(c.GuildName); w.WriteByte(c.Country); w.WriteInt64(c.NextDefend);
            w.WriteString(c.Hero); w.WriteString(c.DefName); w.WriteString(c.AtkName);
            w.WriteUInt16((ushort)c.Points.GetValueOrDefault(c.DefGuildId)); w.WriteUInt16(c.DPoint); w.WriteByte(c.DefCount);
            w.WriteUInt16((ushort)c.Points.GetValueOrDefault(c.AtkGuildId)); w.WriteUInt16(c.CPoint); w.WriteByte(c.AtkCount);
            w.WriteUInt16((ushort)c.Points.GetValueOrDefault(ch.GuildId)); w.WriteByte(ShownStatus(c));
            foreach (byte country in new[] { TcontryD, TcontryC })
            {
                w.WriteByte((byte)c.Top3[country].Count);
                foreach (var (name, point) in c.Top3[country]) { w.WriteString(name); w.WriteUInt16(point); }
            }
            w.WriteUInt16((ushort)c.Locals.Count);
            foreach (var l in c.Locals)
            {
                w.WriteUInt16(l.Id); w.WriteString(l.Zone.Name); w.WriteUInt32(l.Guild); w.WriteString(l.GuildName);
                w.WriteByte(l.Country); w.WriteInt64(l.NextDefend); w.WriteString(l.Hero); w.WriteByte(ShownStatus(l));
            }
        }
        w.WriteByte((byte)_missions.Count);
        foreach (var m in _missions)
        {
            // sic: the C++ sends the raw status here, not the window's.
            w.WriteUInt16(m.Id); w.WriteString(m.Zone.Name); w.WriteByte(m.Country); w.WriteByte(m.Status); w.WriteInt64(m.NextDefend);
        }
        w.WriteByte((byte)_skyGardens.Count);
        foreach (var g in _skyGardens)
        {
            w.WriteUInt16(g.Id); w.WriteString(g.Zone.Name); w.WriteByte(g.Country); w.WriteByte(ShownStatus(g)); w.WriteInt64(g.NextDefend);
        }
        w.WriteByte(bowStatus); w.WriteUInt32(nextBow); w.WriteByte(bowWinner);
        w.WriteByte(brStatus); w.WriteUInt32(nextBr); w.WriteByte(brType);
        return w.ToArray();
    }

    // ================================ castle / camp on the wire ================================

    /// <summary>C++ <c>IsInCastle</c> / <c>IsInSkyGarden</c>: the player is on a castle's (or the sky garden's) map.</summary>
    private bool IsInCastle(Character ch) => _castles.Any(c => c.Valid && c.Zone.MapId == ch.MapId);
    private bool IsInSkyGarden(Character ch) => _skyGardens.Any(g => g.Valid && g.Zone.MapId == ch.MapId);

    /// <summary>The <c>CS_ENTER_ACK</c> castle fields (CSSender.cpp:471): castle and camp inside a castle or the sky garden, the
    /// carried god ball inside a castle (god balls are not ported yet: 0).</summary>
    private (ushort Castle, byte Camp, ushort GodBall) EnterCastleFields(Character ch)
        => IsInCastle(ch) || IsInSkyGarden(ch) ? (ch.Castle, ch.Camp, (ushort)0) : ((ushort)0, (byte)0, (ushort)0);

    // ================================ clock ================================

    /// <summary>The local wall clock the C++ wars run on (<c>CTime::GetCurrentTime</c>), from the injectable unix clock.</summary>
    private DateTime LocalNow() => DateTimeOffset.FromUnixTimeSeconds(UnixNow()).ToLocalTime().DateTime;
    private static DateTime LocalDayOf(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().Date;
}

/// <summary>One territory's live state (C++ <c>TLOCAL</c>).</summary>
public sealed class Territory
{
    public byte Type { get; init; }
    public ushort Id { get; init; }
    public required BattleZone Zone { get; init; }
    public bool Valid { get; set; }
    public byte Status { get; set; }
    public byte Country { get; set; }
    public uint Guild { get; set; }
    public string GuildName { get; set; } = "";
    public long TimeOccupied { get; set; }
    public long NextDefend { get; set; }
    public string Hero { get; set; } = "";
    public long HeroTime { get; set; }
    /// <summary>C++ <c>m_bCanBattle</c> — false makes it a peace zone.</summary>
    public bool CanBattle { get; set; }
    /// <summary>C++ <c>m_bOccupied</c> — taken in this war already.</summary>
    public bool Occupied { get; set; }
    public bool GateOpened { get; set; }
    /// <summary>A fort's week (C++ <c>m_occupyGuild</c> / <c>m_occupyType</c>, by weekday − 1).</summary>
    public uint[] OccupyGuild { get; } = new uint[7];
    public byte[] OccupyType { get; } = new byte[7];
    // A castle's (C++ TLOCAL castle part)
    public byte Day { get; set; }
    public List<Territory> Locals { get; } = new();
    public byte DefCountry { get; set; } = 3;
    public uint DefGuildId { get; set; }
    public uint AtkGuildId { get; set; }
    public string DefName { get; set; } = "";
    public string AtkName { get; set; } = "";
    public ushort DPoint { get; set; }
    public ushort CPoint { get; set; }
    public byte DefCount { get; set; }
    public byte AtkCount { get; set; }
    public Dictionary<uint, uint> Points { get; } = new();
    public List<(string Name, ushort Point)>[] Top3 { get; } = { new(), new() };
    // The sky garden's three capture points (CAMP_*).
    public byte LeftOwner { get; set; }
    public byte MiddleOwner { get; set; }
    public byte RightOwner { get; set; }
}
