# 4Story (Araz-4ever) — Installation & Setup

How to bring up the original RageZone "4Story 5.0" stack from this source tree on a single Windows
machine: a Dockerized **SQL Server**, the **C++ server cluster** (Control / World / Map / Login) built as
console apps, and the **MFC/DirectX 9 client**, then launch the client with the right parameters.

> This is the *original* release. Its servers were ATL Windows services reading config from the registry;
> here they have been **ported to console apps** that read INI files via `TServerSystem` (the same model
> the `4retro-4ever` fork uses). See `CLAUDE.md` for the architecture.

> **Credentials:** this guide uses the placeholder `<SA_PASSWORD>` for the SQL `sa` password. Pick your
> own and keep it only in the config files below (`Servers\Services\Configurations\*.ini`). Do not commit
> real passwords.

---

## 1. Overview & ports

The cluster is distributed and IOCP-based. **TWorldSvr is the hub**; the client connects to **TLoginSvr**
first, authenticates, then is routed to a **map server** whose address comes from the database (`TIPADDR`).

| Component | Port | Proto | Configured in |
|---|---|---|---|
| SQL Server (Docker `4retro-sql`) | **1433** | TCP | container / ODBC DSN |
| TLoginSvr — *client entry point* | **4816** | TCP | `TLoginSvr.ini` → `Port` |
| TControlSvr (admin/monitor) | **3616** | TCP | `TControlSvr.ini` → `Port` |
| TWorldSvr (**hub**) | **3816** | TCP | `TWorldSvr.ini` → `WorldPort` |
| TMapSvr (game/zone) | **5816** | TCP | `TMapSvr.ini` → `GamePort` |

- **Server↔server** traffic stays on `127.0.0.1` on one machine. **Login finds Control via the database**
  (`TSERVER`), not via config.
- The address handed to **clients** for world/map routing is `TGlobal_gsp.dbo.TIPADDR.szIPAddr` — it must
  be an IP the client can reach (this machine's **LAN IP**, e.g. `192.168.1.37`). `szPriAddr` stays
  `127.0.0.1`.

---

## 2. Prerequisites

| Requirement | Why |
|---|---|
| Windows 11 x64 | Servers are Win32/x86; client is MFC/DirectX 9. |
| Visual Studio **Community 2026** with the **C++ ATL** + **MFC** workloads, MSVC **v14.5x** toolset, and Windows 11 SDK **10.0.26100.0** | Builds the C++ cluster (needs ATL) and the client (needs MFC). |
| Docker Desktop | Runs SQL Server 2022 (Linux container). |
| The two database backups | `TGLOBAL_RAGEZONE.bak` + `TGAME_RAGEZONE.bak` (already at the repo root). |

> The projects are pinned to toolset **v141** + SDK **10.0.17763.0**, neither of which is installed — so
> every build below **overrides** them with `/p:PlatformToolset=v145 /p:WindowsTargetPlatformVersion=10.0.26100.0`.

For brevity the build commands use:
```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
```

---

## 3. Step 1 — SQL Server in Docker + databases

Start one SQL Server 2022 container (shared by the whole cluster):

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<SA_PASSWORD>" -e "MSSQL_PID=Developer" `
  -p 1433:1433 --name 4retro-sql --restart unless-stopped `
  -d mcr.microsoft.com/mssql/server:2022-latest
```

Wait ~15s, then restore the two databases from the `.bak` files at the repo root. Run inside the container:

```powershell
$pw = "<SA_PASSWORD>"
docker cp "E:\Projects\4Story\Araz-4ever\TGLOBAL_RAGEZONE.bak" 4retro-sql:/var/opt/mssql/TGLOBAL.bak
docker cp "E:\Projects\4Story\Araz-4ever\TGAME_RAGEZONE.bak"   4retro-sql:/var/opt/mssql/TGAME.bak
$sqlcmd = "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $pw -C -Q"
docker exec 4retro-sql bash -lc "$sqlcmd `"RESTORE DATABASE TGlobal_gsp FROM DISK='/var/opt/mssql/TGLOBAL.bak' WITH MOVE 'TGlobal_gsp' TO '/var/opt/mssql/data/TGlobal_gsp.mdf', MOVE 'TGlobal_gsp_log' TO '/var/opt/mssql/data/TGlobal_gsp_log.ldf', REPLACE`""
docker exec 4retro-sql bash -lc "$sqlcmd `"RESTORE DATABASE TGame_gsp FROM DISK='/var/opt/mssql/TGAME.bak' WITH MOVE 'TGame_gsp' TO '/var/opt/mssql/data/TGame_gsp.mdf', MOVE 'TGame_gsp_log' TO '/var/opt/mssql/data/TGame_gsp_log.ldf', REPLACE`""
```

> Logical file names inside the `.bak` may differ — if RESTORE complains, list them with
> `RESTORE FILELISTONLY FROM DISK='...'` and adjust the `MOVE` clauses.
> - **`TGlobal_gsp`** — accounts/login + topology (`TSERVER`/`TMACHINE`/`TIPADDR`).
> - **`TGame_gsp`** — all game data + stored procedures.

**Set the routing IP** to this machine's LAN IP so clients can reach world/map:

```powershell
docker exec 4retro-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "<SA_PASSWORD>" -C `
  -Q "UPDATE TGlobal_gsp.dbo.TIPADDR SET szIPAddr='192.168.1.37', szPriAddr='127.0.0.1'"
```

**Fix the per-group DB credentials (required).** The login server connects to the **game** database
using the DSN/user/password stored in `TGlobal_gsp.dbo.TGROUP` (one row per world group, e.g. *Lapiris*),
**not** from a `.ini`. The restored `.bak` carries the **original RageZone password (`as123654`)**, so you
must update it to your container's `sa` password. Otherwise every `TLoginSvr` worker thread fails to open
the game DB at startup (`LoadData` → `EC_INITSERVICE_DBOPENFAILED`, `0x01000005`) and silently exits — the
listener still accepts clients, but with no worker servicing receives, **the client connects and hangs
with no login response.**

```powershell
docker exec 4retro-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "<SA_PASSWORD>" -C `
  -Q "UPDATE TGlobal_gsp.dbo.TGROUP SET szPasswd='<SA_PASSWORD>' WHERE szUserID='sa' AND szDSN<>''"
```

> Verify with: `SELECT bGroupID,szNAME,szDSN,szUserID,szPasswd FROM TGlobal_gsp.dbo.TGROUP WHERE bGroupID<>0`
> — `szPasswd` must match your `sa` password and `szDSN` must be `TGAME_GSP`.

After a reboot the DBs persist in the container; if everything fails, `docker start 4retro-sql`.

---

## 4. Step 2 — ODBC Data Source Names (DSNs)

The servers run as 32-bit (x86) and reach SQL through OS **User DSNs**. Create them in **both** ODBC
administrators:

- 64-bit: `odbcad32.exe`
- 32-bit: `C:\Windows\SysWOW64\odbcad32.exe`

In each, **User DSN → Add → "SQL Server"** driver:

| DSN name | Server | Default database |
|---|---|---|
| `TGLOBAL_GSP` | `127.0.0.1,1433` | `TGlobal_gsp` |
| `TGAME_GSP`  | `127.0.0.1,1433` | `TGame_gsp` |

Use SQL Server auth (`sa` / `<SA_PASSWORD>`); **Test Connection** must pass in both.

---

## 5. Step 3 — Build the server cluster (Release / x86)

Output exes land in `Servers\Services\`.

```powershell
& $msbuild Servers\TServer.sln /t:TWorldSvr;TControlSvr;TMapSvr;TLoginSvr `
  /p:Configuration=Release /p:Platform=x86 `
  /p:PlatformToolset=v145 /p:WindowsTargetPlatformVersion=10.0.26100.0 /m
```

(The dependency libs `TProtocol`, `TNetLib`, and `TServerSystem` build automatically. `TServerSystem`
lives in `Lib\Own\TServerSystem` and is compiled as C++20 for `std::format`.)

---

## 6. Step 4 — Configure the servers

Create/verify `Servers\Services\Configurations\*.ini` (a `Logs\` folder beside them is also used).
Put your real SQL password in `DBPasswd` / `GamePasswd`.

**`TControlSvr.ini`**
```ini
[TControlConfig]
DSN=TGLOBAL_GSP
DBUser=sa
DBPasswd=<SA_PASSWORD>
Port=3616
AutoStart=0
```

**`TWorldSvr.ini`** — ⚠ uses **`TGAME_GSP`** (the game DB), not Global
```ini
[TWorldConfig]
DSN=TGAME_GSP
DBUser=sa
DBPasswd=<SA_PASSWORD>
WorldPort=3816
GroupID=1
ServerID=1
```

**`TMapSvr.ini`**
```ini
[TMapConfig]
GameDSN=TGAME_GSP
DBUser=sa
GamePasswd=<SA_PASSWORD>
WorldIP=127.0.0.1
WorldPort=3816
GamePort=5816
GroupID=1
ServerID=1
LogIP=127.0.0.1
LogPort=7000
```

**`TLoginSvr.ini`**
```ini
[TLoginConfig]
DSN=TGLOBAL_GSP
DBUser=sa
DBPasswd=<SA_PASSWORD>
Port=4816
ServerID=1
LogIP=127.0.0.1
LogPort=7000
```

---

## 7. Step 5 — Run the cluster

These are **console apps** — run each in its own window; type `/exit` in a window to stop it. **Do not**
redirect their stdout (the input loop fails with no console and the process spins). Start **in order**:

```powershell
$svc = "E:\Projects\4Story\Araz-4ever\Servers\Services"
Start-Process "$svc\TControlSvr.exe" -WorkingDirectory $svc      # 3616
Start-Sleep 4
Start-Process "$svc\TWorldSvr.exe"   -WorkingDirectory $svc      # 3816 (LoadData ~15s)
Start-Sleep 16
Start-Process "$svc\TMapSvr.exe"     -WorkingDirectory $svc      # 5816  (LoadData ~45s — be patient)
Start-Process "$svc\TLoginSvr.exe"   -WorkingDirectory $svc      # 4816
```

Each window prints progress: `Starting up… / Loading configuration… / Initializing database… /
Loading data… / Creating threads… / Initializing network… / Listening on port XXXX.`

**Verify** all four are listening and Map is linked to World:
```powershell
Get-NetTCPConnection -State Listen | ? { $_.LocalPort -in 3616,3816,5816,4816 } |
  Sort LocalPort | Format-Table LocalPort,OwningProcess
Get-NetTCPConnection -State Established | ? { $_.RemotePort -eq 3816 } |
  Format-Table LocalPort,RemotePort,OwningProcess   # TMapSvr -> TWorldSvr:3816
```

> **TMapSvr takes ~45s** to load game data before it listens on 5816 — wait for the "Listening on port 5816." line.

---

## 8. Step 6 — Build the client (Release / x86)

The client (`Client\TClient.sln`: TClient + Engine Lib, TComp, TCML, TChart, HwidLib) is built with the
same overrides plus **C++14** (the code predates C++17 removals). Output goes to `Game\TClient.exe`.

```powershell
& $msbuild Client\TClient.sln /p:Configuration=Release /p:Platform=x86 `
  /p:PlatformToolset=v145 /p:WindowsTargetPlatformVersion=10.0.26100.0 /p:LanguageStandard=stdcpp14 /m
```

The `Game\` folder already holds the runtime assets (`4storyEU.ini`, `Data\`, `d3d9.dll`,
`d3dx9_34/43.dll`, `GdiPlus.dll`, GameGuard/HShield), so `TClient.exe` can launch in place.

---

## 9. Step 7 — Launch the client (the shortcut)

`TClient.exe` takes the **login server address on its command line** — it parses
`sscanf(cmdline, "%s %d %d", IP, Port, Channel)`, so:

```
TClient.exe  <LoginServerIP>  <LoginPort>  [Channel]
```

- **`<LoginServerIP>`** — the machine running `TLoginSvr` (this box's LAN IP, e.g. `192.168.1.37`; use
  `127.0.0.1` only for a client on the same machine).
- **`<LoginPort>`** — `4816`.
- **`[Channel]`** — channel number, default `1`.

Example: `TClient.exe 192.168.1.37 4816 1`

### Create the shortcut automatically

```powershell
$game = "E:\Projects\4Story\Araz-4ever\Game"
$ip   = "192.168.1.37"   # login server IP (LAN IP for LAN play, 127.0.0.1 for same machine)
$port = 4816
$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut("$env:USERPROFILE\Desktop\4Story (Araz).lnk")
$lnk.TargetPath       = "$game\TClient.exe"
$lnk.Arguments        = "$ip $port 1"
$lnk.WorkingDirectory = $game           # MUST be the Game folder so it finds its assets
$lnk.IconLocation     = "$game\TClient.exe,0"
$lnk.Save()
```

Or do it by hand: right-click `Game\TClient.exe` → **Create shortcut** → Properties →
**Target:** `"…\Game\TClient.exe" 192.168.1.37 4816 1`, **Start in:** `…\Game`.

Double-click the shortcut → login → group/channel → character list → **enter world**. On enter-world the
login server hands the client the map address from `TIPADDR.szIPAddr` (Step 3) — that IP and port `5816`
must be reachable from the client.

---

## 10. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Client opens then immediately closes / can't connect | Wrong/missing launch args. It needs `TClient.exe <IP> <Port> <Channel>`; bare double-click won't connect. Check the login server is listening on 4816. |
| Client connects to TLoginSvr but **hangs** (no login response) | `TGROUP.szPasswd` still holds the baseline password (`as123654`). `TLoginSvr` worker threads fail to open the game DB (`EC_INITSERVICE_DBOPENFAILED 0x01000005`) and exit, so receives are never serviced. Update `TGROUP` to your `sa` password (Step 1). |
| TWorldSvr quits right after "Loading data…" | Wrong DSN — TWorldSvr must use **`TGAME_GSP`** (game DB), not `TGLOBAL_GSP`. Fix `TWorldSvr.ini`. |
| Server window closes instantly / spins | Don't redirect stdout; launch as a normal console (Step 5). |
| TMapSvr never listens on 5816 | It's still in `LoadData` (~45s). Wait for the "Listening on port 5816." line. |
| ODBC "Test Connection" fails | Create the DSN in **both** 32-bit (`SysWOW64\odbcad32.exe`) and 64-bit admins; verify `127.0.0.1,1433` + `sa` password. |
| Client routed to an unreachable map | `TIPADDR.szIPAddr` is stale — re-run the `UPDATE TIPADDR …` (Step 3) with the current LAN IP. |
| Build error: SDK 10.0.17763 / toolset v141 not found | Pass the overrides `/p:PlatformToolset=v145 /p:WindowsTargetPlatformVersion=10.0.26100.0` (client also `/p:LanguageStandard=stdcpp14`). |
| Everything fails after reboot | SQL container stopped: `docker start 4retro-sql`. |
