// Build the item catalog + icons for the web UI.
//   items + names + stats : SQL TGame_gsp.TITEMCHART (Prisma 7 + mssql adapter)
//   icons                 : game files (m_wVisual -> TItemVisual index ->
//                           TClientCmd.tif slot list -> sprite -> TClientI.IDX atlas)
//   AP/DP, description     : TItemAttr.tcd / TInfo.tcd
// Output: public/{items,atlas,meta}.json + public/icons/<id>.png
import fs from "node:fs";
import { getItems } from "./db/items";
import { prisma } from "./db/client";
import { loadGameData } from "./steps/gameData";
import { IconWriter } from "./steps/icons";
import { buildItem, type OutItem } from "./steps/build";
import { computeDiff } from "./steps/diff";
import { buildMounts } from "./steps/mounts";
import { buildMissingSql } from "./steps/missing";
import { writeOutput, writeDiff, writeMounts, writeMissing, type Meta } from "./steps/write";
import { ITEM_TYPES, MOUNT_PET_ITEM_TYPES, typeName } from "./lib/itemtypes";
import { ICONS } from "./paths";

const MISSING_MIGRATION = "006_titemchart_missing_mountpet_items";

const log = (...a: unknown[]) => console.log(...a);

async function main(): Promise<void> {
  fs.rmSync(ICONS, { recursive: true, force: true });
  fs.mkdirSync(ICONS, { recursive: true });

  const db = await getItems();
  log(`SQL TITEMCHART : ${db.size} items (Prisma 7 + @prisma/adapter-mssql)`);

  const g = loadGameData();
  const ok = g.stats.itemRemaining === 0 && g.stats.visualRemaining === 0 ? "✓" : "!!";
  log(`Game files     : TItem ${g.stats.itemCount} / TItemVisual ${g.stats.visualCount} records (byte-exact ${ok})`);
  log(`TClientCmd.tif : ${g.iconList.length} item-slot icon-list entries`);
  log(`Atlas          : ${g.idx.map.size} sprite refs, ${g.pages.size} pages`);

  const writer = new IconWriter(g);
  const out: OutItem[] = [];
  const byType: Record<string, number> = {};
  let withIcon = 0;
  let named = 0;

  for (const [id, dbItem] of db) {
    const tcd = g.tcd.get(id);
    const icon = tcd ? writer.resolve(tcd) : null;
    if (icon != null) withIcon++;
    const item = buildItem(dbItem, tcd, g, icon);
    if (item.name) named++;
    const tn = typeName(item.type);
    byType[tn] = (byType[tn] || 0) + 1;
    out.push(item);
  }
  out.sort((a, b) => a.id - b.id);

  const gallery = writer.gallery();

  const meta: Meta = {
    generatedAt: new Date().toISOString(),
    totalItems: out.length,
    named,
    itemsWithIcon: withIcon,
    galleryIcons: gallery.length,
    customIcons: writer.customCount(),
    itemSource: "SQL TGame_gsp.TITEMCHART (Prisma 7 + @prisma/adapter-mssql)",
    nameSource: "TITEMCHART.szNAME (DB), TItem.tcd fallback",
    iconSource: "m_wVisual[0] -> TItemVisual.m_wIcon (index) -> TClientCmd.tif slot list -> sprite -> TClientI.IDX atlas",
    types: ITEM_TYPES,
    byType,
  };
  writeOutput(out, gallery, meta);

  // DB (TITEMCHART) vs game files (TItem.tcd) differences.
  const diff = computeDiff(db, g.tcd);
  writeDiff(diff);

  // Mount roster (TMount.tcd) joined to the IT_PET items that grant each mount.
  const mounts = buildMounts(g, writer);
  writeMounts(mounts);

  // SQL to add the mount/pet items present in the client data but missing from
  // TITEMCHART (refresh the UI artifact; the tracked migration is promoted by gen:missing).
  const missing = buildMissingSql(db, g.tcd, MOUNT_PET_ITEM_TYPES, MISSING_MIGRATION);
  writeMissing(missing.sql, missing.entries, { scope: "mountpet", migrationName: MISSING_MIGRATION });

  log("");
  log(`Items          : ${out.length} (${named} named)`);
  log(`Item icons     : ${withIcon}/${out.length} resolved a real icon`);
  log(`Icon atlas     : ${gallery.length} sprites (${writer.customCount()} custom), ${writer.written.size} PNGs written`);
  log(`Diff DB↔files  : ${diff.summary.onlyDb} DB-only · ${diff.summary.onlyFile} game-only · ${diff.summary.changed} changed · ${diff.summary.identical} identical`);
  log(`Mounts         : ${mounts.length} definitions (${mounts.filter((m) => m.items.length).length} with a granting item)`);
  log(`Missing (m/p)  : ${missing.entries.length} item(s) in game files but not in TITEMCHART`);
  log(`Wrote          : public/{items,atlas,meta,diff,mounts,missing}.json + missing sql + public/icons/*.png`);

  await prisma.$disconnect();
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
