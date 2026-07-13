import type { Item } from "../../types";
import { Button } from "../ui/Button";
import { ItemIcon } from "./ItemIcon";
import { ItemName } from "./ItemName";
import { TypeBadge } from "./TypeBadge";
import { ItemId } from "../common/ItemId";

export function ItemCard({
  item,
  types,
  showIcons,
  onSelect,
}: {
  item: Item;
  types: Record<string, string>;
  showIcons: boolean;
  onSelect: (it: Item) => void;
}) {
  return (
    <Button
      onClick={() => onSelect(item)}
      className="flex min-h-[56px] items-center gap-3 rounded-[10px] border border-border bg-panel px-2.5 py-2 text-left transition-colors hover:border-borderlt hover:bg-panel2"
    >
      <ItemIcon item={item} show={showIcons} />
      <div className="min-w-0">
        <ItemName name={item.name} />
        <div className="mt-1 flex items-center gap-2">
          <TypeBadge type={item.type} types={types} />
          <ItemId id={item.id} />
        </div>
      </div>
    </Button>
  );
}
