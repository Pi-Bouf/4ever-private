# TLauncher.Net

A C# console port of the **functional core** of the 4Story launcher (`Tools/TLauncher`, the MFC
`4Story.exe`). It does what the launcher does minus the skinned GUI and the native anti-tamper watchdog:

1. Connect to the patch server and send `CT_NEWPATCH_REQ` (current client version).
2. Read `CT_NEWPATCH_ACK` → download base URL, **login-server endpoint**, min beta version, file list.
3. Download + unzip any patch files into the game folder.
4. Send `CT_PATCHSTART_REQ`.
5. Launch `TClient.exe <loginIp> <loginPort>`.

The wire protocol is **not reimplemented** — it references `TPatch.Protocol` from the `TPatchSvr.Net`
port (the same 8-byte plaintext framing), so the launcher and server can never drift.

## Build & run

```powershell
dotnet build TLauncher.Net.csproj -c Release
dotnet run --project TLauncher.Net.csproj -- --server 127.0.0.1 --port 3716 --game-dir "..\..\Game"
```

By default it reads `config.ini` `[Launcher]` (address / port / exe / version) from the game folder, so
run inside the game folder and it needs no arguments. CLI flags override the file.

| Flag | Meaning |
|---|---|
| `--server <host>` | patch host (default: `config.ini` `address`, else `127.0.0.1`) |
| `--port <n>` | patch port (default: `config.ini` `port`, else `3716`) |
| `--version <n>` | client version to report (default: `config.ini` `version`, else `0`) |
| `--game-dir <path>` | folder with `config.ini` + the client exe |
| `--config <path>` | explicit `config.ini` |
| `--exe <name>` | client exe (default: `config.ini` `exe`, else `TClient.exe`) |
| `--no-launch` | do the patch handshake only; print the launch command |
| `--launch-timeout <s>` | after spawning, wait `<s>`s then kill the client (test mode) |

## Notes

- **`TClient.exe` requires administrator elevation** (its manifest is `requireAdministrator`). The C++
  launcher runs elevated so its `CreateProcess` child inherits the token. This tool runs non-elevated, so
  it relaunches the client via `ShellExecute`/`runas` (a UAC prompt appears). Run the console **as
  administrator** to launch without a prompt. This is a UAC requirement, **not** the anti-cheat.
- **No `CTModuleProtector`.** The launcher's native module-tamper watchdog (keyed to `TClientMP.mpc`) is
  dropped — it is a launcher-side watchdog, not a gate the client needs to run on a private server.
- **Scope.** This is the headless core; a GUI (WPF/WinForms) is a thin layer over `PatchClient` + the
  launch step.
