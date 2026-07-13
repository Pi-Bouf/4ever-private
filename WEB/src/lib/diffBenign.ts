import type { DiffEntry, FieldDiff } from "../types";

/**
 * Explanation for a field diff that is only a representation/convention mismatch,
 * not a real data difference (see WEB/script/steps/diff.ts for the rationale).
 * Returns null when the diff is a genuine difference.
 *
 * - level: DB bLevel = 255 (0xFF "no level requirement" sentinel) == client level 1.
 * - stack: DB bStack = 0 (unset — not stackable) == client stack 1 (single).
 *   NOTE: only 0⇄1 is benign; 0⇄200 / 0⇄30 / 30⇄1 are genuine stacking differences.
 */
export function benignReason(f: FieldDiff): string | null {
  if (f.field === "level" && Number(f.db) === 255 && Number(f.file) === 1)
    return "DB 255 (0xFF — no level requirement) equals client level 1.";
  if (f.field === "stack" && Number(f.db) === 0 && Number(f.file) === 1)
    return "DB 0 (unset — not stackable) equals client stack 1.";
  return null;
}

export function isBenignFieldDiff(f: FieldDiff): boolean {
  return benignReason(f) !== null;
}

/** A "changed" entry whose every field diff is benign — so it is not a real difference. */
export function isBenignEntry(entry: DiffEntry): boolean {
  return entry.kind === "changed" && !!entry.fields?.length && entry.fields.every(isBenignFieldDiff);
}
