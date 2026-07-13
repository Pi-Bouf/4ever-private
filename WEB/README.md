# WEB — 4Story Item Browser (TypeScript · Prisma · Vite 8 · Tailwind)

A searchable catalog of every 4Story item (**icon + name + stats**), rebuilt in TypeScript.
The item list, names and stats come from **SQL Server via Prisma**; the real inventory icons
are extracted from the game client files by a typed `script/`.

## Quick start
```bash
cd WEB
npm install
# 1) Bring the DB up (repo root):  docker compose up -d mssql
npm run db:generate     # generate the Prisma client (script/generated/prisma)
npm run extract         # DB + game files -> public/items.json + public/icons/*.png
npm run dev             # http://localhost:5173
# or: npm run build && npm run preview
```
`npm run extract` needs the docker MSSQL container up and the sibling `../Game/` tree.
The SA password is read from the repo-root `../.env` (`SA_PASSWORD`) — never hardcoded.

## Stack (pinned)
Vite **8.1.4** · React **19** · Tailwind **4** (`@tailwindcss/vite`, CSS-first) · TypeScript 5 ·
Prisma **7** (`prisma-client` generator + `@prisma/adapter-mssql` over `mssql` 12) · `tsx`.
One `tsconfig.json` for both `src/` and `script/`.

## How it works
```
script/db (Prisma)  -> TITEMCHART: item list + names + stats
script/lib + tif    -> icons: m_wVisual[0] -> TItemVisual.m_wIcon (INDEX)
                       -> TClientCmd.tif item-slot list -> spriteId
                       -> TClientI.IDX -> *Img.TIM chunk -> *Src.TIS DDS page -> PNG
script/extract.ts   -> merge -> public/{items,atlas,meta}.json + public/icons/<id>.png
src/                -> React (Vite) loads the static JSON + PNGs
```
The key subtlety (already solved): `m_wIcon` is an **index into the UI's item-slot image list**
in `TClientCmd.tif`, not a global sprite id. Result: **7,099 / 7,706 items get a correct icon.**

## Layout
```
prisma/    schema.prisma (TITEMCHART) + prisma.config.ts (Prisma 7)
script/    lib/ (reader, dds, png, tcd, tif, atlas)  db/ (client, items)
           steps/ (gameData, icons, build, write)     extract.ts  paths.ts
src/       components/{layout,controls,items,detail,atlas,common}
           hooks/  lib/  App.tsx  main.tsx  index.css (Tailwind @theme)
public/    generated (git-ignored): items.json, atlas.json, meta.json, icons/
```

## UI
- **Items** tab — search (name or id), type filter, "show icons" toggle, infinite-scroll grid;
  click any item for a detail modal (stats, flags, description; Esc / click-outside to close).
- **Icon Atlas** tab — every extracted sprite, searchable by id.
- **DB vs Game** tab — diff of SQL `TITEMCHART` against `TItem.tcd`: items only in the DB, items
  only in the game files, and items in both whose fields disagree (name/type/level/durability/…;
  price is excluded as a known representation difference). Filter by kind, search by name/id.
  Written to `public/diff.json` by the extractor.
- **Mounts & Pets** tab — three sub-views: **Mounts** (the `TMount.tcd` roster joined to the IT_PET
  items that grant each — icon, mount/monster ids, granting items), **Pets & Companions** (the
  companion item families), and **Comparison**. The Comparison view is a **database-vs-TCD diff scoped
  to the mount/pet item types** (Pet, Saddle, Companion, Companion Costume, Companion Item) — reusing
  the DB vs Game machinery to show mount/pet rows that are DB-only, game-only, or whose fields disagree
  — followed by a family stats table (mount items vs companion items: counts, level range, icon
  coverage, tradable/cash). Mount data → `public/mounts.json`, produced by the main extract or the
  offline `npm run extract:mounts` (files only, no DB); the DB-vs-TCD section reads `public/diff.json`.
  When mount/pet items exist in the game files but are missing from the DB, the Comparison view shows a
  **"Download SQL migration"** button that adds them. `npm run gen:missing` (DB required) writes an
  idempotent `Database/migrations/006_titemchart_missing_mountpet_items.sql` — for each missing item it
  **clones a same-type sibling row and overwrites the fields the game file provides** (the technique of
  migration `004`, since `TITEMCHART` has 52 NOT-NULL columns and ~15 aren't in `TItem.tcd`) — plus
  `public/missing-mountpet-items.sql` + `public/missing.json` for the button. Apply with the tracked
  runner `Database/run-migrations.sh`; **restart the game servers** afterwards (charts load at startup).
