namespace TMap.Protocol;

/// <summary>
/// Message IDs for every plane the map server touches. Values come from THIS repo's
/// <c>Lib/Own/TProtocol/include/ProtocolBase.h</c> plane bases and the per-plane offset headers
/// (<c>CSProtocol.h</c>, <c>MWProtocol.h</c>, <c>SSProtocol.h</c>, <c>DMProtocol.h</c>, <c>CTProtocol.h</c>).
/// They differ from the 4retro fork's IDs.
///
/// Naming trap (same as TWorldSvr.Net): on the map↔world (MW) plane the map is the plane <b>initiator</b>
/// that <b>sends <c>MW_*_ACK</c></b> and <b>receives <c>MW_*_REQ</c></b> — inverted from the usual
/// REQ/ACK intuition. On the client (CS) plane the client sends <c>CS_*_REQ</c> and the map replies
/// <c>CS_*_ACK</c> (and pushes some <c>_ACK</c> unsolicited).
/// </summary>
public static class Msg
{
    // ---- plane bases (ProtocolBase.h) ----
    public const ushort SM_BASE = 0x1581;   // system / batch / timer / AI (same-port server sessions)
    public const ushort MW_BASE = 0x9001;   // map <-> world
    public const ushort DM_BASE = 0x5891;   // map <-> DB manager
    public const ushort CS_LOGIN = 0x1987;  // login <-> client
    public const ushort CS_MAP = 0x5280;    // map <-> client
    public const ushort CT_CONTROL = 0x9301;// control <-> server
    public const ushort CS_CUSTOM = 0x3312;

    // ================= client plane (CS_MAP) =================
    public const ushort CS_CONNECT_REQ = CS_MAP + 0x0001;
    public const ushort CS_CONNECT_ACK = CS_MAP + 0x0002;   // sent only on error (TCONNECT_RESULT)
    public const ushort CS_INVALIDCHAR_ACK = CS_MAP + 0x0003;
    public const ushort CS_ADDCONNECT_ACK = CS_MAP + 0x0004;
    public const ushort CS_CHARINFO_ACK = CS_MAP + 0x0005;  // full char + spawn
    public const ushort CS_ENTER_ACK = CS_MAP + 0x0006;     // a nearby PLAYER entered view
    public const ushort CS_LEAVE_ACK = CS_MAP + 0x0007;     // a nearby PLAYER left view
    public const ushort CS_ADDMON_ACK = CS_MAP + 0x0011;    // a nearby MONSTER entered view (appearance block)
    public const ushort CS_DELMON_ACK = CS_MAP + 0x0012;    // a nearby MONSTER left view (dwMonID + bExitMap)
    public const ushort CS_CONREADY_REQ = CS_MAP + 0x0008;  // client finished loading
    public const ushort CS_MOVE_REQ = CS_MAP + 0x0009;
    public const ushort CS_MOVE_ACK = CS_MAP + 0x000A;
    public const ushort CS_JUMP_REQ = CS_MAP + 0x000B;
    public const ushort CS_JUMP_ACK = CS_MAP + 0x000C;
    public const ushort CS_BLOCK_REQ = CS_MAP + 0x000D;
    public const ushort CS_BLOCK_ACK = CS_MAP + 0x000E;
    public const ushort CS_MONHOST_ACK = CS_MAP + 0x000F;  // monster retarget/host-change (dwMonID + bSet: TRUE to the new host, FALSE to others)
    public const ushort CS_MOVEITEM_REQ = CS_MAP + 0x0028;  // move/swap/split/merge/drop/equip an item
    public const ushort CS_MOVEITEM_ACK = CS_MAP + 0x0029;  // BYTE result (TMOVEITEM_RESULT)
    public const ushort CS_UPDATEITEM_ACK = CS_MAP + 0x002A; // bInvenID + item block (count/slot changed)
    public const ushort CS_ADDITEM_ACK = CS_MAP + 0x002B;    // bInvenID + item block (new item in a slot)
    public const ushort CS_DELITEM_ACK = CS_MAP + 0x002C;    // bInvenID + bItemID (item removed)
    public const ushort CS_EQUIP_ACK = CS_MAP + 0x002D;      // dwCharID + count + equipped items (appearance)
    public const ushort CS_ITEMUSE_REQ = CS_MAP + 0x004A;    // use an item (potion): wTempID/bInven/bItem/wDelayGroup/bCount + targets
    public const ushort CS_ITEMUSE_ACK = CS_MAP + 0x004B;    // BYTE result, WORD delayGroup, BYTE kind, DWORD delay
    public const ushort CS_MONEY_ACK = CS_MAP + 0x0049;      // DWORD gold, silver, cooper (money changed)
    public const ushort CS_DURATIONREP_REQ = CS_MAP + 0x01BC;    // repair durability: bNeedCost/bType/bInven/bItem/wNpcID/bNpcInven/bNpcItem
    public const ushort CS_DURATIONREPCOST_ACK = CS_MAP + 0x01BD; // DWORD cost, BYTE discountRate (the bNeedCost quote)
    public const ushort CS_DURATIONREP_ACK = CS_MAP + 0x01BE;    // BYTE result, BYTE count, count×{bInven,bItem,DWORD duraMax,DWORD duraCur}
    public const ushort CS_CHGMODE_REQ = CS_MAP + 0x001C;
    public const ushort CS_CHGMODE_ACK = CS_MAP + 0x001D;
    public const ushort CS_CHARSTATINFO_REQ = CS_MAP + 0x00A3;  // client requests a char's computed stat sheet
    public const ushort CS_CHARSTATINFO_ACK = CS_MAP + 0x00A4;  // 31-field stats/AP/DP/speed reply
    public const ushort CS_HPMP_ACK = CS_MAP + 0x0022;
    public const ushort CS_DEFEND_REQ = CS_MAP + 0x0020;   // a hit landed on a target — apply damage
    public const ushort CS_DEFEND_ACK = CS_MAP + 0x0021;   // the hit result + per-target damage (broadcast)
    public const ushort CS_DIE_ACK = CS_MAP + 0x0025;      // a target died (dwID + bType)
    public const ushort CS_REVIVAL_REQ = CS_MAP + 0x0026;  // a dead player revives: fPos + bType (REVIVAL_TYPE)
    public const ushort CS_REVIVAL_ACK = CS_MAP + 0x0027;  // dwCharID + revival position (broadcast)
    public const ushort CS_SKILLBUY_REQ = CS_MAP + 0x0032; // learn/level-up a skill from an NPC — REQ handler deferred (SP/price/teach-list unported)
    public const ushort CS_SKILLBUY_ACK = CS_MAP + 0x0033; // a skill was learned/leveled: bRet,wSkillID,bLevel,Tick,gold,silver,copper,skillPoint,kind[4]
    // Every ordinary player attack in this build. Despite the _ACK suffix it is CLIENT -> SERVER: a late
    // addition (near the end of the table) that moves hit resolution server-side. The client routes a finished
    // player skill here instead of CS_DEFEND_REQ (TClientGame.cpp:14381); the server derives the attack power
    // and the hit roll itself and applies Defend to each listed target.
    public const ushort CS_FINISHSKILL_ACK = CS_MAP + 0x0377; // dwAttackID,dwID,bType,fPos,wSkillID,IsLinked(4),IsFake(4),wPartyID,bCount x{dwID,bType}

    // Client-authoritative monster movement. In this engine the server does NOT walk a hosted monster: the
    // AI script's Follow/Roam only announces INTENT to the host client (CS_MONACTION_ACK), and that client
    // then drives the monster and reports each step back here. The server validates (the sender must be the
    // monster's host), applies the position, runs the leash / go-home / roam-range checks, and relays the
    // batch to the OTHER nearby players - the host is excluded, it already knows where it put them.
    // Without this the monster is frozen server-side and can never reach the player, so combat never starts.
    public const ushort CS_MONMOVE_REQ = CS_MAP + 0x0014;  // wMonCount x {dwMonID,bObjType,bChannel,wMapID,fPos,wPitch,wDIR,bMouseDIR,bKeyDIR,bAction}
    public const ushort CS_MONMOVE_ACK = CS_MAP + 0x0015;  // wMonCount x {dwMonID,bType,fPos,wPitch,wDIR,bMouseDIR,bKeyDIR,bAction}

    // The action/animation gate in front of the whole attack+cast sequence (CSHandler.cpp:1233). The client
    // sends this when a swing or cast STARTS and waits for the ACK before it plays the animation and goes on
    // to CS_SKILLUSE/CS_DEFEND. The ACK is broadcast to the 3x3 block INCLUDING the actor (unlike CS_MOVE,
    // which excludes self) — without its own copy the caster never animates and the attack never proceeds.
    public const ushort CS_ACTION_REQ = CS_MAP + 0x002E;   // dwObjID,bObjType,bActionID,dwActID,dwAniID,bChannel,wMapID,wSkillID
    public const ushort CS_ACTION_ACK = CS_MAP + 0x002F;   // bResult,dwObjID,bObjType,bActionID,dwActID,dwAniID,wSkillID

    public const ushort CS_SKILLUSE_REQ = CS_MAP + 0x0034; // announce a skill/attack swing (caster cost + broadcast)
    public const ushort CS_SKILLUSE_ACK = CS_MAP + 0x0035; // the caster's attack-power payload (broadcast to near players)
    public const ushort CS_SKILLEND_REQ = CS_MAP + 0x0036; // client ends/cancels a maintained buff
    public const ushort CS_SKILLEND_ACK = CS_MAP + 0x0037; // a maintained buff ended (dwObjID + bObjType + wSkillID)
    // Map switch/gate (CSProtocol.h:1854-1871). Gate is server-authored (no client REQ). Note the
    // CS_SWITCHCHANGE_ACK body is bResult,switchId,opened — the header comment (switchId,opened) is stale.
    public const ushort CS_GATEADD_ACK = CS_MAP + 0x00EA;      // a gate entered view (dwGateID + bOpened)
    public const ushort CS_GATEDEL_ACK = CS_MAP + 0x00EB;      // a gate left view (dwGateID)
    public const ushort CS_GATECHANGE_ACK = CS_MAP + 0x00EC;   // a gate opened/closed (dwGateID + bOpened)
    public const ushort CS_SWITCHADD_ACK = CS_MAP + 0x00ED;    // a switch entered view (dwSwitchID + bOpened)
    public const ushort CS_SWITCHDEL_ACK = CS_MAP + 0x00EE;    // a switch left view (dwSwitchID)
    public const ushort CS_SWITCHCHANGE_REQ = CS_MAP + 0x00EF; // client activates a switch (dwSwitchID)
    public const ushort CS_SWITCHCHANGE_ACK = CS_MAP + 0x00F0; // a switch flipped (bResult + dwSwitchID + bOpened)
    public const ushort CS_LEVEL_ACK = CS_MAP + 0x0023;    // level changed (dwCharID, bLevel, bShowLevelUp)
    public const ushort CS_EXP_ACK = CS_MAP + 0x0024;      // exp changed (exp, prev-level floor, next-level ceiling, soul)
    public const ushort CS_MONITEMLIST_REQ = CS_MAP + 0x0088; // open a monster corpse's loot list
    public const ushort CS_MONITEMLIST_ACK = CS_MAP + 0x0089; // corpse money (+ items, deferred)
    public const ushort CS_MONMONEYTAKE_REQ = CS_MAP + 0x0114; // take the corpse's money
    public const ushort CS_MONITEMTAKE_REQ = CS_MAP + 0x008A;  // take a corpse item: bItemID/bInvenID/bSlotID
    public const ushort CS_MONITEMTAKE_ACK = CS_MAP + 0x008B;  // BYTE result (MONITEMTAKE_RESULT)
    public const ushort CS_GETITEM_ACK = CS_MAP + 0x0144;      // an item the player received (WrapPacketClient)
    public const ushort CS_MONACTION_ACK = CS_MAP + 0x0013;    // monster AI move/action (dest + action verb + target)
    public const ushort CS_MONATTACK_ACK = CS_MAP + 0x0031;    // monster announces an attack swing (attacker/target/skill)
    // The client-reported aggro bounds. The CLIENT decides when a player crosses a monster's
    // "look bound" / "attack bound" and tells the server, which fires the matching AI trigger (C++
    // OnCS_ENTERLB_REQ etc., CSHandler.cpp:1100-1218). All four share one layout:
    // dwCharID, dwTargetID, bTargetType, dwMonID, bChannel, wMapID.
    public const ushort CS_ENTERLB_REQ = CS_MAP + 0x0018;      // entered a monster's look bound   → AT_ENTERLB
    public const ushort CS_LEAVELB_REQ = CS_MAP + 0x0019;      // left it                          → AT_LEAVELB
    public const ushort CS_ENTERAB_REQ = CS_MAP + 0x001A;      // entered its attack bound         → AT_ENTERAB
    public const ushort CS_LEAVEAB_REQ = CS_MAP + 0x001B;      // left it                          → AT_LEAVEAB
    public const ushort CS_ITEMBUY_REQ = CS_MAP + 0x0084;      // buy an item from an NPC shop (wNpcID/dwQuestID/wItemID/bCount/bNpcInven/bNpcItem)
    public const ushort CS_ITEMBUY_ACK = CS_MAP + 0x0085;      // BYTE result (ITEMBUY_*), wItemID, then the three money tiers
    public const ushort CS_ITEMSELL_REQ = CS_MAP + 0x0086;     // sell an inventory item to an NPC (bInven/bPos/bCount/bNpcInven/bNpcItem)
    public const ushort CS_ITEMSELL_ACK = CS_MAP + 0x0087;     // BYTE result (ITEMSELL_*), then the three money tiers
    public const ushort CS_NPCTALK_REQ = CS_MAP + 0x00DD;      // open an NPC dialog (wNpcID)
    public const ushort CS_NPCTALK_ACK = CS_MAP + 0x00DE;      // dwQuestID (a talk-objective quest, else 0), wNpcID
    public const ushort CS_NPCITEMLIST_ACK = CS_MAP + 0x0083;  // an NPC's item list (wNpcID, TNPC_BOX, discountRate, count, WORD itemIds[]) — quest routing
    public const ushort CS_CHAPTERMSG_ACK = CS_MAP + 0x00E0;   // a quest chapter message: dwQuestID
    // ---- quests (CSProtocol.h 0x50-0x5B) ----
    public const ushort CS_QUESTADD_ACK = CS_MAP + 0x0050;         // a quest began: dwQuestID, bType
    public const ushort CS_QUESTLIST_ACK = CS_MAP + 0x0051;        // running-quest list (enter-time; deferred)
    public const ushort CS_QUESTEXEC_REQ = CS_MAP + 0x0052;        // accept/run a quest: dwQuestID, bRewardType, dwRewardID
    public const ushort CS_QUESTUPDATE_ACK = CS_MAP + 0x0053;      // a term advanced: dwQuestID, dwTermID, bType, bCount, bStatus
    public const ushort CS_QUESTCOMPLETE_ACK = CS_MAP + 0x0054;    // bResult (QR_*), dwQuestID, dwTermID, bType, dwDropID
    public const ushort CS_QUESTDROP_REQ = CS_MAP + 0x0055;        // abandon a quest: dwQuestID
    public const ushort CS_QUESTSTARTTIMER_ACK = CS_MAP + 0x0057;  // a timed quest's clock: dwQuestID, dwTick
    public const ushort CS_QUESTLIST_POSSIBLE_REQ = CS_MAP + 0x0059; // bCount + wNpcID[] — which quests can I get here?
    public const ushort CS_QUESTLIST_POSSIBLE_ACK = CS_MAP + 0x005A; // per-NPC runnable-quest list
    public const ushort CS_CHAT_REQ = CS_MAP + 0x0074;
    public const ushort CS_CHAT_ACK = CS_MAP + 0x0075;
    // The player cabinet (item warehouse). Put-in/take-out have NO dedicated ack — they refresh via
    // CS_CABINETITEMLIST_ACK + the usual bag acks (ADD/DEL/UPDATEITEM) + CS_MONEY_ACK.
    public const ushort CS_CABINETPUTIN_REQ = CS_MAP + 0x0076;   // bCabinetID,bInven,bItemID,bCount,bNpcInvenID,bNpcItemID
    public const ushort CS_CABINETTAKEOUT_REQ = CS_MAP + 0x0078;  // bCabinetID,dwStItemID,bCount,bInvenID,bItemID,bNpcInvenID,bNpcItemID
    public const ushort CS_CABINETLIST_REQ = CS_MAP + 0x007A;
    public const ushort CS_CABINETLIST_ACK = CS_MAP + 0x007B;    // BYTE count, { bCabinetID, bUse }
    public const ushort CS_CABINETITEMLIST_REQ = CS_MAP + 0x007C; // bCabinetID
    public const ushort CS_CABINETITEMLIST_ACK = CS_MAP + 0x007D; // bResult [, bCabinetID, DWORD count, { dwStItemID, item-wrap(addItemId=0) }]
    public const ushort CS_CABINETOPEN_REQ = CS_MAP + 0x007E;    // bCabinetID
    public const ushort CS_CABINETOPEN_ACK = CS_MAP + 0x007F;    // bResult, bCabinetID
    // Party. Membership management is world-authoritative (relays, deferred); this id is the
    // map-local party-loot notification broadcast.
    public const ushort CS_PARTYITEMTAKE_ACK = CS_MAP + 0x0142;  // dwCharID + item block (a party member looted)
    // Player-to-player deal (trade). Same-map, in-memory two-party state machine.
    public const ushort CS_DEALITEMASK_REQ = CS_MAP + 0x010C;    // strTarget (invite)
    public const ushort CS_DEALITEMASK_ACK = CS_MAP + 0x010D;    // strInviter (shown to the target)
    public const ushort CS_DEALITEMRLY_REQ = CS_MAP + 0x010E;    // bReply, strInviter (accept/decline)
    public const ushort CS_DEALITEMSTART_ACK = CS_MAP + 0x010F;  // strTarget (window opens; to both)
    public const ushort CS_DEALITEMADD_REQ = CS_MAP + 0x0110;    // gold,silver,cooper, count, {bInvenID,bItemID}
    public const ushort CS_DEALITEMADD_ACK = CS_MAP + 0x0111;    // gold,silver,cooper, count, {item block} (to partner)
    public const ushort CS_DEALITEM_REQ = CS_MAP + 0x0112;       // bOkey (confirm=1 / cancel=0)
    public const ushort CS_DEALITEMEND_ACK = CS_MAP + 0x0113;    // bResult (DEALITEM_RESULT), strTarget
    // Personal store (player vendor). Same-map, in-memory; offered items stay in the seller's bag.
    public const ushort CS_STOREOPEN_REQ = CS_MAP + 0x0119;      // strName, count, {gold,silver,cooper,credits,inven,itemId,count}
    public const ushort CS_STOREOPEN_ACK = CS_MAP + 0x011A;      // bResult, dwCharID, strName (broadcast to neighbors)
    public const ushort CS_STORECLOSE_REQ = CS_MAP + 0x011B;
    public const ushort CS_STORECLOSE_ACK = CS_MAP + 0x011C;     // bResult, dwCharID (broadcast to neighbors + self)
    public const ushort CS_STOREITEMLIST_REQ = CS_MAP + 0x011D;  // strTarget (browse a seller's store)
    public const ushort CS_STOREITEMLIST_ACK = CS_MAP + 0x011E;  // dwCharID, strName, count, { slotKey, credits, gold, silver, cooper, item block }
    public const ushort CS_STOREITEMBUY_REQ = CS_MAP + 0x011F;   // strTarget, bItem(index), bItemCount
    public const ushort CS_STOREITEMBUY_ACK = CS_MAP + 0x0120;   // bResult, wItemID, bCount (to buyer)
    public const ushort CS_STOREITEMSELL_ACK = CS_MAP + 0x0121;  // bItem(index), bCount (to seller when sold)
    public const ushort CS_REGION_REQ = CS_MAP + 0x00F1;
    public const ushort CS_SYSTEMMSG_ACK = CS_MAP + 0x01D1;
    public const ushort CS_TERMINATE_REQ = CS_MAP + 0x01D2;
    public const ushort CS_CHECKRELAY_REQ = CS_MAP + 0x01DA;
    public const ushort CS_DISCONNECT_REQ = CS_MAP + 0x013C;
    public const ushort CS_CHGCHANNEL_REQ = CS_MAP + 0x013D;
    public const ushort CS_CHGCHANNEL_ACK = CS_MAP + 0x013E;
    public const ushort CS_KICKOUT_REQ = CS_MAP + 0x00D5;
    public const ushort CS_KICKOUTMAP_REQ = CS_MAP + 0x020F;
    public const ushort CS_KICKOUTMAP_ACK = CS_MAP + 0x0210;
    public const ushort CS_WARP_ACK = CS_MAP + 0x01F1;
    public const ushort CS_SETTIME_ACK = CS_MAP + 0x0279;
    public const ushort CS_ACDCLOSE_REQ = CS_MAP + 0x024A;
    public const ushort CS_WINLDIC_REQ = CS_MAP + 0x0943;
    public const ushort CS_PINGMEASUREMENT_REQ = CS_MAP + 0x0370;
    public const ushort CS_PINGMEASUREMENT_ACK = CS_MAP + 0x0371;
    public const ushort CS_TERMINATE_MAP_REQ = CS_MAP + 0x01D2; // alias for readability
    public const ushort CS_VERIFYSESSION_ACK = CS_CUSTOM + 0x1001;

    // ================= map <-> world plane (MW) =================
    // Map SENDS these (_ACK), World SENDS the _REQ counterparts (naming is inverted — see class remark).
    public const ushort MW_CONNECT_ACK = MW_BASE + 0x0001;
    public const ushort MW_ADDCHAR_ACK = MW_BASE + 0x0002;
    public const ushort MW_CHARDATA_REQ = MW_BASE + 0x0003;
    public const ushort MW_CHARDATA_ACK = MW_BASE + 0x0004;
    public const ushort MW_ENTERCHAR_REQ = MW_BASE + 0x0005;
    public const ushort MW_ENTERCHAR_ACK = MW_BASE + 0x0006;
    public const ushort MW_ENTERSVR_REQ = MW_BASE + 0x0007;
    public const ushort MW_ENTERSVR_ACK = MW_BASE + 0x0008;
    public const ushort MW_INVALIDCHAR_REQ = MW_BASE + 0x0009;
    public const ushort MW_CONRESULT_REQ = MW_BASE + 0x000A;
    public const ushort MW_CLOSECHAR_REQ = MW_BASE + 0x000B;
    public const ushort MW_CLOSECHAR_ACK = MW_BASE + 0x000C;
    public const ushort MW_DELCHAR_REQ = MW_BASE + 0x000D;
    public const ushort MW_CHARINFO_REQ = MW_BASE + 0x000E;
    public const ushort MW_ROUTE_REQ = MW_BASE + 0x000F;
    public const ushort MW_ROUTE_ACK = MW_BASE + 0x0010;
    public const ushort MW_ADDCONNECT_REQ = MW_BASE + 0x0011;
    public const ushort MW_MAPSVRLIST_REQ = MW_BASE + 0x0012;
    public const ushort MW_MAPSVRLIST_ACK = MW_BASE + 0x0013;
    public const ushort MW_ROUTELIST_REQ = MW_BASE + 0x0014;
    public const ushort MW_RELEASEMAIN_REQ = MW_BASE + 0x0016;
    public const ushort MW_RELEASEMAIN_ACK = MW_BASE + 0x0017;
    public const ushort MW_CHAT_REQ = MW_BASE + 0x003D;
    public const ushort MW_CHAT_ACK = MW_BASE + 0x003E;
    public const ushort MW_RESETCONNECTION_REQ = MW_BASE + 0x0040;
    public const ushort MW_RESETCONNECTION_ACK = MW_BASE + 0x0041;
    public const ushort MW_GETBLOOD_ACK = MW_BASE + 0x008A;   // HP/MP lifedrain (MTYPE_HI/MI) → world grants it to the attacker
    public const ushort MW_CONLIST_REQ = MW_BASE + 0x00BF;
    public const ushort MW_CONLIST_ACK = MW_BASE + 0x00C0;
    public const ushort MW_ENTERSOLOMAP_REQ = MW_BASE + 0x00C1;
    public const ushort MW_ENTERSOLOMAP_ACK = MW_BASE + 0x00C2;
    public const ushort MW_CHECKMAIN_REQ = MW_BASE + 0x00C4;
    public const ushort MW_CHECKMAIN_ACK = MW_BASE + 0x00C5;
    public const ushort MW_CHARMSG_REQ = MW_BASE + 0x011E;
    public const ushort MW_TERMINATE_REQ = MW_BASE + 0x0162;
    public const ushort MW_TERMINATE_ACK = MW_BASE + 0x0163;

    // ================= system / same-port server plane (SM) =================
    public const ushort SM_DELSESSION_REQ = SM_BASE + 0x0001;
    public const ushort SM_QUITSERVICE_REQ = SM_BASE + 0x0002;
    public const ushort SM_TIMER_REQ = SM_BASE + 0x001B;
    public const ushort SM_VALIDMAPSESSION_REQ = SM_BASE + 0x002D;
}

/// <summary>enum TCONNECT_RESULT (NetCode.h) — the byte carried by CS_CONNECT_ACK / MW_CONRESULT_REQ.</summary>
public enum ConnectResult : byte
{
    Success = 0,   // CN_SUCCESS
    NoChannel,     // CN_NOCHANNEL
    NoChar,        // CN_NOCHAR
    AlreadyExist,  // CN_ALREADYEXIST
    InvalidVer,    // CN_INVALIDVER
    Internal,      // CN_INTERNAL
}

/// <summary>enum CHAT_GROUP (NetCode.h) — the scope byte on CS_CHAT_REQ / CS_CHAT_ACK.</summary>
public enum ChatGroup : byte
{
    Whisper = 0,   // CHAT_WHISPER
    Near,          // CHAT_NEAR   (local cell)
    Map,           // CHAT_MAP
    World,         // CHAT_WORLD
    Party,         // CHAT_PARTY
    Guild,         // CHAT_GUILD
    Force,         // CHAT_FORCE
    Operator,      // CHAT_OPERATOR
    Tactics,       // CHAT_TACTICS
    Show,          // CHAT_SHOW
}

/// <summary>enum TMOVEITEM_RESULT (NetCode.h) — the byte carried by CS_MOVEITEM_ACK.</summary>
public enum MoveItemResult : byte
{
    Success = 0,     // MI_SUCCESS
    NoDestInven,     // MI_NODESTINVEN
    NoSrcInven,      // MI_NOSRCINVEN
    NoSrcItem,       // MI_NOSRCITEM
    SamePos,         // MI_SAMEPOS
    CannotEquip,     // MI_CANNOTEQUIP (slot mask)
    InvenFull,       // MI_INVENFULL
    Dealing,         // MI_DEALING
    BothHandWeapon,  // MI_BOTHHANDWEAPON
    NoSkill,         // MI_NOSKILL
    NoMatchClass,    // MI_NOMATCHCLASS (class mask)
    LowLevel,        // MI_LOWLEVEL
    Block,           // MI_BLOCK
    Dead,            // MI_DEAD
    CantDrop,        // MI_CANTDROP
    Wrap,            // MI_WRAP (sealed/wrapped item)
}

/// <summary>enum TITEMUSE_RESULT (NetCode.h) — the byte carried by CS_ITEMUSE_ACK.</summary>
public enum ItemUseResult : byte
{
    Success = 0,     // IU_SUCCESS
    NotFound,        // IU_NOTFOUND
    NeedTime,        // IU_NEEDTIME (cooldown)
    Full,            // IU_FULL (HP/MP already at max)
    NeedLevel,       // IU_NEEDLEVEL
    Dealing,         // IU_DEALING
    Riding,          // IU_RIDING
    NotParty,        // IU_NOTPARTY
    NotPartyFound,   // IU_NOTPARTYFOUND
    TargetNotFound,  // IU_TARGETNOTFOUND
    TargetBusy,      // IU_TARGETBUSY
    TargetDeny,      // IU_TARGETDENY
    OverlapPremium,  // IU_OVERLAPPREMIUM
    OverlapExpBonus, // IU_OVERLAPEXPBONUS
    Wrapping,        // IU_WRAPPING (sealed item)
    Arena,           // IU_ARENA
}

/// <summary>enum ITEMREPAIR_RESULT (NetCode.h) — the byte carried by CS_DURATIONREP_ACK (values 4-6 are
/// refine-only; the repair handler uses SUCCESS/NOTFOUND/NEEDMONEY/DISALLOW/NPCCALLERROR/INVALIDPOS).</summary>
public enum ItemRepairResult : byte
{
    Success = 0,     // ITEMREPAIR_SUCCESS
    NotFound,        // ITEMREPAIR_NOTFOUND
    NeedMoney,       // ITEMREPAIR_NEEDMONEY
    Disallow,        // ITEMREPAIR_DISALLOW (item's template forbids repair)
    MaxRefine,       // ITEMREPAIR_MAXREFINE (refine-only)
    LevelDiff,       // ITEMREPAIR_LEVELDIFF (refine-only)
    Fail,            // ITEMREPAIR_FAIL (refine-only)
    NpcCallError,    // ITEMREPAIR_NPCCALLERROR (portable-smith item invalid)
    InvalidPos,      // ITEMREPAIR_INVALIDPOS (portable smith not allowed on this map)
}

/// <summary>enum TSKILL_RESULT (NetCode.h:352-388) — the <c>bResult</c> byte carried by CS_SKILLUSE_ACK
/// (sequential from 0). The map's <c>OnCS_SKILLUSE_REQ</c> emits SUCCESS/NOTFOUND/SPEEDYUSE/NEEDMP/NEEDHP
/// (the other codes gate unported subsystems — tournament/arena/premium/peace-zone/buffs/items).</summary>
public enum SkillUseResult : byte
{
    Success = 0,     // SKILL_SUCCESS
    NotFound,        // SKILL_NOTFOUND (unknown/unlearned skill)
    Already,         // SKILL_ALREADY
    NeedParent,      // SKILL_NEEDPARENT
    NeedMoney,       // SKILL_NEEDMONEY
    NeedLevelUp,     // SKILL_NEEDLEVELUP
    SpeedyUse,       // SKILL_SPEEDYUSE (still on cooldown / recast too fast)
    NeedMp,          // SKILL_NEEDMP
    NeedHp,          // SKILL_NEEDHP
    UnsuitWeapon,    // SKILL_UNSUITWEAPON
    NeedPrevAct,     // SKILL_NEEDPREVACT
    MatchClass,      // SKILL_MATCHCLASS
    NeedSkillPoint,  // SKILL_NEEDSKILLPOINT
    WrongTarget,     // SKILL_WRONGTARGET
    TooClose,        // SKILL_TOOCLOSE
    TooFar,          // SKILL_TOOFAR
    WrongDir,        // SKILL_WRONGDIR
    WrongRegion,     // SKILL_WRONGREGION
    NeedGround,      // SKILL_NEEDGROUND
    Trans,           // SKILL_TRANS
    Hide,            // SKILL_HIDE
    Stun,            // SKILL_STUN
    Dead,            // SKILL_DEAD
    Silence,         // SKILL_SILENCE
    Mode,            // SKILL_MODE
    PeaceZone,       // SKILL_PEACEZONE
    NoTarget,        // SKILL_NOTARGET
    NotMoveSkill,    // SKILL_NOTMOVESKILL
    ActionLock,      // SKILL_ACTIONLOCK
    NeedItem,        // SKILL_NEEDITEM
    HaveChild,       // SKILL_HAVECHILD
    NotInit,         // SKILL_NOTINIT
    CannotSee,       // SKILL_CANNOTSEE
}

/// <summary>enum TITEMBUY_RESULT (NetCode.h:489) — the byte carried by CS_ITEMBUY_ACK.</summary>
public enum ItemBuyResult : byte
{
    Success = 0,   // ITEMBUY_SUCCESS
    NotFound,      // ITEMBUY_NOTFOUND (unknown NPC / item not in stock / bCount 0)
    NeedMoney,     // ITEMBUY_NEEDMONEY
    CantPush,      // ITEMBUY_CANTPUSH (bags full)
    Dealing,       // ITEMBUY_DEALING (in a trade — trade unported)
    NpcCallError,  // ITEMBUY_NPCCALLERROR (portable-NPC-call item invalid)
    InvalidPos,    // ITEMBUY_INVALIDPOS (portable NPC call not allowed on this map)
}

/// <summary>enum TITEMSELL_RESULT (NetCode.h:500) — the byte carried by CS_ITEMSELL_ACK.</summary>
public enum ItemSellResult : byte
{
    Success = 0,   // ITEMSELL_SUCCESS
    NotFound,      // ITEMSELL_NOTFOUND (bag/slot empty, count mismatch)
    Dealing,       // ITEMSELL_DEALING (in a trade — trade unported)
    CantSell,      // ITEMSELL_CANTSELL (item's template forbids selling)
    NpcCallError,  // ITEMSELL_NPCCALLERROR (portable-NPC-call item invalid)
    InvalidPos,    // ITEMSELL_INVALIDPOS (portable NPC call not allowed on this map)
}

/// <summary>enum QUEST_TYPE (NetCode.h:1831) — <c>QUESTTEMP.m_bType</c>, the subtype dispatched by
/// <c>CreateQuest</c>. The port implements the foundational four (NpcTalk offer, Mission begin, Complete
/// turn-in, GiveItem) + DefTalk (no-op); the other 15 are deferred (see PORT_STATUS.md).</summary>
public enum QuestType : byte
{
    None = 0, DefTalk = 1, GiveSkill, GiveItem, DropItem, SpawnMon, Teleport, Complete, Mission,
    Routing, NpcTalk, DropQuest, ChapterMsg, Switch, DieMon, DefendSkill, DeleteItem, SendPost,
    Craft, Regen, Guild,
}

/// <summary>enum TQUEST_RESULT (NetCode.h:426) — the <c>bResult</c> byte on CS_QUESTCOMPLETE_ACK.</summary>
public enum QuestResult : byte
{
    Success = 0,      // QR_SUCCESS
    Term,             // QR_TERM (a term is not yet satisfied)
    InventoryFull,    // QR_INVENTORYFULL
    Drop,             // QR_DROP (abandoned)
}

/// <summary>enum QUEST_TERM_STATUS (NetCode.h:1823) — the per-term <c>bStatus</c> on the update/list packets.</summary>
public enum QuestTermStatus : byte
{
    Run = 0,          // QTS_RUN (still in progress)
    Success,          // QTS_SUCCESS (satisfied)
    Failed,           // QTS_FAILED (e.g. timer expired)
}

/// <summary>enum MONITEMTAKE_RESULT (NetCode.h:510) — the byte carried by CS_MONITEMTAKE_ACK.</summary>
public enum MonItemTakeResult : byte
{
    Success = 0,   // MIT_SUCCESS
    FullInven,     // MIT_FULLINVEN
    NotFound,      // MIT_NOTFOUND
    Authority,     // MIT_AUTHORITY (party loot-mode denial)
    Dealing,       // MIT_DEALING
    Lottery,       // MIT_LOTTERY
}

/// <summary>enum CABINET_RESULT (NetCode.h:619) — the <c>bResult</c> byte on CS_CABINETOPEN_ACK /
/// CS_CABINETITEMLIST_ACK (sequential from 0).</summary>
public enum CabinetResult : byte
{
    Success = 0,     // CABINET_SUCCESS
    NotUse,          // CABINET_NOTUSE (cabinet not opened)
    NeedMoney,       // CABINET_NEEDMONEY
    Already,         // CABINET_ALREADY (already opened)
    Full,            // CABINET_FULL (16 items, no mergeable stack)
    NpcCallError,    // CABINET_NPCCALLERROR (the call-scroll path — deferred)
    InvalidPos,      // CABINET_INVALIDPOS (call scroll off the allowed maps — deferred)
    Max,             // CABINET_MAX (all 3 cabinets exist / id out of range)
}

/// <summary>enum PARTY_TYPE (NetCode.h:1960) — a party member's <c>m_bPartyType</c> = the loot/exp share mode.
/// <c>Solo</c> opts the member out of all party sharing.</summary>
public enum PartyType : byte
{
    Free = 0,   // PT_FREE — free-for-all loot
    Solo,       // PT_SOLO — opts out of party exp/loot sharing (GetPartyID → 0)
    Hunter,     // PT_HUNTER — only the top-aggro hunter may loot
    Lottery,    // PT_LOTTERY — dice/roll
    Chief,      // PT_CHIEF — loot goes to the party chief
    Order,      // PT_ORDER — round-robin (world-assigned)
}

/// <summary>enum OWNER_TYPE (NetCode.h:1972) — a monster's keeper class: who owns its exp/loot.</summary>
public enum OwnerType : byte
{
    None = 0,   // OWNER_NONE
    Private,    // OWNER_PRIVATE — a single character (its cumulative-damage keeper)
    Party,      // OWNER_PARTY — a party (keeper id = the party id)
}

/// <summary>enum DEALITEM_RESULT (NetCode.h:677) — the <c>bResult</c> byte on CS_DEALITEMEND_ACK.</summary>
public enum DealResult : byte
{
    Success = 0,   // DEALITEM_SUCCESS
    Deny,          // DEALITEM_DENY (target refused / block list)
    Busy,          // DEALITEM_BUSY (a party is mid-action / pairing broke)
    NoTarget,      // DEALITEM_NOTARGET
    Dealing,       // DEALITEM_DEALING (declared, unused)
    Cancel,        // DEALITEM_CANCEL (bOkey==0)
    NoMoney,       // DEALITEM_NOMONEY
    OverMoney,     // DEALITEM_OVERMONEY (declared, unused)
    NoInven,       // DEALITEM_NOINVEN
    NoItem,        // DEALITEM_NOITEM
    InvalidItem,   // DEALITEM_INVALIDITEM (duplicate slot offered)
    Enemy,         // DEALITEM_ENEMY (declared, unused)
    CantRecv,      // DEALITEM_CANTRECV (partner bag full)
    CantDeal,      // DEALITEM_CANTDEAL (nation/eligibility gate)
}

/// <summary>enum DEAL_STATUS (NetCode.h:233) — the paired <c>m_bStatus</c> and per-side <c>m_bDealing</c> values.</summary>
public enum DealStatus : byte
{
    Ready = 0,   // DEAL_READY (idle)
    Wait,        // DEAL_WAIT (window open, no offer yet — m_bDealing)
    Start,       // DEAL_START (paired window open — the ">= START" in-deal lock threshold)
    AddItem,     // DEAL_ADDITEM (an offer was submitted — m_bDealing)
    Conform,     // DEAL_CONFORM (locked/confirmed)
}

/// <summary>enum STORE_RESULT (NetCode.h:695) — the <c>bResult</c> byte on the store open/close/buy acks.</summary>
public enum StoreResult : byte
{
    Success = 0,     // STORE_SUCCESS
    Fail,            // STORE_FAIL (bad name / no target / not storing)
    ItemNoItem,      // STORE_ITEM_NOITEM
    ItemNotDeal,     // STORE_ITEM_NOTDEAL (untradable / duplicate slot)
    ItemOverMoney,   // STORE_ITEM_OVERMONEY (declared, unused)
    ItemNeedMoney,   // STORE_ITEM_NEEDMONEY
    ItemNoItemCount, // STORE_ITEM_NOITEMCOUNT
    ItemInvenFull,   // STORE_ITEM_INVENFULL (buyer bag full)
}

/// <summary>enum REVIVAL_TYPE (NetCode.h:2169) — the CS_REVIVAL_REQ mode byte. NPC = revive at a town/NPC
/// (30% HP/MP), GHOST = revive-in-place as a ghost (40%), HELP = revived by another player's resurrection
/// skill (full + skill bonus — the deferred CS_REVIVALASK flow).</summary>
public enum RevivalType : byte
{
    Npc = 0,    // REVIVAL_NPC
    Ghost,      // REVIVAL_GHOST
    Help,       // REVIVAL_HELP
}

/// <summary>enum TREPARE_TYPE (NetCode.h) — the CS_DURATIONREP_REQ mode byte.</summary>
public enum RepairType : byte
{
    Normal = 0,      // RPT_NORMAL — one item (bInven/bItem)
    Equip,           // RPT_EQUIP — all equipped items
    All,             // RPT_ALL — every repairable item in every bag
}

/// <summary>enum TSVR_TYPE (NetCode.h) — high byte of a server's <c>wServerID</c>.</summary>
public enum SvrType : byte
{
    None = 0,
    Control,
    Login,
    World,
    Map,      // SVR_MAP = 4
    Relay,
    Kick,
}

/// <summary>Assorted protocol constants + helpers from THIS repo's headers.</summary>
public static class Proto
{
    public const ushort ClientVersion = 0x2918; // TVERSION (ProtocolBase.h)

    public const byte SessionClient = 1;         // SESSION_CLIENT (TNetLib/Session.h)
    public const byte SessionServer = 2;         // SESSION_SERVER

    public const byte SvrGrpMapSvr = 4;          // SVRGRP_MAPSVR (CTProtocol.h)

    public const int MaxName = 50;               // MAX_NAME
    public const int MaxHotkeyPos = 12;          // MAX_HOTKEY_POS

    public const int DefaultGamePort = 5816;     // GamePort (Configurations/TMapSvr.ini)
    public const int DefaultWorldPort = 3816;    // WorldPort (Configurations/TMapSvr.ini)

    // Item storage constants (NetCode.h) — see TITEMTABLE partitioning.
    public const byte OwnerChar = 0;             // TOWNER_CHAR
    public const byte StorageInven = 0;          // STORAGE_INVEN
    public const byte StorageCabinet = 1;        // STORAGE_CABINET (the player warehouse)
    public const byte StoragePost = 2;           // STORAGE_POST (mail — excluded from the char-item load)
    public const byte InvenEquip = 0xFE;         // INVEN_EQUIP   (equipped gear)
    public const byte InvenDefault = 0xFF;       // INVEN_DEFAULT (main backpack)
    public const byte InvenNull = 0xFC;          // INVEN_NULL    (drop/destroy pseudo-target)

    // EQUIP_SLOT indices (NetCode.h) — the equip-slot ids used by the move/equip handler.
    public const byte EsPrmWeapon = 0, EsSndWeapon = 1, EsLongWeapon = 2;

    /// <summary>INVALID_SLOT (NetCode.h) — the "no slot" sentinel returned by the inventory slot finders
    /// (a two-hander has a real sub-slot; a 1H/shield/armour has <c>m_bSubSlotID == INVALID_SLOT</c>).</summary>
    public const byte InvalidSlot = 0xFF;

    // The anti-tamper key shared by the login + connect checksums.
    public const long ConnectChecksumKey = 0x336c3aebf71a8b08;

    /// <summary><c>MAKEWORD(bServerID, bServerType)</c> — the wServerID sent in MW_CONNECT_ACK.</summary>
    public static ushort MakeServerId(byte serverId, SvrType type) => (ushort)(serverId | ((byte)type << 8));

    public static byte ServerIdOf(ushort wId) => (byte)(wId & 0xFF);
    public static SvrType ServerTypeOf(ushort wId) => (SvrType)(byte)(wId >> 8);

    /// <summary>Converts a dotted IPv4 string to the little-endian uint produced by <c>inet_addr</c>.</summary>
    public static uint IpToUInt(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return 0;
        var parts = ip.Split('.');
        if (parts.Length != 4) return 0;
        uint result = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!byte.TryParse(parts[i], out byte b)) return 0;
            result |= (uint)b << (8 * i); // inet_addr packs the first octet into the low byte
        }
        return result;
    }

    /// <summary>
    /// Recomputes the CS_CONNECT_REQ anti-tamper checksum exactly as the client (CSSender.cpp) and map
    /// server (CSHandler.cpp OnCS_CONNECT_REQ) do: a 32-bit wrapping product zero-extended to 64-bit,
    /// then a small mixing loop keyed by <see cref="ConnectChecksumKey"/>.
    /// </summary>
    public static long ComputeConnectChecksum(ushort version, uint userId, uint key, uint charId)
    {
        uint product = unchecked((uint)version * userId + key * charId); // 32-bit wrap (DWORD math)
        long checksum = product;                                         // zero-extend (non-negative)
        long index = checksum % 8;                                       // % sizeof(INT64)
        long body = checksum / 8;
        for (long i = 0; i < index; i++)
        {
            checksum ^= body;
            checksum += ConnectChecksumKey;
        }
        return checksum;
    }
}
