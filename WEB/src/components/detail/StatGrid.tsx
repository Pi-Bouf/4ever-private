import type { ItemDetail } from "../../types";
import { statRows } from "../../lib/format";
import { StatRow } from "./StatRow";

export function StatGrid({ det }: { det: ItemDetail }) {
  const rows = statRows(det);
  if (rows.length === 0) return null;
  return (
    <div className="grid grid-cols-2 gap-px overflow-hidden rounded-[10px] border border-border bg-border">
      {rows.map(([label, value]) => (
        <StatRow key={label} label={label} value={value} />
      ))}
    </div>
  );
}
