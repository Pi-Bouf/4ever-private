import type { FieldDiff } from "../../types";
import { benignReason } from "../../lib/diffBenign";

const fmt = (v: string | number | null): string => (v == null || v === "" ? "∅" : String(v));

/** One differing field: name + DB value (blue) vs game-file value (amber).
 *  Representation-only diffs (e.g. level 255 == 1, stack 0 == 1) are dimmed and tagged. */
export function FieldChange({ diff }: { diff: FieldDiff }) {
  const reason = benignReason(diff);
  const benign = reason !== null;
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded px-2 py-0.5 text-[11.5px] ${
        benign ? "bg-panel2/50 opacity-70" : "bg-panel2"
      }`}
      title={benign ? `Representation only — ${reason} Not a real difference.` : undefined}
    >
      <span className="font-medium text-muted">{diff.field}</span>
      <span className="text-[#7fc0ff]">db {fmt(diff.db)}</span>
      <span className="text-[#f0c078]">game {fmt(diff.file)}</span>
      {benign && <span className="text-[10px] font-medium text-[#8fd6a0]">not a real difference</span>}
    </span>
  );
}
