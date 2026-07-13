// Compare the DB item catalog (TITEMCHART, via Prisma) against the game files
// (TItem.tcd). Produces the data for the "Differences" tab: items present in
// only one source, and items in both whose comparable fields disagree.
//
// NOTE: price is intentionally NOT compared — the DB stores a float rate/0 in
// fPrice while the tcd carries the computed display price (m_dwPrice); they are
// different representations, not a data inconsistency.
//
// KNOWN REPRESENTATION DIFFS (still reported, but tagged "not a real difference"
// in the UI — see src/lib/diffBenign.ts):
//   * level: db bLevel=255 vs game level=1. 255 (0xFF) is this codebase's "none /
//     not-applicable" sentinel (cf. bPrmSlotID=255, storage 0xFF) meaning "no
//     minimum level to use/equip"; the client expresses the same "usable by anyone"
//     as level 1. So 255 == 1 for gate purposes. Genuine level diffs: db 0/70/75/80/85/90.
//   * stack: db bStack=0 vs game stack=1. DB 0 = "unset / not stackable"; the client
//     writes the same single-item item as stack 1. So 0 == 1. Genuine stack diffs are
//     0⇄200, 0⇄30, 30⇄1 (real stacking-limit differences).
import type { DbItem } from "../db/items";
import type { RawItem } from "../lib/types";

export type DiffKind = "onlyDb" | "onlyFile" | "changed";

export interface FieldDiff {
  field: string;
  db: string | number | null;
  file: string | number | null;
}

export interface DiffEntry {
  id: number;
  name: string;
  type: number;
  kind: DiffKind;
  fields?: FieldDiff[]; // only for "changed"
}

export interface DiffSummary {
  dbTotal: number;
  fileTotal: number;
  onlyDb: number;
  onlyFile: number;
  changed: number;
  identical: number;
}

export interface DiffData {
  summary: DiffSummary;
  entries: DiffEntry[];
}

type Val = string | number | null;

export function computeDiff(db: Map<number, DbItem>, tcd: Map<number, RawItem>): DiffData {
  const ids = new Set<number>([...db.keys(), ...tcd.keys()]);
  const entries: DiffEntry[] = [];
  let onlyDb = 0;
  let onlyFile = 0;
  let changed = 0;
  let identical = 0;

  const norm = (v: Val): string => (v == null ? "" : String(v).trim());

  for (const id of ids) {
    const d = db.get(id);
    const f = tcd.get(id);

    if (d && !f) {
      onlyDb++;
      entries.push({ id, name: d.name, type: d.type, kind: "onlyDb" });
      continue;
    }
    if (f && !d) {
      onlyFile++;
      entries.push({ id, name: f.name, type: f.type, kind: "onlyFile" });
      continue;
    }
    if (!d || !f) continue;

    const fields: FieldDiff[] = [];
    const cmp = (field: string, dbv: Val, filev: Val): void => {
      if (norm(dbv) !== norm(filev)) fields.push({ field, db: dbv, file: filev });
    };
    cmp("name", d.name, f.name);
    cmp("type", d.type, f.type);
    cmp("level", d.level, f.level);
    cmp("durability", d.dura, f.duraMax);
    cmp("refine", d.refine, f.refineMax);
    cmp("stack", d.stack, f.stack);
    cmp("attrId", d.attrId, f.attrId);
    cmp("slotId", d.slotId, f.slotId);
    cmp("classId", d.classId, f.classId);
    cmp("useValue", d.useValue, f.useValue);
    cmp("useTime", d.useTime, f.useTime);

    if (fields.length) {
      changed++;
      entries.push({ id, name: d.name || f.name, type: d.type, kind: "changed", fields });
    } else {
      identical++;
    }
  }

  entries.sort((a, b) => a.id - b.id);
  return {
    summary: { dbTotal: db.size, fileTotal: tcd.size, onlyDb, onlyFile, changed, identical },
    entries,
  };
}
