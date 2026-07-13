// Generate a SQL migration that ADDS the items present in the client data
// (TItem.tcd) but missing from the server DB (TITEMCHART), scoped to a set of
// item types (default: the mount/pet domain).
//
// TITEMCHART has 52 NOT-NULL columns and ~15 of them do not exist in TItem.tcd,
// so a full row cannot be built from the client file alone. We therefore reuse
// the technique from Database/migrations/004_companion_missing_species.sql:
// CLONE an existing sibling row of the same item type, then overwrite only the
// columns the client file provides. Each insert is idempotent.
import type { DbItem } from "../db/items";
import type { RawItem } from "../lib/types";

export interface MissingEntry {
  id: number;
  name: string;
  type: number;
  siblingId: number;
}

const SMALLINT_MAX = 32767; // wItemID is smallint

/** nvarchar literal: N'...' with single quotes doubled. */
function nstr(s: string): string {
  return `N'${s.replace(/'/g, "''")}'`;
}

/** Best sibling to clone: same bType, prefer same bKind, tiebreak nearest wItemID. */
function pickSibling(item: RawItem, db: Map<number, DbItem>): DbItem | null {
  let best: DbItem | null = null;
  let bestScore = -Infinity;
  for (const d of db.values()) {
    if (d.type !== item.type) continue;
    const kindMatch = d.kind != null && d.kind === item.kind ? 1 : 0;
    const score = kindMatch * 1e9 - Math.abs(d.id - item.id);
    if (score > bestScore) {
      bestScore = score;
      best = d;
    }
  }
  return best;
}

/** Columns overwritten from the client file (everything else inherited from the sibling). */
function assignments(it: RawItem): string {
  return [
    `wItemID=${it.id}`,
    `szNAME=${nstr(it.name)}`,
    `bType=${it.type}`,
    `bKind=${it.kind}`,
    `wAttrID=${it.attrId}`,
    `wUseValue=${it.useValue}`,
    `dwSlotID=${it.slotId}`,
    `dwClassID=${it.classId}`,
    `bPrmSlotID=${it.prmSlotId}`,
    `bSubSlotID=${it.subSlotId}`,
    `bLevel=${it.level}`,
    `bCanRepair=${it.canRepair}`,
    `dwDuraMax=${it.duraMax}`,
    `bRefineMax=${it.refineMax}`,
    `bMinRange=${it.minRange}`,
    `bMaxRange=${it.maxRange}`,
    `bStack=${it.stack}`,
    `bSlotCount=${it.slotCount}`,
    `bCanGamble=${it.canGamble}`,
    `bGambleProb=${it.gambleProb}`,
    `bDestroyProb=${it.destroyProb}`,
    `bCanGrade=${it.canGrade}`,
    `bCanMagic=${it.canMagic}`,
    `bCanRare=${it.canRare}`,
    `wDelayGroupID=${it.delayGroupId}`,
    `dwDelay=${it.delay}`,
    `bIsSpecial=${it.isSpecial}`,
    `wUseTime=${it.useTime}`,
    `bUseType=${it.useType}`,
    `bCanWrap=${it.canWrap}`,
    `dwCode=${it.auctionCode}`,
    `bCanColor=${it.canColor}`,
  ].join(", ");
}

function itemBlock(it: RawItem, siblingId: number): string {
  const tmp = `#t${it.id}`;
  return `-- ${it.id}  ${it.name}  (clone of ${siblingId}, bType=${it.type})
IF NOT EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = ${it.id})
   AND EXISTS (SELECT 1 FROM dbo.TITEMCHART WHERE wItemID = ${siblingId})
BEGIN
    SELECT * INTO ${tmp} FROM dbo.TITEMCHART WHERE wItemID = ${siblingId};
    UPDATE ${tmp} SET ${assignments(it)};
    INSERT INTO dbo.TITEMCHART SELECT * FROM ${tmp};
    DROP TABLE ${tmp};
END
GO
`;
}

function header(name: string, count: number): string {
  return `-- ${name}
--
-- Adds the ${count} mount/pet item(s) present in the client data (Game/Tcd/TItem.tcd)
-- but missing from dbo.TITEMCHART. TITEMCHART has 52 NOT-NULL columns and ~15 of them
-- do not exist in the client file, so each row is built by CLONING an existing sibling
-- of the same item type (bType) and overwriting the fields the client file provides
-- (same technique as 004_companion_missing_species.sql on TMONSTERCHART).
--
-- Inherited from the sibling (not present in TItem.tcd): fPrice, fPvPrice, bIsSell,
-- bEquipSkill, bUseItemKind/Count, bGrade, bDropLevel, dwSpeedInc, bItemCountry,
-- fRevision/fMRevision/fAtRate/fMAtRate, wItemProb_G, wWeight, bGroupID, bInitState, wExpandValue.
--
-- NOTE: TMapSvr/TWorldSvr load TITEMCHART once at startup - RESTART the servers after applying.
-- Idempotent: each insert is guarded by IF NOT EXISTS (target) AND EXISTS (sibling).
USE [TGame_gsp];
GO

SET NOCOUNT ON;
GO

`;
}

export function buildMissingSql(
  db: Map<number, DbItem>,
  tcd: Map<number, RawItem>,
  typeSet: readonly number[],
  migrationName = "006_titemchart_missing_mountpet_items",
): { sql: string; entries: MissingEntry[] } {
  const set = new Set(typeSet);
  const entries: MissingEntry[] = [];
  const blocks: string[] = [];

  const missing = [...tcd.values()].filter((it) => set.has(it.type) && !db.has(it.id)).sort((a, b) => a.id - b.id);

  for (const it of missing) {
    if (it.id > SMALLINT_MAX) {
      console.warn(`  skip ${it.id} "${it.name}" — exceeds smallint PK max (${SMALLINT_MAX})`);
      continue;
    }
    const sib = pickSibling(it, db);
    if (!sib) {
      console.warn(`  skip ${it.id} "${it.name}" — no sibling of bType=${it.type} in DB`);
      continue;
    }
    entries.push({ id: it.id, name: it.name, type: it.type, siblingId: sib.id });
    blocks.push(itemBlock(it, sib.id));
  }

  const sql = header(migrationName, entries.length) + blocks.join("\n");
  return { sql, entries };
}
