import { useMemo, useState } from "react";
import type { DiffData } from "../../types";
import { parseQuery } from "../../lib/search";
import { useIncremental } from "../../hooks/useIncremental";
import { DiffSummary, type KindFilter } from "./DiffSummary";
import { DiffList } from "./DiffList";
import { EmptyState } from "../common/EmptyState";

export function DiffView({ diff, query, types }: { diff: DiffData; query: string; types: Record<string, string> }) {
  const [kind, setKind] = useState<KindFilter>("all");
  const { needle, id } = parseQuery(query);

  const filtered = useMemo(
    () =>
      diff.entries.filter((e) => {
        if (kind !== "all" && e.kind !== kind) return false;
        if (!needle) return true;
        if (id != null && e.id === id) return true;
        return e.name.toLowerCase().includes(needle) || String(e.id).includes(needle);
      }),
    [diff, kind, needle, id],
  );

  const { limit, sentinel } = useIncremental(`diff|${kind}|${needle}`);

  return (
    <>
      <DiffSummary summary={diff.summary} active={kind} onSelect={setKind} />
      {filtered.length === 0 ? (
        <EmptyState>No differences match.</EmptyState>
      ) : (
        <>
          <DiffList entries={filtered.slice(0, limit)} types={types} />
          <div ref={sentinel} className="h-px" />
        </>
      )}
    </>
  );
}
