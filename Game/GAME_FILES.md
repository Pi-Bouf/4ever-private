# `Game/` File Relationship Map

> What every file in `Game/` is, and the **exact** TClient/Engine source symbol that loads or
> consumes it. `Game/` is the compiled, runnable client; sources live under `Client/` (TClient,
> MFC + DirectX 9), `Lib/Own/` (engine/UI/data libraries) and `Tools/` (launchers).
>
> Derived by cross-referencing the running assets against the indexed source tree
> (codebase-memory project `E-Projects-4Story-Araz-4ever`). File:line refs are from that tree.

## TL;DR — the five loader subsystems

Almost every file in `Game/` is owned by one of five subsystems. Learn these and the rest follows:

| Loader | Source | Owns |
|---|---|---|
| **`CTChart`** | `Lib/Own/TChart/TChart/TChart.cpp` | ALL `*.tcd` data tables **and** `TQuest.mpq` / `TQNode.qpd`. MFC `CFile`+`CArchive`, records are count-prefixed, **no magic signature**. |
| **`CTachyonRes`** | `Lib/Own/Engine Lib/Engine Lib/TachyonRes.cpp` | ALL binary 3D/media asset containers under `Data/` (`.TMH .TAC .TMD .TTX .TIM .TIS .TOB .TFX .TMA`), indirected through the `Index/*.IDX/.LST` catalogs. |
| **`TCMLParser`** | `Lib/Own/TCML/TCML/TCMLParser.cpp` | `TClientCmd.tif` — the entire compiled UI (HUD + dialogs), realized as `TComp` widgets. |
| **`CGameConfig` (`g_Config`)** | `Lib/Own/GameConfig/GameConfig.cpp` | `config.ini` — plaintext client + launcher settings (replaced the old registry keys). |
| **per-map loaders in `CTClientMAP`** | `Client/TClient/TClientMAP.cpp` | On-demand, per-zone files: `Tcd/TNPC*.tcd`, `Path/*.tpf`, `Node/*.pnd`, and the `Data/MAP/*.tmu` terrain units. |

Everything else is a bootstrap EXE/DLL, an anti-cheat package (both **compiled out** in this build),
or loose text/media handled ad-hoc.

---

## 1. Data tables — `Tcd/*.tcd`, `TQuest.mpq`, `TQNode.qpd`

**Universal reader:** `CTChart` (`Lib/Own/TChart/TChart/TChart.h:3`, `TChart.cpp` ~11k lines). Pure static
registry — every table is a `static` member map/vector, filled by an `Init*` method that does
`CFile(path, modeRead|typeBinary)` → `CArchive(load)` → read a leading record **count** (usually a
`WORD`) → stream N records via MFC's overloaded `operator>>`. There is **no file signature**; validity =
"did `CFile::Open` succeed". Lookups via `Find*` / `LoadString` / `Format`.

**Startup orchestration:** `CTClientWnd::InitChart` (`Client/TClient/TClientWnd.cpp:4280`) loads ~65
global tables. `TString.tcd` is loaded even earlier, in `CTClientApp::InitInstance`
(`Client/TClient/TClient.cpp:313`) — a missing `TString.tcd` is fatal.

### Global tables loaded by `InitChart` (file → `CTChart::Init*` → holds)

| `.tcd` | `Init*` method | Holds |
|---|---|---|
| `TItem.tcd` | `InitTITEMTEMP` (`TChart.cpp:2146`) | Item templates (id, type, slot, level, price, durability, magic/grade flags, visual id) |
| `TItemAttr / TItemVisual / TItemMagic / TItemMagicSfx / TItemGrade / TItemGradeVisual / TItemGradeSfx` | `InitTITEMATTR / …VISUAL / …MAGIC / …GRADE / …` | Item stat / icon+mesh visual / magic-option / grade sub-tables |
| `TQuestItem.tcd` | `InitTQUESTMAGICITEM` | Quest-reward magic items |
| `TMon.tcd` | `InitTMONTEMP` | Monster templates |
| `TNPCTemp.tcd`, `TNPCGlobal.tcd` | `InitTNPCTEMP` (`:3000`), `InitTNPCGlobal` | Master NPC templates / global NPC defs |
| `TSkill / TSkillTree / TSkillFunction / TSkillPoint / TSkillup` | `InitTSKILLTEMP / …TREE / …FUNCTION / …POINT` | Skill templates + skill-tree layout per country/class |
| `TString.tcd` | `InitTString` (`:265`) | **Localized UI string table** (see §1a) |
| `TStringCF.tcd` | (string variant) | Additional/community-format strings |
| `TMAP.tcd`, `TNODE.tcd` | `InitTMAPINFO` | Map + node/region metadata |
| `TMinimap.tcd`, `TRegion.tcd` | `InitTMINIMAP`, `InitTREGION` | Minimap / world-map + region defs |
| `TRace / TRaceInfo / TClassInfo / TCountryInfo / TFace / TBody / THand / TFoot / TPants` | `InitTRACETEMP / …INFO / …CLASSINFO / …COUNTRYINFO / …FACETEMP / …` | Character creation: race/class/appearance meshes |
| `TEquipCreateChar.tcd` | `InitTEQUIPCREATECHAR` | Starting equipment per class at char-create |
| `TAction.tcd`, `TADEF.tcd` | `InitTACTIONTEMP`, `InitTACTIONDATA` | Emote/action `/`-commands |
| `TCharTitle.tcd`, `TCharTitleGroup.tcd` | `InitTTITLETEMP`, `InitTTITLEGROUPTEMP` (`:4304`) | Player titles + title groups |
| `TMenuItem.tcd`, `TPopupMenu.tcd`, `TUtilityBar.tcd` | `InitTPOPUPITEM`, `InitTPOPUPMENU` | Right-click menus / hotkey bar |
| `TBGM / TENV / TSky / TLIGHT / TFog / TSFX / TSTEP / TFoot` | `InitTBGM / …ENV / …SKYBOX / …LIGHT / …FOG / …SFXTEMP / …STEPSND` | Ambience: music, environment, skybox, lights, fog, effects, footstep sounds |
| `TFormula / TLevel / TSkillPoint / TGate / TPortal / TPortalRegion / TJoint / TArrowDIR / TSwitch` | `InitTFORMULA / …LEVEL / …GATE / …PORTAL / …` | Combat formulas, level costs, gates/portals, mantle joints, switches |
| `TMount / TPet / TPetLevel / TPetFlyMesh` | `InitTPET` / mount+pet inits | Mounts & pets/companions (see [[pet-mount-system]]) |
| `THelp / THelpLink` | `InitTHelp`, `InitTHelpLink` | Help pages + links |
| `TGodTower / TGodBall / TArena / TBattleInsignia / TRpsGame` | `InitGODTOWER / …GODBALL / …Arena / …BattleInsignia` | Event / PvP mini-game data |
| `TMonShop / TMonthRank / TAuctionTree*` | `InitMonShop`, `InitTMonthRank` | Monster shops, monthly rankings |
| `TMantleCoord / TMantleDetailTexture` | `InitTMANTLEINFO` | Mantle (cape) coordinates + detail textures |
| `TUnitLink / TDest / TInfo / TClassInfo` | `InitTUNITLINK`, `InitTDESTINATION` | Zone-link/teleport destinations |

### Loaded on demand (not in `InitChart`)

| `.tcd` | Loader | Trigger |
|---|---|---|
| `Tcd/TNPC%08x.tcd` (the ~180 `TNPC********.tcd`) | `CTClientMAP::LoadTNPC` (`TClientMAP.cpp:4447`) → `CTChart::ReadTNPC` | **Per-map NPC spawn charts**, streamed as each zone loads. Path built from `IDS_FILE_MAPNPC = ".\Tcd\TNPC%08x.tcd"` with `MAKELONG(unitID, mapID)`. This is the bulk of the 229 files. |
| `TAuctionTree.tcd` | `CTChart::InitTAUCTIONTREE` (`:10607`), from `CTAuctionSearch` ctor | Auction category tree (opened when auction UI shows) |
| `TDynamicH.tcd` | `CTChart::InitTDYNAMICHELP` (`:10265`) | Tips / dynamic-help text |

### Quest data (misleading extensions — NOT Blizzard MPQ)
| File | Loader | Holds |
|---|---|---|
| `TQuest.mpq` | `CTChart::InitTQUESTTEMP(".\\TQuest.mpq")` (`TChart.cpp:4351`, called `TClientWnd.cpp:4297`) | Quest **definitions**: class list `{ClassID,name,bMAIN}` + quest/term defs. Plain `CFile` blob. |
| `TQNode.qpd` | `CTChart::InitTQUESTPOS(".\\TQNode.qpd")` (`TChart.cpp:9962`, `TClientWnd.cpp:4298`) | Quest **objective world-positions** (map markers): per term `{type,id,monKind,npcId,mapId,x,y,z}`. `CArchive` blob. |

Both are client-side assets; the server keeps its own quest templates in DB.

### `.bak` files & duplicates
`Tcd/*.bak` are editor backups — no loader references them. Top-level `Game/TCharTitle.tcd` is a stray
duplicate of `Tcd/TCharTitle.tcd` (the client reads the one in `Tcd/`).

### No client loader found (editor/legacy/server-side)
`THKState.tcd`, `THKCondition.tcd`, `TDest1.tcd` — present but no client `Init*` opens them.

### 1a. Localized strings — the string subsystem
- File `Tcd/TString.tcd` → `CTChart::InitTString` (`TChart.cpp:265`): reads `WORD count`, then
  `count × (WORD stringID, CString text)` into static `m_vTSTRING[TSTR_COUNT]`. `\n` is unescaped;
  **stringID 0 is special = the nation code** (`m_strNationCode`).
- Lookup: `CTChart::LoadString(TSTRING id)` (`:324`) and the printf-style
  `CTChart::Format(TSTR_id, …)` — the latter is used pervasively both for UI text *and to build file
  paths* (e.g. `TSTR_FILE_MAPNPC`, `TSTR_FMT_PATHFILE`, `TSTR_WORLDUNIFY_URL`).

---

## 2. 3D / media asset containers — the `Data/` tree via `Index/` catalogs

**Key indirection:** none of these extensions ever appears as a literal filename in C++. The engine's
master resource manager **`CTachyonRes`** (`Lib/Own/Engine Lib/Engine Lib/TachyonRes.cpp`, driver
`Load` at `:394`, group id `"TClient"`) reads a binary **catalog** from `Index/` (`.IDX`/`.LST`) that
lists container filenames plus `ID → (fileID, byteOffset)` triples, then opens the container files under
`Data/<subdir>/`. Those containers ARE the `.TMH/.TAC/.TMD/...` files — each holds many resources
concatenated. `.TEX/.IMG/.SFX` entries are additionally run through `CTachyonUncompressor`.

| Ext | Folder | Catalog (Index) | `CTachyonRes` loader | Runtime class | Holds |
|---|---|---|---|---|---|
| `.TMH` | `Tmh/` (137), `Data/Mesh/` (213) | `*M.IDX` (`TClientM.IDX`) | `LoadMESH` (`:1050` / `:1549`) | `CTachyonMesh` (`TachyonMesh.cpp:125`) | Skeletal/static meshes: VB/IB, bone matrices (skinned `WMESHVERTEX`), LOD. `Tmh/` = char/equip set. |
| `.TAC` | `Data/Action/` (141) | `*A.IDX` (`TClientA.IDX`) | `LoadANI` (`:984` / `:1475`) | `CTachyonAnimation` (`TachyonAnimation.cpp:30`) | "Actions" = bundled bone animations + root-motion `ANIKEY` keyframes + animation-event IDs |
| `.TMD` | `Data/Media/` (813) | `*W.IDX` (`TClientW.IDX`) | `LoadMEDIA` (`:948` / `:1453`) | `CTachyonMedia` (`TachyonMedia.h:4`) | Tachyon Media Data: DirectSound/DirectMusic/DirectShow clips (dispatched by media type) |
| `.TTX` | `Data/Skin/` (129) | `%u_*S.IDX` (`0/1/2_TClientS.IDX`, `%u`=detail level) | `LoadTEX` (`:799` / `:1238`) | `TEXTURESET` → `CT3DTexture::LoadT3DTEX` (`T3DTexture.cpp:102`) | Animated/skinned material texture sets (UV-scroll/anim keys, mips) |
| `.TIS` | `Data/Img/` (`*Src.TIS`) | `*List.LST` (`TClientList.LST`) | `LoadIMGBUF` (`:863` / `:1333`) | `CT3DTexture` | Image **source** buffers / atlases (raw packed pixels keyed by ID) |
| `.TIM` | `Data/Img/` (`*Img.TIM`) | `*I.IDX` (`TClientI.IDX`) | `LoadIMG` (`:882` / `:1381`) | `IMAGESET` → `CD3DImage::Load` (`D3DImage.cpp:123`) | UI/sprite image **set** definitions — sub-rects into the `.TIS` atlas + color-anim keys |
| `.TOB` | `Data/OBJ/` (7) | `*O.IDX` (`TClientO.IDX`) | `LoadOBJ` (`:1105` / `:1699`) | `OBJECT` | Assembled objects binding meshes + animations + textures + SFX + sounds + attributes |
| `.TFX` | `Data/SFX/` (7) | `*X.IDX` (`TClientX.IDX`) | `LoadSFX` (`:1158` / `:1959`) | `CTachyonSFX` / `CTachyonSlashSFX` | Particle/billboard special-effect defs (color-over-time keys, spray anims) |
| `.TMA` | `Data/MAP/` (`BSP*.TMA`, 10) | `*P.IDX` (`TClientP.IDX`) | `LoadMAP` (`:1208`) / `LockMAP` (`:418`) | `CTachyonBSPMAP` (`:1312`) / `CTachyonHUGEMAP` | Map container. Leading type byte: `MT_BSP`→indoor/dungeon BSP levels; `MT_EXT`→outdoor HUGEMAP header |
| `.tmu` | `Data/MAP/` (`EM<map>_<unit>.tmu`, 85) | (streamed) | `CTachyonHUGEMAP::LoadUNIT` (`TachyonHUGEMAP.cpp:2406`) | `CTachyonHUGEMAP` | One outdoor terrain **unit** (heightmap, region, shadow, detail arrays, placed objects/SFX/sounds), streamed on demand as the player moves |

### Image pipeline note
`Data/Img/` pairs (`XxxImg.TIM` + `XxxSrc.TIS`) are the UI atlas system: `.TIS` = pixel buffer,
`.TIM` = sprite definitions into it. `Index/TClientI.LST` lists the `.TIS` files
(`Login/Window/MainFrm/Map/Icon.TIS`); `Index/TClientList.LST` lists their `*Src.TIS` counterparts.
This is the same chain the item-icon extractor uses — see [[icon-atlas-decoding]].

### `Index/` catalog format
No dedicated class; consumed directly by the `CTachyonRes::Load*` methods. Each file:
`int nCount; int nTotal;` → `nCount` container filenames (via `CTachyonRes::LoadString`, `:2328`) →
`nTotal × {DWORD id; DWORD fileID; DWORD pos}`. Suffix → loader:
`M`→mesh, `A`→anim, `W`→media, `O`→object, `X`→SFX, `P`→map, `I`→image, `%u_…S`→skin (per detail
level), `List`→image-buffer/atlas.

---

## 3. Per-map navigation — `Path/*.tpf`, `Node/*.pnd`

Both are MFC-`CArchive`-serialized (unlike the raw `CFile` engine containers) and streamed per map unit.

| File | Loader | Holds |
|---|---|---|
| `Path/TFLAG*.tpf` (72) | `CTClientMAP::LoadTFLAG` (`TClientMAP.cpp:4405`); path from `TSTR_FMT_PATHFILE` (`TChartType.h:184`) | `CTClientFlag` list: named waypoint "flags", world position, and connected node route list (guided/patrol movement) |
| `Node/TNODE*.pnd` (81) | `CTClientPath::LoadTPATH` (`TClientPath.cpp:24`), filename from `CTClientMAP::LoadTPATH` (`:4389`) `.\Node\TNODE%04X%02X%02X.pnd` | The 2D **nav graph**: point map (`wPointID→T2DPOINT`), blocking edges, precomputed routing table (dist + waypoint sequence per source/target). Used by `FindTPATH`/`CanMove`. |

> Naming is counter-intuitive: the `Node/` folder (`.pnd`) holds the **nav graph**; the `Path/` folder
> (`.tpf`) holds the **flag/patrol routes**.

---

## 4. UI — `TClientCmd.tif`

| File | Loader | Holds |
|---|---|---|
| `TClientCmd.tif` (154 MB) | `TCMLParser::Load` (`Lib/Own/TCML/TCML/TCMLParser.cpp:39`, recursive `LoadFRAME` `:93`); invoked by `CTachyonWnd::InitResource` (`Lib/Own/Engine Lib/Engine Lib/TachyonWnd.cpp:448`, resolves `<group>Cmd.TIF` → `TClientCmd.TIF`) | The **entire compiled UI**: a tree of `FRAMEDESC` (per-component: id, type, menus, 2 image ids, tooltip/font/style/color/sound ids, margins, pos/size, text) + a `TCML_LOGFONT` table. **No magic sig** — starts with the frame count. |

TCML is 4Story's XML-like UI markup, compiled by an editor into the binary `.tif`. At runtime
`InitTachyonComponent` (`Lib/Own/TComp/TComp/TComp.cpp:72`) turns each `FRAMEDESC` into a live `TComp`
widget (`TComponent : CWnd` → `TFrame/TButton/TEdit/TList/TGauge/TScroll/TTabCtrl/…`). Dialogs fetch
templates via `pParser->FindFrameTemplate(ID_FRAME_...)`.

---

## 5. Config — `config.ini`

| File | Loader | Holds |
|---|---|---|
| `config.ini` (plaintext) | `CGameConfig` / `g_Config` (`Lib/Own/GameConfig/GameConfig.cpp`, `Init()` `:123`, defaults table `s_DefaultInts/Strs` `:23`) | Shared next-to-exe replacement for the old `HKCU\Software\4Story` registry. `[Settings]`=client graphics/audio/UI, `[Launcher]`=patch server + launch, `[CustomUI]`/`[ChatInterface]`=UI layout. See [[file-config-loader]]. |

Wiring:
- Client: `TClient.cpp:534` calls `g_Config.Init()`, then `:536` redirects MFC's `m_pszProfileName` to
  `config.ini` (no `SetRegistryKey`). `Engine Lib/TachyonApp.cpp:18,28` forwards every
  `GetProfileInt/WriteProfileInt` to `g_Config` — the choke point that moved all settings off the registry.
- Launchers: classic `Tools/TLauncher/GameSetting.cpp:5` writes `[Settings]`; the .NET
  `Tools/TLauncher.Core/LauncherConfig.cs` reads `[Launcher]` (`address→Server`, `port→Port` default
  3716, `version`, `exe`, `homeurl/newsurl`). See [[tlauncher-net]], [[file-config-loader]].

---

## 6. Encrypted config — `4storyEU.ini`

| File | Owner | Notes |
|---|---|---|
| `4storyEU.ini` (1058 B, binary/encrypted) | **nProtect GameGuard**, NOT 4Story code | An identical 1058-byte `4StoryEU.ini` lives inside `GameGuard/` next to the nProtect modules. It is GameGuard's encrypted per-game config; decryption is inside the closed-source module — **no decryption code in this repo**. The only source linkage is the product string `"4StoryEU"` passed to `new CNPGameLib("4StoryEU")` (`TClient.cpp:399`), inside an `#ifdef USE_GG` block that is commented out. |

> The CLAUDE.md description of `4storyEU.ini` as "the main client config" is misleading for this
> build — the game EXE never opens it; it belongs to GameGuard.

---

## 7. Bootstrap executables & redistributables

| File | Source / origin | Purpose |
|---|---|---|
| `TClient.exe` | `Client/TClient.sln` → `TClient` (`CTClientApp`, MFC+DX9) | The game client itself |
| `TClient.pdb` | Same build | Debug symbols; read at crash time by `CTMiniDump` |
| `4Story.exe` | `Tools/TLauncher/` (classic MFC, `CStoryApp`/`CStoryDlg`) | Legacy launcher (patch + settings + Play) |
| `TLauncher.Wpf.exe` | `Tools/TLauncher.Wpf/` + `Tools/TLauncher.Core/` (WPF, net10) | **Current** launcher (newest file). Reads `config.ini` `[Launcher]`, patches, then `Patcher.Launch(exePath=TClient.exe)`. See [[tlauncher-net]] |
| `Uninstall.exe` + `Uninstall.ini` | Third-party installer — **no source in repo** | `Uninstall.ini` `[f]` = plaintext manifest of installed files under `Araz4Story PvP\` |
| `dbghelp.dll` | Microsoft Debug Help Library (redist) | `CTMiniDump` (`Client/TClient/TMiniDump.cpp`) calls `MiniDumpWriteDump`; wired at `TClient.cpp:618` |
| `GdiPlus.dll` | Microsoft GDI+ runtime (redist) | `GdiplusStartup` at `TClient.cpp:479`; used by `CD3DFont` text / image loading |
| `TClient - Raccourci.lnk` | Windows shortcut (dev artifact, FR "shortcut") | Targets `Game\TClient.exe` |

---

## 8. Anti-cheat packages (both COMPILED OUT in this build)

| Package | Files | Source integration | Status |
|---|---|---|---|
| **nProtect GameGuard** | `GameGuard/` (`GameMon*.des`, `npggNT*.des`, `npsc.des`, `*.erl`, `event.erv`, `GameGuard.ver`, `Splash.jpg`, encrypted `4StoryEU.ini`, `update/`) + top-level `GameGuard.des` | Wrapper `CNPGameLib` (`Lib/3rdParty/NPGame/NPGameLib.h`); driven from `TClient.cpp` (`NPGameMonCallback` `:50`, `new CNPGameLib("4StoryEU")` `:399`, `SetHwnd` `:622`). Server side `Lib/3rdParty/NPGame/Server/ggsrv25.h` | Entire block is `#ifdef USE_GG` **and commented out** — ships but not started |
| **AhnLab HackShield** | `HShield/` (`EHSvc.dll`, `V3Hunt/V3InetGS/v3pro32s.dll`, `AhnUp*.dll`, `HSInst.dll`, `HSUpdate/AhnRpt/HsLogMgr.exe`, `*.mhe/*.dat`, `*.ini/*.env`, `*.log`) | `TClient.cpp` `#include <HShield.h>` `:24`, `#pragma comment(lib,"HShield.lib")` `:27`; `HackShield_Update/_Init/_StartService/_StopService/_UnInit/HS_CallbackProc` (`:1650–1824`), gated by `CTNationOption::INSTALL_HACKSHIELD` + `#ifdef USE_HS`. Server side `TMapSvr` `CheckHackShield`/`OnCS_HACKSHIELD_REQ` | Client start path `#ifdef`/comment-gated — not started |

> Despite shipping both packages, this `TClient.exe` launches **neither** at startup.

---

## 9. Loose media & text

| File | Consumer | Purpose |
|---|---|---|
| `Intro/Intro.cfg` (+ `*.tga`) | `CTClientWnd::InitTRESIMG` (`TClientWnd.cpp:3890`, `#ifdef MODIFY_LOADING`) | Loading/splash scene script: `scene_start…scene_end` blocks with `color/fade_in/keep(-1=hold)/fade_out/image/gauge/gauge_x,y/text_x,y/show_bar/realtime`. TGAs = splash frames + `Gauge.tga` progress bar |
| `Outro/*.tga` (1..22) | `COutro::ShowComponent` (`Client/TClient/Outro.cpp:33`) | Random background art for the logout/exit screen; no `.cfg`, just numbered TGAs |
| `agreement.txt` | `CTClientWnd::OnAgreementPage` (`TClientWnd.cpp:6323`), path `IDS_FILE_AGREEMENT` | Terms-of-Use text |
| `4Story.txt` (`Araz4Story`) | `CStoryDlg::ReadTextFile` (`4StoryDlg.cpp:403`) | Product / legacy registry-subkey id (`m_strSubkey`) |
| `URL.txt` (`New Account` / `araz4story.com`) | Client `CTClientNET::LoadCAURL` (`TClientNET.cpp:1933`); launchers `4StoryDlg.cpp:377`, `LauncherConfig.cs:59` | Create-account / homepage / news links (client falls back to hardcoded per-nation URLs if absent) |
| `Data/Cache/*` (`0.txt`, numeric) | `CTClientCustomCloak` (`TClientCustomCloak.cpp:177`); dir `CUSTOMTEX_PATH` (`TClientWnd.cpp:4139`) | Client download cache for custom cloak/emblem PNGs (`URLDownloadToFile` from `…/cloaks/%d.png`), keyed by id |
| `LOL.txt` | **none** | Obfuscated/garbage — no source reference; leftover junk |
| `4Story.pdb`-adjacent `.dmp` (if present) | `CTMiniDump` output | Leftover crash dump |

---

## Corrections to prior notes
- **`TClientMP.mpc` is NOT a "map/patch catalog."** It is a **Module Protector packed/anti-tamper PE
  image** (resource type `"MPCFILE"`). `CTModuleProtector::InitProtector`
  (`Lib/Own/Engine Lib/Engine Lib/TModuleProtector.cpp`, called from `Tools/TLauncher/4StoryDlg.cpp:1732`)
  memory-maps it, validates `IMAGE_DOS_SIGNATURE`/`IMAGE_NT_SIGNATURE`, and walks PE sections. The
  follow-on `ExecPE(".\\TClient.exe", …)` is commented out in this build.
- **`TQuest.mpq` / `TQNode.qpd` are NOT standard Blizzard MPQ archives** — proprietary
  `CFile`/`CArchive` blobs read by `CTChart` (§1).
- **`4storyEU.ini` belongs to GameGuard**, not the game (§6).
