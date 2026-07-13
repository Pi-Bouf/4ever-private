import type { DiffData } from "../types";

/**
 * Restrict a DB-vs-game-file diff to a set of item types and recompute the
 * difference counts. Used by the Mounts & Pets comparison to reuse the full
 * `DiffView` machinery scoped to the mount/pet item families.
 *
 * `dbTotal` / `fileTotal` / `identical` are not derivable per-type from the
 * diff alone (identical rows are never emitted as entries), and the summary
 * cards only render the three difference counts, so those totals are left at 0.
 */
export function scopeDiff(diff: DiffData, typeSet: readonly number[]): DiffData {
  const set = new Set(typeSet);
  const entries = diff.entries.filter((e) => set.has(e.type));
  let onlyDb = 0;
  let onlyFile = 0;
  let changed = 0;
  for (const e of entries) {
    if (e.kind === "onlyDb") onlyDb++;
    else if (e.kind === "onlyFile") onlyFile++;
    else changed++;
  }
  return {
    summary: { dbTotal: 0, fileTotal: 0, onlyDb, onlyFile, changed, identical: 0 },
    entries,
  };
}
