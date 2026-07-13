// TITEM_TYPE enum (Lib/Own/TProtocol/Include/NetCode.h:1147). Type 23 is a gap
// the client repurposes for "Companion Costume" cosmetics.
export const ITEM_TYPES: Record<number, string> = {
  1: "Weapon",
  2: "Defensive",
  3: "Accessory",
  4: "Long-range Weapon",
  5: "Magic Long-range",
  6: "Shield",
  7: "Usable",
  8: "Parts",
  9: "Grade",
  10: "Money",
  11: "Inventory",
  12: "Pet / Mount",
  13: "Gamble",
  14: "Refine",
  15: "Package",
  16: "Craft",
  17: "Costume",
  18: "Count",
  21: "Saddle",
  22: "Companion",
  23: "Companion Costume",
  24: "Companion Item",
  25: "Act Item",
};

export function typeName(t: number): string {
  return ITEM_TYPES[t] ?? `Type ${t}`;
}

// The mount/pet domain: Pet/Mount, Saddle, Companion, Companion Costume, Companion Item.
// (Mirrors src/lib/petTypes.ts on the UI side.)
export const MOUNT_PET_ITEM_TYPES = [12, 21, 22, 23, 24];
