// Build the mount roster from TMount.tcd (the rideable-pet definitions) and
// join it to the IT_PET items (type 12) that grant each mount — an item's
// m_wUseValue is the mount id it creates (CTChart::FindTPETTEMP).
import { parseMounts } from "../lib/tcd";
import { GAME_FILES } from "../paths";
import type { GameData } from "./gameData";
import type { IconWriter } from "./icons";
import type { RawItem } from "../lib/types";

const IT_PET = 12;

export interface Mount {
  id: number;
  monId: number;
  saddleMonId: number;
  iconIdx: number; // TMount m_wIcon (index)
  icon: number | null; // resolved sprite/custom id (PNG key), or null
  name: string; // from the granting item, else "Mount #id"
  items: number[]; // IT_PET item ids that grant this mount
}

export function buildMounts(g: GameData, writer: IconWriter): Mount[] {
  const { map } = parseMounts(GAME_FILES.mounts);

  // IT_PET item useValue -> the items that create that mount id.
  const byUseValue = new Map<number, RawItem[]>();
  for (const it of g.tcd.values()) {
    if (it.type !== IT_PET || !it.useValue) continue;
    const arr = byUseValue.get(it.useValue);
    if (arr) arr.push(it);
    else byUseValue.set(it.useValue, [it]);
  }

  const out: Mount[] = [];
  for (const m of map.values()) {
    const items = (byUseValue.get(m.id) ?? []).sort((a, b) => a.id - b.id);
    // Prefer the granting item's icon; fall back to the mount's own m_wIcon.
    const icon = (items[0] ? writer.resolve(items[0]) : null) ?? writer.resolveIndex(m.icon);
    const name = items[0]?.name.trim() || `Mount #${m.id}`;
    out.push({
      id: m.id,
      monId: m.monId,
      saddleMonId: m.saddleMonId,
      iconIdx: m.icon,
      icon,
      name,
      items: items.map((i) => i.id),
    });
  }
  out.sort((a, b) => a.id - b.id);
  return out;
}
