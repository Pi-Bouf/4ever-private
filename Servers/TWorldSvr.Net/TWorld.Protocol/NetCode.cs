namespace TWorld.Protocol;

/// <summary>
/// Message-plane bases and the Phase-1 message IDs for TWorldSvr. Values come from THIS repo's
/// <c>Lib/Own/TProtocol/include/ProtocolBase.h</c> and <c>MWProtocol.h</c>.
/// </summary>
public static class Msg
{
    // Plane bases (ProtocolBase.h)
    public const ushort SM_BASE = 0x1581; // system / batch / timer
    public const ushort MW_BASE = 0x9001; // map ↔ world (the main inter-server plane)
    public const ushort DM_BASE = 0x5891; // world ↔ db (async)
    public const ushort CT_CONTROL = 0x9301; // control server ↔ world
    public const ushort RW_RELAY = 0x9999; // relay server ↔ world

    // --- MW: map-server connect + character session (MWProtocol.h offsets) ---
    public const ushort MW_CONNECT_ACK = MW_BASE + 0x0001;     // map registers (wServerID + channels)
    public const ushort MW_ADDCHAR_ACK = MW_BASE + 0x0002;     // char login start (charId,key,ip,port,userId)
    public const ushort MW_CHARDATA_REQ = MW_BASE + 0x0003;    // world -> map: send char data
    public const ushort MW_CHARDATA_ACK = MW_BASE + 0x0004;    // map -> world: char data
    public const ushort MW_ENTERCHAR_REQ = MW_BASE + 0x0005;   // world -> map: enter char (full state)
    public const ushort MW_ENTERCHAR_ACK = MW_BASE + 0x0006;   // map -> world: char entered
    public const ushort MW_ENTERSVR_REQ = MW_BASE + 0x0007;    // world -> map: load char from DB (bDBLoad,charId,key)
    public const ushort MW_ENTERSVR_ACK = MW_BASE + 0x0008;
    public const ushort MW_CONRESULT_REQ = MW_BASE + 0x000A;   // world -> map: connect result (charId,key,result,conServerIds) — the "enter granted" signal
    public const ushort MW_RELEASEMAIN_REQ = MW_BASE + 0x0016; // world -> map: release main (charId,key,channel,mapId,pos)
    public const ushort MW_RELEASEMAIN_ACK = MW_BASE + 0x0017; // map -> world: main released
    public const ushort MW_CHECKMAIN_REQ = MW_BASE + 0x00C4;   // world -> map: confirm main (charId,key,channel,mapId,pos)
    public const ushort MW_CHECKMAIN_ACK = MW_BASE + 0x00C5;   // map -> world: main confirmed

    // --- Phase 5a: cross-map movement / teleport / routing (MWProtocol.h) ---
    public const ushort MW_CLOSECHAR_REQ = MW_BASE + 0x000B;   // world -> map: close a (dead) connection
    public const ushort MW_MAPSVRLIST_REQ = MW_BASE + 0x0012;  // world -> map: which map servers cover dest (charId,key,channel,mapId,pos)
    public const ushort MW_MAPSVRLIST_ACK = MW_BASE + 0x0013;  // map -> world: server list (charId,key,count,serverIds)
    public const ushort MW_ROUTELIST_REQ = MW_BASE + 0x0014;   // world -> main: route to these new servers (charId,key,count,serverIds)
    public const ushort MW_REGION_ACK = MW_BASE + 0x00BC;      // map -> world: char changed region (charId,key,region)
    public const ushort MW_TELEPORT_REQ = MW_BASE + 0x00BD;    // world -> map: teleport result (charId,key,channel,mapId,pos,result)
    public const ushort MW_TELEPORT_ACK = MW_BASE + 0x00BE;    // map -> world: teleport request (charId,key,destServerId)
    public const ushort MW_CONLIST_REQ = MW_BASE + 0x00BF;     // world -> map: which connections are needed at dest (charId,key,channel,mapId,pos)
    public const ushort MW_CONLIST_ACK = MW_BASE + 0x00C0;     // map -> world: needed connection list (charId,key,count,serverIds)
    public const ushort MW_ENTERSOLOMAP_REQ = MW_BASE + 0x00C1;// world -> map: enter solo/instance map (charId,key,partyId,partyType,chiefId)
    public const ushort MW_ENTERSOLOMAP_ACK = MW_BASE + 0x00C2;// map -> world: entered solo map (charId,key)
    public const ushort MW_LEAVESOLOMAP_ACK = MW_BASE + 0x00C3;// map -> world: left solo map (charId,key)
    public const ushort MW_BEGINTELEPORT_ACK = MW_BASE + 0x00C7;// map -> world: begin teleport (charId,key,sameChannel,channel[,mapId,pos])
    public const ushort MW_STARTTELEPORT_REQ = MW_BASE + 0x00C8;// world -> map: start teleport on a connection (charId,key,channel,mapId,pos)

    // --- Phase 5b: combat / progression / loot (MWProtocol.h) ---
    public const ushort MW_LEVELUP_REQ = MW_BASE + 0x0026;            // world -> map: propagate level to other connections
    public const ushort MW_LEVELUP_ACK = MW_BASE + 0x0027;           // map -> world: char leveled up (charId,key,level)
    public const ushort MW_MONSTERDIE_REQ = MW_BASE + 0x0042;        // world -> main: forwarded monster-death
    public const ushort MW_MONSTERDIE_ACK = MW_BASE + 0x0043;        // map -> world: monster died (route to main)
    public const ushort MW_TAKEMONMONEY_REQ = MW_BASE + 0x0044;      // world -> main: forwarded loot-money
    public const ushort MW_TAKEMONMONEY_ACK = MW_BASE + 0x0045;      // map -> world: took monster money (route to main)
    public const ushort MW_ADDITEM_REQ = MW_BASE + 0x0046;           // world -> main: forwarded loot-item grant
    public const ushort MW_ADDITEM_ACK = MW_BASE + 0x0047;           // map -> world: add looted item (charId,key,svr,chan,map,monId,inven,slot,itemId,...)
    public const ushort MW_PARTYORDERTAKEITEM_REQ = MW_BASE + 0x0048;// world -> next member's main: ordered-loot item
    public const ushort MW_PARTYORDERTAKEITEM_ACK = MW_BASE + 0x0049;// map -> world: ordered-loot drop (round-robin)
    public const ushort MW_ADDITEMRESULT_REQ = MW_BASE + 0x0061;     // world -> map: loot-item result (charId,key,chan,map,monId,itemId,result)
    public const ushort MW_ADDITEMRESULT_ACK = MW_BASE + 0x0062;     // map -> world: relay loot result to origin server

    // --- Phase 5c: castle / territory war — ownership broadcasts (MWProtocol.h) ---
    public const ushort MW_LOCALOCCUPY_REQ = MW_BASE + 0x0069;       // world -> all maps: local territory owner changed
    public const ushort MW_LOCALOCCUPY_ACK = MW_BASE + 0x006A;       // map -> world: local territory captured
    public const ushort MW_CASTLEOCCUPY_REQ = MW_BASE + 0x0085;      // world -> all maps: castle owner changed
    public const ushort MW_CASTLEOCCUPY_ACK = MW_BASE + 0x0086;      // map -> world: castle captured
    public const ushort MW_CASTLEWARINFO_REQ = MW_BASE + 0x010F;     // world -> all maps: castle-war scoreboard (def/atk, country points, top-3, per-guild bonus)
    public const ushort MW_CASTLEWARINFO_ACK = MW_BASE + 0x0110;     // map -> world: castle-war occupation snapshot (castle[,guild,locals×(guild,type)×6])
    public const ushort MW_ENDWAR_REQ = MW_BASE + 0x0111;            // world -> all maps: castle war ended
    public const ushort MW_ENDWAR_ACK = MW_BASE + 0x0112;            // map -> world: castle war ended
    public const ushort MW_MISSIONOCCUPY_REQ = MW_BASE + 0x0166;     // world -> all maps: mission territory owner changed
    public const ushort MW_MISSIONOCCUPY_ACK = MW_BASE + 0x0167;     // map -> world: mission territory captured
    public const ushort MW_SKYGARDENOCCUPY_REQ = MW_BASE + 0x0183;   // world -> all maps: sky-garden owner changed
    public const ushort MW_SKYGARDENOCCUPY_ACK = MW_BASE + 0x0184;   // map -> world: sky-garden captured

    // --- Phase 5d: castle-war application (MWProtocol.h) ---
    public const ushort MW_CASTLEAPPLY_REQ = MW_BASE + 0x0080;        // world -> map: apply result (charId,key,result,castle,target,camp)
    public const ushort MW_CASTLEAPPLY_ACK = MW_BASE + 0x0081;        // map -> world: guild chief assigns a member to a castle slot
    public const ushort MW_CASTLEAPPLICANTCOUNT_REQ = MW_BASE + 0x013C;// world -> all maps: a guild's applicant count for a castle (castle,guildId,camp,count)
    public const ushort MW_CHARINFO_REQ = MW_BASE + 0x000E;    // world -> map: guild/tactics/party/title info
    public const ushort MW_ROUTE_REQ = MW_BASE + 0x000F;       // world -> map: route the char (post char-data)
    public const ushort MW_ROUTE_ACK = MW_BASE + 0x0010;       // map -> world: routing done (charId,key,extraConnCount)
    public const ushort MW_ADDCONNECT_REQ = MW_BASE + 0x0011;  // world -> map: open the extra channel connections
    public const ushort MW_INVALIDCHAR_REQ = MW_BASE + 0x0009; // world -> map: reject (charId,key,bReleaseMain)
    public const ushort MW_CLOSECHAR_ACK = MW_BASE + 0x000C;   // map -> world: char logout/leave
    public const ushort MW_DELCHAR_REQ = MW_BASE + 0x000D;     // world -> map: drop char (charId,key,bLogout,bSave)
    public const ushort MW_CHAT_ACK = MW_BASE + 0x003E;        // chat relay
    public const ushort MW_CHECKCONNECT_ACK = MW_BASE + 0x00C9; // keepalive

    // --- CT / RW: peer registration ---
    public const ushort CT_CTRLSVR_REQ = CT_CONTROL + 0x0001;  // control server announces itself
    public const ushort RW_RELAYSVR_REQ = RW_RELAY + 0x0001;   // relay server announces itself
    public const ushort RW_RELAYSVR_ACK = RW_RELAY + 0x0002;

    // --- MW guild (Phase 2). Convention: map sends *_ACK to world; world replies *_REQ to maps. ---
    public const ushort MW_GUILDESTABLISH_REQ = MW_BASE + 0x002A;
    public const ushort MW_GUILDESTABLISH_ACK = MW_BASE + 0x002B;
    public const ushort MW_GUILDDISORGANIZATION_REQ = MW_BASE + 0x002C;
    public const ushort MW_GUILDDISORGANIZATION_ACK = MW_BASE + 0x002D;
    public const ushort MW_GUILDINVITE_REQ = MW_BASE + 0x002E;
    public const ushort MW_GUILDINVITE_ACK = MW_BASE + 0x002F;
    public const ushort MW_GUILDINVITEANSWER_ACK = MW_BASE + 0x0030;
    public const ushort MW_GUILDJOIN_REQ = MW_BASE + 0x0031;
    public const ushort MW_GUILDLEAVE_REQ = MW_BASE + 0x0032;
    public const ushort MW_GUILDLEAVE_ACK = MW_BASE + 0x0033;
    public const ushort MW_GUILDDUTY_REQ = MW_BASE + 0x0034;
    public const ushort MW_GUILDDUTY_ACK = MW_BASE + 0x0035;
    public const ushort MW_GUILDPEER_REQ = MW_BASE + 0x0036;
    public const ushort MW_GUILDPEER_ACK = MW_BASE + 0x0037;
    public const ushort MW_GUILDINFO_REQ = MW_BASE + 0x0038;
    public const ushort MW_GUILDINFO_ACK = MW_BASE + 0x0039;
    public const ushort MW_GUILDKICKOUT_ACK = MW_BASE + 0x003A;
    public const ushort MW_GUILDMEMBERLIST_REQ = MW_BASE + 0x003B;
    public const ushort MW_GUILDMEMBERLIST_ACK = MW_BASE + 0x003C;

    // --- MW party/corps (Phase 2, in-memory) ---
    public const ushort MW_PARTYADD_REQ = MW_BASE + 0x0018;
    public const ushort MW_PARTYADD_ACK = MW_BASE + 0x0019;
    public const ushort MW_PARTYJOIN_REQ = MW_BASE + 0x0020;
    public const ushort MW_PARTYJOIN_ACK = MW_BASE + 0x0021;
    public const ushort MW_PARTYDEL_REQ = MW_BASE + 0x0022;
    public const ushort MW_PARTYDEL_ACK = MW_BASE + 0x0023;
    public const ushort MW_PARTYMANSTAT_REQ = MW_BASE + 0x0024;
    public const ushort MW_PARTYMANSTAT_ACK = MW_BASE + 0x0025;
    public const ushort MW_CHGPARTYCHIEF_ACK = MW_BASE + 0x00A1;
    public const ushort MW_CHGPARTYCHIEF_REQ = MW_BASE + 0x00A2;
    public const ushort MW_CHGPARTYTYPE_REQ = MW_BASE + 0x00CA;
    public const ushort MW_CHGPARTYTYPE_ACK = MW_BASE + 0x00CB;

    // --- Phase 2b: guild cabinet / contribution / articles / fame ---
    public const ushort MW_GUILDCABINETLIST_REQ = MW_BASE + 0x00D0;
    public const ushort MW_GUILDCABINETLIST_ACK = MW_BASE + 0x00D1;
    public const ushort MW_GUILDCABINETPUTIN_REQ = MW_BASE + 0x00D2;
    public const ushort MW_GUILDCABINETPUTIN_ACK = MW_BASE + 0x00D3;
    public const ushort MW_GUILDCABINETTAKEOUT_REQ = MW_BASE + 0x00D4;
    public const ushort MW_GUILDCABINETTAKEOUT_ACK = MW_BASE + 0x00D5;
    public const ushort MW_GUILDCONTRIBUTION_REQ = MW_BASE + 0x00D6;
    public const ushort MW_GUILDCONTRIBUTION_ACK = MW_BASE + 0x00D7;
    public const ushort MW_GUILDARTICLELIST_REQ = MW_BASE + 0x00D8;
    public const ushort MW_GUILDARTICLELIST_ACK = MW_BASE + 0x00D9;
    public const ushort MW_GUILDARTICLEADD_REQ = MW_BASE + 0x00DA;
    public const ushort MW_GUILDARTICLEADD_ACK = MW_BASE + 0x00DB;
    public const ushort MW_GUILDARTICLEDEL_REQ = MW_BASE + 0x00DC;
    public const ushort MW_GUILDARTICLEDEL_ACK = MW_BASE + 0x00DD;
    public const ushort MW_GUILDFAME_REQ = MW_BASE + 0x00DE;
    public const ushort MW_GUILDFAME_ACK = MW_BASE + 0x00DF;
    public const ushort MW_GUILDARTICLEUPDATE_REQ = MW_BASE + 0x00E0;
    public const ushort MW_GUILDARTICLEUPDATE_ACK = MW_BASE + 0x00E1;

    // --- Phase 2b: guild wanted / volunteer ---
    public const ushort MW_GUILDWANTEDADD_REQ = MW_BASE + 0x00E2;
    public const ushort MW_GUILDWANTEDADD_ACK = MW_BASE + 0x00E3;
    public const ushort MW_GUILDWANTEDDEL_REQ = MW_BASE + 0x00E4;
    public const ushort MW_GUILDWANTEDDEL_ACK = MW_BASE + 0x00E5;
    public const ushort MW_GUILDWANTEDLIST_REQ = MW_BASE + 0x00E6;
    public const ushort MW_GUILDWANTEDLIST_ACK = MW_BASE + 0x00E7;
    public const ushort MW_GUILDVOLUNTEERING_REQ = MW_BASE + 0x00E8;
    public const ushort MW_GUILDVOLUNTEERING_ACK = MW_BASE + 0x00E9;
    public const ushort MW_GUILDVOLUNTEERINGDEL_REQ = MW_BASE + 0x00EA;
    public const ushort MW_GUILDVOLUNTEERINGDEL_ACK = MW_BASE + 0x00EB;
    public const ushort MW_GUILDVOLUNTEERLIST_REQ = MW_BASE + 0x00EC;
    public const ushort MW_GUILDVOLUNTEERLIST_ACK = MW_BASE + 0x00ED;
    public const ushort MW_GUILDVOLUNTEERREPLY_REQ = MW_BASE + 0x00EE;
    public const ushort MW_GUILDVOLUNTEERREPLY_ACK = MW_BASE + 0x00EF;

    // --- Phase 2b: guild-tactics wanted / volunteer / reply / kickout ---
    public const ushort MW_GUILDTACTICSWANTEDADD_REQ = MW_BASE + 0x00F0;
    public const ushort MW_GUILDTACTICSWANTEDADD_ACK = MW_BASE + 0x00F1;
    public const ushort MW_GUILDTACTICSWANTEDDEL_REQ = MW_BASE + 0x00F2;
    public const ushort MW_GUILDTACTICSWANTEDDEL_ACK = MW_BASE + 0x00F3;
    public const ushort MW_GUILDTACTICSWANTEDLIST_REQ = MW_BASE + 0x00F4;
    public const ushort MW_GUILDTACTICSWANTEDLIST_ACK = MW_BASE + 0x00F5;
    public const ushort MW_GUILDTACTICSVOLUNTEERING_REQ = MW_BASE + 0x00F6;
    public const ushort MW_GUILDTACTICSVOLUNTEERING_ACK = MW_BASE + 0x00F7;
    public const ushort MW_GUILDTACTICSVOLUNTEERINGDEL_REQ = MW_BASE + 0x00F8;
    public const ushort MW_GUILDTACTICSVOLUNTEERINGDEL_ACK = MW_BASE + 0x00F9;
    public const ushort MW_GUILDTACTICSVOLUNTEERLIST_REQ = MW_BASE + 0x00FA;
    public const ushort MW_GUILDTACTICSVOLUNTEERLIST_ACK = MW_BASE + 0x00FB;
    public const ushort MW_GUILDTACTICSREPLY_REQ = MW_BASE + 0x00FC;
    public const ushort MW_GUILDTACTICSREPLY_ACK = MW_BASE + 0x00FD;
    public const ushort MW_GUILDTACTICSKICKOUT_REQ = MW_BASE + 0x00FE;
    public const ushort MW_GUILDTACTICSKICKOUT_ACK = MW_BASE + 0x00FF;
    public const ushort MW_GUILDTACTICSINVITE_REQ = MW_BASE + 0x0135;
    public const ushort MW_GUILDTACTICSINVITE_ACK = MW_BASE + 0x0136;
    public const ushort MW_GUILDTACTICSANSWER_REQ = MW_BASE + 0x0137;
    public const ushort MW_GUILDTACTICSANSWER_ACK = MW_BASE + 0x0138;
    public const ushort MW_GUILDTACTICSLIST_REQ = MW_BASE + 0x0139;
    public const ushort MW_GUILDTACTICSLIST_ACK = MW_BASE + 0x013A;

    // --- Phase 2b: guild points / pvp record / money / skill ---
    public const ushort MW_GUILDPOINTLOG_REQ = MW_BASE + 0x0123;
    public const ushort MW_GUILDPOINTLOG_ACK = MW_BASE + 0x0124;
    public const ushort MW_GUILDPOINTREWARD_REQ = MW_BASE + 0x0125;
    public const ushort MW_GUILDPOINTREWARD_ACK = MW_BASE + 0x0126;
    public const ushort MW_GUILDPVPRECORD_REQ = MW_BASE + 0x0127;
    public const ushort MW_GUILDPVPRECORD_ACK = MW_BASE + 0x0128;
    public const ushort MW_GUILDPOINTTAKE_REQ = MW_BASE + 0x0129;
    public const ushort MW_GUILDMONEYRECOVER_ACK = MW_BASE + 0x012C;
    public const ushort MW_GUILDSKILLACTION_REQ = MW_BASE + 0x022B;
    public const ushort MW_GUILDSKILLACTION_ACK = MW_BASE + 0x022C;
    public const ushort MW_UPDATEGUILDCOOLDOWN_ACK = MW_BASE + 0x022D;
    public const ushort MW_UPDATEGUILDCOOLDOWN_REQ = MW_BASE + 0x022E;

    // --- Phase 2b: corps + party move/recall ---
    public const ushort MW_PARTYATTR_REQ = MW_BASE + 0x004A;
    public const ushort MW_CORPSASK_REQ = MW_BASE + 0x006B;
    public const ushort MW_CORPSASK_ACK = MW_BASE + 0x006C;
    public const ushort MW_CORPSREPLY_REQ = MW_BASE + 0x006D;
    public const ushort MW_CORPSREPLY_ACK = MW_BASE + 0x006E;
    public const ushort MW_CORPSJOIN_REQ = MW_BASE + 0x006F;
    public const ushort MW_ADDSQUAD_REQ = MW_BASE + 0x0070;
    public const ushort MW_DELSQUAD_REQ = MW_BASE + 0x0071;
    public const ushort MW_CORPSLEAVE_ACK = MW_BASE + 0x0072;
    public const ushort MW_DELCORPSUNIT_REQ = MW_BASE + 0x0073;
    public const ushort MW_ADDCORPSUNIT_REQ = MW_BASE + 0x0074;
    public const ushort MW_CORPSCMD_REQ = MW_BASE + 0x0075;
    public const ushort MW_CORPSCMD_ACK = MW_BASE + 0x0092;
    public const ushort MW_CORPSENEMYLIST_REQ = MW_BASE + 0x0093;
    public const ushort MW_CORPSENEMYLIST_ACK = MW_BASE + 0x0094;
    public const ushort MW_CORPSHP_REQ = MW_BASE + 0x009D;
    public const ushort MW_CORPSHP_ACK = MW_BASE + 0x009E;
    public const ushort MW_PARTYMOVE_REQ = MW_BASE + 0x009F;
    public const ushort MW_PARTYMOVE_ACK = MW_BASE + 0x00A0;
    public const ushort MW_CHGCORPSCOMMANDER_ACK = MW_BASE + 0x00A3;
    public const ushort MW_CHGCORPSCOMMANDER_REQ = MW_BASE + 0x00A4;
    public const ushort MW_REPORTENEMYLIST_REQ = MW_BASE + 0x00A5;
    public const ushort MW_CHGSQUADCHIEF_REQ = MW_BASE + 0x00A6;
    public const ushort MW_PARTYMEMBERRECALL_REQ = MW_BASE + 0x0103;
    public const ushort MW_PARTYMEMBERRECALL_ACK = MW_BASE + 0x0104;
    public const ushort MW_PARTYMEMBERRECALLANS_REQ = MW_BASE + 0x0113;
    public const ushort MW_PARTYMEMBERRECALLANS_ACK = MW_BASE + 0x0114;

    // --- Phase 3: friends (MWProtocol.h 0x50-0x60). World replies *_REQ to maps; map sends *_ACK. ---
    public const ushort MW_FRIENDASK_REQ = MW_BASE + 0x0050;
    public const ushort MW_FRIENDASK_ACK = MW_BASE + 0x0051;
    public const ushort MW_FRIENDADD_REQ = MW_BASE + 0x0052;
    public const ushort MW_FRIENDREPLY_ACK = MW_BASE + 0x0053;
    public const ushort MW_FRIENDERASE_REQ = MW_BASE + 0x0054;
    public const ushort MW_FRIENDERASE_ACK = MW_BASE + 0x0055;
    public const ushort MW_FRIENDLIST_REQ = MW_BASE + 0x0056;
    public const ushort MW_FRIENDREGION_REQ = MW_BASE + 0x0057;
    public const ushort MW_FRIENDCONNECTION_REQ = MW_BASE + 0x0058;
    public const ushort MW_FRIENDGROUPMAKE_REQ = MW_BASE + 0x0059;
    public const ushort MW_FRIENDGROUPMAKE_ACK = MW_BASE + 0x005A;
    public const ushort MW_FRIENDGROUPDELETE_REQ = MW_BASE + 0x005B;
    public const ushort MW_FRIENDGROUPDELETE_ACK = MW_BASE + 0x005C;
    public const ushort MW_FRIENDGROUPCHANGE_REQ = MW_BASE + 0x005D;
    public const ushort MW_FRIENDGROUPCHANGE_ACK = MW_BASE + 0x005E;
    public const ushort MW_FRIENDGROUPNAME_REQ = MW_BASE + 0x005F;
    public const ushort MW_FRIENDGROUPNAME_ACK = MW_BASE + 0x0060;
    public const ushort MW_FRIENDLIST_ACK = MW_BASE + 0x012E;
    public const ushort MW_FRIENDPROTECTEDASK_ACK = MW_BASE + 0x008D;

    // --- Phase 3: soulmate (MWProtocol.h 0x105-0x10E) ---
    public const ushort MW_SOULMATE_REQ = MW_BASE + 0x0105;
    public const ushort MW_SOULMATE_ACK = MW_BASE + 0x0106;
    public const ushort MW_SOULMATESEARCH_REQ = MW_BASE + 0x0107;
    public const ushort MW_SOULMATESEARCH_ACK = MW_BASE + 0x0108;
    public const ushort MW_SOULMATEREG_REQ = MW_BASE + 0x0109;
    public const ushort MW_SOULMATEREG_ACK = MW_BASE + 0x010A;
    public const ushort MW_SOULMATEEND_REQ = MW_BASE + 0x010B;
    public const ushort MW_SOULMATEEND_ACK = MW_BASE + 0x010C;
    public const ushort MW_SOULMATEDEL_REQ = MW_BASE + 0x010D;
    public const ushort MW_SOULMATEDEL_ACK = MW_BASE + 0x010E;

    // --- Phase 3: rankings / monthly rollover (MWProtocol.h) ---
    public const ushort MW_MONTHRANKUPDATE_REQ = MW_BASE + 0x013D;
    public const ushort MW_MONTHRANKUPDATE_ACK = MW_BASE + 0x013E;
    public const ushort MW_MONTHRANKLIST_REQ = MW_BASE + 0x013F;
    public const ushort MW_MONTHRANKRESET_REQ = MW_BASE + 0x0140;
    public const ushort MW_WARLORDSAY_REQ = MW_BASE + 0x0141;
    public const ushort MW_WARLORDSAY_ACK = MW_BASE + 0x0142;
    public const ushort MW_FIRSTGRADEGROUP_REQ = MW_BASE + 0x0143;
    public const ushort MW_FAMERANKUPDATE_REQ = MW_BASE + 0x0169;
    public const ushort MW_FAMERANKUPDATE_ACK = MW_BASE + 0x016A;
    public const ushort MW_MONTHRANKRESETCHAR_REQ = MW_BASE + 0x016E;
    public const ushort MW_MONTHRANKRESETCHAR_ACK = MW_BASE + 0x016F;

    // --- Phase 4: Battle of the Warlords (BoW). Map sends *_REQ to world; world replies/commands *_ACK
    //     to the char's map or *_REQ/*_ACK to the dedicated BoW map server. (MWProtocol.h 0x194-0x200) ---
    public const ushort MW_ADDTOBOWQUEUE_REQ = MW_BASE + 0x0194;        // map -> world: char joins queue
    public const ushort MW_BOWTIMEUPDATE_ACK = MW_BASE + 0x0195;        // world -> all maps: status + countdown + points
    public const ushort MW_PREPAREFORBOW_REQ = MW_BASE + 0x0196;        // world -> char map: assign team, prep teleport
    public const ushort MW_BOWCOMMANDEXEC_REQ = MW_BASE + 0x0197;       // world -> BoW map: START / END
    public const ushort MW_ENDBOWWAR_REQ = MW_BASE + 0x0198;            // world -> BoW map: end + winner + player list
    public const ushort MW_BOWPOINTSUPDATE_REQ = MW_BASE + 0x0199;      // BoW map -> world: a country scored
    public const ushort MW_ADDTOBOWQUEUE_ACK = MW_BASE + 0x019A;        // world -> char map: queue-join result
    public const ushort MW_CANCELBOWQUEUE_REQ = MW_BASE + 0x019B;       // map -> world: leave queue
    public const ushort MW_CANCELBOWQUEUE_ACK = MW_BASE + 0x019C;       // world -> char map: cancel result
    public const ushort MW_NOTIFYNONQUEUEDPLAYER_ACK = MW_BASE + 0x019D;// world -> char map: battle started, you're not in it
    public const ushort MW_ADDBOWPLAYERS_ACK = MW_BASE + 0x019E;        // world -> BoW map: the full matched roster
    public const ushort MW_LEAVEBATTLEFIELD_REQ = MW_BASE + 0x019F;     // map -> world: char left the field early
    public const ushort MW_RELEASESINGLEBOWPLAYER_REQ = MW_BASE + 0x0200; // world -> BoW map: release one player

    // --- Phase 4b: Battle Royale (BR). (MWProtocol.h 0x201-0x20D) ---
    public const ushort MW_ADDTOBRQUEUE_REQ = MW_BASE + 0x0201;         // map -> world: join BR queue / ready
    public const ushort MW_BRTIMEUPDATE_ACK = MW_BASE + 0x0202;        // world -> all maps: status + countdown + type
    public const ushort MW_BRTEAMMATEADD_ACK = MW_BASE + 0x0203;       // world -> char map: teammate-add prompt/result
    public const ushort MW_BRTEAMMATEADD_REQ = MW_BASE + 0x0204;       // map -> world: invite a teammate
    public const ushort MW_BRTEAMMATEADDRESULT_ACK = MW_BASE + 0x0205; // map -> world: teammate's answer
    public const ushort MW_UPDATEBRTEAM_ACK = MW_BASE + 0x0206;        // world -> char map: current premade-team state
    public const ushort MW_BRTEAMMATEDEL_REQ = MW_BASE + 0x0207;       // map -> world: kick/leave premade
    public const ushort MW_ADDBRTEAMS_ACK = MW_BASE + 0x0207;          // world -> BR map: the full matched bracket (same value, BR-server bound)
    public const ushort MW_PREPAREFORBR_REQ = MW_BASE + 0x0208;        // world -> char map: prep teleport to BR map
    public const ushort MW_ADDTOBRQUEUE_ACK = MW_BASE + 0x0209;        // world -> char map: queue-join result
    public const ushort MW_VOTEFORBRMAP_REQ = MW_BASE + 0x020A;        // map -> world: vote map / mode
    public const ushort MW_BRCOMMANDEXEC_REQ = MW_BASE + 0x020B;       // world -> BR map: START / END
    public const ushort MW_RELEASESINGLEBRPLAYER_REQ = MW_BASE + 0x020C; // world -> BR map: release one player
    public const ushort MW_ENDBRWAR_REQ = MW_BASE + 0x020D;            // world -> BR map: end + team list
    public const ushort MW_CMTELEPORTBATTLEMODE_REQ = MW_BASE + 0x0222; // GM: teleport into BoW/BR

    // --- Phase 4d: tournament. Map sends MW_TOURNAMENT_ACK with a wProtocol sub-command; world replies via
    //     MW_TOURNAMENT_REQ / TOURNAMENTINFO_REQ / TOURNAMENTENABLE_REQ. (MWProtocol.h 0x144-0x161) ---
    public const ushort MW_TOURNAMENTENABLE_REQ = MW_BASE + 0x0144;   // world -> maps: step enable + next-step time
    public const ushort MW_TOURNAMENT_REQ = MW_BASE + 0x0145;         // world -> char map: a sub-command result
    public const ushort MW_TOURNAMENT_ACK = MW_BASE + 0x0146;         // map -> world: a sub-command (wProtocol)
    public const ushort MW_TOURNAMENTAPPLYINFO_REQ = MW_BASE + 0x0147;
    public const ushort MW_TOURNAMENTAPPLYINFO_ACK = MW_BASE + 0x0148;
    public const ushort MW_TOURNAMENTAPPLY_REQ = MW_BASE + 0x0149;
    public const ushort MW_TOURNAMENTAPPLY_ACK = MW_BASE + 0x014A;
    public const ushort MW_TOURNAMENTJOINLIST_REQ = MW_BASE + 0x014B;
    public const ushort MW_TOURNAMENTJOINLIST_ACK = MW_BASE + 0x014C;
    public const ushort MW_TOURNAMENTPARTYLIST_REQ = MW_BASE + 0x014D;
    public const ushort MW_TOURNAMENTPARTYLIST_ACK = MW_BASE + 0x014E;
    public const ushort MW_TOURNAMENTPARTYADD_REQ = MW_BASE + 0x014F;
    public const ushort MW_TOURNAMENTPARTYADD_ACK = MW_BASE + 0x0150;
    public const ushort MW_TOURNAMENTMATCHLIST_REQ = MW_BASE + 0x0151;
    public const ushort MW_TOURNAMENTMATCHLIST_ACK = MW_BASE + 0x0152;
    public const ushort MW_TOURNAMENTEVENTLIST_ACK = MW_BASE + 0x0154;
    public const ushort MW_TOURNAMENTEVENTINFO_ACK = MW_BASE + 0x0156;
    public const ushort MW_TOURNAMENTEVENTJOIN_ACK = MW_BASE + 0x0158;
    public const ushort MW_TOURNAMENTEVENTLIST_REQ = MW_BASE + 0x0153;
    public const ushort MW_TOURNAMENTEVENTINFO_REQ = MW_BASE + 0x0155;
    public const ushort MW_TOURNAMENTEVENTJOIN_REQ = MW_BASE + 0x0157;
    public const ushort MW_TOURNAMENTMATCH_REQ = MW_BASE + 0x0159;
    public const ushort MW_TOURNAMENTINFO_REQ = MW_BASE + 0x015A;     // world -> maps: bracket/entry config
    public const ushort MW_TOURNAMENTENTERGATE_ACK = MW_BASE + 0x015B;// map -> world: enter the arena gate (set bet ticket)
    public const ushort MW_TOURNAMENTRESULT_REQ = MW_BASE + 0x015C;   // world -> maps: a match outcome
    public const ushort MW_TOURNAMENTRESULT_ACK = MW_BASE + 0x015D;   // map -> world: a match outcome
    public const ushort MW_TOURNAMENTPARTYDEL_ACK = MW_BASE + 0x015E;
    public const ushort MW_TOURNAMENTSCHEDULE_REQ = MW_BASE + 0x015F;
    public const ushort MW_TOURNAMENTSCHEDULE_ACK = MW_BASE + 0x0160;
    public const ushort MW_TOURNAMENTBATPOINT_REQ = MW_BASE + 0x0161; // world -> char map: betting payout

    // --- Phase 4c: scheduled battle-field enables (world -> all maps), driven by the battle-time machine. ---
    public const ushort MW_LOCALENABLE_REQ = MW_BASE + 0x0067;     // local (open-field) PvP window
    public const ushort MW_CASTLEENABLE_REQ = MW_BASE + 0x007E;    // castle-siege window
    public const ushort MW_MISSIONENABLE_REQ = MW_BASE + 0x0165;   // mission/event window
    public const ushort MW_SKYGARDENENABLE_REQ = MW_BASE + 0x0181; // sky-garden window (off in the shipped build)
}

/// <summary>enum TGUILD_RESULT (NetCode.h).</summary>
public enum GuildResult : byte
{
    Success = 0,
    JoinDeny,
    JoinBusy,
    Fail,
    AlreadyGuildName,
    NotChief,
    AlreadyMember,
    NotMember,
    HaveGuild,
    NotFound,
    EstablishErr,
    DisorganizationErr,
    LeaveSelf,
    LeaveKick,
    LeaveDisorganization,
    JoinSuccess,
    NoDuty,
    MemberFull,
    MismatchLevel,
    SameGuildTactics,
}

/// <summary>enum FRIEND_RESULT (NetCode.h).</summary>
public enum FriendResult : byte
{
    Success = 0,
    Refuse,
    Busy,
    NotFound,
    Already,
    Max,
}

/// <summary>enum FRIEND_TYPE (NetCode.h): one-way target / mutual friend.</summary>
public enum FriendType : byte
{
    Friend = 0,       // I added them; they have not added me back
    Target,           // they added me; I have not added them
    FriendFriend,     // mutual
}

/// <summary>enum FRIEND_CONNECTION (NetCode.h): online / offline notify.</summary>
public enum FriendConnState : byte
{
    Connection = 0,
    Disconnection,
}

/// <summary>enum SOULMATE_RESULT (NetCode.h).</summary>
public enum SoulmateResult : byte
{
    Success = 0,
    Fail,
    Silence,
    NotFound,
    NeedMoney,
    Already,
    NpcCallError,
    InvalidPos,
}

/// <summary>enum TCONTRY_TYPE (NetCode.h): the BoW/PvP team/country values.</summary>
public enum Contry : byte
{
    Defugel = 0, // TCONTRY_D
    Craxion = 1, // TCONTRY_C
    Broa = 2,    // TCONTRY_B
    None = 3,    // TCONTRY_N
    Peace = 4,   // TCONTRY_PEACE
}

/// <summary>enum TELEPORT_RESULT (NetCode.h) — result code carried by MW_TELEPORT_REQ.</summary>
public enum TprResult : byte
{
    Success = 0,        // TPR_SUCCESS
    NotTeleportNpc = 1, // TPR_NOTTELEPORTNPC
    NoPortal = 2,       // TPR_NOPORTAL
    NoDestination = 3,  // TPR_NODESTINATION
    NeedMoney = 4,      // TPR_NEEDMONEY
    NoItem = 5,         // TPR_NOITEM
    Invalid = 6,        // TPR_INVALID
    Used = 7,           // TPR_USED
    Channel = 8,        // TPR_CHANNEL
}

/// <summary>enum CASTLEBESIEGE_STATUS (NetCode.h) — result of a castle-war application.</summary>
public enum CastleApplyResult : byte
{
    Success = 0,    // CBS_SUCCESS
    Full = 1,       // CBS_FULL
    NotFound = 2,   // CBS_NOTFOUND
    NotReady = 3,   // CBS_NOTREADY
    CantApply = 4,  // CBS_CANTAPPLY
}

/// <summary>enum OCCUPY_TYPE (NetCode.h) — a guild's role in a local's occupation snapshot.</summary>
public enum OccupyType : byte
{
    Defend = 0,  // OCCUPY_DEFEND (bonus 11)
    Accept = 1,  // OCCUPY_ACCEPT (bonus 10, counts toward per-day occupation)
}

/// <summary>enum MONITEMTAKE_RESULT (NetCode.h) — result code for looting a monster-drop item.</summary>
public enum MonItemTake : byte
{
    Success = 0,    // MIT_SUCCESS
    FullInven = 1,  // MIT_FULLINVEN
    NotFound = 2,   // MIT_NOTFOUND
    Authority = 3,  // MIT_AUTHORITY
    Dealing = 4,    // MIT_DEALING
    Lottery = 5,    // MIT_LOTTERY
}

/// <summary>enum BATTLE_STATUS (NetCode.h) — the BoW/BR/castle phase machine states.</summary>
public enum BattleStatus : byte
{
    Normal = 0,
    Battle = 1,
    Alarm = 2,
    Ready = 3,
    Peace = 4,
    OpenLGate = 5,
    OpenRGate = 6,
    NoBattle = 7,
    SkyGardenStart = 8,
    WaitTime = 9,
    BodPeace = 10,
}

/// <summary>enum BOWREG_RESULT (NetCode.h). Note: the late-join path returns <c>Fail + 1</c> (= 4).</summary>
public enum BowReg : byte
{
    Success = 0,
    Country = 1,
    AlreadyInQueue = 2,
    Fail = 3,
    JoinedLate = 4, // BOWREG_FAIL + 1, the in-battle late-join code
}

/// <summary>enum BOWMATCH_RESULT (NetCode.h).</summary>
public enum BowMatch : byte { Success = 0, Fail = 1 }

/// <summary>enum BOW_WINNER (NetCode.h).</summary>
public enum BowWinner : byte { Defugel = 0, Craxion = 1, Tie = 2 }

/// <summary>enum TBOW_COMMANDS (NetCode.h).</summary>
public enum BowCommand : byte { Start = 0, End = 1 }

/// <summary>enum TOURNAMENT_RESULT (NetCode.h).</summary>
public enum TournamentResult : byte
{
    Success = 0,
    Disqualify = 1,   // not in the 1st-grade group during the 1st-grade step
    Timeout = 2,      // outside the registration window
    AlreadyReg = 3,
    NotFound = 4,
    Full = 5,
    Class = 6,
    Money = 7,
    Item = 8,
    Level = 9,
    Fail = 10,
}

/// <summary>enum TOURNAMENT entry type (NetCode.h): ENTRY_PARTY = 1.</summary>
public enum TournamentEntryType : byte { Solo = 0, Party = 1 }

/// <summary>enum TNMTWIN (NetCode.h): a match result per round.</summary>
public enum TnmtWin : byte { None = 0, Win = 1, Lose = 2 }

/// <summary>enum TNMT_STEP (NetCode.h) — the tournament progression steps.</summary>
public enum TnmtStep : byte
{
    Ready = 0, First = 1, Normal = 2, Party = 3, Match = 4, Enter = 5,
    QFinal = 6, QFEnd = 7, SFEnter = 8, SFinal = 9, SFEnd = 10,
    FEnter = 11, Final = 12, End = 13, Count = 14,
}

/// <summary>enum BATTLE_TYPE (TWorldType.h) — the scheduled battle-field kinds.</summary>
public enum BattleType : byte
{
    Local = 0,
    Castle = 1,
    Tournament = 2,
    Mission = 3,
    SkyGarden = 4,
    Bow = 5,
    Br = 6,
    Count = 7,
}

/// <summary>BR battle type (BR_TYPE_TEAM): solo matchmaking vs premade teams.</summary>
public enum BrType : byte { Solo = 0, Team = 1 }

/// <summary>BR team size mode: 3v3 (party of 3) or 2v2 (party of 2).</summary>
public enum BrMode : byte { ThreeV3 = 0, TwoV2 = 1 }

/// <summary>enum (NetCode.h) BR start/end command.</summary>
public enum BrCommand : byte { Start = 0, End = 1 }

/// <summary>enum TEAMADD_RESULT (NetCode.h) — BR premade invite answers.</summary>
public enum TeamAdd : byte
{
    Success = 0,
    NotFound = 1,
    Busy = 2,
    Refuse = 3,
    AlreadyInTeam = 4,
}

/// <summary>enum TCLASS_KIND (NetCode.h) — the six base classes (used by BR role bucketing).</summary>
public enum TClass : byte
{
    Warrior = 0,
    Ranger = 1,
    Archer = 2,
    Wizard = 3,
    Priest = 4,
    Sorcerer = 5,
}

/// <summary>enum guild duty (rank): regular / vice-chief / chief.</summary>
public enum GuildDuty : byte
{
    None = 0,
    ViceChief = 1,
    Chief = 2,
}

/// <summary>Answer codes for invite/party prompts (ASK_YES = 0).</summary>
public static class Ask
{
    public const byte Yes = 0;
    public const byte No = 1;
    public const byte Busy = 2;
}

/// <summary>enum TCONNECT_RESULT (subset used by Phase-1 char-session flow).</summary>
public enum ConnectResult : byte
{
    Success = 0,
    Invalid,
    Duplicate,
    Internal,
}

/// <summary>Assorted constants.</summary>
public static class Proto
{
    public const int WorldPort = 3815;   // DEF_WORLDPORT

    public const byte CnSuccess = 0;     // CN_SUCCESS (connect-result code)

    public const uint LocalOccupyStatExp = 1000;   // LOCALOCCUPY_STATEXP — guild stat-exp for taking a local
    public const uint CastleOccupyStatExp = 2000;  // CASTLEOCCUPY_STATEXP — guild stat-exp for taking a castle
    public const uint GuildStatExpPerLevel = 2400; // CALCULATE_NEXTGEXP(level) = level * 2400

    public const byte BowServerId = 30;  // BOW_SERVER_ID
    public const byte BrServerId = 50;   // BR_SERVER_ID
    public const ushort BowMapId = 3000; // BOW_MAP_ID
    public const uint DayOne = 86400;    // DAY_ONE (seconds per day)
    public const uint HourOne = 3600;    // HOUR_ONE

    public const int TournamentSlot = 8;       // TOURNAMENT_SLOT
    public const int TournamentBasePrize = 100;// TOURNAMENT_BASEPRIZE

    public const int MaxPartyMember = 7;     // MAX_PARTY_MEMBER
    public const byte MaxGuildPeer = 5;      // MAX_GUILD_PEER_COUNT
    public const ushort PartyIdMin = 0x100;  // party/corps id allocation range
    public const ushort PartyIdMax = 0xFFFF;

    // --- Phase 3: friends / soulmate / rankings ---
    public const int MaxFriend = 64;          // MAX_FRIEND
    public const int MaxFriendGroup = 5;      // MAX_FRIENDGROUP
    public const int MaxGroupName = 20;       // MAX_GROUPNAME
    public const int SoulmateLevel = 10;      // SOULMATE_LEVEL (max level gap)
    public const uint SoulmateSilenceDuration = 86400; // SOULMATE_SILENCE_DURATION (1 day)

    public const int CountryCount = 3;        // COUNTRY_COUNT (Defugel/Craxion/Broa)
    public const int MonthRankCount = 33;     // MONTHRANKCOUNT
    public const int FameRankCount = 9;       // FAMERANKCOUNT
    public const int FirstGradeGroupCount = 17; // FIRSTGRADEGROUPCOUNT
    public const int TotalMonthRankCount = FirstGradeGroupCount * 3; // TOTALMONTHRANKCOUNT

    /// <summary>Server-id WORD layout: MAKEWORD(bServerID, bServerType).</summary>
    public static byte ServerIdOf(ushort wId) => (byte)(wId & 0xFF);
    public static byte ServerTypeOf(ushort wId) => (byte)(wId >> 8);
}
