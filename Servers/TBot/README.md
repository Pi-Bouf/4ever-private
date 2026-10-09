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

## Scenario: two bots, every feature

`--Bot:Scenario=features` logs two accounts in side by side and drives party (invite, join, HP bars, loot type,
leader change, kick), mail (send, notify, list, open, take money, delete), hotkeys, bags and a far NPC-portal
teleport against the live cluster. Every reply is parsed the way TClient reads it and must be used up to its last
byte; every packet must decode with a valid checksum; after logout the saved rows (money, position, hotkeys, bag)
are checked in the DB. The characters' money, HP, position and test items are put back at the end.

```bash
dotnet run -- --Bot:Scenario=features --Bot:Account=tbot27 --Bot:Account2=tbot28 --Bot:Password=...               --Bot:Channel=1 --Bot:NoCrypt=false --Bot:MapNoCrypt=false               --Bot:GameConnectionString="Server=localhost,11433;Database=TGame_gsp;User ID=sa;Password=...;TrustServerCertificate=True;Encrypt=False"
```

Exit code 0 when every check passes.

## Scenario: castle (a real castle war)

`--Bot:Scenario=castle` (same arguments as `features`) plays a castle war on Chesed end to end. The database has no
guilds, so it inserts two (A chief of the defenders, B chief of the attackers) and **restarts the world container**
(`docker compose restart worldsvr`) so it loads them; it names them Chesed's sides (`CT_CASTLEGUILDCHG_REQ`), the
chiefs sign up, and the bots come in, carry, mount and knock off god balls, and the war ends on time — result, news,
save, reward mail, everyone sent out. Everything is put back after, and the world and map containers are restarted.
Needs the docker stack, and no fort war due in the next 3 minutes (castle sign-up closes once a fort's war comes first).

## Scenario: guild (a guild's life)

`--Bot:Scenario=guild` (same arguments) drives the map's guild relay against the live world: A (set to level 20)
founds "TbotGuild", invites B who accepts, opens the guild window, makes B vice-chief, gives a peerage; posts, edits
and removes a board article, contributes silver, tries a fame mark (refused: no guild points), puts a stack in the
guild cabinet (refused at guild level 1, then accepted once the test raises the cabinet size) and takes part back out,
opens the point log and PvP record; puts B out, posts a wanted ad that B applies to and A accepts, then disbands. A disbanded guild stays 7 days in the world, so the scenario deletes the guild rows and **restarts the
world container** at the end; the bots' level and guild-leave marks are put back.

A second part (`GuildTacticsScenario.cs`) needs a level-5 guild with money and PvP points, so it inserts "TbotTactics"
(A chief, 1 gold, 1000 points) and restarts the world to load it. A posts a mercenary ad, B applies and is taken (a
welcome letter), B leaves its contract (the guild is paid back), A invites B as a mercenary and B accepts, A fires B
(B gets its points and a letter with its pay); B joins the guild and A gives it 50 guild points; A buys a castle guard
post from the mercenary merchant (23100) with the guild's money. The contract, treasury and mails are checked in the
database. Then everything is removed, the bots' PvP points are restored, and the world **and the map** are restarted (the
bought guards go with the map). 53 checks in all.

## Scenario: stress (hundreds of bots)

`--Bot:Scenario=stress` runs `StressCount` bots in one process, each on its own thread and account
(`StressPrefix` + number, created up front), ramped in at `StressRampPerSec`. Each bot does the normal flow
(login → create char → enter the map → walk `MoveDurationSec`), on a square of a different radius (20..`StressMaxRadius`)
so the crowd spreads over several cells — set `StressMaxRadius=20` to pack everyone into the same cells.

Every 5 s it prints how many bots are connecting / in world / done / kicked / failed, plus the map server's RSS and CPU
(read from `/proc/1` inside the `StressDockerContainer`, default `araz-mapsvr`; empty = don't sample). The summary gives
login and enter latency (p50/p95/max), kicks with the socket error, and failures grouped by message.

```bash
TBot.exe --Bot:Scenario=stress --Bot:StressPrefix=st --Bot:StressCount=400 --Bot:MoveDurationSec=90 \
  --Bot:Password=... --Bot:CreateAccount=true --Bot:GlobalConnectionString="...TGlobal_gsp..." \
  --Bot:Class=3 --Bot:Country=4 --Bot:Hair=4 --Bot:Face=5 --Bot:Channel=1 --Bot:NoCrypt=false --Bot:MapNoCrypt=false
```

Use a **new prefix every run**: an account that was in the world logs in as "Duplicate" until the servers restart.
Exit code 0 when every bot completed its walk.

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
