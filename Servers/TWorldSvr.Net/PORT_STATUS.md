# TWorldSvr → TWorldSvr.Net — Port Status

Faithful C#/.NET 10 port of the C++/ATL `TWorldSvr`. Byte-exact wire/DB compat with this repo's
client + SQL baselines. **119 tests passing** (xUnit, DB-free harness) · container boots on **:3816**.

**Every `TWorldSvr` handler is now ported — including the GM-only subsystems.** Beyond the
single-world-complete core, a later pass ported the active-char nation refresh
(`DM_ACTIVECHARUPDATE`), guild auto-extinction (`SM_GUILDDISORGANIZATION`), the CT item-admin + cash-mall
gift-catalog/take-check handlers, the full **event/lottery/quarter subsystem** (`CT_EVENTUPDATE` +
`CT_EVENTQUARTER*` + `SM_EVENTQUARTER*` / `SM_EVENTEXPIRED`), and the complete **GM tournament-event admin**
(`CT_TOURNAMENTEVENT` + `SM_TOURNAMENTEVENT`, now incl. the per-event schedule machinery and the
PLAYERADD char-info lookup) — see "Done — previously-deferred". **The DM-thread DB persistence the C++ does is
now wired too** (tournament apply/clear/status/result/payback, cash-item sale, tournament-event
schedule/entry/reward, help message) — all 17 target stored procs verified present in the `TGame_gsp`
baseline. What remains is only behaviors gated on peers absent from this repo (a relay, a second world) —
none change the handler surface. The precise list is at the bottom.

---

## ✅ Done

- [x] **Core session** — map connect, full char-login handshake (ENTERSVR→CHARINFO/ROUTE→CHARDATA→ENTERCHAR→**CHECKMAIN→CONRESULT**), close, keepalive, chat relay
- [x] **Guild** + **guild extended** (cabinet, articles, fame, wanted, volunteering, points, PvP record, skills, cooldowns, tactics sub-guild)
- [x] **Party** + **Corps**
- [x] **Friends** (incl. `PROTECTEDCHECK` online-status) + **Soulmate**
- [x] **Rankings** + monthly rollover
- [x] **BoW** + **BR** + **Tournament** (scheduler/bracket/betting) + **battle-time machine**
- [x] **Movement / teleport** — `TELEPORT`/`CONLIST`/`MAPSVRLIST`/`RELEASEMAIN`/`REGION`/`BEGINTELEPORT`/solo-map, ConCess queue, CheckMainCON, main hand-off
- [x] **Combat / loot** — `LEVELUP`, `MONSTERDIE`, `TAKEMONMONEY`, `ADDITEM`(+`RESULT`), `PARTYORDERTAKEITEM`
- [x] **Castle war (full lifecycle)** — `CASTLEAPPLY` → battle-time start → `CASTLEWARINFO` scoreboard + defender/attacker selection → `CASTLEOCCUPY`/`LOCALOCCUPY`/`MISSIONOCCUPY`/`SKYGARDENOCCUPY`/`ENDWAR` → war-end (week records + scoreboard clear)
- [x] **PvP scoring** — `GAINPVPPOINT`, `LOCALRECORD`
- [x] **Nation balance** — `WARCOUNTRYBALANCE` + `SetCharLevel` war-country ladder
- [x] **Pets / mounts / summons** — `MONTEMPT`(+`EVO`), `GETBLOOD`, `MAGICMIRROR`, `DEALITEMERROR`, `PETRIDING`, `HELMETHIDE`, `RECALLMONDEL`, `SPOLECNIKMONDEL`, `RECALLMONDATA`
- [x] **Char info / mail / misc** — `CHARSTATINFO`(+ANS), `POSTRECV`, `CHANGECHARBASE` (face/hair/race/sex/name/title), `HEROSELECT`
- [x] **TMS** multi-person private chat — send / invite / invite-ask / out
- [x] **Arena join** (`ARENAJOIN`)
- [x] **Summon creation** — `CREATERECALLMON` / `CREATESPOLECNIKMON` (GenRecallID alloc + full-record fan-out)
- [x] **Guild economy** — `GUILDPOINTREWARD` (chief→member PvP-point grant), `MONSTERBUY` (treasury spend)
- [x] **Minigames** — `RPSGAME` (win-keep period arbitration), `MEETINGROOM` (invite/accept + CT_USERMOVE teleport)
- [x] **Battle-mode status** — `BATTLEMODESTATUS` (BoW/BR snapshot)
- [x] **Cash mall** — `CMGIFT` / `CMGIFTRESULT` (gift delivery + result routing), `CASHITEMSALE` (all-maps-confirmed gate)
- [x] **CT control plane (self-contained ~13)** — service monitor, GM teleport (`USERMOVE`) / position, chat-ban, char-message, castle owner override (`CASTLEGUILDCHG`), broadcasts (`EVENTMSG` / `CASHSHOPSTOP` / `HELPMESSAGE`), RPS config read+change, gift-list, GM gift (`CMGIFT`). Fixed `CT_CTRLSVR_REQ` id (was wrongly `+0x0001`, now `+0x0058`).
- [x] **CT cash-item sale feeder** (`CASHITEMSALE`) — records the sale catalog, resets per-map confirm flags, fans to all maps; completes the round trip with the `MW_CASHITEMSALE_ACK` consumer. CT plane is now **dispatch-complete** (remaining handlers recognized, see below).

> **All `MW_*` handlers are ported.** Three handlers carry deferred downstreams (not MW-plane work):
> RPS chart **DB load** + win-record persistence; cash-mall gift catalog + cash-sale catalog are **CT-fed**
> (empty until the CT plane lands); the gift **take-check** and cash-sale **record write** ride the DM plane.
> Each degrades faithfully (e.g. an unconfigured RPS row → "win denied").

- [x] **Char lifecycle** — `ChangeCountry` (re-bucket nation balance + party/guild/tactics/social fix-ups), rename DB clean-up (friend/soulmate erase, wanted-app + tournament-player name), guild PvP-rank recompute (`CalcGuildRanking`), map-server-drop char cleanup (`SM_DELSESSION`, via `OnDisconnect`).

> **Single-world deployment is functionally complete.** Everything the client + map server exercise is
> ported, tested (97 xUnit cases), and verified live (char login, pets/summons, guild/party, castle war,
> BoW/BR, tournaments, TMS, the self-contained CT admin plane). The DB-persistence and config-load gaps are
> all closed (`GenRecallID`, `CTBLSvrMsg`, RPS chart, `CASTLEAPPLY`, RPS records, guild ranking).

---

## ✅ Done — previously-deferred handlers now ported (inline-DB, unit-tested)

A later pass ported the bulk of the "remaining" list. These no longer require the external peer to be
**implemented** (they're faithful ports of the C++ handlers); they only require it to be **live-exercised**.
Each folds the C++ `CT→DM→ACK` (separate DB-thread) round trip into a single inline best-effort call on the
batch task — the established Phase-2 simplification — and is covered by DB-free wire-format xUnit tests.
Test count rose **97 → 109**.

- [x] **`CT_ITEMFIND` / `CT_ITEMSTATE`** — GM item search (`CTBLItemFind`) + item init-state change
  (`TItemStateChange`), fanning `MW_ITEMSTATE_REQ` to all maps and `CT_ITEMSTATE_ACK` to control
  (`World/WorldService.ControlDb.cs`, `GameDatabase`). Tests: `ControlDbTests`.
- [x] **`CT_CMGIFTCHARTUPDATE`** (gift-catalog add/update/del; DB-assigned ids via `TCMGiftAdd`) + the catalog
  is now loaded at boot from `TCMGIFTCHART` and re-sent to control via `CT_CMGIFTLIST_ACK`.
- [x] **Cash-mall gift take-check** — `CSPCMGiftCanTake` wired into the `CT_CMGIFT` / `MW_CMGIFT` paths; the
  full `RouteCmGift` now implements the `CMGIFT_DUPLICATE → errGift` refetch (`→ CMGIFT_ERRPOST`) that was
  previously deferred (`World/WorldService.Mall.cs`).
- [x] **`DM_ACTIVECHARUPDATE`** — the active-char nation-balance rebuild (`CTBLActiveCharTable/Del`), ported as
  `RefreshActiveCharBucketsAsync` (best-effort at startup), **guarded** so an empty/absent table never wipes
  the live single-world buckets (`World/WorldService.Nation.cs`).
- [x] **`SM_GUILDDISORGANIZATION`** + the guild **auto-extinction** timer it drives — disbanding guilds are
  deleted (members + tactics unlinked, `TGuildDelete`) once the 7-day grace elapses, driven off the live
  `Guild.Disorg`/`Time` (`World/WorldService.GuildExtinction.cs`). Tests: `GuildExtinctionTests`.
- [x] **Event subsystem** — `CT_EVENTUPDATE` (byte-exact `EVENTINFO` read/write + the lottery / gift-time
  item distribution, delivered as system mail relayed via `MW_WORLDPOSTSEND_REQ` like the C++ `SendPost`),
  `CT_EVENTQUARTERLIST` / `CT_EVENTQUARTERUPDATE` (the `LUCKYEVENT` chart + `TGetItemName` lookups +
  `TEventQuarterUpdate`), and the SM half driven off the 1-second tick: `SM_EVENTQUARTER` /
  `SM_EVENTQUARTERNOTIFY` (the timed lucky-draw + world-chat announce, via `CheckEventQuarter`) and
  `SM_EVENTEXPIRED` (the sorted auto-expiry queue + `CheckEventExpired`, dropping expired guild-wanted /
  tactics-wanted ads). (`World/WorldService.Event.cs`, `EventModels.cs`.) Tests: `EventTests`.
- [x] **GM tournament-event admin** — `CT_TOURNAMENTEVENT`, **complete**: `TET_ENTRYADD`/`TET_ENTRYDEL` (the
  event-tournament entry+reward set, `m_mapTournament`); the per-event **schedule machinery**
  `TET_SCHEDULEADD`/`TET_SCHEDULEDEL` — `SetTournamentTime` date-math (Nth-weekday window → chained step
  start/end, this month then next), the window-overlap arbitration, and `TournamentUpdate`'s
  earliest-schedule-wins activation that copies the winning schedule's steps + entries into the running
  tournament (`m_mapTournamentSchedule`/`m_mapTournamentTime`); `TET_PLAYERADD` — the **DB char-info lookup**
  (`CTBLGetCharInfo` by name) + registration into the 1st-grade roster (folds `DM_TOURNAMENTEVENTCHARINFO`);
  `TET_PLAYERDEL` (drop a registrant); `TET_PLAYEREND` (seed+match via the live `TournamentSelectPlayer` /
  `TournamentMatchBroadcast`); and `TET_LIST` (schedules + entries + 1st-grade rosters back to control).
  (`World/WorldService.TournamentEvent.cs`.) Tests: `TournamentEventTests`, `TournamentEventScheduleTests`.
  Per-char rank in seeding is reported 0 (as elsewhere in the tournament port).
- [x] **DM-thread DB persistence** — every side-write the C++ does via `SayToDB` is now wired (best-effort,
  matching the inline pattern, no-op when no game DB): `TTournamentApply` (apply/party-add/del, GM
  player-add/del, unseeded unregister), `TTournamentClear`, `TTournamentStatus` (per step), `TTournamentResult`,
  `TTournamentPayback` (unseeded fee-back), `TCashItemSale` (per item once all maps confirm),
  `TTnmtEventTime`/`TTnmtEventSchedule`/`TTnmtEventDel` + `TTnmtEventEntry`/`TTnmtEventReward` (entry/reward
  writes sequenced after the clear sentinel), and `THelpMessage`. Proc names + param orders are verbatim from
  `DBAccess.h`; **all 17 verified present in the `TGame_gsp` baseline**. (`TWorld.Data/GameDatabase.cs` +
  call sites in `WorldService.{Mall,Control,Tournament,TournamentSched,TournamentResult,TournamentEvent}.cs`.)

## Genuinely N/A here (no behavior to add single-world)

- `SM_*` already covered via direct ticks/disconnect: `DELSESSION`, `QUITSERVICE` (no-op in C++ too),
  `BATTLESTATUS`, `MONTHRANKSAVE`, `TOURNAMENT`/`TOURNAMENTUPDATE`, `CHANGEDAY`.
- `DM_ACTIVECHARUPDATE`: ported, but a no-op reconcile single-world (the live buckets are exact).

## Not ported — not in these sources

- **`RW_*` relay plane** — there is no `TRelaySvr` in this release, so the relay plane is intentionally not
  ported (the world never gets a relay peer). The C++ `TWorldSvr` carries `RWHandler`/`RWSender`, but with no
  relay server they are dead code here.
