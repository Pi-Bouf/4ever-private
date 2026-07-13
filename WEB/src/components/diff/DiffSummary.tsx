import type { DiffKind, DiffSummary as Summary } from "../../types";
import { DiffStatCard } from "./DiffStatCard";

export type KindFilter = DiffKind | "all";

export function DiffSummary({
  summary,
  active,
  onSelect,
}: {
  summary: Summary;
  active: KindFilter;
  onSelect: (k: KindFilter) => void;
}) {
  const total = summary.onlyDb + summary.onlyFile + summary.changed;
  return (
    <div className="mb-4 grid grid-cols-2 gap-2 lg:grid-cols-4">
      <DiffStatCard label="All differences" value={total} active={active === "all"} onClick={() => onSelect("all")} />
      <DiffStatCard
        label="DB only"
        hint="in TITEMCHART, not in files"
        value={summary.onlyDb}
        active={active === "onlyDb"}
        onClick={() => onSelect("onlyDb")}
      />
      <DiffStatCard
        label="Game only"
        hint="in TItem.tcd, not in DB"
        value={summary.onlyFile}
        active={active === "onlyFile"}
        onClick={() => onSelect("onlyFile")}
      />
      <DiffStatCard
        label="Field changes"
        hint="in both, values differ"
        value={summary.changed}
        active={active === "changed"}
        onClick={() => onSelect("changed")}
      />
    </div>
  );
}
