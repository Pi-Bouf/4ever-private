# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

The **original RageZone "4Story 5.0" release** of **4Story** (a Korean "Trickster/Tachyon-engine" MMO,
EU build "Araz4Story") — server emulator source, the compiled game client, the database baselines, and a
Windows-service install kit. This is the **pristine upstream**; the sibling folder `..\4retro-4ever\` is a
modified fork of it. When something here looks dated, that fork is the modernized reference — see
`..\4retro-4ever\CLAUDE.md` for the contrast (it rewrote servers as console apps + INI config, added a
.NET login server, DB migrations, and a launcher).

Everything is **C++ targeting Win32/x86** (the servers are **ATL/COM**, the client is **MFC + DirectX 9**),
built with **MSVC** — the projects are `PlatformToolset v141` (Visual Studio 2017), a handful still on
`v140` (VS 2015). There is no cross-platform build and no .NET anywhere in this release.

### Layout

```
Client/    C++ game client source (TClient.sln) + TClientCmd.tif (the compiled-UI artifact shipped with source)
Lib/       Own/ (engine + UI + net libraries, each its own .sln) and 3rdParty/
Servers/   The ATL Windows-service cluster (TServer.sln) + TLogSvr (own sln) + Tools/Happy
Game/      The COMPILED runtime client + all assets (this is what actually runs; = 4retro's Game/)
Install Services/   .lnk shortcuts that install each server as a Windows service
Reg Services/       .reg files that seed each service's registry config (see "Deployment" below)
TGLOBAL_RAGEZONE.bak / TGAME_RAGEZONE.bak   SQL Server database baselines (restore once)
```

> Note: `Game/` was extracted from the "Araz4Story PvP" client package and renamed to match 4retro's
> layout. `Client/TClientCmd.tif` (Dec 2018 build) differs from `Game/TClientCmd.tif` (Jul 2018 build) —
> the one the client actually loads is the one in `Game/`.

## Building

No CLI/test harness — build with MSBuild or open the `.sln` in Visual Studio. All projects are **x86**.
The servers need the **ATL** workload installed (ATL is what `_AtlModule`/`.rgs`/`.idl` require).

```powershell
# Server cluster — TWorldSvr, TLoginSvr, TMapSvr, TControlSvr, TPatchSvr, TBoWSvr, TBRSvr, TNetLib, TProtocol
msbuild Servers\TServer.sln /p:Configuration=Release /p:Platform=x86

# Log server is a SEPARATE solution (UDP log sink), not part of TServer.sln
msbuild Servers\TLogSvr\4SLogServer.sln /p:Configuration=Release /p:Platform=x86

# Client — TClient, Engine Lib, TChart, TCML, TComp, HwidLib
msbuild Client\TClient.sln /p:Configuration=Release /p:Platform=x86

# Shared libraries can also be built standalone (each has its own .sln under Lib\Own\*)
msbuild "Lib\Own\Engine Lib\Engine Lib.sln" /p:Configuration=Release /p:Platform=x86

# Server-side tool
msbuild Servers\Tools\Happy\Happy.sln /p:Configuration=Release /p:Platform=x86
```

## Deployment & configuration (the important difference from 4retro)

These servers are **ATL Windows services**, not console apps. Each `main` is
`CT<Name>SvrModule _AtlModule; _tWinMain → _AtlModule.WinMain()`, so the same `.exe` self-registers as a
service (`/Service`, `/RegServer`, `/UnregServer`) and, when running as a service, reads its settings from
the **Windows registry**, *not* from an INI file:

```
HKLM\SYSTEM\CurrentControlSet\Services\<SERVICE>\Config
  DSN, DBUser, DBPasswd, Port, GroupID, ServerID, LogIP, LogPort, ...
```

Bring-up uses the kit at the repo root:
- **`Reg Services\*.reg`** seed each service's `…\Config` key. Service names are `TWORLD_GSP`,
  `TLOGIN_GSP`, `TMAP_GSP` (and `TMAP2/3_GSP`), `TCTRL_GSP`, `TPATCH_GSP`, `TRELAY_GSP`, `TBOW_GSP`,
  `TBR_GSP`, `TLOG0/1`, `TWORLD2_GSP`.
- **`Install Services\*.lnk`** install/register the services.

DB access is via two **ODBC DSNs**: `TGLOBAL_GSP` (accounts/login + topology) and `TGAME_GSP`
(game data). Default ports from the `.reg`: **World 3816**, **Login 4816**, **Log 7000** (UDP). The
service install kit references **more services than there are source projects** here (e.g. `TRELAY_GSP`,
`TMAP2/3`, `TWORLD2`) — those are extra/duplicate instances of the same binaries for a multi-machine
deployment.

> **The `.reg` files contain real `sa`/DB credentials in plaintext.** Do not print or commit them; scrub
> before sharing. Two databases back the cluster: restore `TGLOBAL_RAGEZONE.bak` → `TGlobal_gsp` (DSN
> `TGLOBAL_GSP`) and `TGAME_RAGEZONE.bak` → `TGame_gsp` (DSN `TGAME_GSP`). There is **no** migration
> tooling in this release (the fork added that).

## Server architecture (the big picture)

A distributed, IOCP-based ATL-service cluster. **TWorldSvr is the hub** holding world state; the other
services connect to it. Inter-server messages are split by plane in `Lib/Own/TProtocol/Include/`:
`CS*`/`CT*` (client↔server), `MW*` (map↔world), `SS*` (server↔server), `DM*`, plus `LogPacket.h`.

| Service       | Role |
|---------------|------|
| `TWorldSvr`   | **Central hub** — guilds (`TGuild`/`TCorps`), characters, parties (`TParty`), rankings, and the **BoW/BR game modes in-process** (`BowSystem.*`, `BRSystem.*`). The `TBoWSvr`/`TBRSvr` projects exist but the logic lives here. |
| `TLoginSvr`   | Account auth / login; the client's entry point (port 4816). |
| `TMapSvr`     | Per-map/zone game logic (mobs, NPCs, movement, quests, combat); deployed as multiple instances (`TMAP_GSP`/`TMAP2`/`TMAP3`), each connecting up to TWorldSvr. |
| `TControlSvr` | Admin/monitoring service. |
| `TPatchSvr`   | Client file patch distribution (see `TPatchSvr/ReadMe.txt`). |
| `TLogSvr`     | Centralized **UDP** log sink (`4SLogServer.sln`, `LogServer.cpp`) — separate solution. |

### Shared server code

There is **no `TServerSystem` base library** here (that is a 4retro addition). Servers share:
- **`Servers/global/globalinc.h`** — common defines.
- **`TNetLib`** (`Servers/TNetLib/`, prebuilt `Servers/Lib/TNetLib.lib`, and a copy under `Lib/Own/TNetLib`) — the networking + DB core:
  - `Packet.*` — wire format (fixed header + body via overloaded `<<`/`>>`, per-session key encryption).
  - `Session.*` — IOCP/overlapped async socket wrapper.
  - `SqlBase/SqlDatabase/SqlQuery/SqlDirect.*` — **ODBC** layer (connection + prepared-statement pooling) over the two DSNs.
- **`TProtocol`** (`Lib/Own/TProtocol/Include/`) — the shared packet definitions listed above.

Each server still carries its own `TServer.cpp`/`TServer.h` plus a `<Name>Session` class; the handler
pattern is the familiar one — worker threads pull completed IOCP I/O, parse the packet header, and
`switch` on packet ID to `On<MSG>` handlers that reply via the session or enqueue DB/batch jobs.

## Client architecture

`Client/TClient.sln` builds the **MFC** game client (rendered with **DirectX 9, June 2010 SDK**) from
these projects:

- **`TClient`** — the game client app (`Client/TClient/`, with `global/` and `History Files/`). There is **no separate launcher project** in this release (the fork added a "4Retro" launcher).
- **`Engine Lib`** — the **Tachyon 3D engine**: D3D9 device/camera/font/light wrappers, audio, skeletal animation, crypto, and the MFC `CTachyonApp`/`CTachyonWnd` base classes.
- **`TCML`** — parser for **TCML**, the XML-like UI markup language.
- **`TComp`** — the UI widget toolkit built on TCML (TButton/TEdit/TList/TFrame/… : `TComponent : CWnd`).
- **`TChart`** — loads compiled game data tables (`.tcd`) and localized strings.
- **`HwidLib`** — hardware-ID fingerprinting for account binding / anti-cheat (also has its own `.sln` under `Lib/Own/HwidLib`).

`Lib/Own/` additionally contains **`TachyonControl`** and the **`TProtocol`** include package. Third-party
code lives under `Lib/3rdParty/`.

## Game data formats & the `Game/` tree

`Game/` is the **compiled, runnable client** (DLLs `d3d9`/`d3dx9_34`/`d3dx9_43`/`GdiPlus`/`dbghelp`,
`Uninstall.exe`, the anti-cheat packages, and all assets). Custom binary formats:

| Format        | Location                       | Holds |
|---------------|--------------------------------|-------|
| `.TMH`        | `Game/Tmh/`, `Game/Data/Mesh/` | Skeletal 3D meshes (Tachyon format) |
| `.TCD`        | `Game/Tcd/`, `Game/*.tcd`      | Compiled data tables (items, NPCs, skills, strings…) |
| `.TAC`        | `Game/Data/Action/`            | Animation/action sequences |
| `.TIF`        | `Game/TClientCmd.tif`          | **Compiled UI** (produced from TCML) |
| `.MPQ`/`.QPD` | `Game/TQuest.mpq`, `TQNode.qpd`| Quest definitions / quest-node graph |
| `.MPC`        | `Game/TClientMP.mpc`           | Client map/patch catalog |
| index/nav     | `Game/Index/`, `Node/`, `Path/`| File catalogs / navigation + pathfinding graphs |

`Game/4storyEU.ini` is the main client config but is **encrypted/obfuscated** (not plain text — don't try
to read or hand-edit it as INI). `Game/GameGuard/` (nProtect) and `Game/HShield/` (AhnLab) are the two
runtime anti-cheat packages. The stray `TClient(...).dmp` is a leftover crash dump.

## Tools

`Servers/Tools/Happy/` (`Happy.sln`) is the only build tool in this release — see its `ReadMe.txt` and
`UserPosList.txt`. (There is no `TCMLParser` here; the fork added one.)
