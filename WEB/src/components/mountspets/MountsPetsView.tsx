import { useMemo, useState } from "react";
import type { DiffData, Item, MissingData, Mount } from "../../types";
import { parseQuery } from "../../lib/search";
import { PET_ITEM_TYPES } from "../../lib/petTypes";
import { useIncremental } from "../../hooks/useIncremental";
import { SegmentedControl } from "../ui/SegmentedControl";
import { MountGrid } from "./MountGrid";
import { ItemGrid } from "../items/ItemGrid";
import { ComparisonView } from "./ComparisonView";
import { EmptyState } from "../common/EmptyState";

type Sub = "mounts" | "pets" | "compare";

export function MountsPetsView({
  mounts,
  items,
  itemsById,
  diff,
  missing,
  types,
  query,
  showIcons,
  onSelect,
}: {
  mounts: Mount[];
  items: Item[];
  itemsById: Map<number, Item>;
  diff: DiffData | null;
  missing: MissingData | null;
  types: Record<string, string>;
  query: string;
  showIcons: boolean;
  onSelect: (it: Item) => void;
}) {
  const [sub, setSub] = useState<Sub>("mounts");
  const { needle, id } = parseQuery(query);

  const petAll = useMemo(() => items.filter((it) => PET_ITEM_TYPES.includes(it.type)), [items]);
  const match = (name: string, itemId: number): boolean =>
    !needle || (id != null && itemId === id) || name.toLowerCase().includes(needle) || String(itemId).includes(needle);

  const filteredMounts = useMemo(() => mounts.filter((m) => match(m.name, m.id)), [mounts, needle, id]);
  const filteredPets = useMemo(() => petAll.filter((it) => match(it.name, it.id)), [petAll, needle, id]);

  const inc = useIncremental(`mp|${sub}|${needle}`);

  const openMount = (m: Mount): void => {
    const itemId = m.items[0];
    const it = itemId != null ? itemsById.get(itemId) : undefined;
    if (it) onSelect(it);
  };

  return (
    <>
      <SegmentedControl
        value={sub}
        onChange={setSub}
        options={[
          { value: "mounts", label: `Mounts (${mounts.length})` },
          { value: "pets", label: `Pets & Companions (${petAll.length})` },
          { value: "compare", label: "Comparison" },
        ]}
      />

      {sub === "mounts" &&
        (filteredMounts.length === 0 ? (
          <EmptyState>No mounts match.</EmptyState>
        ) : (
          <>
            <MountGrid mounts={filteredMounts.slice(0, inc.limit)} onSelect={openMount} />
            <div ref={inc.sentinel} className="h-px" />
          </>
        ))}

      {sub === "pets" &&
        (filteredPets.length === 0 ? (
          <EmptyState>No pet or companion items match.</EmptyState>
        ) : (
          <>
            <ItemGrid items={filteredPets.slice(0, inc.limit)} types={types} showIcons={showIcons} onSelect={onSelect} />
            <div ref={inc.sentinel} className="h-px" />
          </>
        ))}

      {sub === "compare" && (
        <ComparisonView items={items} diff={diff} missing={missing} query={query} types={types} />
      )}
    </>
  );
}
