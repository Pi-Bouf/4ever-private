// Resolves + writes item icons, reproducing the client's exact path:
//   item.m_wVisual[0] -> TItemVisual.m_wIcon (INDEX)
//     index >= 60000 : Custom/<index>.png
//     else           : TClientCmd.tif list[index] -> spriteId -> atlas
import fs from "node:fs";
import path from "node:path";
import { extractIcon } from "../lib/atlas";
import { encodePNG } from "../lib/png";
import { CUSTOM, ICONS } from "../paths";
import type { GameData } from "./gameData";
import type { RawItem, RgbaImage } from "../lib/types";

const MAX_ICON = 64; // reject oversized (non-icon) graphics
const OWN_IMAGE_BASE = 60000;

function nonBlank(img: RgbaImage): boolean {
  let n = 0;
  for (let i = 3; i < img.rgba.length; i += 4) if (img.rgba[i] > 16) n++;
  return n > img.width * img.height * 0.02;
}

export class IconWriter {
  readonly written = new Map<number, "custom" | "atlas">();
  private readonly failed = new Set<number>();

  constructor(private readonly g: GameData) {}

  /** Write one sprite/custom icon PNG once. Returns whether it exists after. */
  ensure(id: number): boolean {
    if (!id) return false;
    if (this.written.has(id)) return true;
    if (this.failed.has(id)) return false;
    const dst = path.join(ICONS, `${id}.png`);
    if (this.g.customIds.has(id)) {
      fs.copyFileSync(path.join(CUSTOM, `${id}.png`), dst);
      this.written.set(id, "custom");
      return true;
    }
    const img = extractIcon(id, this.g.idx, this.g.timBufs, this.g.pages);
    if (img && Math.max(img.width, img.height) <= MAX_ICON && nonBlank(img)) {
      fs.writeFileSync(dst, encodePNG(img.rgba, img.width, img.height));
      this.written.set(id, "atlas");
      return true;
    }
    this.failed.add(id);
    return false;
  }

  /** icon INDEX -> written icon key (spriteId or custom id) | null.
   *  index >= 60000 is a Custom/<index>.png; else it indexes the TClientCmd.tif
   *  item-slot list to a sprite id. Used by both items and mounts. */
  resolveIndex(index: number | null | undefined): number | null {
    if (index == null) return null;
    if (index >= OWN_IMAGE_BASE) return this.ensure(index) ? index : null;
    if (index < this.g.iconList.length) {
      const sprite = this.g.iconList[index];
      if (sprite != null && this.ensure(sprite)) return sprite;
    }
    return null;
  }

  /** item -> written icon key (spriteId or custom id) | null. */
  resolve(item: RawItem): number | null {
    const v0 = item.visual[0];
    if (!v0) return null;
    return this.resolveIndex(this.g.visuals.get(v0));
  }

  /** Every distinct sprite in the item-slot list we can render + custom PNGs. */
  gallery(): number[] {
    const ids: number[] = [];
    const seen = new Set<number>();
    for (const s of this.g.iconList) {
      if (s != null && !seen.has(s)) {
        seen.add(s);
        if (this.ensure(s)) ids.push(s);
      }
    }
    for (const c of this.g.customIds) if (this.ensure(c)) ids.push(c);
    return ids.sort((a, b) => a - b);
  }

  customCount(): number {
    let n = 0;
    for (const v of this.written.values()) if (v === "custom") n++;
    return n;
  }
}
