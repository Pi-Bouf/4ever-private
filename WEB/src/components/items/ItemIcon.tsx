import { useState } from "react";
import type { Item } from "../../types";
import { iconUrl } from "../../lib/iconUrl";
import { PixelImg } from "../ui/PixelImg";
import { PlaceholderIcon } from "./PlaceholderIcon";

/** Item icon with a graceful placeholder fallback (missing file or icons off). */
export function ItemIcon({ item, show, size = 40 }: { item: Item; show: boolean; size?: number }) {
  const [ok, setOk] = useState(true);
  const showImg = show && item.iconBest != null && ok;
  return (
    <div
      className="grid shrink-0 place-items-center overflow-hidden rounded-[7px] bg-panel2"
      style={{ width: size, height: size }}
    >
      {showImg ? (
        <PixelImg src={iconUrl(item.iconBest as number)} size={size} onError={() => setOk(false)} />
      ) : (
        <PlaceholderIcon id={item.id} type={item.type} size={size} />
      )}
    </div>
  );
}
