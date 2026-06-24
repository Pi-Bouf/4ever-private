# TWorldSvr → TWorldSvr.Net — Port Status

Faithful C#/.NET 10 port of the C++/ATL `TWorldSvr`. Byte-exact wire/DB compat with this repo's
client + SQL baselines. **97 tests passing** (xUnit, DB-free harness) · container boots on **:3816**.

**The port is complete for a single-world live deployment.** Every `MW_*` (map↔world) handler, the
self-contained `CT_*` control plane, and all DB-persistence/config-load gaps are done. The only remaining
items require components that don't exist in this repo/deployment (a second world, a relay server, the
TControlSvr GM client) or are the GM/event-only subsystem — see the bottom section.

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

## ⬜ Remaining — requires components not present in this repo/deployment

These are the only items left. None can be exercised here: each is gated on an external peer (a second
world, a relay server, the GM operator client) or is a large GM/event-only subsystem. Implementing them
without a consumer would be untested, unverifiable code — so they're left as documented stubs/recognized
no-ops rather than speculative ports.

- [ ] **`CT_*` — 7 deferred handlers** (recognized in dispatch, no-op): `EVENTUPDATE` (the event/lottery/gift **subsystem** — `EVENTINFO` serialization + lottery/gift-time item distribution), `ITEMFIND`/`ITEMSTATE`/`EVENTQUARTERLIST`/`EVENTQUARTERUPDATE`/`CMGIFTCHARTUPDATE` (DB-job + GM-client driven), `TOURNAMENTEVENT` (tournament-admin DB). **Needs the TControlSvr + its GM client (not in this repo).**
- [ ] **`SM_*` server-to-server** — quit/del-session, guild-disorg sync, event quarters, change-day, tournament-event. The *behaviors* that matter single-world are already done via direct timer ticks (rank rollover, tournament scheduler, battle timer) and `OnDisconnect`. The rest is **multi-instance sync — needs a second world/map cluster.**
- [ ] **`RW_*` relay** — cross-map-instance visibility forwarding. Registration exists; forwarding is stubbed. **Needs a TRelay server (not ported).**
- [ ] **`DM_ACTIVECHARUPDATE`** — cross-server nation-balance reconciliation. **Multi-server only;** single-world buckets are maintained live.
- [ ] **Cash-mall gift catalog feed / take-check** — `CMGIFTCHARTUPDATE` (DB-assigned ids) + `CSPCMGiftCanTake`. **GM-client driven** (the MW gift-delivery + result routing is done; only the GM-tool-fed catalog remains).

## Out of scope

- `APEX_*` (Taiwan-only, `#ifdef __TW_APEX`)
