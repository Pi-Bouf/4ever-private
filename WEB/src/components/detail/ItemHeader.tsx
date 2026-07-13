import type { Item } from "../../types";
import { LargeIcon } from "./LargeIcon";
import { TypeBadge } from "../items/TypeBadge";
import { FlagList } from "./FlagList";
import { ItemId } from "../common/ItemId";

export function ItemHeader({ item, types }: { item: Item; types: Record<string, string> }) {
  return (
    <div className="mb-4 flex items-start gap-4 pr-7">
      <LargeIcon item={item} />
      <div className="min-w-0">
        <div className="text-lg font-bold">{item.name || "(no name)"}</div>
        <div className="mt-1.5 flex flex-wrap items-center gap-2">
          <TypeBadge type={item.type} types={types} />
          <ItemId id={item.id} />
          <span className="text-[11px] text-muted">kind {item.kind}</span>
        </div>
        {item.det?.flags && <FlagList flags={item.det.flags} />}
      </div>
    </div>
  );
}
