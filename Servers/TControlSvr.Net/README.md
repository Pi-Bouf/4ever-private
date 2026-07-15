# TControlSvr.Net

A C#/.NET 10 port of the C++/ATL **TControlSvr** — the 4Story cluster's **operations / control hub**.
It authenticates GM admin tools, monitors the game-server cluster, and relays GM commands to the world /
map / login / relay servers. Byte-compatible with this repo's protocol (`Lib/Own/TProtocol/…/CTProtocol.h`)
and SQL baselines, so it interoperates with the already-ported `TWorldSvr.Net`.

See **[PORT_STATUS.md](PORT_STATUS.md)** for the handler-by-handler port map and the faithful-degradation notes.

## What it is

TControlSvr plays two roles at once:

- **Server** — accepts GM admin-tool connections (the C++ `CTManager`), authenticates operators via
  `TOPLogin`, and serves topology + service-monitoring + admin commands.
- **Client** — connects **out** to each game server (the C++ `CTServer`), registers with `CT_CTRLSVR_REQ`,
  polls live counts (`CT_SERVICEMONITOR`), and relays GM commands (chat-ban, char-message, GM teleport,
  castle override, events, cash-sale, RPS, cash-mall gift, item find/state, help, tournament-event,
  monster, announcement, kick-out), routing each server's reply back to the originating manager.

It also runs the **scheduled-event engine** (`CheckEvent`: daily/term event start-end alarms, lottery /
gift-time distribution, cash-sale windows) and persists via the DB (`TEventUpdate`, `TUserProtectedAdd`,
the patch procs, `OPTool_SMSEmergency`).

All traffic (manager↔control and control↔server) is the **plaintext 16-byte TNetLib framing** — no RC4/XOR
(unlike the login server). Wire framing/codec is shared verbatim with `TWorldSvr.Net`.

## Projects

| Project | Purpose |
|---|---|
| `TControl.Protocol` | Plaintext codec (`PacketHeader/Reader/Writer/Framer`) + `Msg` (all CT ids from `CTProtocol.h`) + `Proto` constants + wire models (`EventInfo`, `CmGift`, `BanInfo`, `PatchFile`, topology). No deps. |
| `TControl.Data` | `Microsoft.Data.SqlClient` access: topology load (`TMACHINE/TNETWORK/TGROUP/TSVRTYPE/TSERVER/TIPADDR`, `TEVENTCHART`, `TCASHSHOPITEMCHART`, `TPREVERSION`) + procs (`TOPLogin`, `TLoadService`, `TEventUpdate`, `TUserProtectedAdd`, `OPTool_SMSEmergency`, the patch procs). |
| `TControl.Server` | Generic host + Serilog, manager accept loop, outbound connector, single batch task + 1 s timer, `ControlService` handlers, and the `Ops` abstractions. |
| `*.Tests` | xUnit: protocol codec/golden-id/`EventInfo` round-trip; a loopback end-to-end (connect → register → login → chat-ban relay → ACK); env-gated DB smoke. |

## Build & test

```powershell
dotnet build TControlSvr.slnx -c Release
dotnet test  TControlSvr.slnx
```

Protocol + end-to-end tests run with **no database**. The DB smoke test runs only when
`TCONTROL_TEST_GLOBAL` is set (a `TGlobal_gsp` connection string).

## Run

```powershell
dotnet run --project TControl.Server -c Release
```

With no connection string it boots **DB-less** (empty topology/events) and listens on **:3615** — useful
for a wire smoke test. Point a GM tool at it, and add game servers to dial via the `Control:Servers` list.

## Configuration

Bound from the `Control` section of `appsettings.json`, overridable by environment variables
(`Control__Port`, `Control__Db__GlobalConnectionString`, …). The C++ read these from an INI
(`Configurations/TControlSvr.ini`); keep the DB connection string out of the file and pass it via env.

| Key | Default | Meaning |
|---|---|---|
| `Control:Port` | `3615` | TCP listen port for GM managers (`DEFAULT_CTL_PORT`; the C++ INI default was 3616). |
| `Control:AutoStart` | `false` | Auto-restart stopped services. No-op unless a Windows service controller is wired. |
| `Control:ConnectIntervalSeconds` | `10` | How often the connector re-dials game servers that aren't connected. |
| `Control:Servers` | `[]` | Explicit game-server dial list for DB-less mode: `{ Host, Port, Group, Type, ServerId, Name }` (`Type`: 2=login, 3=world, 4=map, 8=relay). When the DB is present, the topology is authoritative and this may be empty. |
| `Control:Db:GlobalConnectionString` | `""` | `TGlobal_gsp` connection string. Empty ⇒ DB-less mode. |

Example (PowerShell):

```powershell
$env:Control__Db__GlobalConnectionString = 'Server=localhost,11433;Database=TGlobal_gsp;User Id=sa;Password=…;TrustServerCertificate=true'
dotnet run --project TControl.Server -c Release
```

## Docker

```powershell
docker build -t tcontrol .
docker run --rm -p 3615:3615 -e Control__Db__GlobalConnectionString='…' tcontrol
```

The topology (which servers to dial, at which private IP:port) comes from the DB (`TSERVER` + `TMACHINE` +
`TIPADDR`); those addresses must be reachable from the container.

## Behaviour notes / known simplifications

- **OS-specific ops are wired but disabled** behind interfaces (`Ops/`), exactly like the sibling ports'
  anti-cheat. The C++ used the Windows Service Control Manager (remote start/stop/status), PDH performance
  counters, and SMB admin-share file upload — none cross-platform. The defaults:
  `NoOpServiceController` (start/stop are logged no-ops; **service "running" = a live TCP connection**),
  `NoOpPlatformMonitor` (CPU/MEM/NET = 0), `NoOpFileDeploy` (upload accepted, discarded). A Windows
  implementation can be registered in `Program.cs` behind the same seams. The DB-side patch/preversion
  procs are still ported.
- The connector **dials** each configured server and treats a live connection as RUNNING (replacing the
  C++ "SCM detects RUNNING → connect out" trigger).
- The GM manager side has **no client in this repo**, so it is wire-tested, not live-exercised. The
  server-facing relay is live-tested end-to-end against `TWorldSvr.Net`.
- **Wire reconciliation with `TWorldSvr.Net`** (RESOLVED): `CTProtocol.h` defines
  `CT_SERVICEMONITOR_REQ = 0x931E` / `_ACK = 0x931F`; `TWorldSvr.Net/TWorld.Protocol/NetCode.cs` previously
  had them **swapped** (never live-exercised, since no control server existed). The swap has been fixed in
  the world port, and the 1 s monitor poll now round-trips end-to-end (verified live: poll `0x931F` →
  counts reply `0x931E`).
