# 4Story (Araz-4ever) — Installation & Setup

How to bring the 4Story stack up on a single Windows machine. The server backend runs **entirely under
Docker Compose** — SQL Server, the database restore/migrations, and the **TPatch / TWorld / TLogin /
TControl** servers (all ported to **.NET**). The **only** piece still run by hand is **TMapSvr**, which
remains the original **C++ console app** until its .NET port is finished. The game **client** is the
MFC/DirectX 9 app, built with Visual Studio.

> **Rule of thumb:** always use `docker compose` for SQL, the migrations, and the four .NET servers.
> Do **not** run the C++ `Servers\Services\*.exe` for World/Login/Control/Patch — the Docker ones are
> authoritative. TMap is the lone exception (see §5).

---

## 1. Overview & ports

`docker compose up` starts everything below except TMap. The client connects to **TLoginSvr** first,
then is routed to the map server.

| Component | Container / where | Host port | Proto |
|---|---|---|---|
| SQL Server 2022 | `araz-mssql` (compose) | **11433** → 1433 | TCP |
| DB restore + migrations | `araz-mssql-importer` (compose, one-shot) | — | — |
| TLoginSvr (.NET) — *client entry point* | `araz-loginsvr` | **4816** | TCP |
| TWorldSvr (.NET) — hub | `araz-worldsvr` | **3816** | TCP |
| TControlSvr (.NET) | `araz-controlsvr` | **3615** | TCP |
| TPatchSvr (.NET) | `araz-patchsvr` | **3716** | TCP |
| **TMapSvr (C++ console, manual)** | host process | **5816** | TCP |

- The SA password is read from a **`.env`** file next to `docker-compose.yml` (`SA_PASSWORD=...`); the
  .NET servers get their connection strings from it. It is **git-ignored** — never commit it.
- The client is handed the map address from `TGlobal_gsp.dbo.TIPADDR.szIPAddr`. For same-machine play
  `127.0.0.1` works; for LAN play set it to this box's LAN IP.

---

## 2. Prerequisites

| Requirement | Why |
|---|---|
| Windows 11 x64 | TMap is Win32/x86; client is MFC/DirectX 9. |
| **Docker Desktop** | Runs SQL Server + the four .NET servers + migrations. |
| **Git + Git LFS** | Two client assets are LFS-tracked (see §3) — **without them the client crashes on start**. |
| **VS 2022 Build Tools** with **C++ ATL + MFC**, MSVC **v143**, Windows 11 SDK **10.0.22621.0** | Builds **TMapSvr** (C++) and the **client**. Projects are already pinned to v143 / SDK 22621, so no `/p:` toolset overrides are needed. Install with `scripts\install-build-deps.ps1` (elevated). |

> The DirectX SDK (June 2010) is **vendored** in the repo (`Lib\3rdParty\DirectX9 (June 2010)`) and
> referenced by relative path — nothing to install machine-wide.

---

## 3. Clone + pull LFS assets (do this first)

```powershell
git clone git@github.com:Pi-bouf/4ever-private.git
cd 4ever-private
git lfs install
git lfs pull
```

`Game\Data\Mesh\Character.TMH` (~143 MB) and `Game\TClientCmd.tif` (~147 MB) are **Git LFS** files. If
they are left as 134-byte pointer stubs, `TClient.exe` crashes at startup in `CTachyonRes::LoadMESH`
(MFC `CMemoryException`, exit `0xE06D7363`). Verify they are real:

```powershell
Get-Item "Game\Data\Mesh\Character.TMH","Game\TClientCmd.tif" | Select Name,Length
```

---

## 4. Step 1 — Bring up the server backend (Docker Compose)

Create **`.env`** next to `docker-compose.yml` with your chosen SA password:

```
SA_PASSWORD=<SA_PASSWORD>
```

Then build + start the whole backend:

```powershell
docker compose up -d --build
```

This brings up, in order:
1. **`araz-mssql`** — SQL Server 2022 (host port **11433**).
2. **`araz-mssql-importer`** — restores `TGLOBAL_RAGEZONE.bak` → `TGlobal_gsp` and `TGAME_RAGEZONE.bak`
   → `TGame_gsp` (idempotent), then applies every `Database\migrations\*.sql` exactly once. Migration
   `002` sets `TGROUP.szPasswd` to your `.env` SA password (so login can open the game DB) and seeds the
   routing IP; migration `001` seeds a **`test` / `test123`** account.
3. **`araz-loginsvr` / `araz-worldsvr` / `araz-controlsvr` / `araz-patchsvr`** — the .NET servers, which
   start only after the importer completes successfully.

**Verify:**
```powershell
docker compose ps
docker logs araz-loginsvr   # expect: "Login server listening on port 4816."
```

> **If the importer fails / the stack was interrupted** (e.g. a reboot mid-restore leaves `TGlobal_gsp`
> stuck `RESTORING`), reset the data volume and re-run: `docker compose down -v; docker compose up -d`.
> The `.bak`s + migrations rebuild the databases deterministically.

---

## 5. Step 2 — TMapSvr (C++ console) — the only manual server

There is **no .NET map server yet**, so the map server runs as the original C++ console app and connects
to the Docker world + Docker SQL. **This step is temporary — remove it once the .NET TMap port lands.**

1. **Build** (Release / x86, v143):
   ```powershell
   & msbuild Servers\TServer.sln /t:TMapSvr /p:Configuration=Release /p:Platform=x86 /m
   ```
   Output: `Servers\Services\TMapSvr.exe`.

2. **ODBC DSN** → point it at the **Docker** SQL (host port **11433**, not 1433). Create `TGAME_GSP` in
   **both** the 64-bit (`odbcad32.exe`) and 32-bit (`C:\Windows\SysWOW64\odbcad32.exe`) admins:

   | DSN | Server | Default DB |
   |---|---|---|
   | `TGAME_GSP` | `127.0.0.1,11433` | `TGame_gsp` |
   | `TGLOBAL_GSP` | `127.0.0.1,11433` | `TGlobal_gsp` |

   Use SQL auth (`sa` / `<SA_PASSWORD>`); Test Connection must pass in both.

3. **Config** `Servers\Services\Configurations\TMapSvr.ini`:
   ```ini
   [TMapConfig]
   GameDSN=TGAME_GSP
   DBUser=sa
   GamePasswd=<SA_PASSWORD>
   WorldIP=127.0.0.1
   WorldPort=3816       ; the Docker world (araz-worldsvr, host-mapped)
   GamePort=5816
   GroupID=1
   ServerID=1
   ```

4. **Run** (own console window; do not redirect stdout):
   ```powershell
   $svc="E:\4ever-private-main\Servers\Services"
   Start-Process "$svc\TMapSvr.exe" -WorkingDirectory $svc
   ```
   It takes **~45 s** to load game data, then listens on **5816** and connects to `worldsvr:3816`.
   Verify: `Get-NetTCPConnection -State Listen | ? LocalPort -eq 5816`.

---

## 6. Step 3 — Build & launch the client

Build (Release / x86, v143) — output goes to `Game\TClient.exe`:

```powershell
& msbuild Client\TClient.sln /p:Configuration=Release /p:Platform=x86 /m
```

`TClient.exe` takes the login-server address on its command line:

```
TClient.exe <LoginServerIP> 4816 [Channel]
```

Example (same machine): `TClient.exe 127.0.0.1 4816 1` — log in with **`test` / `test123`**.

> **Resolution:** `config.ini` ships with `ScreenX=`/`ScreenY=` **empty**; on first launch the client
> adapts to the primary display (writes the detected resolution back). Set explicit values there to
> override.

---

## 7. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| Client crashes instantly at startup (`CMemoryException`, a `Game\TClient(...).dmp` appears) | LFS assets are pointer stubs. Run `git lfs pull` (§3). |
| Client opens then closes / won't connect | Missing launch args. Needs `TClient.exe <IP> <Port> <Channel>`; check `araz-loginsvr` is up on 4816. |
| Client connects to login but **hangs** | `TGROUP.szPasswd` doesn't match SQL `sa`. The importer's migration `002` sets it from `.env`; make sure `.env` SA password matches the running container, then `docker compose down -v; docker compose up -d`. |
| `.NET` servers don't start | The `mssql-importer` failed — `docker logs araz-mssql-importer`. Usual fix: `docker compose down -v; docker compose up -d`. |
| TMap never listens on 5816 | Still in `LoadData` (~45 s), or its DSN points at `1433` instead of the Docker **`11433`**. |
| TMap/DSN "Test Connection" fails | Create the DSN in **both** 32- and 64-bit ODBC admins, `127.0.0.1,11433`, `sa`. |
| Client routed to an unreachable map | `TIPADDR.szIPAddr` is stale. For LAN play set it to this box's LAN IP (see the `002` migration / update `TGlobal_gsp.dbo.TIPADDR`). |
| Everything fails after reboot | SQL container stopped: `docker compose up -d` (or `docker start araz-mssql`). |
