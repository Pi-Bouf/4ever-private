// Shared domain types for the extraction pipeline.

export interface RgbaImage {
  width: number;
  height: number;
  rgba: Buffer;
}

/** A record decoded from TItem.tcd (client catalog). */
export interface RawItem {
  id: number;
  type: number;
  kind: number;
  attrId: number;
  name: string;
  useValue: number;
  visual: number[]; // m_wVisual[5]
  infoId: number;
  slotId: number;
  classId: number;
  prmSlotId: number;
  subSlotId: number;
  level: number;
  canRepair: number;
  duraMax: number;
  refineMax: number;
  price: number;
  minRange: number;
  maxRange: number;
  stack: number;
  slotCount: number;
  canGamble: number;
  gambleProb: number;
  destroyProb: number;
  canGrade: number;
  canMagic: number;
  canRare: number;
  delayGroupId: number;
  delay: number;
  canTrade: number;
  isSpecial: number;
  useTime: number;
  useType: number;
  canWrap: number;
  auctionCode: number;
  canColor: number;
}

/** AP/DP row from TItemAttr.tcd, keyed by wAttrID. */
export interface ItemAttr {
  minAP: number;
  maxAP: number;
  dp: number;
  minMAP: number;
  maxMAP: number;
  mdp: number;
  block: number;
  speed: number;
}

export interface IdxRef {
  fileID: number;
  pos: number;
}

export interface Idx {
  files: string[];
  map: Map<number, IdxRef>;
}
