# TControlSvr → TControlSvr.Net — Port Status

Faithful C#/.NET 10 port of the C++/ATL `TControlSvr` operations hub. Byte-compatible wire/DB with this
repo's `CTProtocol.h` + SQL baselines. **17 tests passing** (xUnit, DB-free) · listens on **:3615** ·
connects out to game servers and interoperates with `TWorldSvr.Net`.

**All 64 `CT_*` handlers + the `SM_DELSESSION`/`CT_TIMER`/`CT_NEWCONNECT` internals from the C++ dispatch
are ported.** The Windows-only operational plumbing (Service Control Manager, PDH performance counters,
SMB file upload) is abstracted behind `Ops/` interfaces with cross-platform no-op defaults, matching the
sibling ports' "wired-but-disabled" philosophy.

---

## ✅ Done

- [x] **Wire + protocol** — plaintext 16-byte TNetLib framing (copied from `TWorld.Protocol`); all CT ids
  from `CTProtocol.h`; `Proto` constants (`SVRGRP_*`, `DCSVC_STAT_*`, `MAKESVRID`, `MANAGER_CLASS`,
  `EVENT_*`); byte-exact `EventInfo` (`WrapPacketIn/Out`) + `szValue` pack/unpack (`ParseStrValue`/`MakeStrValue`).
- [x] **Topology load** — `TMACHINE` + `TNETWORK` + `TIPADDR` → machines; `TGROUP`, `TSVRTYPE (bControl=1)`,
  `TSERVER (bType<>6)` → services (keyed by `MAKESVRID`); `TEVENTCHART` → the event map; `TCASHSHOPITEMCHART`;
  `TLoadService(CTLSVR)` → own address.
- [x] **Operator login / authority** — `CT_OPLOGIN` / `CT_STLOGIN` (via `TOPLogin`; dup-operator kick;
  DB-less dev mode grants full authority), `CheckAuthority` (`CT_AUTHORITY_ACK` on denial), the topology
  list burst (`GROUPLIST`/`MACHINELIST`/`SVRTYPELIST`/`SERVICEAUTOSTART`).
- [x] **Service monitor / control** — the 1 s tick (`CT_TIMER` → `CT_SERVICEMONITOR` poll, 60 s timeout
  detection + SMS, `CT_SERVICEDATA` push to managers), `CT_SERVICESTAT`, `CT_SERVICECONTROL` (via the
  no-op controller, world-stop group cascade), `CT_SERVICEAUTOSTART`, `CT_SERVICEDATACLEAR`,
  `CT_SERVICECHANGE` broadcast, `CT_RECONNECT`, `CT_CTRLSVR` outbound registration.
- [x] **GM-command relays** — `USERKICKOUT`, `USERMOVE`, `USERPOSITION`, `CHARMSG`, `ANNOUNCEMENT`,
  `CHATBAN` (+ two-phase gather/`CHATBAN_ACK`), `CHATBANLIST`/`CHATBANLISTDEL`, `USERPROTECTED`
  (`TUserProtectedAdd`), `MONSPAWNFIND`(+ACK), `MONACTION`, `ITEMFIND`(+ACK), `ITEMSTATE`(+ACK),
  `CASTLEINFO`(+ACK), `CASTLEGUILDCHG`(+ACK), `CASTLEENABLE` (`SM_BATTLESTATUS_REQ`), `HELPMESSAGE`,
  `RPSGAMEDATA`(+ACK)/`RPSGAMECHANGE`, `TOURNAMENTEVENT`(+ACK), `EVENTQUARTERLIST`(+ACK)/
  `EVENTQUARTERUPDATE`(+ACK), `CMGIFT`(+ACK)/`CMGIFTLIST`(+ACK)/`CMGIFTCHARTUPDATE` — each authority-gated,
  routed by server-type + group, with the manager-id ACK round-trip. **Body-forwarding fidelity verified**
  against `Sender.cpp`/`TServer.cpp`: `Copy` (keep whole, incl. RPS group byte + ACK id tokens) vs.
  `CopyData(pkt, sizeof(BYTE))` (strip the routing group byte: help/tournament/quarter/cmgift-chart) vs.
  `CopyData(pkt, sizeof(DWORD))` (drop the manager-id token: `CMGIFTLIST_ACK`).
- [x] **Event engine** — `CheckEvent` (daily + term windows, start/end alarms, value push, auto-expiry),
  `CT_EVENTCHANGE` (overlap arbitration + `TEventUpdate`), `CT_EVENTLIST`, `CT_CASHITEMLIST`, and the
  internal fan (`EVENTMSG` relay-preferred, `EVENTUPDATE`, `CASHITEMSALE`, `CASHSHOPSTOP`, `EVENTDEL`),
  plus `SendEventToNewConnect` (push running events to a freshly-connected server).
- [x] **Patch / preversion** — `CT_UPDATEPATCH` (`TUpdateVersion`), `CT_PREVERSIONTABLE`,
  `CT_PREVERSIONUPDATE` (`TBetaToVersion` → `TDeletePreVersion` → `TUpdatePreVersion` → list); the
  `CT_SERVICEUPLOAD*` wire flow via `IFileDeploy` (no-op default).

---

## Faithful degradations (documented, matching the C++ "wired-but-disabled" pattern)

- **Windows Service Control Manager** → `IServiceController` / `NoOpServiceController`. `Start`/`Stop`/
  auto-start are logged no-ops; a service's status is derived from **connection liveness** (the connector
  dials each configured server and treats a live connection as RUNNING). A Windows `ServiceController`
  implementation can be dropped in behind the interface.
- **PDH platform counters** → `IPlatformMonitor` / `NoOpPlatformMonitor` (CPU/MEM/NET = 0 → `CT_PLATFORM`).
- **SMB admin-share file upload** → `IFileDeploy` / `NoOpFileDeploy` (upload wire flow accepted, bytes
  discarded). The DB-side patch/preversion procs are still executed.
- **`OPTool_SMSEmergency`** — best-effort (no-op when no DB).
- **Manager side not live-exercised** — no GM admin-tool client exists in this repo, so the operator-facing
  handlers are wire-tested (xUnit) rather than driven by a real tool. The server-facing relay **is**
  live-tested end-to-end against a simulated world.

## Byte-level audit (all 64 handlers + EVENTINFO)

A field-by-field wire audit compared every handler's read/write sequence against the C++ (`Handler.cpp`
reads, `Sender.cpp`/`TServer.cpp` sends, `TControlType.h` `EVENTINFO`). **`EVENTINFO` is an exact match in
both directions (23 fields).** Four real defects were found and fixed:

1. **`CT_SERVICECONTROL_ACK` return value** — inited `1` instead of C++'s `0`; a not-performed start/stop on
   a STOPPED/RUNNING service now returns `0` (only the non-controllable `default` case returns `1`).
2. **`CT_EVENTQUARTERUPDATE_ACK`** — the world sends `[bRet:BYTE][dwManagerID:DWORD]…`; the port skipped the
   leading `bRet`, so the manager id was parsed from garbage and the result never routed back. Now reads the
   byte first.
3. **`CT_EVENTQUARTERUPDATE_REQ`** — was missing the C++ `CheckAuthority(ALL)` + `m_dwID == dwManagerID`
   gate and fanned to only one world; now gated and fans to all matching worlds.
4. **`CT_SERVICEUPLOADEND_REQ`** — the non-cancel END packet carries a trailing final chunk
   `[wSize:WORD][wSize bytes]`; the port dropped it. Now consumed + written.

Everything else is byte-faithful. Intentional/known divergences (no wire-byte change): the `CheckEvent`
internal-loopback packets are fanned directly (game-server-facing bodies identical); `CT_OPLOGIN`'s
localhost gate is dead code in C++ (checked before the query runs) so it is omitted; the `CT_CHATBAN_ACK`
send-count decrement is guarded against underflow; `ParseStrValue` fixes the C++ LOTTERY multi-reward parse
and adds the missing GIFTTIME branch; the cash-sale `≤100` clamp and the `CASHSHOPSTOP` SvrId filter apply
uniformly (C++ clamps/filters inconsistently) — observable only for out-of-range values or `SvrId != 0`
cash-sale events. Korean (CP949) strings ride the shared `Latin1` codec as in the sibling ports.

## Interop note (RESOLVED in `TWorldSvr.Net`)

`CTProtocol.h` (authoritative, followed here): `CT_SERVICEMONITOR_REQ = CT_CONTROL + 0x1D` (0x931E),
`CT_SERVICEMONITOR_ACK = CT_CONTROL + 0x1E` (0x931F). `TWorldSvr.Net/TWorld.Protocol/NetCode.cs` had these
two **swapped** — never noticed because no control server existed to exercise them. **Fixed**: the two
offsets were swapped back to match `CTProtocol.h` (the world's handler usage — receive on `_ACK`, reply
with `_REQ` — was already correct, so only the constant values changed). Verified live: the control
server's 1 s poll (`CT_SERVICEMONITOR_ACK`/0x931F) now round-trips to the world's counts reply
(`CT_SERVICEMONITOR_REQ`/0x931E). TWorld's 15 control tests still pass.

## Not applicable here

- COM/ATL scaffolding (`.idl`/`.rgs`/AppID), the recursive `main()` input loop, the `DebugSocket` UDP log
  feed, and `TMiniDump` — Windows/ATL-only plumbing with no cross-platform equivalent (the port uses the
  Generic Host + Serilog instead).
- `TMANAGER` DB table load — already commented out in the C++ (`LoadData`); operator auth is via `TOPLogin`.
