import type { Item } from "../../types";
import { ItemCard } from "./ItemCard";

export function ItemGrid({
  items,
  types,
  showIcons,
  onSelect,
}: {
  items: Item[];
  types: Record<string, string>;
  showIcons: boolean;
  onSelect: (it: Item) => void;
}) {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(210px,1fr))] gap-2.5">
      {items.map((it) => (
        <ItemCard key={it.id} item={it} types={types} showIcons={showIcons} onSelect={onSelect} />
      ))}
    </div>
  );
}
