// Standalone generator for the "missing mount/pet items" SQL migration.
//
// Needs the DB up (to find which client items are absent from TITEMCHART and to
// pick a sibling row to clone). Writes the public artifacts (for the UI Download
// button) AND promotes the tracked Database/migrations/006_*.sql — create-if-absent,
// so a second run never mutates an applied migration.
//
// Run: `npm run gen:missing` (docker MSSQL must be up).
import { getItems } from "./db/items";
import { prisma } from "./db/client";
import { loadGameData } from "./steps/gameData";
import { buildMissingSql } from "./steps/missing";
import { writeMissing } from "./steps/write";
import { MOUNT_PET_ITEM_TYPES, typeName } from "./lib/itemtypes";
import { MISSING_SQL_FILE } from "./paths";

const MIGRATION = "006_titemchart_missing_mountpet_items";

async function main(): Promise<void> {
  const db = await getItems();
  const g = loadGameData();
  const ok = g.stats.itemRemaining === 0 && g.stats.visualRemaining === 0 ? "✓" : "!!";
  console.log(`SQL TITEMCHART : ${db.size} items`);
  console.log(`Game files     : TItem ${g.stats.itemCount} / TItemVisual ${g.stats.visualCount} records (byte-exact ${ok})`);

  const { sql, entries } = buildMissingSql(db, g.tcd, MOUNT_PET_ITEM_TYPES, MIGRATION);
  writeMissing(sql, entries, { scope: "mountpet", migrationName: MIGRATION, promote: true });

  console.log(`Missing (mount/pet): ${entries.length} item(s) to add`);
  for (const e of entries) console.log(`  + ${e.id}  ${e.name}  [${typeName(e.type)}]  <- clone ${e.siblingId}`);
  console.log(`Wrote          : public/${MISSING_SQL_FILE} + public/missing.json`);

  await prisma.$disconnect();
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
