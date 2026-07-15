# TPatchSvr → TPatchSvr.Net — Port Status

Faithful C#/.NET 10 port of the C++/ATL `TPatchSvr` (the client patch/version server). Byte-exact wire/DB
compat with this repo's launcher (`Tools/TLauncher`) + SQL baselines. **14 tests passing** (xUnit, DB-free
harness) · boots + listens on **:3715**.

**Every `TPatchSvr` handler is ported.** This is a small, self-contained, client-facing server: the launcher
connects, the server answers with the download base URL + login-server endpoint + the list of patch files
newer than the client's version. There is no game state — each handler runs one query and writes one reply.

---

## Wire format (verified byte-exact against the launcher)

- **Plaintext TCP, no cipher.** The C++ `CSession::Say`→`Post` sends the raw buffer; the checksum field is
  written `0` and never validated (confirmed in both `TPatchSvr/Session.cpp` and `Tools/TLauncher/Packet.h`).
- **8-byte header** (`WORD wSize · WORD wID · DWORD dwChkSUM`) — deliberately **not** TWorldSvr.Net's 16-byte
  header. `TPatch.Protocol` therefore has its own header/framer/reader/writer (it does not reuse `TWorld.Protocol`).
- **Strings** = 4-byte LE length prefix + raw CP949/Latin1 bytes, no NUL. Scalars little-endian.
- **IDs** computed from `CT_PATCH = 0x4201` / `CT_CONTROL = 0x9301` (ProtocolBase.h) — see `TPatch.Protocol/Msg.cs`.

## ✅ Done — every handler

- [x] **`CT_NEWPATCH_REQ` → `CT_NEWPATCH_ACK`** (`0x420B`→`0x420C`) — the launcher's live path: `TVERSION` rows
  newer than the client version + `TMinBetaVer`, with the FTP base + login endpoint. Reply layout asserted
  field-by-field against the launcher's `OnCT_NEWPATCH_ACK`.
- [x] **`CT_PATCH_REQ` → `CT_PATCH_ACK`** (`0x4202`→`0x4203`) — the older list (no min-beta, no per-file beta).
- [x] **`CT_PREPATCH_REQ` → `CT_PREPATCH_ACK`** (`0x4207`→`0x4208`) — beta/prepatch list from `TPREVERSION`
  (per-file `betaVer` written first, prepatch FTP base).
- [x] **`CT_CHANGEIF_REQ` → `CT_NEWPATCH_ACK`** (`0x420E`) — `TUSER_INTERFACE` files, FTP base gets `"/interface"`,
  min beta = 0, path always empty (matches the C++).
- [x] **`CT_PREPATCHCOMPLETE_REQ`** (`0x420D`) — calls `TPreCompleteAdd`, then closes (C++ `EC_SESSION_EXIT`).
- [x] **`CT_PATCHSTART_REQ`** (`0x4206`) — closes the session (no reply; the launcher then downloads via FTP).
- [x] **`CT_SERVICEMONITOR_ACK` → `CT_SERVICEMONITOR_REQ`** (`0x931F`→`0x931E`) — control-plane monitor reply
  (session/user counts), marks the peer as a server, and sweeps client sessions idle > 60s.
- [x] **`CT_SERVICEDATACLEAR_ACK`** (`0x9350`) / **`CT_CTRLSVR_REQ`** (`0x9359`) — recognized no-ops (as in C++).

## ✅ Done — infrastructure

- [x] **Data layer** (`TPatch.Data`) — `PatchDatabase : IPatchSource` over `Microsoft.Data.SqlClient`.
  Tables `TVERSION` / `TPREVERSION` / `TUSER_INTERFACE`; procs `TMinBetaVer` / `TPreCompleteAdd` /
  `TLoadService`. Column + parameter orders transcribed verbatim from `TPatchSvr/DBAccess.h`.
- [x] **Config** — the C++ reads DSN/DBUser/DBPasswd/Port/ServerID from the registry (`TPATCH_GSP\Config`).
  Ported to `appsettings.json` + environment overrides (`Patch:*`), exactly like TWorldSvr.Net. Secrets
  (the DB connection string) stay out of the file — supply via env.
- [x] **Login endpoint** — resolved at startup from `TLoadService(0, SVRGRP_LOGINSVR)` when a DB is present
  (mirroring the C++ `LoadData`), else from config. The FTP base is the C++'s hard-coded default, overridable.
- [x] **Host** — .NET generic host + Serilog, single batch task serializing all handling (mirrors the C++
  IOCP workers + one shared session map), async TCP acceptor. Boots + serves with **no DB** (empty file
  lists) so it never hard-fails on a missing baseline.
- [x] **Tests** — `TPatch.Protocol.Tests` (6: 8-byte framing, string/scalar round-trip, past-end zero-fill,
  monitor skip) + `TPatch.Server.Tests` (8: every handler exercised end-to-end over the real net path with a
  fake `IPatchSource`, including close-after-`PATCHSTART`/`PREPATCHCOMPLETE` and unknown-id close).
- [x] **Docker / publish** — `Dockerfile` (EXPOSE 3715) + `publish-console.ps1`, matching the TWorld layout.

## Adapted / behavioral notes

- **FTP base URL** — the C++ `LoadData` hard-codes `http://…/patch/pvp` (overriding the `TLoadService(0,6)`
  result) and comments out the prepatch-FTP lookup. Preserved as the `Patch:FtpUrl` default; `PreFtpUrl` is a
  config value (empty by default). Override both for a real deployment.
- **Login port** — the C++ stores the raw DB `wPort` into `sin_port` (no `htons`) and sends it raw; the
  launcher reads it as a plain integer. The port carries a host-order value on the wire — reproduced exactly.
- **Idle sweep** — the 60s client-idle drop fires on a control monitor tick (as in C++), not on a timer.

## Not ported — not applicable here

- **`OnSM_QUITSERVICE_REQ`** — declared on the C++ module but **never added to the `OnReceive` switch**
  (dead in the original), so there is nothing to dispatch. Not ported.
- **Registry service self-install** (`/Service`, `/RegServer`, ATL `CAtlServiceModuleT`) — replaced by the
  generic host / container model, same as the other `.Net` ports.
