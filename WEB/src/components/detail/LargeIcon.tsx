import type { Item } from "../../types";
import { iconUrl } from "../../lib/iconUrl";
import { PixelImg } from "../ui/PixelImg";
import { PlaceholderIcon } from "../items/PlaceholderIcon";

export function LargeIcon({ item }: { item: Item }) {
  return item.iconBest != null ? (
    <PixelImg src={iconUrl(item.iconBest)} size={64} className="shrink-0 rounded-[9px] bg-panel2" />
  ) : (
    <PlaceholderIcon id={item.id} type={item.type} size={64} />
  );
}
