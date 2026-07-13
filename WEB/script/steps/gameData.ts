// Loads everything the extractor needs from the game files (the DB is separate).
import fs from "node:fs";
import path from "node:path";
import { parseItems, parseVisuals, parseAttrs, parseInfo } from "../lib/tcd";
import { parseIdx, loadPages } from "../lib/atlas";
import { loadIconList } from "../lib/tif";
import { GAME, GAME_FILES, CUSTOM } from "../paths";
import type { RawItem, ItemAttr, Idx, RgbaImage } from "../lib/types";

export interface GameData {
  tcd: Map<number, RawItem>; // TItem.tcd — needed for m_wVisual (client-only) + flags/infoId
  visuals: Map<number, number>; // visualId -> icon INDEX
  attrs: Map<number, ItemAttr>; // wAttrID -> AP/DP
  infos: Map<number, string[]>; // infoId -> tooltip lines
  iconList: (number | null)[]; // TClientCmd.tif item-slot list: index -> spriteId
  idx: Idx; // TClientI.IDX
  timBufs: (Buffer | null)[];
  pages: Map<number, RgbaImage>;
  customIds: Set<number>;
  stats: { itemCount: number; itemRemaining: number; visualCount: number; visualRemaining: number };
}

export function loadGameData(): GameData {
  const { items: tcd, remaining: itemRemaining, count: itemCount } = parseItems(GAME_FILES.items);
  const { map: visuals, remaining: visualRemaining, count: visualCount } = parseVisuals(GAME_FILES.visuals);
  const { map: attrs } = parseAttrs(GAME_FILES.attrs);
  const { map: infos } = parseInfo(GAME_FILES.info);
  const iconList = loadIconList(GAME_FILES.tif);
  const idx = parseIdx(GAME_FILES.idx);
  const timBufs = idx.files.map((f) => {
    const fp = path.join(GAME, "Data", f);
    return fs.existsSync(fp) ? fs.readFileSync(fp) : null;
  });
  const pages = loadPages(GAME);

  const customIds = new Set<number>();
  if (fs.existsSync(CUSTOM))
    for (const f of fs.readdirSync(CUSTOM)) {
      const m = f.match(/^(\d+)\.png$/i);
      if (m) customIds.add(Number(m[1]));
    }

  return {
    tcd,
    visuals,
    attrs,
    infos,
    iconList,
    idx,
    timBufs,
    pages,
    customIds,
    stats: { itemCount, itemRemaining, visualCount, visualRemaining },
  };
}
