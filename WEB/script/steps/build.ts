// Merge a DB row (stats/name) + tcd record (flags/visual/desc) + attrs/info
// into the final Item object written to items.json.
import type { DbItem } from "../db/items";
import type { GameData } from "./gameData";
import type { RawItem } from "../lib/types";

export interface ItemDetail {
  level?: number;
  price?: number;
  dura?: number;
  stack?: number;
  refine?: number;
  slot?: number;
  cls?: number;
  useValue?: number;
  useTime?: number;
  range?: [number, number];
  ap?: number[]; // [max] or [min,max]
  dp?: number;
  map?: number[];
  mdp?: number;
  block?: number;
  speed?: number;
  flags?: string[];
  desc?: string;
}

export interface OutItem {
  id: number;
  name: string;
  type: number;
  kind: number;
  iconBest: number | null;
  det?: ItemDetail;
}

export function buildItem(db: DbItem, tcd: RawItem | undefined, g: GameData, icon: number | null): OutItem {
  const d: ItemDetail = {};
  if (db.level) d.level = db.level;
  const price = db.price || tcd?.price; // DB fPrice is 0 for many rows; tcd has the display price
  if (price) d.price = price;
  if (db.dura) d.dura = db.dura;
  if (db.stack && db.stack > 1) d.stack = db.stack;
  if (db.refine) d.refine = db.refine;
  if (db.slotId) d.slot = db.slotId;
  if (db.classId) d.cls = db.classId;
  if (db.useValue) d.useValue = db.useValue;
  if (db.useTime) d.useTime = db.useTime;
  if (tcd && (tcd.minRange || tcd.maxRange)) d.range = [tcd.minRange, tcd.maxRange];

  const attrId = db.attrId || tcd?.attrId;
  const a = attrId ? g.attrs.get(attrId) : undefined;
  if (a) {
    if (a.maxAP) d.ap = a.minAP === a.maxAP ? [a.maxAP] : [a.minAP, a.maxAP];
    if (a.dp) d.dp = a.dp;
    if (a.maxMAP) d.map = a.minMAP === a.maxMAP ? [a.maxMAP] : [a.minMAP, a.maxMAP];
    if (a.mdp) d.mdp = a.mdp;
    if (a.block) d.block = a.block;
    if (a.speed) d.speed = a.speed;
  }

  const flags: string[] = [];
  if (tcd?.isSpecial) flags.push("cash");
  if (tcd && tcd.canTrade & 1) flags.push("tradable");
  if (tcd?.canMagic) flags.push("magic");
  if (tcd?.canGrade) flags.push("grade");
  if (tcd?.canRare) flags.push("rare");
  if (flags.length) d.flags = flags;

  // desc lines are tooltip templates; keep only fully-static lines (no /token).
  const desc = tcd?.infoId ? g.infos.get(tcd.infoId) : undefined;
  if (desc && desc.length) {
    const clean = desc.filter((s) => s && s.trim() && !s.includes("/"));
    if (clean.length) d.desc = clean.join("\n");
  }

  return {
    id: db.id,
    name: (db.name || tcd?.name || "").trim(),
    type: db.type,
    kind: tcd?.kind ?? 0,
    iconBest: icon,
    det: Object.keys(d).length ? d : undefined,
  };
}
