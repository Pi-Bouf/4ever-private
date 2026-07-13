import type { Item } from "../../types";
import { useIncremental } from "../../hooks/useIncremental";
import { ItemGrid } from "./ItemGrid";
import { EmptyState } from "../common/EmptyState";

export function ItemsView({
  items,
  types,
  showIcons,
  onSelect,
  resetKey,
}: {
  items: Item[];
  types: Record<string, string>;
  showIcons: boolean;
  onSelect: (it: Item) => void;
  resetKey: string;
}) {
  const { limit, sentinel } = useIncremental(resetKey);
  if (items.length === 0) return <EmptyState>No items match your search.</EmptyState>;
  return (
    <>
      <ItemGrid items={items.slice(0, limit)} types={types} showIcons={showIcons} onSelect={onSelect} />
      <div ref={sentinel} className="h-px" />
    </>
  );
}
