// Parsers for the client data tables, following the exact `CArchive >>` read
// order in CTChart::Init* (TChart.cpp). The read order is authoritative (NOT
// struct declaration order). Each file starts with a WORD record count.
// Correctness is asserted by exact byte consumption (remaining === 0).
import fs from "node:fs";
import { Reader } from "./reader";
import type { RawItem, ItemAttr } from "./types";

const IK_PETTRANSFORM = 40; // kind value set for item 31200 (label only)
const IK_ACCESSORY_SCROLL = 41; // label only
const IT_USE = 7;

export interface ParsedItems {
  items: Map<number, RawItem>;
  remaining: number;
  count: number;
}

/** TItem.tcd -> Map(id -> RawItem). */
export function parseItems(path: string): ParsedItems {
  const r = new Reader(fs.readFileSync(path));
  const count = r.u16();
  const items = new Map<number, RawItem>();
  for (let i = 0; i < count; i++) {
    const it = { visual: [0, 0, 0, 0, 0] } as RawItem;
    it.id = r.u16();
    it.type = r.u8();
    it.kind = r.u8();
    it.attrId = r.u16();
    it.name = r.cstring();
    it.useValue = r.u16();
    it.slotId = r.u32();
    it.classId = r.u32();
    it.prmSlotId = r.u8();
    it.subSlotId = r.u8();
    it.level = r.u8();
    it.canRepair = r.u8();
    it.duraMax = r.u32();
    it.refineMax = r.u8();
    r.f32(); // priceRate
    it.price = r.u32();
    it.minRange = r.u8();
    it.maxRange = r.u8();
    it.stack = r.u8();
    it.slotCount = r.u8();
    it.canGamble = r.u8();
    it.gambleProb = r.u8();
    it.destroyProb = r.u8();
    r.u8(); // pad
    r.u8(); // pad
    it.canGrade = r.u8();
    it.canMagic = r.u8();
    it.canRare = r.u8();
    it.delayGroupId = r.u16();
    it.delay = r.u32();
    it.canTrade = r.u8();
    r.u8(); // pad
    it.isSpecial = r.u8();
    it.useTime = r.u16();
    it.useType = r.u8();
    r.u8(); // weaponId
    r.f32(); // shotSpeed
    r.f32(); // gravity
    it.infoId = r.u32();
    r.u8(); // skillItemType
    for (let v = 0; v < 5; v++) it.visual[v] = r.u16();
    r.u16(); // gradeSFX
    r.u16(); // optionSFX[0]
    r.u16(); // optionSFX[1]
    r.u16(); // optionSFX[2]
    it.canWrap = r.u8();
    it.auctionCode = r.u32();
    it.canColor = r.u8();
    r.u32(); // NIU
    r.u8(); // pad
    r.u16(); // useBP
    r.u8(); // pad
    r.u8(); // pad
    r.u32(); // NIU
    r.u8(); // pad
    items.set(it.id, it);
  }
  const remaining = r.remaining;

  // Hard-coded items the client injects after load (TChart.cpp:2270-2323).
  const clone = (baseId: number, over: Partial<RawItem>): void => {
    const base = items.get(baseId);
    if (!base) return;
    const c: RawItem = { ...base, visual: [...base.visual], ...over };
    items.set(c.id, c);
  };
  clone(7921, { id: 21500, useValue: 1700, name: "Rank Boost (+50%)", infoId: 21500 });
  clone(7921, { id: 21501, useValue: 1701, name: "Rank Boost (+75%)", infoId: 21501 });
  clone(17013, { id: 31200, kind: IK_PETTRANSFORM, name: "Companion Transform Rune", infoId: 31200, type: IT_USE });
  clone(25030, { id: 25041, kind: 19, name: "Al Pacino's Cloak", infoId: 25041, type: 2, attrId: 21148, useValue: 24 });
  clone(25030, { id: 25042, kind: 19, name: "eZ Gucci :^) Cloak", infoId: 25042, type: 2, attrId: 21148, useValue: 25 });
  const scroll = items.get(9584);
  const scrollVis = items.get(11671);
  if (scroll && scrollVis)
    items.set(24505, { ...scroll, id: 24505, visual: [...scrollVis.visual], kind: IK_ACCESSORY_SCROLL, name: "Accessory Refine Scroll" });

  return { items, remaining, count };
}

/** TItemVisual.tcd -> Map(visualId -> iconIndex). Fixed 63-byte records. */
export function parseVisuals(path: string): { map: Map<number, number>; remaining: number; count: number } {
  const r = new Reader(fs.readFileSync(path));
  const count = r.u16();
  const map = new Map<number, number>();
  for (let i = 0; i < count; i++) {
    const id = r.u16();
    r.skip(32); // InvenID, ObjectID, CLKID, CLIID, Mesh[2], Pivot[2] (8 * u32)
    const icon = r.u16();
    r.skip(27); // hide[3] + slashColor + slashTex + slashLen + effectFunc[2] + costumeHide
    map.set(id, icon);
  }
  const remaining = r.remaining;
  // Client clones visual 19263 -> ids 60000..60004 (icon = same id, loose PNGs).
  if (map.has(19263)) for (let id = 60000; id <= 60004; id++) map.set(id, id);
  return { map, remaining, count };
}

/** TItemAttr.tcd -> Map(attrId -> ItemAttr). Fixed 17-byte records. */
export function parseAttrs(path: string): { map: Map<number, ItemAttr>; remaining: number; count: number } {
  const r = new Reader(fs.readFileSync(path));
  const count = r.u16();
  const map = new Map<number, ItemAttr>();
  for (let i = 0; i < count; i++) {
    const id = r.u16();
    r.u8(); // grade
    map.set(id, {
      minAP: r.u16(),
      maxAP: r.u16(),
      dp: r.u16(),
      minMAP: r.u16(),
      maxMAP: r.u16(),
      mdp: r.u16(),
      block: r.u8(),
      speed: r.u8(),
    });
  }
  return { map, remaining: r.remaining, count };
}

export interface MountDef {
  id: number;
  monId: number;
  saddleMonId: number;
  icon: number; // icon INDEX (same space as item m_wIcon)
}

/** TMount.tcd -> Map(id -> MountDef). 4 WORDs/record (CTChart::InitTPET), plus
 *  the 5 mounts (50-54) the client injects as clones of 14/28. */
export function parseMounts(path: string): { map: Map<number, MountDef>; remaining: number; count: number } {
  const r = new Reader(fs.readFileSync(path));
  const count = r.u16();
  const map = new Map<number, MountDef>();
  for (let i = 0; i < count; i++) {
    const id = r.u16();
    const monId = r.u16();
    const saddleMonId = r.u16();
    const icon = r.u16();
    map.set(id, { id, monId, saddleMonId, icon });
  }
  const remaining = r.remaining;
  const inject = (id: number, monId: number, saddleMonId: number, icon: number): void => {
    map.set(id, { id, monId, saddleMonId, icon });
  };
  if (map.has(14)) inject(50, 31250, 31251, 60004);
  if (map.has(28)) {
    inject(51, 31254, 31255, 60000);
    inject(52, 31256, 31257, 60001);
    inject(53, 31258, 31259, 60002);
    inject(54, 31252, 31253, 60003);
  }
  return { map, remaining, count };
}

/** TInfo.tcd -> Map(infoId -> lines). Per record: DWORD id, BYTE count, count×CString. */
export function parseInfo(path: string): { map: Map<number, string[]>; remaining: number; count: number } {
  const r = new Reader(fs.readFileSync(path));
  const count = r.u16();
  const map = new Map<number, string[]>();
  for (let i = 0; i < count; i++) {
    const id = r.u32();
    const n = r.u8();
    const lines: string[] = [];
    for (let j = 0; j < n; j++) lines.push(r.cstring());
    map.set(id, lines);
  }
  return { map, remaining: r.remaining, count };
}
