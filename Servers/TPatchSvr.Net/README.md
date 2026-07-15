# TPatchSvr.Net

A C#/.NET 10 port of the C++/ATL **TPatchSvr** — the 4Story client **patch/version server**. The launcher
(`Tools/TLauncher`, `4Story.exe`) connects on boot and the server replies with the download base URL, the
login-server endpoint, and the list of patch files newer than the client's current version.

See **[PORT_STATUS.md](PORT_STATUS.md)** for the handler-by-handler port map and wire-format notes.

## Projects

| Project | Purpose |
|---|---|
| `TPatch.Protocol` | The **8-byte** plaintext TNetLib framing (header, framer, reader, writer) + message IDs. |
| `TPatch.Data` | `Microsoft.Data.SqlClient` access to `TVERSION` / `TPREVERSION` / `TUSER_INTERFACE` + the topology procs. |
| `TPatch.Server` | Generic host + Serilog, async TCP acceptor, single batch task, the patch handlers. |
| `TPatch.Protocol.Tests` · `TPatch.Server.Tests` | xUnit, DB-free (14 tests). |

## Build & test

```powershell
dotnet build TPatchSvr.slnx -c Release
dotnet test  TPatchSvr.slnx
```

## Run

```powershell
dotnet run --project TPatch.Server -c Release
```

With no connection string it boots DB-less (empty file lists) and listens on **:3715** — useful for a wire
smoke test. Point the launcher's `[Launcher] address`/`port` (in `Game/config.ini`) at it.

## Configuration

Bound from the `Patch` section of `appsettings.json`, overridable by environment variables (`Patch__Key`).
The C++ read these from the registry (`HKLM\SYSTEM\CurrentControlSet\Services\TPATCH_GSP\Config`); keep the
DB connection string out of the file and pass it via environment.

| Key | Default | Meaning |
|---|---|---|
| `Patch:Port` | `3715` | TCP listen port (DEF_PATCHPORT). |
| `Patch:ServerId` / `Patch:GroupId` | `1` / `0` | Server identity. |
| `Patch:FtpUrl` | *(C++ hard-coded default)* | Download base URL sent to clients. **Override for your deployment.** |
| `Patch:PreFtpUrl` | `""` | Prepatch (beta) base URL. |
| `Patch:LoginAddress` / `Patch:LoginPort` | `127.0.0.1` / `4815` | Login endpoint handed to clients (overridden by `TLoadService` when a DB is present). |
| `Patch:ResolveLoginFromDb` | `true` | Resolve the login endpoint from `TLoadService(0, LOGINSVR)` at startup. |
| `Patch:IdleTimeoutSeconds` | `60` | Idle client sessions dropped on a control monitor tick. |
| `Patch:Db:ConnectionString` | `""` | SQL Server connection string. Empty ⇒ DB-less mode. |

Example (PowerShell):

```powershell
$env:Patch__Db__ConnectionString = 'Server=localhost,11433;Database=TGlobal_gsp;User Id=sa;Password=…;TrustServerCertificate=true'
$env:Patch__FtpUrl = 'http://patch.example.com/4story'
dotnet run --project TPatch.Server -c Release
```

## Docker

```powershell
docker build -t tpatchsvr .
docker run --rm -p 3715:3715 -e Patch__Db__ConnectionString='…' tpatchsvr
```
