using Microsoft.Extensions.Logging;
using TMap.Data;
using TMap.Protocol;
using TMap.Server.Net;

namespace TMap.Server.Map;

/// <summary>
/// The map server's game logic — the C# counterpart of the C++ <c>CTMapSvrModule</c> methods spread
/// across <c>CSHandler.cpp</c>/<c>SSHandler.cpp</c>/<c>CSSender.cpp</c>/<c>SSSender.cpp</c>. One
/// <c>sealed partial class</c> split into <c>MapService.&lt;Feature&gt;.cs</c> files. Every method runs on the
/// single batch thread (see <see cref="MapWorker"/>), so <see cref="MapState"/> access is lock-free.
///
/// Dispatch has two planes: <see cref="DispatchClientAsync"/> handles the encrypted client plane
/// (<c>CS_*</c>) and <see cref="DispatchWorldAsync"/> handles the plaintext world plane (<c>MW_*</c>/
/// <c>SM_*</c>). Handlers are named <c>On&lt;MSG&gt;</c>; senders <c>SendMW_*</c> / <c>SendCS_*</c>.
/// </summary>
public sealed partial class MapService
{
    private readonly MapServerOptions _opt;
    private readonly MapState _state;
    private readonly IWorldSink _world;
    private readonly GameDatabase? _gameDb;
    private readonly TemplateStore _templates;
    private readonly ILogger<MapService> _log;

    private bool _worldReady;
    private uint _tickSeconds;

    /// <summary>The map clock in milliseconds (C++ module <c>m_dwTick</c>) — the tick fed to skill-cooldown
    /// math. Advances with <see cref="OnTimerAsync"/>; settable so tests can drive the reuse checks.</summary>
    public uint NowMs { get; set; }

    public MapService(MapServerOptions opt, MapState state, IWorldSink world, GameDatabase? gameDb,
        TemplateStore templates, ILogger<MapService> log)
    {
        _opt = opt;
        _state = state;
        _world = world;
        _gameDb = gameDb;
        PostStore = gameDb;
        PetStore = gameDb;
        CompanionStore = gameDb;
        _templates = templates;
        _log = log;
    }

    // Exposed for tests.
    public MapState State => _state;
    public bool WorldReady => _worldReady;

    /// <summary>Computed Max HP/MP for the wire (C++ <c>GetMaxHP</c>/<c>GetMaxMP</c>); falls back to the
    /// synthesized value when the stat charts aren't loaded (DB-free). See <see cref="StatEngine"/>.</summary>
    private uint MaxHpFor(Character ch) => StatEngine.MaxHp(ch, _templates);
    private uint MaxMpFor(Character ch) => StatEngine.MaxMp(ch, _templates);

    // ---- connection lifecycle ----

    public ValueTask OnClientConnectedAsync(ClientConnection conn)
    {
        // Only sets connection-scoped state (accept thread). The single reference write is ordered before
        // any packet from this connection by the FIFO batch channel, and the registry (ByChar) is populated
        // later on the batch thread in OnCS_CONNECT_REQ — so no lock is needed.
        conn.State = new ClientSession(conn);
        return ValueTask.CompletedTask;
    }

    public void OnClientDisconnect(ClientSession session)
    {
        // Broadcast the leave to neighbours before de-registering.
        var watching = new List<Monster>();
        if (session.State == EnterState.InGame && session.Char is not null)
        {
            BroadcastLeave(session, exitMap: true);
            watching.AddRange(_state.MonstersInView(session));
        }

        // Tell the world the char left this map (best-effort). The C++ closes via MW_CLOSECHAR / MW_TERMINATE.
        if (session.CharId != 0 && _worldReady)
            SendMW_CLOSECHAR_ACK(session.CharId, session.Key);

        SaveCharData(session);   // flush the char record + dirty quests on logout (C++ SetEventCloseSession)
        if (session.Char is { } leaving) { ClearRecalls(session, leaving); ClearCompanionObjs(leaving); }
        if (session.CharId != 0) EraseBill(session.CharId, 0);   // C++ SM_POSTBILLERASE_REQ(id, 0)
        _state.Remove(session);

        // C++ CTMap::LeaveMAP(player) takes the player off its cell first, then runs LeavePlayer over the 3×3:
        // every monster that could see it drops its hate and re-checks its host, so one this player was
        // driving is handed to someone else (or reset) instead of staying hosted by a ghost.
        foreach (var m in watching) MonsterLostSight(m, session.CharId);
        _log.LogDebug("Client char {Char} disconnected.", session.CharId);
    }

    // ---- client plane (CS_*) ----

    public async Task DispatchClientAsync(ClientSession session, byte[] packet)
    {
        var r = new PacketReader(packet);
        try
        {
            switch (r.Id)
            {
                case Msg.CS_CONNECT_REQ: await OnCS_CONNECT_REQ(session, r); break;
                case Msg.CS_POSTSEND_REQ: await OnCS_POSTSEND_REQ(session, r); break;
                case Msg.CS_POSTLIST_REQ: await OnCS_POSTLIST_REQ(session, r); break;
                case Msg.CS_POSTVIEW_REQ: await OnCS_POSTVIEW_REQ(session, r); break;
                case Msg.CS_POSTDEL_REQ: await OnCS_POSTDEL_REQ(session, r); break;
                case Msg.CS_POSTGETITEM_REQ: await OnCS_POSTGETITEM_REQ(session, r); break;
                case Msg.CS_POSTRETURN_REQ: await OnCS_POSTRETURN_REQ(session, r); break;
                case Msg.CS_CONREADY_REQ: OnCS_CONREADY_REQ(session, r); break;
                case Msg.CS_HOTKEYADD_REQ: OnCS_HOTKEYADD_REQ(session, r); break;
                case Msg.CS_INVENADD_REQ: OnCS_INVENADD_REQ(session, r); break;
                case Msg.CS_ITEMUPGRADE_REQ: OnCS_ITEMUPGRADE_REQ(session, r); break;
                case Msg.CS_REFINE_REQ: OnCS_REFINE_REQ(session, r); break;
                case Msg.CS_ITEMCHANGE_REQ: OnCS_ITEMCHANGE_REQ(session, r); break;
                case Msg.CS_REVIVALASK_REQ: OnCS_REVIVALASK_REQ(session, r); break;
                case Msg.CS_TELEPORT_REQ: OnCS_TELEPORT_REQ(session, r); break;
                case Msg.CS_PETMAKE_REQ: OnCS_PETMAKE_REQ(session, r); break;
                case Msg.CS_PETDEL_REQ: await OnCS_PETDEL_REQ(session, r); break;
                case Msg.CS_PETRECALL_REQ: OnCS_PETRECALL_REQ(session, r); break;
                case Msg.CS_PETCANCEL_REQ: OnCS_PETCANCEL_REQ(session, r); break;
                case Msg.CS_PETRIDING_REQ: OnCS_PETRIDING_REQ(session, r); break;
                case Msg.CS_PETEFFECTCHANGE_REQ: OnCS_PETEFFECTCHANGE_REQ(session, r); break;
                case Msg.CS_REQUESTSADDLE_REQ: OnCS_REQUESTSADDLE_REQ(session, r); break;
                case Msg.CS_CREATESADDLE_REQ: await OnCS_CREATESADDLE_REQ(session, r); break;
                case Msg.CS_DELETESADDLE_REQ: await OnCS_DELETESADDLE_REQ(session, r); break;
                case Msg.CS_DELRECALLMON_REQ: OnCS_DELRECALLMON_REQ(session, r); break;
                case Msg.CS_CHGMODERECALLMON_REQ: OnCS_CHGMODERECALLMON_REQ(session, r); break;
                case Msg.CS_CREATECOMPANION_REQ: OnCS_CREATECOMPANION_REQ(session, r); break;
                case Msg.CS_DELETECOMPANION_REQ: await OnCS_DELETECOMPANION_REQ(session, r); break;
                case Msg.CS_COMPANIONRECALL_REQ: OnCS_COMPANIONRECALL_REQ(session, r); break;
                case Msg.CS_COMPANIONCANCEL_REQ: OnCS_COMPANIONCANCEL_REQ(session, r); break;
                case Msg.CS_HIDECOMPANION_REQ: OnCS_HIDECOMPANION_REQ(session, r); break;
                case Msg.CS_COMPANIONUPGRADE_REQ: OnCS_COMPANIONUPGRADE_REQ(session, r); break;
                case Msg.CS_COMPANIONLUP_REQ: OnCS_COMPANIONLUP_REQ(session, r); break;
                case Msg.CS_USEPETITEM_REQ: OnCS_USEPETITEM_REQ(session, r); break;
                case Msg.CS_USECOMPANIONITEM_REQ: OnCS_USECOMPANIONITEM_REQ(session, r); break;
                case Msg.CS_DELETECOMPITEMS_REQ: OnCS_DELETECOMPITEMS_REQ(session, r); break;
                case Msg.CS_CHANGECOMPANIONEFFECT_REQ: OnCS_CHANGECOMPANIONEFFECT_REQ(session, r); break;
                case Msg.CS_USECOMPANIONPOWDER_REQ: OnCS_USECOMPANIONPOWDER_REQ(session, r); break;
                case Msg.CS_USECOMPRESET_REQ: OnCS_USECOMPRESET_REQ(session, r); break;
                case Msg.CS_FINISHCOMPANIONTRANSFER_ACK: OnCS_FINISHCOMPANIONTRANSFER_ACK(session, r); break;
                case Msg.CS_CHGMODESPOLECNIKMON_REQ: OnCS_CHGMODESPOLECNIKMON_REQ(session, r); break;
                case Msg.CS_DELSPOLECNIKMON_REQ: OnCS_DELSPOLECNIKMON_REQ(session, r); break;
                case Msg.CS_SPOLECNIKRECALL_REQ: break;   // its C++ body is commented out
                case Msg.CS_INVENDEL_REQ: OnCS_INVENDEL_REQ(session, r); break;
                case Msg.CS_INVENMOVE_REQ: OnCS_INVENMOVE_REQ(session, r); break;
                case Msg.CS_SETRETURNPOS_REQ: OnCS_SETRETURNPOS_REQ(session, r); break;
                case Msg.CS_PARTYADD_REQ: OnCS_PARTYADD_REQ(session, r); break;
                case Msg.CS_PARTYJOIN_REQ: OnCS_PARTYJOIN_REQ(session, r); break;
                case Msg.CS_PARTYDEL_REQ: OnCS_PARTYDEL_REQ(session, r); break;
                case Msg.CS_CHGPARTYCHIEF_REQ: OnCS_CHGPARTYCHIEF_REQ(session, r); break;
                case Msg.CS_CHGPARTYTYPE_REQ: OnCS_CHGPARTYTYPE_REQ(session, r); break;
                case Msg.CS_PARTYMOVE_REQ: OnCS_PARTYMOVE_REQ(session, r); break;
                case Msg.CS_HOTKEYDEL_REQ: OnCS_HOTKEYDEL_REQ(session, r); break;
                case Msg.CS_MOVE_REQ: OnCS_MOVE_REQ(session, r); break;
                case Msg.CS_JUMP_REQ: OnCS_JUMP_REQ(session, r); break;
                case Msg.CS_BLOCK_REQ: OnCS_BLOCK_REQ(session, r); break;
                case Msg.CS_CHGMODE_REQ: OnCS_CHGMODE_REQ(session, r); break;
                // The client-reported aggro bounds — one handler, four triggers.
                case Msg.CS_ENTERLB_REQ: OnAggroBoundReq(session, r, AiTrigger.EnterLb); break;
                case Msg.CS_LEAVELB_REQ: OnAggroBoundReq(session, r, AiTrigger.LeaveLb); break;
                case Msg.CS_ENTERAB_REQ: OnAggroBoundReq(session, r, AiTrigger.EnterAb); break;
                case Msg.CS_LEAVEAB_REQ: OnAggroBoundReq(session, r, AiTrigger.LeaveAb); break;
                case Msg.CS_CHARSTATINFO_REQ: OnCS_CHARSTATINFO_REQ(session, r); break;
                case Msg.CS_MOVEITEM_REQ: OnCS_MOVEITEM_REQ(session, r); break;
                case Msg.CS_ITEMUSE_REQ: OnCS_ITEMUSE_REQ(session, r); break;
                case Msg.CS_DURATIONREP_REQ: OnCS_DURATIONREP_REQ(session, r); break;
                case Msg.CS_MONMOVE_REQ: OnCS_MONMOVE_REQ(session, r); break;
                case Msg.CS_ACTION_REQ: OnCS_ACTION_REQ(session, r); break;
                case Msg.CS_FINISHSKILL_ACK: OnCS_FINISHSKILL_ACK(session, r); break;
                case Msg.CS_DEFEND_REQ: OnCS_DEFEND_REQ(session, r); break;
                case Msg.CS_SKILLUSE_REQ: OnCS_SKILLUSE_REQ(session, r); break;
                case Msg.CS_SKILLEND_REQ: OnCS_SKILLEND_REQ(session, r); break;
                case Msg.CS_SWITCHCHANGE_REQ: OnCS_SWITCHCHANGE_REQ(session, r); break;
                case Msg.CS_MONITEMLIST_REQ: OnCS_MONITEMLIST_REQ(session, r); break;
                case Msg.CS_MONMONEYTAKE_REQ: OnCS_MONMONEYTAKE_REQ(session, r); break;
                case Msg.CS_MONITEMTAKE_REQ: OnCS_MONITEMTAKE_REQ(session, r); break;
                case Msg.CS_CABINETOPEN_REQ: OnCS_CABINETOPEN_REQ(session, r); break;
                case Msg.CS_CABINETLIST_REQ: OnCS_CABINETLIST_REQ(session, r); break;
                case Msg.CS_CABINETITEMLIST_REQ: OnCS_CABINETITEMLIST_REQ(session, r); break;
                case Msg.CS_CABINETPUTIN_REQ: OnCS_CABINETPUTIN_REQ(session, r); break;
                case Msg.CS_CABINETTAKEOUT_REQ: OnCS_CABINETTAKEOUT_REQ(session, r); break;
                case Msg.CS_DEALITEMASK_REQ: OnCS_DEALITEMASK_REQ(session, r); break;
                case Msg.CS_DEALITEMRLY_REQ: OnCS_DEALITEMRLY_REQ(session, r); break;
                case Msg.CS_DEALITEMADD_REQ: OnCS_DEALITEMADD_REQ(session, r); break;
                case Msg.CS_DEALITEM_REQ: OnCS_DEALITEM_REQ(session, r); break;
                case Msg.CS_STOREOPEN_REQ: OnCS_STOREOPEN_REQ(session, r); break;
                case Msg.CS_STORECLOSE_REQ: OnCS_STORECLOSE_REQ(session, r); break;
                case Msg.CS_STOREITEMLIST_REQ: OnCS_STOREITEMLIST_REQ(session, r); break;
                case Msg.CS_STOREITEMBUY_REQ: OnCS_STOREITEMBUY_REQ(session, r); break;
                case Msg.CS_REVIVAL_REQ: OnCS_REVIVAL_REQ(session, r); break;
                case Msg.CS_NPCTALK_REQ: OnCS_NPCTALK_REQ(session, r); break;
                case Msg.CS_ITEMBUY_REQ: OnCS_ITEMBUY_REQ(session, r); break;
                case Msg.CS_ITEMSELL_REQ: OnCS_ITEMSELL_REQ(session, r); break;
                case Msg.CS_QUESTEXEC_REQ: OnCS_QUESTEXEC_REQ(session, r); break;
                case Msg.CS_QUESTDROP_REQ: OnCS_QUESTDROP_REQ(session, r); break;
                case Msg.CS_QUESTLIST_POSSIBLE_REQ: OnCS_QUESTLIST_POSSIBLE_REQ(session, r); break;
                case Msg.CS_CHAT_REQ: OnCS_CHAT_REQ(session, r); break;
                case Msg.CS_REGION_REQ: OnCS_REGION_REQ(session, r); break;
                case Msg.CS_PINGMEASUREMENT_REQ: OnCS_PINGMEASUREMENT_REQ(session, r); break;
                case Msg.CS_DISCONNECT_REQ: OnCS_DISCONNECT_REQ(session, r); break;
                case Msg.CS_TERMINATE_REQ: OnCS_TERMINATE_REQ(session, r); break;
                case Msg.CS_CHGCHANNEL_REQ: OnCS_CHGCHANNEL_REQ(session, r); break;
                default:
                    _log.LogDebug("Unhandled client message 0x{Id:X4} from char {Char}.", r.Id, session.CharId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Client handler 0x{Id:X4} failed.", r.Id);
        }
    }

    // ---- world plane (MW_* / SM_*) ----

    public async Task DispatchWorldAsync(byte[] packet)
    {
        var r = new PacketReader(packet);
        try
        {
            switch (r.Id)
            {
                case Msg.MW_ENTERSVR_REQ: await OnMW_ENTERSVR_REQ(r); break;
                case Msg.MW_CHARDATA_REQ: OnMW_CHARDATA_REQ(r); break;
                case Msg.MW_CHARINFO_REQ: OnMW_CHARINFO_REQ(r); break;
                case Msg.MW_ROUTE_REQ: OnMW_ROUTE_REQ(r); break;
                case Msg.MW_ENTERCHAR_REQ: OnMW_ENTERCHAR_REQ(r); break;
                case Msg.MW_CHECKMAIN_REQ: OnMW_CHECKMAIN_REQ(r); break;
                case Msg.MW_CONRESULT_REQ: OnMW_CONRESULT_REQ(r); break;
                case Msg.MW_CHAT_REQ: OnMW_CHAT_REQ(r); break;
                case Msg.MW_PARTYADD_REQ: OnMW_PARTYADD_REQ(r); break;
                case Msg.MW_POSTRECV_REQ: OnMW_POSTRECV_REQ(r); break;
                case Msg.MW_STARTTELEPORT_REQ: OnMW_STARTTELEPORT_REQ(r); break;
                case Msg.MW_TELEPORT_REQ: OnMW_TELEPORT_REQ(r); break;
                case Msg.MW_CONLIST_REQ: OnMW_CONLIST_REQ(r); break;
                case Msg.MW_PARTYJOIN_REQ: OnMW_PARTYJOIN_REQ(r); break;
                case Msg.MW_PARTYDEL_REQ: OnMW_PARTYDEL_REQ(r); break;
                case Msg.MW_PARTYATTR_REQ: OnMW_PARTYATTR_REQ(r); break;
                case Msg.MW_CREATERECALLMON_REQ: OnMW_CREATERECALLMON_REQ(r); break;
                case Msg.MW_RECALLMONDEL_REQ: OnMW_RECALLMONDEL_REQ(r); break;
                case Msg.MW_PETRIDING_REQ: OnMW_PETRIDING_REQ(r); break;
                case Msg.MW_CREATESPOLECNIKMON_REQ: OnMW_CREATESPOLECNIKMON_REQ(r); break;
                case Msg.MW_SPOLECNIKMONDEL_REQ: OnMW_SPOLECNIKMONDEL_REQ(r); break;
                case Msg.MW_RECALLMONDATA_REQ: break;   // cross-server summon handoff — never sent to a single map server
                case Msg.MW_CHGPARTYCHIEF_REQ: OnMW_CHGPARTYCHIEF_REQ(r); break;
                case Msg.MW_CHGPARTYTYPE_REQ: OnMW_CHGPARTYTYPE_REQ(r); break;
                case Msg.MW_PARTYMANSTAT_REQ: OnMW_PARTYMANSTAT_REQ(r); break;
                case Msg.MW_PARTYMOVE_REQ: OnMW_PARTYMOVE_REQ(r); break;
                case Msg.MW_TERMINATE_REQ: OnMW_TERMINATE_REQ(r); break;
                case Msg.MW_CLOSECHAR_REQ: OnMW_CLOSECHAR_REQ(r); break;
                case Msg.SM_TIMER_REQ: break; // world-driven tick; local timer already ticks
                case Msg.SM_DELSESSION_REQ: break;
                case Msg.SM_QUITSERVICE_REQ: break;
                default:
                    _log.LogDebug("Unhandled world message 0x{Id:X4}.", r.Id);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "World handler 0x{Id:X4} failed.", r.Id);
        }
    }

    // ---- world link lifecycle ----

    public ValueTask OnWorldConnectedAsync()
    {
        _worldReady = true;
        SendMW_CONNECT_ACK();
        _log.LogInformation("Announced to world: server {Server}, channels [{Channels}].",
            _opt.ServerId, string.Join(",", _opt.Channels));
        return ValueTask.CompletedTask;
    }

    public void OnWorldDisconnect()
    {
        _worldReady = false;
        _log.LogWarning("World link down; clients will re-handshake on reconnect.");
    }

    public async Task OnTimerAsync()
    {
        _tickSeconds++;
        NowMs = unchecked((uint)(_tickSeconds * 1000L)); // advance the map ms clock (skill cooldowns)
        RunMonsterRegen(_tickSeconds * 1000L); // monster spawn/respawn regen (map clock in ms)
        RunMaintainSkills(NowMs);              // expire ended buffs/debuffs (C++ CheckMaintainSkill, before Recover)
        RunSwitchReverts(NowMs);               // auto-revert duration-limited switches (C++ m_vTSWITCHOBJ sweep)
        RunRecover(NowMs);                     // HP/MP regeneration (players + monsters)
        RunAftermath(NowMs);                   // one step of death-penalty recovery when due (CTPlayer::OnTimer)
        RunRecallTimers();                     // summons whose life ran out are sent away (CheckTimeRecallMon)
        RunSelfObjTimers();                    // and placed objects whose time is up die
        RunCompanionTimers(NowMs);             // companion stamina/exp each minute, expired companion items
        RunCorpseExpiry(_tickSeconds * 1000L); // despawn + re-arm lootable corpses past their lifetime
        RunScheduledAi(_tickSeconds * 1000L);  // Due TAICHART commands (the local SM_AICMD stand-in)
        RunPendingResetHome();                 // abandoned monsters back to their spawn (SM_RESETHOST_ACK)
        RunMonsterAI(_tickSeconds * 1000L);    // legacy sweep — script-less monsters only (roam / chase)
        RunPeriodicSaves(NowMs);               // 30-min per-char DB save (no-op DB-free); off-thread write
        FlushItemDirect();                     // incremental item persistence (TSaveItemDirect); off-thread write
        await RunPostBills();                  // unpaid bills past 3 days go back to their sender
        // Still deferred here: war timers — see PORT_STATUS.md.
    }
}
