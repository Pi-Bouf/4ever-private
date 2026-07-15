namespace TControl.Protocol;

/// <summary>
/// Message ids for the control server. Values are the authoritative ones from
/// <c>Lib/Own/TProtocol/Include/CTProtocol.h</c> + <c>ProtocolBase.h</c> + <c>SSProtocol.h</c> in THIS
/// repo. These are the fix-point the control server must speak; note the sibling <c>TWorldSvr.Net</c>
/// has <c>CT_SERVICEMONITOR_REQ</c>/<c>_ACK</c> swapped vs. this file — the world port must be reconciled
/// to interoperate (see PORT_STATUS.md).
/// </summary>
public static class Msg
{
    // Plane bases (ProtocolBase.h)
    public const ushort CT_CONTROL = 0x9301; // control server ↔ {managers, game servers}
    public const ushort SM_BASE = 0x1581;    // system / batch / timer plane

    // System-plane ids the control server relays/receives (SSProtocol.h)
    public const ushort SM_DELSESSION_REQ = SM_BASE + 0x0001;   // internal: finalize a closing session
    public const ushort SM_BATTLESTATUS_REQ = SM_BASE + 0x001C; // control -> world: castle/local siege enable (CASTLEENABLE)

    // --- CT control plane (CTProtocol.h offsets off CT_CONTROL) ---
    public const ushort CT_OPLOGIN_REQ = CT_CONTROL + 0x0001;          // manager -> control: operator login
    public const ushort CT_OPLOGIN_ACK = CT_CONTROL + 0x0002;
    public const ushort CT_SVRTYPELIST_ACK = CT_CONTROL + 0x0003;      // control -> manager: server-type list
    public const ushort CT_MACHINELIST_ACK = CT_CONTROL + 0x0004;      // control -> manager: machine list
    public const ushort CT_GROUPLIST_ACK = CT_CONTROL + 0x0005;        // control -> manager: group list
    public const ushort CT_SERVICESTAT_REQ = CT_CONTROL + 0x0006;      // manager -> control: request service list
    public const ushort CT_SERVICESTAT_ACK = CT_CONTROL + 0x0007;
    public const ushort CT_SERVICECONTROL_REQ = CT_CONTROL + 0x0008;   // manager -> control: start/stop a service
    public const ushort CT_SERVICECONTROL_ACK = CT_CONTROL + 0x0009;
    public const ushort CT_NEWCONNECT_REQ = CT_CONTROL + 0x001A;       // internal: connect out to a (running) game server
    public const ushort CT_SERVICECHANGE_REQ = CT_CONTROL + 0x001B;    // internal: a service status changed
    public const ushort CT_SERVICECHANGE_ACK = CT_CONTROL + 0x001C;    // control -> manager: service status change
    public const ushort CT_SERVICEMONITOR_REQ = CT_CONTROL + 0x001D;   // game server -> control: live-count reply
    public const ushort CT_SERVICEMONITOR_ACK = CT_CONTROL + 0x001E;   // control -> game server: poll live counts
    public const ushort CT_TIMER_REQ = CT_CONTROL + 0x001F;            // internal: 1s tick
    public const ushort CT_SERVICEDATA_ACK = CT_CONTROL + 0x0020;      // control -> manager: per-service live data
    public const ushort CT_SERVICEUPLOADSTART_REQ = CT_CONTROL + 0x0021;
    public const ushort CT_SERVICEUPLOADSTART_ACK = CT_CONTROL + 0x0022;
    public const ushort CT_SERVICEUPLOAD_REQ = CT_CONTROL + 0x0023;
    public const ushort CT_SERVICEUPLOAD_ACK = CT_CONTROL + 0x0024;
    public const ushort CT_SERVICEUPLOADEND_REQ = CT_CONTROL + 0x0025;
    public const ushort CT_SERVICEUPLOADEND_ACK = CT_CONTROL + 0x0026;
    public const ushort CT_UPDATEPATCH_REQ = CT_CONTROL + 0x0027;
    public const ushort CT_UPDATEPATCH_ACK = CT_CONTROL + 0x0028;
    public const ushort CT_ANNOUNCEMENT_REQ = CT_CONTROL + 0x0029;     // manager -> control: broadcast announcement
    public const ushort CT_ANNOUNCEMENT_ACK = CT_CONTROL + 0x002A;     // control -> game server: announcement
    public const ushort CT_USERKICKOUT_REQ = CT_CONTROL + 0x002B;      // manager -> control: kick a user
    public const ushort CT_USERKICKOUT_ACK = CT_CONTROL + 0x002C;      // control -> map server: kick
    public const ushort CT_USERMOVE_REQ = CT_CONTROL + 0x002D;         // manager -> control: GM teleport
    public const ushort CT_USERMOVE_ACK = CT_CONTROL + 0x002E;         // control -> world server: teleport
    public const ushort CT_AUTHORITY_ACK = CT_CONTROL + 0x002F;        // control -> manager: authority denied
    public const ushort CT_STLOGIN_REQ = CT_CONTROL + 0x0030;          // manager -> control: status-tool login
    public const ushort CT_STLOGIN_ACK = CT_CONTROL + 0x0031;
    public const ushort CT_ACCOUNTINPUT_REQ = CT_CONTROL + 0x0032;
    public const ushort CT_ACCOUNTINPUT_ACK = CT_CONTROL + 0x0033;
    public const ushort CT_PLATFORM_REQ = CT_CONTROL + 0x0034;         // internal: machine CPU/MEM/NET sample
    public const ushort CT_PLATFORM_ACK = CT_CONTROL + 0x0035;         // control -> manager: platform stats
    public const ushort CT_MONSPAWNFIND_REQ = CT_CONTROL + 0x0036;     // manager -> control: locate a spawn
    public const ushort CT_MONSPAWNFIND_ACK = CT_CONTROL + 0x0037;     // map server -> control (-> manager)
    public const ushort CT_MONACTION_REQ = CT_CONTROL + 0x0038;        // manager -> control: monster action
    public const ushort CT_MONACTION_ACK = CT_CONTROL + 0x0039;        // control -> map server: monster action
    public const ushort CT_USERPROTECTED_REQ = CT_CONTROL + 0x003A;    // manager -> control: account ban (TUserProtectedAdd)
    public const ushort CT_USERPROTECTED_ACK = CT_CONTROL + 0x003B;
    public const ushort CT_CHARMSG_REQ = CT_CONTROL + 0x003C;          // manager -> control: system message to a char
    public const ushort CT_CHARMSG_ACK = CT_CONTROL + 0x003D;          // control -> world/relay: message
    public const ushort CT_LOCALGUILDCHANGE_REQ = CT_CONTROL + 0x003E;
    public const ushort CT_LOCALGUILDCHANGE_ACK = CT_CONTROL + 0x003F;
    public const ushort CT_LOCALINIT_REQ = CT_CONTROL + 0x0040;
    public const ushort CT_LOCALINIT_ACK = CT_CONTROL + 0x0041;
    public const ushort CT_USERPOSITION_REQ = CT_CONTROL + 0x0043;     // manager -> control: move users to a target
    public const ushort CT_USERPOSITION_ACK = CT_CONTROL + 0x0044;     // control -> world server
    public const ushort CT_SERVICECLOSE_REQ = CT_CONTROL + 0x0045;
    public const ushort CT_RECONNECT_REQ = CT_CONTROL + 0x0046;        // manager -> control: reconnect a service
    public const ushort CT_DISCONNECT_REQ = CT_CONTROL + 0x0048;
    public const ushort CT_SERVICEAUTOSTART_REQ = CT_CONTROL + 0x004A; // manager -> control: toggle auto-start
    public const ushort CT_SERVICEAUTOSTART_ACK = CT_CONTROL + 0x004B;
    public const ushort CT_CHATBAN_REQ = CT_CONTROL + 0x004C;          // manager -> control (-> world/relay): chat ban
    public const ushort CT_CHATBAN_ACK = CT_CONTROL + 0x004D;          // world -> control (-> manager): ban result
    public const ushort CT_SERVICEDATACLEAR_REQ = CT_CONTROL + 0x004E; // manager -> control: reset stats
    public const ushort CT_SERVICEDATACLEAR_ACK = CT_CONTROL + 0x004F; // control -> game server: reset
    public const ushort CT_ITEMFIND_REQ = CT_CONTROL + 0x0050;         // manager -> control (-> world): item search
    public const ushort CT_ITEMFIND_ACK = CT_CONTROL + 0x0051;         // world -> control (-> manager): matches
    public const ushort CT_ITEMSTATE_REQ = CT_CONTROL + 0x0052;        // manager -> control (-> world): set item states
    public const ushort CT_ITEMSTATE_ACK = CT_CONTROL + 0x0053;
    public const ushort CT_CHATBANLIST_REQ = CT_CONTROL + 0x0054;      // manager -> control: ban list
    public const ushort CT_CHATBANLIST_ACK = CT_CONTROL + 0x0055;
    public const ushort CT_CHATBANLISTDEL_REQ = CT_CONTROL + 0x0056;   // manager -> control: delete ban(s)
    public const ushort CT_CHATBANLISTDEL_ACK = CT_CONTROL + 0x0057;
    public const ushort CT_CTRLSVR_REQ = CT_CONTROL + 0x0058;          // control -> game server: register self (header-only)
    public const ushort CT_CASTLEINFO_REQ = CT_CONTROL + 0x0059;       // manager -> control (-> map): castle info
    public const ushort CT_CASTLEINFO_ACK = CT_CONTROL + 0x005A;
    public const ushort CT_CASTLEGUILDCHG_REQ = CT_CONTROL + 0x005B;   // manager -> control (-> world): castle guild override
    public const ushort CT_CASTLEGUILDCHG_ACK = CT_CONTROL + 0x005C;
    public const ushort CT_CASTLEENABLE_REQ = CT_CONTROL + 0x005D;     // manager -> control (-> world SM_BATTLESTATUS): siege enable
    public const ushort CT_CASTLEENABLE_ACK = CT_CONTROL + 0x005E;
    public const ushort CT_EVENTUPDATE_REQ = CT_CONTROL + 0x005F;      // control -> game server: event value push
    public const ushort CT_EVENTUPDATE_ACK = CT_CONTROL + 0x0060;
    public const ushort CT_EVENTCHANGE_REQ = CT_CONTROL + 0x0061;      // manager -> control: add/update/del event (TEventUpdate)
    public const ushort CT_EVENTCHANGE_ACK = CT_CONTROL + 0x0062;
    public const ushort CT_EVENTLIST_REQ = CT_CONTROL + 0x0063;        // manager -> control: event list
    public const ushort CT_EVENTLIST_ACK = CT_CONTROL + 0x0064;
    public const ushort CT_EVENTMSG_REQ = CT_CONTROL + 0x0065;         // control -> game server: event start/end message
    public const ushort CT_EVENTMSG_ACK = CT_CONTROL + 0x0066;
    public const ushort CT_EVENTDEL_REQ = CT_CONTROL + 0x0067;         // internal: delete an event
    public const ushort CT_CASHSHOPSTOP_REQ = CT_CONTROL + 0x0068;     // control -> game server: stop/resume cash shop
    public const ushort CT_CASHITEMSALE_REQ = CT_CONTROL + 0x0069;     // control -> game server: push cash-item sale
    public const ushort CT_CASHITEMSALE_ACK = CT_CONTROL + 0x006A;
    public const ushort CT_CASHITEMLIST_REQ = CT_CONTROL + 0x006B;     // manager -> control: sellable cash items
    public const ushort CT_CASHITEMLIST_ACK = CT_CONTROL + 0x006C;
    public const ushort CT_EVENTQUARTERUPDATE_REQ = CT_CONTROL + 0x006D; // manager -> control (-> world): lucky-event edit
    public const ushort CT_EVENTQUARTERUPDATE_ACK = CT_CONTROL + 0x006E;
    public const ushort CT_EVENTQUARTERLIST_REQ = CT_CONTROL + 0x006F;   // manager -> control (-> world): lucky-event list
    public const ushort CT_EVENTQUARTERLIST_ACK = CT_CONTROL + 0x0070;
    public const ushort CT_TOURNAMENTEVENT_REQ = CT_CONTROL + 0x0071;    // manager -> control (-> world): tournament-event admin
    public const ushort CT_TOURNAMENTEVENT_ACK = CT_CONTROL + 0x0072;
    public const ushort CT_HELPMESSAGE_REQ = CT_CONTROL + 0x0073;        // manager -> control (-> world): scheduled help message
    public const ushort CT_RPSGAMEDATA_REQ = CT_CONTROL + 0x0074;        // manager -> control (-> world): read RPS config
    public const ushort CT_RPSGAMEDATA_ACK = CT_CONTROL + 0x0075;        // world -> control (-> managers): RPS config
    public const ushort CT_RPSGAMECHANGE_REQ = CT_CONTROL + 0x0076;      // manager -> control (-> world): change RPS config
    public const ushort CT_PREVERSIONTABLE_REQ = CT_CONTROL + 0x0077;    // manager -> control: preversion file list
    public const ushort CT_PREVERSIONTABLE_ACK = CT_CONTROL + 0x0078;
    public const ushort CT_PREVERSIONUPDATE_REQ = CT_CONTROL + 0x0079;   // manager -> control: promote/register preversion files
    public const ushort CT_INSTALLVERSION_REQ = CT_CONTROL + 0x007A;
    public const ushort CT_INSTALLVERSION_ACK = CT_CONTROL + 0x007B;
    public const ushort CT_INSTALLVERSIONUPDATE_REQ = CT_CONTROL + 0x007C;
    public const ushort CT_CMGIFT_REQ = CT_CONTROL + 0x007D;             // manager -> control (-> world): cash-mall gift
    public const ushort CT_CMGIFT_ACK = CT_CONTROL + 0x007E;
    public const ushort CT_CMGIFTLIST_REQ = CT_CONTROL + 0x007F;         // manager -> control (-> world): gift catalog
    public const ushort CT_CMGIFTLIST_ACK = CT_CONTROL + 0x0080;
    public const ushort CT_CMGIFTCHARTUPDATE_REQ = CT_CONTROL + 0x0081;  // manager -> control (-> world): gift catalog edit
}

/// <summary>
/// Non-message protocol constants ported from <c>CTProtocol.h</c> / <c>NetCode.h</c>: server-group ids,
/// service-status codes, the <c>MAKESVRID</c> packing, operator authority classes, and event enums.
/// </summary>
public static class Proto
{
    // Server groups (ProtocolBase.h)
    public const byte SVRGRP_NULL = 0;
    public const byte SVRGRP_CTLSVR = 1;
    public const byte SVRGRP_LOGINSVR = 2;
    public const byte SVRGRP_WORLDSVR = 3;
    public const byte SVRGRP_MAPSVR = 4;
    public const byte SVRGRP_PATCHSVR = 5;
    public const byte SVRGRP_FTPSVR = 6;
    public const byte SVRGRP_LOG = 7;
    public const byte SVRGRP_RLYSVR = 8;
    public const byte SVRGRP_PREFTPSVR = 9;

    public const ushort DefaultCtlPort = 3615; // DEFAULT_CTL_PORT

    // Windows service status codes (winsvc.h SERVICE_*) as reported to managers.
    public const uint SvcStopped = 1;         // SERVICE_STOPPED
    public const uint SvcStartPending = 2;    // SERVICE_START_PENDING
    public const uint SvcStopPending = 3;     // SERVICE_STOP_PENDING
    public const uint SvcRunning = 4;         // SERVICE_RUNNING
    public const uint SvcContinuePending = 5; // SERVICE_CONTINUE_PENDING
    public const uint SvcPausePending = 6;    // SERVICE_PAUSE_PENDING
    public const uint SvcPaused = 7;          // SERVICE_PAUSED
    public const uint SvcCannotControl = 0xFFFFFFFF; // (DWORD)-1

    public const byte AckSuccess = 0;
    public const byte AckFailed = 1;

    // MAKESVRID(group,type,id) = group<<16 | type<<8 | id
    public static uint MakeSvrId(byte group, byte type, byte id) => (uint)(group << 16 | type << 8 | id);
    public static byte SvrGroup(uint id) => (byte)(id >> 16);
    public static byte SvrType(uint id) => (byte)(id >> 8);
    public static byte SvrId(uint id) => (byte)id;
}

/// <summary>Operator authority classes (TControlType.h MANAGER_CLASS). Lower value = higher privilege;
/// <c>CheckAuthority(cls)</c> passes when <c>authority &lt;= cls</c>.</summary>
public enum ManagerClass : byte
{
    All = 1,       // MANAGER_ALL
    Control = 2,   // patch/upload
    User = 3,      // announce/kick/move
    Service = 4,   // service on/off
    GmLevel1 = 5,
    GmLevel2 = 6,
    GmLevel3 = 7,
}

/// <summary>Event add/update/delete kind (TControlType.h EVENT_KIND).</summary>
public enum EventKind : byte { Del = 0, Add = 1, Update = 2 }

/// <summary>Event operation result (TControlType.h EVENT_RESULT).</summary>
public enum EventResult : byte { Success = 0, Fail, NotFound, Run, InvalidTime, MaxCount }

/// <summary>Event type ids (NetCode.h EVENT_TYPE, 1-based).</summary>
public enum EventType : byte
{
    ExpAdd = 1, CashSale = 2, ItemDrop = 3, ItemMagicDrop = 4, Refine = 5, Trans = 6,
    ItemUpgrade = 7, MagicUpgrade = 8, RareMagicUpgrade = 9, GambleOption = 10, MoneyDrop = 11,
    MonSpawn = 12, MonRegen = 13, Lottery = 14, GiftTime = 15,
}
