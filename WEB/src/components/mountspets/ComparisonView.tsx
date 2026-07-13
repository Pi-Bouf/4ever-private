import { useMemo } from "react";
import type { DiffData, Item, MissingData } from "../../types";
import { MOUNT_ITEM_TYPES, MOUNT_PET_ITEM_TYPES, PET_ITEM_TYPES } from "../../lib/petTypes";
import { scopeDiff } from "../../lib/diffScope";
import { SectionTitle } from "../ui/SectionTitle";
import { DiffView } from "../diff/DiffView";
import { EmptyState } from "../common/EmptyState";
import { CompareTable } from "./CompareTable";
import { MissingItemsCallout } from "./MissingItemsCallout";

interface GroupStats {
  count: number;
  withIcon: number;
  levels: number[];
  tradable: number;
  cash: number;
}

function groupStats(items: Item[], typeSet: number[]): GroupStats {
  const g = items.filter((it) => typeSet.includes(it.type));
  return {
    count: g.length,
    withIcon: g.filter((it) => it.iconBest != null).length,
    levels: g.map((it) => it.det?.level).filter((n): n is number => n != null && n > 0),
    tradable: g.filter((it) => it.det?.flags?.includes("tradable")).length,
    cash: g.filter((it) => it.det?.flags?.includes("cash")).length,
  };
}

const range = (ns: number[]): string => (ns.length ? `${Math.min(...ns)}–${Math.max(...ns)}` : "—");
const avg = (ns: number[]): string => (ns.length ? String(Math.round(ns.reduce((a, b) => a + b, 0) / ns.length)) : "—");
const pct = (n: number, d: number): string => (d ? `${n} (${Math.round((100 * n) / d)}%)` : "0");

export function ComparisonView({
  items,
  diff,
  missing,
  query,
  types,
}: {
  items: Item[];
  diff: DiffData | null;
  missing: MissingData | null;
  query: string;
  types: Record<string, string>;
}) {
  const scoped = useMemo(() => (diff ? scopeDiff(diff, MOUNT_PET_ITEM_TYPES) : null), [diff]);

  const m = groupStats(items, MOUNT_ITEM_TYPES);
  const p = groupStats(items, PET_ITEM_TYPES);
  const familyRows: [string, string, string][] = [
    ["Items", String(m.count), String(p.count)],
    ["With icon", pct(m.withIcon, m.count), pct(p.withIcon, p.count)],
    ["Level range", range(m.levels), range(p.levels)],
    ["Average level", avg(m.levels), avg(p.levels)],
    ["Tradable", pct(m.tradable, m.count), pct(p.tradable, p.count)],
    ["Cash-shop", pct(m.cash, m.count), pct(p.cash, p.count)],
  ];

  return (
    <>
      <SectionTitle hint="Mount, saddle and companion items in the SQL database (TITEMCHART) compared against the game files (TItem.tcd). Only differing rows are shown; price is excluded as a known representation difference.">
        Database vs game files (TCD)
      </SectionTitle>
      {missing && <MissingItemsCallout missing={missing} />}
      {scoped ? (
        scoped.entries.length === 0 ? (
          <EmptyState>Database and game files agree for every mount/pet item.</EmptyState>
        ) : (
          <DiffView diff={scoped} query={query} types={types} />
        )
      ) : (
        <EmptyState>No diff data — run the full extract (needs the database) to generate it.</EmptyState>
      )}

      <SectionTitle hint="The two item families side by side, from the merged catalog.">Item families</SectionTitle>
      <p className="mb-3 max-w-2xl text-[12.5px] text-muted">
        <span className="text-[#7fc0ff]">Mount items</span> (Pet + Saddle types) versus{" "}
        <span className="text-[#f0c078]">companion items</span> (Companion, Companion Costume, Companion Item).
      </p>
      <CompareTable colA="Mount items" colB="Companion items" rows={familyRows} />
    </>
  );
}
