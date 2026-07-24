# TBot — headless test client

A console "fake client" that drives the live server cluster end-to-end for automated testing:
**(optional) create account → login → reuse/create character → enter the first zone → walk around.**
No MFC/DirectX `TClient` needed.

It reuses the login server's wire toolkit (`TLogin.Protocol`: the asymmetric session cipher, packet
framing, `PacketWriter`/`PacketReader`, `Msg`/`Proto`) and adds the client-facing game-plane (`CS_MAP`)
packets the bot needs (`GamePackets.cs`).

## How it talks to the cluster

```
TBot ──CS_LOGIN plane──▶ login server (C++ TLoginSvr or .NET TLogin.Server, :4816)
     ◀── CS_START_ACK gives the game-server address
TBot ──CS_MAP plane────▶ game server (C++ TMapSvr; coordinates with TWorldSvr behind the scenes)
```

The cipher is asymmetric and **the same for both connections** (TMapSvr also marks client sessions
`SESSION_CLIENT` + `m_bUseCrypt`): send = XOR body+header then RC4 (outermost); recv = XOR only.
The map↔world handshake (`ENTERSVR → CHARINFO/ROUTE → CHARDATA → ENTERCHAR → CHECKMAIN → CONRESULT`)
runs server-side; the bot just waits for `CS_CHARINFO_ACK`, which carries the spawn `mapId`/position.

## Configure

Edit `appsettings.json` (or override on the command line with `--Bot:Key=Value`). Key settings:
`LoginHost`/`LoginPort`, `Account`/`Password`, `GroupId`/`Channel`, the `Char*` template (used only if
the chosen slot is empty), and the `Move*` params. `MoveSpeed` is hard-capped at 3.40 — the map server's
anti-cheat closes the session above that.

Accounts can't be made over the protocol. With `CreateAccount=true` and a `GlobalConnectionString`, TBot
inserts the account into `TGlobal_gsp.dbo.TACCOUNT_PW` (UPPER SHA-1 of `Password`) before logging in —
the same row the seed migration writes (`Database/migrations/001_seed_test_account.sql`).

## Run

```bash
cd Servers/TBot
dotnet run -- --Bot:LoginHost=192.168.1.37 --Bot:Account=test --Bot:Password=test123
# provision a fresh account first:
dotnet run -- --Bot:CreateAccount=true --Bot:Account=bot1 --Bot:Password=pw123 \
              --Bot:GlobalConnectionString="Server=localhost,11433;Database=TGlobal_gsp;User ID=sa;Password=...;TrustServerCertificate=True;Encrypt=False"
```

Prerequisites for a full run: a login server, **TWorldSvr**, and the **C++ TMapSvr** all running and
routed (restore the `.bak` DBs and run `Database/run-migrations.sh` for a local LAN setup).

## Tests

`TBot.Tests` verifies the new wire format **without a live server** by pairing the bot's `ClientCipher`
with the repo's server `SessionCipher`: a `CS_MOVE_REQ` the bot encodes decrypts cleanly server-side, a
server-encoded `CS_CHARINFO_ACK` parses to the right spawn, and the connect checksum matches.

```bash
dotnet test TBot.Tests/TBot.Tests.csproj
```

## Scope

Minimal single-bot proof-of-concept. The `BotConnection`/`ClientCipher`/`BotRunner` split leaves room to
spawn many bots later for load/smoke testing.
